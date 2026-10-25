using System.Security.Cryptography;
namespace LibKwz.Test;

public class KwzFileTest
{
    public static string CorpusRoot
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("KWZ_CORPUS_ROOT");
            if (configured != null) return Path.GetFullPath(configured);
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "unpack"))) return d.FullName;
            throw new DirectoryNotFoundException("Set KWZ_CORPUS_ROOT to the Note project directory.");
        }
    }
    public static IEnumerable<object[]> Samples()
    {
        string root = CorpusRoot;
        var files = Directory.EnumerateFiles(Path.Combine(root, "unpack"), "*.kwz", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.kwz"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools", "kwz-parser"), "*.kwz", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools", "libkwz", "test", "LibKwz.Test"), "*.kwz"));
        return files.OrderBy(p => p).Select(p => new object[] { p });
    }
    [Theory]
    [MemberData(nameof(Samples))]
    public void CorpusRoundtripAndPixelRecompression(string path)
    {
        byte[] original = File.ReadAllBytes(path);
        using var file = new KwzFile(new MemoryStream(original));
        // This rebuilds every layer from decoded pixels with the original instruction plan.
        byte[] rebuilt = file.ToArray();
        int mismatch = Enumerable.Range(0, Math.Min(original.Length, rebuilt.Length)).FirstOrDefault(i => original[i] != rebuilt[i], -1);
        Assert.True(original.Length == rebuilt.Length && mismatch == -1, $"{path}: lengths {original.Length}/{rebuilt.Length}, first mismatch 0x{mismatch:X}");
        Assert.True(file.SignatureMatchesOriginalBody);
        using var reread = new KwzFile(new MemoryStream(rebuilt));
        var hashes = new List<byte[]>();
        byte[] previous = new byte[KwzFrameData.FramePixelCount];
        for (int frame = 0; frame < file.Header.FrameCount; frame++)
        {
            byte[] pixels = file.FrameData.DecodeFrame(frame);
            hashes.Add(SHA256.HashData(pixels));
            Assert.True(pixels.AsSpan().SequenceEqual(reread.FrameData.DecodeFrame(frame)), $"Reread frame {frame}");
            for (int layer = 0; layer < 3; layer++)
            {
                var target = pixels.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                var basis = previous.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                byte[] compressed = KwzLayerCodec.Encode(target, frame == 0 ? default : basis);
                byte[] decoded = frame == 0 ? new byte[KwzLayerCodec.PixelCount] : basis.ToArray();
                KwzLayerCodec.Decode(compressed, decoded);
                Assert.True(target.SequenceEqual(decoded), $"Independent recompression frame {frame}, layer {layer}");
            }
            previous = pixels;
        }
        foreach (int frame in new[] { file.Header.FrameCount - 1, 0, file.Header.FrameCount / 2, 0 })
            Assert.Equal(hashes[frame], SHA256.HashData(file.FrameData.DecodeFrame(frame)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void EditsPreserveOtherFramesAndUpdateChecksums(int changed)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "sound.kwz");
        using var file = new KwzFile(path);
        var hashes = Enumerable.Range(0, file.Header.FrameCount).Select(i => SHA256.HashData(file.FrameData.DecodeFrame(i))).ToArray();
        if (changed == -1) changed = file.Header.FrameCount - 1;
        byte[] edited = file.FrameData.DecodeFrame(changed);
        for (int layer = 0; layer < 3; layer++)
            for (int y = 100; y < 120; y++)
            for (int x = 100; x < 120; x++) edited[layer * KwzLayerCodec.PixelCount + y * 320 + x] = 2;
        file.FrameData.SetFramePixels(changed, edited);
        byte[] written = file.ToArray();
        Assert.False(file.SignatureMatchesOriginalBody);
        using var reread = new KwzFile(new MemoryStream(written));
        for (int i = 0; i < file.Header.FrameCount; i++)
            Assert.Equal(i == changed ? SHA256.HashData(edited) : hashes[i], SHA256.HashData(reread.FrameData.DecodeFrame(i)));
        Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(reread.FrameData.Data), reread.FrameData.Crc32);
        Assert.Equal(written, reread.ToArray());
    }
    [Fact]
    public void TruncationIsRejected()
    {
        byte[] bytes = File.ReadAllBytes((string)Samples().First()[0]);
        foreach (int size in new[] { 0, 7, 211, bytes.Length - 1, bytes.Length - 257 })
            Assert.Throws<InvalidDataException>(() => new KwzFile(new MemoryStream(bytes[..size])));
    }
}
