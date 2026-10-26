using System.Buffers.Binary;
using LibKwz;
using KwzTestFixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LibKwz.Test;

public class FolderIconTests
{
    [Fact]
    public void StandardIconMatchesIndependentDecoderVector()
    {
        // RGBA hash independently produced by flipnote.js 6.3.0 for this synthetic KIC.
        using var image = KwzFolderIcon.Decode(FolderIconFixture.Create(configure: m => m.LayerAFirstColorIndex = 2));
        byte[] rgba = new byte[24 * 24 * 4]; image.CopyPixelDataTo(rgba);
        Assert.Equal("7CBFAD69F1892656276EE05987D303E28814E711623C7D7950AD45D78725C19E",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rgba)));
    }

    [Fact]
    public void DecodesTopLeftCropAndLayerDepths()
    {
        byte[][] layers = [new byte[76800], new byte[76800], new byte[76800]];
        layers[0][320 + 1] = 2; layers[1][320 + 2] = 1; layers[2][320 + 3] = 1;
        foreach (var layer in layers) layer[320 + 4] = 1;
        layers[0][200 * 320 + 200] = 2;
        using var image = KwzFolderIcon.Decode(FolderIconFixture.Create(layers));
        Assert.Equal(24, image.Width); Assert.Equal(24, image.Height);
        Assert.Equal(new Rgba32(255, 16, 16), image[1, 1]);
        Assert.Equal(new Rgba32(255, 231, 0), image[2, 1]);
        Assert.Equal(new Rgba32(0, 56, 206), image[3, 1]);
        Assert.Equal(new Rgba32(0, 134, 49), image[4, 1]);
        Assert.Equal(new Rgba32(255, 255, 255), image[23, 23]);
        using var depth = KwzFolderIcon.Decode(FolderIconFixture.Create(layers, m => { m.LayerADepth = 50; m.LayerBDepth = 25; }));
        Assert.Equal(new Rgba32(0, 56, 206), depth[4, 1]);
    }

    [Fact]
    public void EmptyLayersAndTransparentInkRevealPaper()
    {
        byte[][] layers = [new byte[76800], new byte[76800], new byte[76800]];
        layers[0][0] = 2;
        using var image = KwzFolderIcon.Decode(FolderIconFixture.Create(layers, m => { m.PaperColorIndex = 5; m.LayerASecondColorIndex = 6; }, omitEmptyLayers: true));
        Assert.Equal(new Rgba32(0, 56, 206), image[0, 0]);
        using var empty = KwzFolderIcon.Decode(FolderIconFixture.Create([new byte[76800], new byte[76800], new byte[76800]], omitEmptyLayers: true));
        Assert.Equal(new Rgba32(255, 255, 255), empty[0, 0]);
    }

    [Fact]
    public void RejectsCorruptionAndInvalidLayoutEvenWhenResigned()
    {
        byte[] original = FolderIconFixture.Create();
        byte[] corrupt = (byte[])original.Clone(); corrupt[20] ^= 1;
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(corrupt));
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(KwzSigning.Sign(corrupt)));
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(original.AsSpan(0, 50)));
        int kmi = 16 + BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(12));
        corrupt = (byte[])original.Clone(); corrupt[kmi + 8] = 0xf;
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(KwzSigning.Sign(corrupt)));
        corrupt = (byte[])original.Clone(); corrupt[kmi + 12] = 1; corrupt[kmi + 13] = 0;
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(KwzSigning.Sign(corrupt)));
        corrupt = (byte[])original.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(12), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => KwzFolderIcon.Decode(KwzSigning.Sign(corrupt)));
    }

    [Fact]
    public void OptionalPrivateIconDecodesWithoutPublishingItsContents()
    {
        string? source = Environment.GetEnvironmentVariable("KWZ_FOLDER_ICON_SAMPLE");
        if (source == null) return;
        using var image = KwzFolderIcon.Load(source);
        Assert.Equal(24, image.Width); Assert.Equal(24, image.Height);
        // An optional local-only oracle output; no user icon or metadata enters the repository.
        string? scratch = Environment.GetEnvironmentVariable("KWZ_TEST_TEMP");
        if (scratch != null)
        {
            byte[] pixels = new byte[24 * 24 * 4]; image.CopyPixelDataTo(pixels);
            File.WriteAllBytes(Path.Combine(scratch, "private-icon.rgba"), pixels);
            image.SaveAsPng(Path.Combine(scratch, "private-icon.png"));
        }
    }
}
