using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LibKwz.Test;

public class KwzPaletteThumbnailTest
{
    // Reference palette: Flipnote-Collective/kwz-parser and flipnote.js KwzParser.
    // Blue must be the game's dark blue, not the older documentation's cyan.
    [Theory]
    [InlineData(0, 255, 255, 255)]
    [InlineData(1, 16, 16, 16)]
    [InlineData(2, 255, 16, 16)]
    [InlineData(3, 255, 231, 0)]
    [InlineData(4, 0, 134, 49)]
    [InlineData(5, 0, 56, 206)]
    public void StandardThumbnailUsesTheKwzPalette(int index, int red, int green, int blue)
    {
        using var note = KwzFile.CreateBlank();
        note.Meta.Entries[0].PaperColorIndex = (byte)index;
        uint flags = note.Meta.Entries[0].Flags;
        byte[] pixels = note.FrameData.DecodeFrame(0);
        note.UpdateThumbnail(0);
        using var jpeg = Image.Load<Rgb24>(note.Thumbnail!.Data);
        Assert.Equal(80, jpeg.Width);
        Assert.Equal(64, jpeg.Height);
        Rgb24 actual = jpeg[40, 32];
        Assert.InRange(Math.Abs(actual.R - red), 0, 3);
        Assert.InRange(Math.Abs(actual.G - green), 0, 3);
        Assert.InRange(Math.Abs(actual.B - blue), 0, 3);
        Assert.Equal(flags, note.Meta.Entries[0].Flags);
        Assert.Equal(pixels, note.FrameData.DecodeFrame(0));
    }

    [Fact]
    public void TransparentPaperRemainsWhiteWhenExportedToJpeg()
    {
        using var note = KwzFile.CreateBlank();
        note.Meta.Entries[0].PaperColorIndex = 6;
        uint flags = note.Meta.Entries[0].Flags;
        note.UpdateThumbnail(0);
        using var jpeg = Image.Load<Rgb24>(note.Thumbnail!.Data);
        Assert.Equal(new Rgb24(255, 255, 255), jpeg[40, 32]);
        Assert.Equal(flags, note.Meta.Entries[0].Flags);
    }
}
