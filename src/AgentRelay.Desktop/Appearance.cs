using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AgentRelay.Core;
using Microsoft.Win32;

namespace AgentRelay.Desktop;

public sealed record DesktopPreferences(
    [property: JsonRequired] string Language = "zh-CN",
    [property: JsonRequired] string Palette = "system",
    string? Accent = null,
    bool? CloseToTray = null,
    string NotificationSound = "waterdrop",
    long? LastNotificationId = null);

public sealed record AppearanceOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public static class Appearance
{
    public static IReadOnlyList<AppearanceOption> Languages { get; } = Array.AsReadOnly(new[]
    {
        new AppearanceOption("en", "English"), new AppearanceOption("zh-CN", "简体中文"),
        new AppearanceOption("zh-TW", "繁體中文"), new AppearanceOption("ja", "日本語"),
        new AppearanceOption("ko", "한국어"), new AppearanceOption("es", "Español")
    });

    public static IReadOnlyList<AppearanceOption> Palettes { get; } = Array.AsReadOnly(new[]
    {
        new AppearanceOption("minimal", "Minimal"), new AppearanceOption("warm", "Warm"),
        new AppearanceOption("fresh", "Fresh"), new AppearanceOption("night", "Night"),
        new AppearanceOption("system", "Follow system")
    });

    /// <summary>Palettes from earlier releases, mapped to the closest current theme so saved preferences keep loading.</summary>
    private static readonly Dictionary<string, string> LegacyPalettes = new(StringComparer.Ordinal)
        { ["mist"] = "minimal", ["sage"] = "fresh", ["clay"] = "warm", ["rose"] = "warm" };

    private static readonly string[] TokenKeys =
    [
        "PageBrush", "NavBrush", "NavTextBrush", "NavMutedBrush", "NavActiveBrush", "SurfaceBrush", "SurfaceAltBrush",
        "BorderBrush", "BorderStrongBrush", "TextBrush", "MutedBrush", "FaintBrush", "AccentBrush", "OnAccentBrush", "AccentInkBrush",
        "AccentSoftBrush", "OkBrush", "OkSoftBrush", "WarnBrush", "WarnSoftBrush", "BadBrush", "BadSoftBrush", "InfoBrush",
        "InfoSoftBrush", "Avatar1Brush", "Avatar2Brush", "Avatar3Brush", "Avatar4Brush", "AvatarTextBrush"
    ];

    /// <summary>One color per entry of <see cref="TokenKeys"/>, in the same order.</summary>
    private static readonly Dictionary<string, string[]> Tokens = new(StringComparer.Ordinal)
    {
        // Frosted steel: cool neutral greys with one steel-blue accent.
        ["minimal"] = ["#F6F7F9", "#ECEEF1", "#1E2733", "#687482", "#FFFFFF", "#FFFFFF", "#F1F3F6", "#DDE1E7", "#C7CED8",
            "#1C2530", "#5B6775", "#8C96A3", "#334E70", "#FFFFFF", "#334E70", "#E4EAF2", "#2F7A55", "#E2F1E8", "#94650E",
            "#FBF0D9", "#B23A3A", "#F9E3E1", "#3B6EA8", "#E3ECF7", "#334E70", "#5C7896", "#4F7158", "#8A6446", "#FFFFFF"],
        // Warm: cream surfaces, orange actions and teal text accents.
        ["warm"] = ["#FFF9EF", "#FFF0D3", "#2A2118", "#7A6852", "#FFFCF6", "#FFFDF8", "#FFF4E2", "#F0E0C4", "#E2CBA4",
            "#2A2118", "#6E5D49", "#A08D75", "#FF8C52", "#3A1A08", "#237A7B", "#FFE3D0", "#2B7A5C", "#DDF2EC", "#9E620A",
            "#FFF0C5", "#C0443A", "#FBE0DA", "#237A7B", "#DDF3F1", "#FF8C52", "#359FA0", "#8AD6D1", "#E3B04B", "#2A2118"],
        // Fresh: sprout-green page, pale green actions, butter and coral for attention.
        ["fresh"] = ["#F5FBEF", "#E7F4DB", "#1E2B22", "#5F735E", "#FFFFFF", "#FFFFFF", "#F0F8E8", "#D6E8C8", "#BCD7A9",
            "#1D2A21", "#536754", "#8DA08B", "#8FD4A8", "#123222", "#2A7752", "#DCF2E2", "#2A7752", "#DCF2E2", "#866500",
            "#FDF1BF", "#BE4A4A", "#FDE2E2", "#2B7C83", "#DCF3EF", "#8FD4A8", "#95E1D3", "#FCE38A", "#F38181", "#1D2A21"],
        // Night: deep grey, never pure black, with a light steel accent.
        ["night"] = ["#16191E", "#111418", "#E6E9EE", "#8E98A6", "#232933", "#1D2128", "#242A32", "#2E3540", "#3D4653",
            "#E6E9EE", "#A3ADBA", "#6F7A88", "#8FB0D9", "#0E1621", "#A9C3E6", "#233246", "#6CC59A", "#1D3329", "#E3B35C",
            "#382D18", "#F08A80", "#3D2323", "#86B6E8", "#1F2F42", "#8FB0D9", "#6F8FA8", "#7FAE8E", "#C49A74", "#0E1621"]
    };

