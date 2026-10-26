using System.Buffers.Binary;
using System.IO.Hashing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LibKwz;

/// <summary>Decodes the signed KIC variant stored as !.ico, not a Windows ICO file.</summary>
public static class KwzFolderIcon
{
    public const int Width = 24, Height = 24;
    public const int MaxFileSize = 64 * 1024;

    public static Image<Rgba32> Load(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > MaxFileSize) throw new InvalidDataException("The folder icon is too large.");
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        return Decode(bytes);
    }

    /// <summary>Returns an owned 24x24 RGBA image. Validates section bounds, CRC and RSA signature.</summary>
    public static Image<Rgba32> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 + 12 + 36 + 256 || bytes.Length > MaxFileSize ||
            !bytes[..8].SequenceEqual("KIC\x03\0\0\0\0"u8))
            throw new InvalidDataException("Invalid KIC folder icon.");
        if (!KwzSigning.Verify(bytes)) throw new InvalidDataException("The folder icon signature is invalid.");
        ReadOnlySpan<byte> body = bytes[..^256];
        byte[]? compressed = null;
        KwzMeta.FrameMeta? meta = null;
        int offset = 8;
        while (offset < body.Length)
        {
            if (body.Length - offset < 8) throw new InvalidDataException("Truncated folder icon section.");
            var section = body[offset..];
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(section[4..]);
            if (size > section.Length - 8) throw new InvalidDataException("Truncated folder icon section.");
            var payload = section.Slice(8, (int)size);
            if (section[..4].SequenceEqual("KMC\x02"u8) && compressed == null && size >= 4)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(payload) != Crc32.HashToUInt32(payload[4..]))
                    throw new InvalidDataException("The folder icon checksum does not match.");
                compressed = payload[4..].ToArray();
            }
            else if (section[..4].SequenceEqual("KMI\x05"u8) && meta == null && size == 28)
            {
                using var reader = new BinaryReader(new MemoryStream(payload.ToArray()));
                meta = KwzMeta.FrameMeta.ReadFrom(reader);
            }
            else throw new InvalidDataException("Unsupported or duplicate folder icon section.");
            offset += 8 + (int)size;
        }
        if (compressed == null || meta == null) throw new InvalidDataException("Missing folder icon image sections.");
        var sizes = meta.LayerSizes;
        int total = sizes.Sum(s => (int)s);
        if (sizes.Any(s => s > 0x4aff || (s & 1) != 0) || (total + 3 & ~3) != compressed.Length)
            throw new InvalidDataException("Invalid folder icon layer sizes.");
        int[] palette = [meta.PaperColorIndex, meta.LayerAFirstColorIndex, meta.LayerASecondColorIndex,
            meta.LayerBFirstColorIndex, meta.LayerBSecondColorIndex, meta.LayerCFirstColorIndex, meta.LayerCSecondColorIndex];
        if (palette.Any(p => p >= KwzMeta.FramePalette.Length)) throw new InvalidDataException("Invalid folder icon palette.");
        byte[][] layers = [new byte[KwzLayerCodec.PixelCount], new byte[KwzLayerCodec.PixelCount], new byte[KwzLayerCodec.PixelCount]];
        offset = 0;
        for (int layer = 0; layer < 3; layer++)
        {
            // Unlike regular animation frames, KIC can omit an entirely empty layer.
            if (sizes[layer] != 0) KwzLayerCodec.Decode(compressed.AsMemory(offset, sizes[layer]), layers[layer]);
            offset += sizes[layer];
        }
        var colors = palette.Select(p => KwzMeta.FramePalette[p].ToPixel<Rgba32>()).ToArray();
        var image = new Image<Rgba32>(Width, Height, colors[0]);
        foreach (int layer in new[] { 2, 1, 0 }.OrderByDescending(i => meta.LayerDepths[i]))
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    byte ink = layers[layer][y * KwzLayerCodec.Width + x];
                    if (ink != 0 && colors[1 + layer * 2 + ink - 1].A != 0) image[x, y] = colors[1 + layer * 2 + ink - 1];
                }
        return image;
    }
}
