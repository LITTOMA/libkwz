using System.Globalization;
using System.Text.Json;

namespace KwzStudio;

/// <summary>Embedded UI catalogs. Project data and serialization never depend on the UI language.</summary>
public static class L
{
    public sealed record Language(string Code, string NativeName);
    public static IReadOnlyList<Language> Languages { get; } = Array.AsReadOnly(new Language[]
    {
        new("en", "English"), new("zh-Hans", "简体中文"), new("zh-Hant", "繁體中文"),
        new("ja", "日本語"), new("ko", "한국어"), new("fr", "Français"),
        new("de", "Deutsch"), new("es", "Español"), new("it", "Italiano"),
        new("pt-BR", "Português (Brasil)"), new("ru", "Русский"), new("uk", "Українська"),
        new("pl", "Polski"), new("nl", "Nederlands"), new("tr", "Türkçe"),
        new("ar", "العربية"), new("hi", "हिन्दी"), new("id", "Bahasa Indonesia"),
        new("vi", "Tiếng Việt"), new("th", "ไทย")
    });
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);
    private static string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KWZ Studio", "settings.json");
    private static string _systemLanguage = Resolve(CultureInfo.CurrentUICulture.Name);
    public static string Preference { get; private set; } = "system";
    public static string Current { get; private set; } = "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current);
    public static bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;
    public static event Action? Changed;

    public static string Resolve(string culture)
    {
        string tag = culture.Replace('_', '-');
        var exact = Languages.FirstOrDefault(l => string.Equals(l.Code, tag, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Code;
        if (tag.Equals("zh", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = tag.Split('-');
            if (parts.Any(s => s.Equals("Hant", StringComparison.OrdinalIgnoreCase))) return "zh-Hant";
            if (parts.Any(s => s.Equals("Hans", StringComparison.OrdinalIgnoreCase))) return "zh-Hans";
            return parts.Any(s => s.Equals("TW", StringComparison.OrdinalIgnoreCase) || s.Equals("HK", StringComparison.OrdinalIgnoreCase) || s.Equals("MO", StringComparison.OrdinalIgnoreCase)) ? "zh-Hant" : "zh-Hans";
        }
        string language = tag.Split('-')[0];
        if (language.Equals("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        return Languages.FirstOrDefault(l => l.Code.Equals(language, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    /// <summary>An optional path/system culture keeps automated checks separate from user preferences.</summary>
    public static void Initialize(string? settingsPath = null, string? systemCulture = null)
    {
        if (settingsPath != null) _settingsPath = Path.GetFullPath(settingsPath);
        _systemLanguage = Resolve(systemCulture ?? CultureInfo.CurrentUICulture.Name);
        string preference = "system";
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(_settingsPath));
                if (settings?.Language is { } code && IsPreference(code)) preference = code;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        SetLanguage(preference, persist: false);
    }

    private sealed record Settings(string Language);
    private static bool IsPreference(string code) => code == "system" || Languages.Any(l => l.Code == code);
    public static void SetLanguage(string code, bool persist = true)
    {
        if (!IsPreference(code)) throw new ArgumentException("Unsupported UI language.", nameof(code));
        string resolved = code == "system" ? _systemLanguage : code;
        _ = GetCatalog(resolved);
        if (persist)
        {
            string temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Settings(code)));
                File.Move(temporary, _settingsPath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new IOException(T("The language preference could not be saved."), error); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Preference = code; Current = resolved; Changed?.Invoke();
    }

    public static IReadOnlyDictionary<string, string> GetCatalog(string code)
    {
        if (Catalogs.TryGetValue(code, out var cached)) return cached;
        if (!Languages.Any(l => l.Code == code)) throw new ArgumentException("Unsupported UI language.", nameof(code));
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"KwzStudio.Localization.{code}.json")
            ?? throw new InvalidDataException($"Missing UI catalog: {code}");
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Invalid UI catalog: {code}");
        var catalog = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values);
        Catalogs.Add(code, catalog); return catalog;
    }

    public static string T(string key) => GetCatalog(Current).TryGetValue(key, out var value) ? value
        : GetCatalog("en").TryGetValue(key, out var fallback) ? fallback : key;
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);
}