    public static DesktopPreferences Load(string home)
    {
        var path = Path.Combine(home, "desktop.preferences.json");
        if (!File.Exists(path)) return new DesktopPreferences(SystemLanguage());
        DesktopPreferences preferences;
        try
        {
            preferences = RelayJson.Read<DesktopPreferences>(File.ReadAllText(path));
        }
        catch (JsonException error)
        {
            throw new RelayException("invalid_desktop_preferences", $"Invalid desktop preferences in {path}: {error.Message}");
        }
        if (LegacyPalettes.TryGetValue(preferences.Palette, out var current)) preferences = preferences with { Palette = current };
        preferences = preferences with { NotificationSound = preferences.NotificationSound switch
        {
            "default" => "waterdrop", "information" => "soft-piano", "warning" => "warm-chord",
            _ => preferences.NotificationSound
        } };
        Validate(preferences);
        return preferences;
    }

    public static void Save(string home, DesktopPreferences preferences)
    {
        Validate(preferences);
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, "desktop.preferences.json");
        var temporary = path + ".relay-" + Guid.NewGuid() + ".tmp";
        try
        {
            File.WriteAllText(temporary, RelayJson.Write(preferences));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>The theme actually drawn: "system" follows the Windows app mode.</summary>
    public static string Resolve(string palette) => palette != "system" ? palette : SystemUsesDarkMode() ? "night" : "minimal";

    /// <summary>Page, sidebar, border and accent colors, for the theme picker's previews.</summary>
    public static (Color Page, Color Nav, Color Line, Color Accent) Preview(string palette)
    {
        var tokens = Tokens[palette];
        return (ParseColor(tokens[0]), ParseColor(tokens[1]), ParseColor(tokens[8]), ParseColor(tokens[12]));
    }

    public static bool IsDark(Window window) => window.Resources["IsDarkTheme"] is true;

    public static void Apply(Window window, DesktopPreferences preferences)
    {
        window.FontFamily = new FontFamily(preferences.Language switch
        {
            "zh-CN" => "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI",
            "zh-TW" => "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI",
            "ja" => "Segoe UI Variable Text, Segoe UI, Yu Gothic UI",
            "ko" => "Segoe UI Variable Text, Segoe UI, Malgun Gothic",
            _ => "Segoe UI Variable Text, Segoe UI"
        });
        Validate(preferences);
        var theme = Resolve(preferences.Palette);
        var colors = Tokens[theme].Select(ParseColor).ToArray();
        for (var i = 0; i < TokenKeys.Length; i++) Set(TokenKeys[i], colors[i]);
        var dark = theme == "night";
        if (preferences.Accent is not null)
        {
            // A custom accent fills buttons; text uses a variant that stays readable on this theme's surface.
            var accent = ParseColor(preferences.Accent);
            var surface = colors[Array.IndexOf(TokenKeys, "SurfaceBrush")];
            Set("AccentBrush", accent);
            Set("OnAccentBrush", Luminance(accent) > 0.36 ? ParseColor("#16191E") : Colors.White);
            Set("AccentInkBrush", ReadableOn(accent, surface, dark));
            Set("AccentSoftBrush", Blend(accent, surface, dark ? 0.24 : 0.16));
        }
        // Earlier names, still used by the connection graph, bridge badges and dialogs.
        Alias("AllowedBrush", "OkBrush"); Alias("DeniedBrush", "BadBrush"); Alias("UnavailableBrush", "FaintBrush");
        Alias("FocusBrush", "AccentInkBrush");
        window.Resources["ScrimBrush"] = Frozen(Color.FromArgb(dark ? (byte)0x8C : (byte)0x52, 10, 14, 20));
        window.Resources["ShadowColor"] = dark ? Colors.Black : ParseColor("#1C2530");
        window.Resources["ShadowOpacity"] = dark ? 0.45 : 0.12;
        window.Resources["IsDarkTheme"] = dark;
        ApplyTitleBar(window);

        void Set(string key, Color color) => window.Resources[key] = Frozen(color);
        void Alias(string key, string source) => window.Resources[key] = window.Resources[source];
    }

    /// <summary>Dark themes also darken the native caption bar (Windows 10 2004 and later); older systems ignore the request.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark(window) ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        // Windows 11: rounded corners and a hairline border in the theme's own color.
        var round = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        if (window.Resources["BorderStrongBrush"] is SolidColorBrush border)
        {
            var color = border.Color.R | border.Color.G << 8 | border.Color.B << 16;
            _ = DwmSetWindowAttribute(handle, 34, ref color, sizeof(int));
        }
    }

