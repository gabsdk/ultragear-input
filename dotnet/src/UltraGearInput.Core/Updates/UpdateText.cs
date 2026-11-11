using System.Globalization;
using System.Text.Json;

namespace UltraGearInput.Core.Updates;

public static class UpdateText
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = Load();

    public static string Language(string? locale) => locale?.Split('.', '@')[0].Replace('_', '-').Split('-')[0].ToLowerInvariant() switch
    {
        "pt" => "pt",
        "es" => "es",
        _ => "en",
    };

    public static string? PreferredLanguage { get; set; }

    public static string WindowsLanguage(ushort languageId) => (languageId & 0x3FF) switch
    {
        0x16 => "pt",
        0x0A => "es",
        _ => "en",
    };

    public static string CurrentLanguage => Language(PreferredLanguage ??
        Environment.GetEnvironmentVariable("LC_ALL")
        ?? Environment.GetEnvironmentVariable("LC_MESSAGES")
        ?? Environment.GetEnvironmentVariable("LANG")
        ?? CultureInfo.CurrentUICulture.Name);

    public static string Get(string key, string? language = null, string? value = null)
    {
        var catalog = Catalogs[Language(language ?? CurrentLanguage)];
        var text = catalog.GetValueOrDefault(key) ?? Catalogs["en"][key];
        return value is null ? text : text.Replace("{0}", value, StringComparison.Ordinal);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in new[] { "en", "pt", "es" })
        {
            using var stream = typeof(UpdateText).Assembly.GetManifestResourceStream($"updates.{language}.json")!;
            using var document = JsonDocument.Parse(stream);
            result[language] = document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal);
        }
        return result;
    }
}
