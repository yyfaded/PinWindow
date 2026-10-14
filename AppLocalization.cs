namespace PinWindow;

// 汉化版：新增简体中文支持，默认中文
internal static class AppLocalization
{
    private static string _language = "zh";

    public static string Language => _language;

    public static bool IsEnglish => _language.Equals("en", StringComparison.OrdinalIgnoreCase);

    public static void SetLanguage(string? language)
    {
        if (language is not null && language.Equals("zh", StringComparison.OrdinalIgnoreCase))
            _language = "zh";
        else if (language is not null && language.Equals("en", StringComparison.OrdinalIgnoreCase))
            _language = "en";
        else
            _language = "ru";
    }

    public static string Text(string russian, string english)
    {
        if (_language.Equals("zh", StringComparison.OrdinalIgnoreCase))
            return TranslateToChinese(english);
        return IsEnglish ? english : russian;
    }

    private static string TranslateToChinese(string english)
    {
        if (ZhTable.TryGetValue(english, out var zh))
            return zh;

        // 带动态内容的句子，按前缀翻译
        const string balloonPrefix = "Double-click the tray icon to open settings. Hotkey: ";
        if (english.StartsWith(balloonPrefix, StringComparison.Ordinal))
            return "双击托盘图标打开设置。快捷键：" + english[balloonPrefix.Length..];

        const string updatePrefix = "Download update v";
        if (english.StartsWith(updatePrefix, StringComparison.Ordinal))
            return "下载更新 v" + english[updatePrefix.Length..];

        const string winErrPrefix = "Windows did not allow the window to be changed: ";
        if (english.StartsWith(winErrPrefix, StringComparison.Ordinal))
            return "Windows 不允许更改该窗口：" + english[winErrPrefix.Length..];

        if (english.StartsWith("Current version: ", StringComparison.Ordinal))
            return english
                .Replace("Current version: ", "当前版本：", StringComparison.Ordinal)
                .Replace("New version: ", "新版本：", StringComparison.Ordinal);

        return english;
    }

    private static readonly Dictionary<string, string> ZhTable = new(StringComparer.Ordinal)
    {
        { "PinWindow is already running. Find its icon in the system tray.", "PinWindow 已在运行。" },
        { "PinWindow Error", "PinWindow 错误" },
        { "Pin/unpin active window", "置顶/取消置顶当前窗口" },
        { "Settings…", "设置…" },
        { "Show button near active window", "启用点击窗口右上角控件置顶" },
        { "Trigger mouse button:", "触发置顶鼠标按键：" },
        { "Right button (Recommended)", "鼠标右键 (推荐)" },
        { "Middle button (Wheel click)", "鼠标中键 (滚轮按下)" },
        { "Side button 1 (Back)", "鼠标侧键 1 (后退 / XButton 1)" },
        { "Side button 2 (Forward)", "鼠标侧键 2 (前进 / XButton 2)" },
        { "Any button (Right/Middle/Side)", "任意按键 (右键/中键/侧键)" },
        { "Global keyboard shortcut:", "键盘全局快捷键：" },
        { "Show icon in system tray", "在系统托盘(任务栏隐藏图标)中显示图标" },
        { "Tip: If tray icon is hidden, run this program again to open settings.", "提示：关闭托盘图标后，再次双击运行本程序可打开设置窗口。" },
        { "None", "无" },
        { "Exit", "退出" },
        { "PinWindow is running", "PinWindow 正在运行" },
        { "Failed to open update page", "无法打开更新页面" },
        { "Settings saved.", "设置已保存。" },
        { "PinWindow — always on top", "PinWindow — 窗口置顶" },
        { "Hotkey", "快捷键" },
        { "Shortcuts & Mouse Button", "快捷键与鼠标按键" },
        { "Failed to change window", "无法更改窗口" },
        { "Unpin window", "取消置顶" },
        { "Keep window always on top", "保持窗口置顶" },
        { "The desktop and taskbar cannot be pinned.", "桌面和任务栏不能置顶。" },
        { "Unpinned", "已取消置顶" },
        { "Pinned on top", "已置顶" },
        { "Failed to unpin window.", "取消置顶失败。" },
        { "Failed to unpin the previous window.", "取消上一个窗口的置顶失败。" },
        { "Window unpinned, but the window order could not be restored.", "已取消置顶，但未能恢复窗口前后顺序。" },
        { "Previous window unpinned, but the window order could not be restored.", "已取消上一个窗口的置顶，但未能恢复窗口前后顺序。" },
        { "Window not found.", "找不到窗口。" },
        { "PinWindow Settings", "PinWindow 设置" },
        { "Settings are saved to %AppData%\\PinWindow\\settings.json", "设置保存在 %AppData%\\PinWindow\\settings.json" },
        { "Keyboard shortcut", "快捷键" },
        { "Pin appearance", "图钉外观" },
        { "Button size", "按钮大小" },
        { "Horizontal offset", "水平偏移" },
        { "Vertical offset", "垂直偏移" },
        { "Pinned icon color", "置顶时图标颜色" },
        { "Behavior", "行为" },
        { "Show notifications", "显示通知" },
        { "Run PinWindow with Windows", "开机时自动启动 PinWindow" },
        { "Bug report", "报告问题" },
        { "Save", "保存" },
        { "Cancel", "取消" },
        { "Defaults", "恢复默认" },
        { "Select at least one modifier: Ctrl, Alt, Shift, or Win.", "请至少选择一个组合键：Ctrl、Alt、Shift 或 Win。" },
        { "Select a key.", "请选择一个按键。" },
        { "Failed to save settings", "保存设置失败" },
        { "PinWindow Update", "PinWindow 更新" },
        { "A new version is available", "发现新版本" },
        { "A PinWindow update is available. Installing the latest version is recommended.", "发现 PinWindow 新版本，建议安装最新版本。" },
        { "Download", "下载" },
        { "Later", "稍后" },
    };
}
