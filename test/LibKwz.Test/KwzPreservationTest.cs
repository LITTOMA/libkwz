using System.Buffers.Binary;
using SixLabors.ImageSharp.PixelFormats;
namespace LibKwz.Test;

public class KwzPreservationTest
{
    [Fact]
    public void UnknownSectionsOrderAndOpaqueMetadataSurvive()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "sound.kwz"));
        // Use legal UTF-16 code units that text encoders commonly normalize, and unrelated filenames.
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(54), 0xD812);
        source.AsSpan(120, 28).Fill((byte)'z');
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(8), System.IO.Hashing.Crc32.HashToUInt32(source.AsSpan(12, 200)));
        var sections = new List<byte[]>(); int offset = 0;
        while (source.Length - offset > 256)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset + 4));
            byte[] section = source.AsSpan(offset, size + 8).ToArray();
            if (section.AsSpan(0, 3).SequenceEqual("KMI"u8))
            {
                Array.Resize(ref section, section.Length + 3);
                section[^3] = 0xF1; section[^2] = 0x23; section[^1] = 0x45;
                BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), size + 3);
            }
            sections.Add(section); offset += size + 8;
        }
        using var ms = new MemoryStream();
        ms.Write(sections[0]);
        ms.Write(new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 0x80, 3, 0, 0, 0, 0x12, 0x34, 0x56 });
        foreach (var section in sections.Skip(1).Reverse()) ms.Write(section);
        ms.Write(source.AsSpan(offset));
        byte[] custom = ms.ToArray();
        using var file = new KwzFile(new MemoryStream(custom));
        Assert.Equal(custom, file.ToArray());
    }

    [Fact]
    public void CompositingUsesPaperVisibilityAndStableDepthOrder()
    {
        using var file = new KwzFile(Path.Combine(AppContext.BaseDirectory, "sound.kwz"));
        byte[] pixels = new byte[KwzFrameData.FramePixelCount];
        pixels[0] = pixels[KwzLayerCodec.PixelCount] = pixels[KwzLayerCodec.PixelCount * 2] = 1;
        file.FrameData.SetFramePixels(0, pixels);
        var meta = file.Meta.Entries[0];
        meta.PaperColorIndex = 3; meta.LayerAFirstColorIndex = 2; meta.LayerBFirstColorIndex = 4; meta.LayerCFirstColorIndex = 5;
        meta.LayerADepth = meta.LayerBDepth = meta.LayerCDepth = 0;
        file.Header.LayerVisibilityFlags = 0;
        var (layers, composite) = file.FrameData.FrameToImage(0);
        using (composite)
        {
            Assert.Equal(meta.LayerAFirstColor.ToPixel<Rgba32>(), ((SixLabors.ImageSharp.Image<Rgba32>)composite)[0, 0]);
            Assert.Equal(meta.PaperColor.ToPixel<Rgba32>(), ((SixLabors.ImageSharp.Image<Rgba32>)composite)[1, 0]);
        }
        foreach (var image in layers) image.Dispose();
        file.Header.LayerAVisible = false;
        (layers, composite) = file.FrameData.FrameToImage(0);
        using (composite) Assert.Equal(meta.LayerBFirstColor.ToPixel<Rgba32>(), ((SixLabors.ImageSharp.Image<Rgba32>)composite)[0, 0]);
        foreach (var image in layers) image.Dispose();
        Assert.Equal(1, file.FrameData.DecodeFrame(0)[0]); // hidden layers still decode
    }

    [Fact]
    public void UnusedBitsAndNoncanonicalTileChoicesArePreserved()
    {
        // 1200 type-1 tiles deliberately use a 13-bit reference to the common empty row.
        byte[] input = new byte[2402];
        for (int tile = 0; tile < 1200; tile++) input[tile * 2] = 1;
        input[^2] = 0xA5; input[^1] = 0xF0;
        var encoding = KwzLayerEncoding.Read(input);
        byte[] pixels = new byte[KwzLayerCodec.PixelCount]; encoding.Decode(pixels);
        Assert.Equal(input, encoding.ToBytes());
        Assert.Equal(input, encoding.EncodePixels(pixels, pixels));
        // The encoding plan also supports a changed row that still fits the same shape.
        pixels.AsSpan().Fill(2);
        byte[] changed = encoding.EncodePixels(pixels, new byte[pixels.Length]);
        byte[] decoded = new byte[pixels.Length]; KwzLayerCodec.Decode(changed, decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void TypeSevenPatternsAndShiftedRowsMatchKnownPixels()
    {
        int[][] patterns = [ [0,1,0,1,0,1,0,1], [0,0,1,0,0,1,0,0], [0,1,0,0,1,0,0,1], [0,1,1,0,1,1,0,1] ];
        foreach (bool common in new[] { false, true })
        for (int pattern = 0; pattern < 4; pattern++)
        {
            var bits = new List<int>();
            void Write(int value, int length) { for (int i = 0; i < length; i++) bits.Add((value >> i) & 1); }
            Write(7, 3); Write(common ? (pattern + 3) % 4 : pattern, 2); Write(common ? 1 : 0, 1);
            Write(common ? 1 : 3280, common ? 5 : 13); Write(common ? 2 : 6560, common ? 5 : 13);
            for (int tile = 1; tile < 1200; tile += 32) { Write(5, 3); Write(Math.Min(32, 1200 - tile) - 1, 5); }
            byte[] bytes = new byte[(bits.Count + 15) / 16 * 2];
            for (int i = 0; i < bits.Count; i++) bytes[i / 8] |= (byte)(bits[i] << (i % 8));
            byte[] decoded = new byte[KwzLayerCodec.PixelCount]; KwzLayerCodec.Decode(bytes, decoded);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) Assert.Equal(patterns[pattern][y] + 1, decoded[y * 320 + x]);
            Assert.Equal(bytes, KwzLayerEncoding.Read(bytes).EncodePixels(decoded, new byte[decoded.Length]));
        }
    }
}
