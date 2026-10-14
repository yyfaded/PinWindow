using System.Text.Json;
using Microsoft.Win32;

namespace PinWindow;

internal sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public string Language { get; set; } = "zh"; // 默认中文
    public bool ShowTrayIcon { get; set; } = false; // 汉化版修复：托盘图标默认隐藏（可在设置中打开）
    public int MouseTriggerButton { get; set; } // 0: 鼠标右键, 1: 鼠标中键, 2: 侧键1(后退), 3: 侧键2(前进), 4: 任意按键

    public int PinSize { get; set; } = 27;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public string ActiveColor { get; set; } = "#0078D7";

    public bool HotkeyControl { get; set; }
    public bool HotkeyAlt { get; set; }
    public bool HotkeyShift { get; set; }
    public bool HotkeyWin { get; set; }
    public int HotkeyKey { get; set; } = (int)Keys.None;

    public AppSettings Clone() =>
        new()
        {
            StartWithWindows = StartWithWindows,
            Language = Language,
            ShowTrayIcon = ShowTrayIcon,
            MouseTriggerButton = MouseTriggerButton,
            PinSize = PinSize,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            ActiveColor = ActiveColor,
            HotkeyControl = HotkeyControl,
            HotkeyAlt = HotkeyAlt,
            HotkeyShift = HotkeyShift,
            HotkeyWin = HotkeyWin,
            HotkeyKey = HotkeyKey,
        };

    public void Normalize()
    {
        Language = Language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh"
            : Language.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "ru";

        MouseTriggerButton = Math.Clamp(MouseTriggerButton, 0, 4);

        PinSize = Math.Clamp(PinSize, 20, 42);

        OffsetX = Math.Clamp(OffsetX, -80, 80);
        OffsetY = Math.Clamp(OffsetY, -80, 80);

        if (!Enum.IsDefined(typeof(Keys), HotkeyKey))
        {
            HotkeyKey = (int)Keys.None;
        }

        ActiveColor = ColorToHtml(GetActiveColor());
    }

    public Color GetActiveColor()
    {
        try
        {
            var color = ColorTranslator.FromHtml(ActiveColor);

            return color.A == 0
                ? Color.FromArgb(0, 120, 215)
                : Color.FromArgb(255, color.R, color.G, color.B);
        }
        catch
        {
            return Color.FromArgb(0, 120, 215);
        }
    }

    public HotkeyDefinition GetHotkey() =>
        new(HotkeyControl, HotkeyAlt, HotkeyShift, HotkeyWin, (Keys)HotkeyKey);

    public static AppSettings CreateDefault() => new();

    public static string ColorToHtml(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

internal readonly record struct HotkeyDefinition(
    bool Control,
    bool Alt,
    bool Shift,
    bool Win,
    Keys Key
)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public bool IsConfigured => (Control || Alt || Shift || Win) && Key != Keys.None;

    public uint NativeModifiers =>
        (Control ? ModControl : 0)
        | (Alt ? ModAlt : 0)
        | (Shift ? ModShift : 0)
        | (Win ? ModWin : 0);

    public string DisplayText
    {
        get
        {
            if (!IsConfigured)
            {
                return AppLocalization.Text("нет", "None");
            }

            var parts = new List<string>();

            if (Control)
                parts.Add("Ctrl");
            if (Alt)
                parts.Add("Alt");
            if (Shift)
                parts.Add("Shift");
            if (Win)
                parts.Add("Win");

            parts.Add(HotkeyKeyNames.ToDisplayName(Key));
            return string.Join(" + ", parts);
        }
    }
}

internal static class HotkeyKeyNames
{
    public static string ToDisplayName(Keys key)
    {
        if (key == Keys.None)
        {
            return AppLocalization.Text("нет", "None");
        }

        var value = (int)key;

        if (value >= (int)Keys.A && value <= (int)Keys.Z)
        {
            return key.ToString();
        }

        if (value >= (int)Keys.D0 && value <= (int)Keys.D9)
        {
            return (value - (int)Keys.D0).ToString();
        }

        return key switch
        {
            Keys.Space => "Space",
            Keys.Tab => "Tab",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "Page Up",
            Keys.PageDown => "Page Down",
            _ => key.ToString(),
        };
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinWindow",
            "settings.json"
        );

    public static AppSettings Load()
    {
        AppSettings settings;

        try
        {
            if (!File.Exists(SettingsPath))
            {
                settings = AppSettings.CreateDefault();
            }
            else
            {
                var json = File.ReadAllText(SettingsPath);
                settings =
                    JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                    ?? AppSettings.CreateDefault();
            }
        }
        catch
        {
            settings = AppSettings.CreateDefault();
        }

        settings.StartWithWindows = AutostartManager.IsEnabled();
        settings.Normalize();
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalize();

        var directory =
            Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("Не удалось определить папку настроек.");

        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}

internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "PinWindow";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException(
                "Windows не разрешила изменить настройки автозапуска."
            );

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Application.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("Не удалось определить путь к PinWindow.exe.");
        }

        key.SetValue(
            ValueName,
            $"\"{executablePath}\" --autostart",
            RegistryValueKind.String
        );
    }
}
