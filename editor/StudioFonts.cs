using SkiaSharp;

namespace KwzStudio;

/// <summary>Text objects use the bundled, unmodified Fusion Pixel font on every machine.</summary>
public static class StudioFonts
{
    public const string DefaultFamily = "Fusion Pixel 12px Prop zh-Hans";
    // Keep one native font-data buffer for the application's lifetime. Each caller owns its typeface.
    private static readonly Lazy<SKData> FusionPixel = new(() =>
    {
        using var stream = typeof(StudioFonts).Assembly.GetManifestResourceStream("KwzStudio.Fonts.FusionPixel12.otf")
            ?? throw new InvalidDataException("Missing bundled Fusion Pixel font.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return SKData.CreateCopy(bytes.ToArray());
    });

    public static SKTypeface CreateTypeface(string family) =>
        string.Equals(family, DefaultFamily, StringComparison.OrdinalIgnoreCase)
            ? SKTypeface.FromData(FusionPixel.Value)
            : SKTypeface.FromFamilyName(family);
}
