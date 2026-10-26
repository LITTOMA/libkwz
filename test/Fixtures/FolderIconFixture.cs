using LibKwz;
using System.IO.Hashing;

namespace KwzTestFixtures;

// Synthetic, anonymous KIC files. Never commits icons or author IDs from a user's SD card.
internal static class FolderIconFixture
{
    public static byte[] Create(byte[][]? layers = null, Action<KwzMeta.FrameMeta>? configure = null, bool omitEmptyLayers = false)
    {
        if (layers == null)
        {
            layers = [new byte[KwzLayerCodec.PixelCount], new byte[KwzLayerCodec.PixelCount], new byte[KwzLayerCodec.PixelCount]];
            for (int y = 3; y < 22; y++)
                for (int x = 2; x < 23; x++)
                {
                    if ((x - 17) * (x - 17) + (y - 7) * (y - 7) <= 9) layers[1][y * 320 + x] = 1;
                    if (y >= 14 + Math.Abs(x - 10) / 2) layers[0][y * 320 + x] = 1;
                }
        }
        byte[][] encoded = layers.Select(p => omitEmptyLayers && p.All(v => v == 0) ? Array.Empty<byte>() : KwzLayerCodec.Encode(p)).ToArray();
        var meta = new KwzMeta.FrameMeta { PaperColorIndex = 0, LayerAFirstColorIndex = 4, LayerASecondColorIndex = 2,
            LayerBFirstColorIndex = 3, LayerBSecondColorIndex = 1, LayerCFirstColorIndex = 5, LayerCSecondColorIndex = 6,
            LayerASize = (ushort)encoded[0].Length, LayerBSize = (ushort)encoded[1].Length, LayerCSize = (ushort)encoded[2].Length };
        configure?.Invoke(meta);
        byte[] image = new byte[(encoded.Sum(a => a.Length) + 3) & ~3];
        int position = 0;
        foreach (var layer in encoded) { layer.CopyTo(image, position); position += layer.Length; }
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write("KIC\x03\0\0\0\0"u8);
        writer.Write("KMC\x02"u8); writer.Write(image.Length + 4); writer.Write(Crc32.HashToUInt32(image)); writer.Write(image);
        writer.Write("KMI\x05"u8); writer.Write(28); meta.WriteTo(writer);
        writer.Write(new byte[256]);
        return KwzSigning.Sign(bytes.ToArray());
    }
}