    public static Color ParseAccent(string accent)
    {
        // The accent can be entered or pasted by the user; reject invalid color text at this boundary.
        if (accent.Length != 7 || accent[0] != '#' || !accent.Skip(1).All(Uri.IsHexDigit))
            throw new RelayException("invalid_desktop_accent", "Desktop accent must use #RRGGBB, for example #637A68.");
        return ParseColor(accent);
    }

    private static void Validate(DesktopPreferences preferences)
    {
        // Preferences are editable user input; invalid values must produce an actionable error.
        if (!Languages.Any(option => option.Id == preferences.Language))
            throw new RelayException("invalid_desktop_language", $"Unknown desktop language: {preferences.Language}. Supported: en, zh-CN, zh-TW, ja, ko, es.");
        if (!Palettes.Any(option => option.Id == preferences.Palette))
            throw new RelayException("invalid_desktop_palette", $"Unknown desktop palette: {preferences.Palette}. Supported: minimal, warm, fresh, night, system.");
        if (preferences.Accent is not null) ParseAccent(preferences.Accent);
        if (preferences.NotificationSound is not ("none" or "waterdrop" or "soft-piano" or "warm-chord" or "error"))
            throw new RelayException("invalid_notification_sound","通知声音应为 none、waterdrop、soft-piano、warm-chord 或 error。");
        if (preferences.LastNotificationId is < 0) throw new RelayException("invalid_notification_cursor","通知记录位置不能为负数。");
    }

    private static string SystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        var exact = Languages.FirstOrDefault(option => option.Id == culture.Name);
        if (exact is not null) return exact.Id;
        if (culture.TwoLetterISOLanguageName == "zh")
            return culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                culture.Name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                culture.Name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        return Languages.Any(option => option.Id == culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : "en";
    }

    private static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string color) => Color.FromRgb(
        byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R * amount + second.R * (1 - amount)),
        (byte)Math.Round(first.G * amount + second.G * (1 - amount)),
        (byte)Math.Round(first.B * amount + second.B * (1 - amount)));

    private static Color ReadableOn(Color color, Color surface, bool dark)
    {
        // Text in the accent hue keeps at least 4.5:1 against the surface it sits on.
        for (var i = 0; i < 40 && Contrast(color, surface) < 4.5; i++) color = Blend(color, dark ? Colors.White : Colors.Black, 0.92);
        return color;
    }

    private static double Contrast(Color first, Color second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
