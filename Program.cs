using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PinWindow;

internal static class Program
{
    private const string MutexName =
        "PinWindow.SingleInstance.1499A0DA-7CD9-43C8-88E1-67E4DBCEAD43";

    // 汉化版改进：用命名事件替代不可靠的 HWND_BROADCAST 广播，
    // 第二实例 Set 事件，第一实例定时器收到后弹设置窗口
    internal const string ShowSettingsEventName =
        @"Local\PinWindow.ShowSettings.1499A0DA-7CD9-43C8-88E1-67E4DBCEAD43";

    internal static readonly uint WmShowSettings =
        NativeMethods.RegisterWindowMessage("PinWindow.ShowSettings.Message");

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();

            // 汉化版改进：开机自启动时带 --autostart 参数（静默后台，不弹设置）；
            // 用户手动双击启动时无参数，启动后自动打开设置窗口
            var isAutostart = args.Any(
                a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)
            );

            using var mutex = new Mutex(
                initiallyOwned: true,
                name: MutexName,
                createdNew: out var isFirstInstance
            );

            if (!isFirstInstance)
            {
                // 第二实例：触发第一实例弹设置窗口（命名事件，比广播消息可靠）
                try
                {
                    using var showSettingsEvent = EventWaitHandle.OpenExisting(
                        ShowSettingsEventName
                    );

                    showSettingsEvent.Set();
                }
                catch
                {
                }

                return;
            }

            var context = new TrayApplicationContext();

            if (!isAutostart)
            {
                // 汉化版改进：等消息循环启动后再弹设置窗口。
                // 在 Application.Run 之前直接 Show 窗口不稳定（时弹时不弹），
                // 所以用定时器延迟到消息循环运行中再打开。
                var startupTimer = new System.Windows.Forms.Timer { Interval = 300 };
                startupTimer.Tick += (_, _) =>
                {
                    startupTimer.Stop();
                    startupTimer.Dispose();
                    context.ShowSettingsWindow();
                };
                startupTimer.Start();
            }

            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                AppLocalization.Text("Ошибка PinWindow", "PinWindow Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly PinController _pinController;
    private readonly ToolStripMenuItem _toggleWindowItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _hotkeyLabel;
    private readonly ToolStripMenuItem _exitItem;
    private readonly System.Windows.Forms.Timer _updateCheckTimer;
    private readonly System.Windows.Forms.Timer _settingsEventTimer;
    private readonly System.Windows.Forms.Timer _pinMaintainTimer;
    private readonly EventWaitHandle _showSettingsEvent;

    private string? _latestReleaseUrl;
    private string? _latestVersionText;

    private AppSettings _settings;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();
        AppLocalization.SetLanguage(_settings.Language);

        var menu = new ContextMenuStrip();

        _toggleWindowItem = new ToolStripMenuItem(
            AppLocalization.Text("Закрепить/открепить активное окно", "Pin/unpin active window"),
            null,
            (_, _) => ToggleForegroundWindow()
        );

        _settingsItem = new ToolStripMenuItem(
            AppLocalization.Text("Настройки…", "Settings…"),
            null,
            (_, _) => OpenSettings()
        );

        _updateItem = new ToolStripMenuItem { Visible = false };
        _updateItem.Click += (_, _) => OpenLatestRelease();

        _hotkeyLabel = new ToolStripMenuItem { Enabled = false };

        _exitItem = new ToolStripMenuItem(
            AppLocalization.Text("Выход", "Exit"),
            null,
            (_, _) => ExitThread()
        );
        menu.Items.Add(_toggleWindowItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_hotkeyLabel);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _appIcon = LoadAppIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = AppLocalization.Text(
                "PinWindow — закрепление окон",
                "PinWindow — always on top"
            ),
            ContextMenuStrip = menu,
            Visible = _settings.ShowTrayIcon,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        // 汉化版改进：创建命名事件（供第二实例触发"打开设置"），
        // UI 线程定时器检查信号后直接弹设置窗口，避免后台线程/广播消息不可靠
        _showSettingsEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            Program.ShowSettingsEventName
        );

        _settingsEventTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _settingsEventTimer.Tick += (_, _) =>
        {
            try
            {
                if (_showSettingsEvent.WaitOne(0))
                {
                    OpenSettings();
                }
            }
            catch
            {
            }
        };
        _settingsEventTimer.Start();

        // 汉化版改进：每 500ms 把置顶窗口重新保持到最顶，
        // 防止豆包等应用最大化/激活时临时置顶盖住置顶窗口
        _pinMaintainTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _pinMaintainTimer.Tick += (_, _) => WindowPinning.MaintainPinnedTop();
        _pinMaintainTimer.Start();

        _updateCheckTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _updateCheckTimer.Tick += CheckForUpdates;
        _updateCheckTimer.Start();

        _pinController = new PinController(_settings, ShowResult);

        _hotkeyWindow = new HotkeyWindow(ToggleForegroundWindow, OpenSettings, _settings.GetHotkey());

        UpdateMenuFromSettings();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            return extractedIcon is not null
                ? (Icon)extractedIcon.Clone()
                : (Icon)SystemIcons.Application.Clone();
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    private void ToggleForegroundWindow()
    {
        var window = NativeMethods.GetForegroundWindow();
        var result = WindowPinning.ToggleWindow(window);

        _pinController.RefreshNow();
        ShowResult(result);
    }

    private async void CheckForUpdates(object? sender, EventArgs eventArgs)
    {
        _updateCheckTimer.Stop();

        var update = await UpdateChecker.CheckAsync();

        if (update is null)
        {
            return;
        }

        _latestReleaseUrl = update.ReleaseUrl;

        var versionText =
            update.Version.Build >= 0
                ? $"{update.Version.Major}.{update.Version.Minor}.{update.Version.Build}"
                : $"{update.Version.Major}.{update.Version.Minor}";
        _latestVersionText = versionText;

        _updateItem.Text = AppLocalization.Text(
            $"Скачать обновление v{versionText}",
            $"Download update v{versionText}"
        );

        _updateItem.Visible = true;

        var currentVersion = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);

        using var updateForm = new UpdateAvailableForm(
            currentVersion,
            update.Version,
            OpenLatestRelease
        );

        updateForm.ShowDialog();
    }

    private void OpenLatestRelease()
    {
        if (string.IsNullOrWhiteSpace(_latestReleaseUrl))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _latestReleaseUrl,
                    UseShellExecute = true,
                }
            );
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                AppLocalization.Text(
                    "Не удалось открыть страницу обновления",
                    "Failed to open update page"
                ),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Activate();
            _settingsForm.BringToFront();
            return;
        }

        _settingsForm = new SettingsForm(_settings, ApplySettings);

        _settingsForm.FormClosed += (_, _) => _settingsForm = null;

        _settingsForm.Show();
        _settingsForm.Activate();
    }

    // 汉化版改进：手动双击启动时调用，弹出设置窗口
    public void ShowSettingsWindow() => OpenSettings();

    // 汉化版改进：供后台线程发送"打开设置"消息用的窗口句柄
    public IntPtr HotkeyWindowHandle => _hotkeyWindow.Handle;

    private string? ApplySettings(AppSettings requestedSettings)
    {
        requestedSettings.Normalize();

        var previousSettings = _settings.Clone();

        try
        {
            _hotkeyWindow.UpdateHotkey(requestedSettings.GetHotkey());

            AutostartManager.SetEnabled(requestedSettings.StartWithWindows);

            SettingsStore.Save(requestedSettings);
            _settings = requestedSettings.Clone();
            _notifyIcon.Visible = _settings.ShowTrayIcon;
            AppLocalization.SetLanguage(_settings.Language);
            _pinController.ApplySettings(_settings);
            UpdateMenuFromSettings();

            return null;
        }
        catch (Exception exception)
        {
            try
            {
                _hotkeyWindow.UpdateHotkey(previousSettings.GetHotkey());

                AutostartManager.SetEnabled(previousSettings.StartWithWindows);
            }
            catch
            {
                // Не скрываем первоначальную ошибку отката.
            }

            return exception.Message;
        }
    }

    private void UpdateMenuFromSettings()
    {
        _toggleWindowItem.Text = AppLocalization.Text(
            "Закрепить/открепить активное окно",
            "Pin/unpin active window"
        );

        _settingsItem.Text = AppLocalization.Text("Настройки…", "Settings…");

        if (!string.IsNullOrWhiteSpace(_latestVersionText))
        {
            _updateItem.Text = AppLocalization.Text(
                $"Скачать обновление v{_latestVersionText}",
                $"Download update v{_latestVersionText}"
            );
        }

        _exitItem.Text = AppLocalization.Text("Выход", "Exit");

        _notifyIcon.Text = AppLocalization.Text(
            "PinWindow — закрепление окон",
            "PinWindow — always on top"
        );
        _hotkeyLabel.Text =
            $"{AppLocalization.Text("Горячая клавиша", "Hotkey")}: "
            + _settings.GetHotkey().DisplayText;
    }

    private void ShowResult(ToggleResult result)
    {
        // 汉化版：通知已移除，置顶/取消结果不再弹气泡
    }

    protected override void OnMainFormClosed(object? sender, EventArgs e)
    {
        // 阻止对话框关闭导致托盘退出
    }

    protected override void ExitThreadCore()
    {
        _updateCheckTimer.Stop();
        _updateCheckTimer.Dispose();

        _settingsEventTimer.Stop();
        _settingsEventTimer.Dispose();

        _pinMaintainTimer.Stop();
        _pinMaintainTimer.Dispose();

        _showSettingsEvent.Dispose();
        _settingsForm?.Close();
        _pinController.Dispose();
        _hotkeyWindow.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();

        base.ExitThreadCore();
    }
}

internal sealed class PinController : IDisposable
{
    private readonly Action<ToggleResult> _showResult;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly System.Windows.Forms.Timer _toggleTimer;
    private readonly Queue<IntPtr> _pendingToggles = new();
    private AppSettings _settings;
    private IntPtr _hook = IntPtr.Zero;
    private bool _enabled;
    private bool _disposed;
    private IntPtr _lastInterceptedWindow = IntPtr.Zero;
    private int _lastInterceptedButtonUp;
    private uint _lastInterceptedXButton;

    public PinController(AppSettings settings, Action<ToggleResult> showResult)
    {
        _settings = settings.Clone();
        _showResult = showResult;
        _enabled = true; // 汉化版：右键置顶功能永远启用
        _mouseProc = HookCallback;
        _toggleTimer = new System.Windows.Forms.Timer { Interval = 1 };
        _toggleTimer.Tick += ProcessPendingToggles;

        if (_enabled)
        {
            InstallHook();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            if (_enabled)
            {
                InstallHook();
            }
            else
            {
                UninstallHook();
            }
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        Enabled = true; // 汉化版：右键置顶功能永远启用
    }

    public void RefreshNow()
    {
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero || _disposed)
        {
            return;
        }

        var hMod = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseProc,
            hMod,
            0
        );
    }

    private void UninstallHook()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);

                if (IsTriggerButtonDown(msg, hookStruct.MouseData))
                {
                    if (TryHandleMouseClick(hookStruct.Pt, msg, hookStruct.MouseData))
                    {
                        return (IntPtr)1; // 拦截按键，防止产生默认动作或菜单
                    }
                }
                else if (IsInterceptedButtonUp(msg, hookStruct.MouseData))
                {
                    var window = _lastInterceptedWindow;
                    _lastInterceptedWindow = IntPtr.Zero;
                    _pendingToggles.Enqueue(window);
                    _toggleTimer.Start();
                    return (IntPtr)1; // 拦截成对的抬起事件
                }
            }
        }
        catch
        {
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsTriggerButtonDown(int msg, uint mouseData)
    {
        var buttonSetting = _settings.MouseTriggerButton;
        return buttonSetting switch
        {
            0 => msg == NativeMethods.WmRButtonDown, // 鼠标右键 (推荐)
            1 => msg == NativeMethods.WmMButtonDown, // 鼠标中键 (滚轮按下)
            2 => msg == NativeMethods.WmXButtonDown && (mouseData >> 16) == 1, // 侧键1 (后退)
            3 => msg == NativeMethods.WmXButtonDown && (mouseData >> 16) == 2, // 侧键2 (前进)
            4 => msg is NativeMethods.WmRButtonDown or NativeMethods.WmMButtonDown or NativeMethods.WmXButtonDown, // 任意按键
            _ => msg == NativeMethods.WmRButtonDown,
        };
    }

    private bool IsInterceptedButtonUp(int msg, uint mouseData) =>
        _lastInterceptedWindow != IntPtr.Zero
        && msg == _lastInterceptedButtonUp
        && (msg != NativeMethods.WmXButtonUp || (mouseData >> 16) == _lastInterceptedXButton);

    private void ProcessPendingToggles(object? sender, EventArgs eventArgs)
    {
        _toggleTimer.Stop();

        while (_pendingToggles.Count > 0)
        {
            var window = _pendingToggles.Dequeue();
            try
            {
                _showResult(WindowPinning.ToggleWindow(window));
            }
            catch (Exception exception)
            {
                _showResult(ToggleResult.Error(exception.Message));
            }
        }
    }

    private bool TryHandleMouseClick(NativeMethods.Point pt, int msg, uint mouseData)
    {
        try
        {
            var targetHwnd = NativeMethods.WindowFromPoint(pt);
            if (targetHwnd == IntPtr.Zero)
            {
                return false;
            }

            var rootWindow = NativeMethods.GetAncestor(targetHwnd, NativeMethods.GaRoot);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = targetHwnd;
            }

            if (!WindowFiltering.IsEligible(rootWindow))
            {
                return false;
            }

            if (IsHitOnCaptionButtons(rootWindow, targetHwnd, pt))
            {
                _lastInterceptedWindow = rootWindow;
                _lastInterceptedButtonUp = msg switch
                {
                    NativeMethods.WmMButtonDown => NativeMethods.WmMButtonUp,
                    NativeMethods.WmXButtonDown => NativeMethods.WmXButtonUp,
                    _ => NativeMethods.WmRButtonUp,
                };
                _lastInterceptedXButton = msg == NativeMethods.WmXButtonDown
                    ? mouseData >> 16
                    : 0;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsHitOnCaptionButtons(IntPtr window, IntPtr targetHwnd, NativeMethods.Point pt)
    {
        try
        {
            // 1. 优先使用 WM_NCHITTEST 进行标准非客户区三按钮命中判定（关闭、最大化/还原、最小化）
            var lParam = unchecked((IntPtr)(uint)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF)));
            if (NativeMethods.SendMessageTimeout(
                    window,
                    NativeMethods.WmNcHitTest,
                    IntPtr.Zero,
                    lParam,
                    NativeMethods.SmtoAbortIfHung,
                    35,
                    out var hitTestResult
                ) != IntPtr.Zero)
            {
                var hitTest = hitTestResult.ToInt32();
                if (hitTest is NativeMethods.HtClose or NativeMethods.HtMaxButton or NativeMethods.HtMinButton)
                {
                    return true;
                }
            }

            if (targetHwnd != IntPtr.Zero && targetHwnd != window)
            {
                if (NativeMethods.SendMessageTimeout(
                        targetHwnd,
                        NativeMethods.WmNcHitTest,
                        IntPtr.Zero,
                        lParam,
                        NativeMethods.SmtoAbortIfHung,
                        35,
                        out var childHitResult
                    ) != IntPtr.Zero)
                {
                    var childHit = childHitResult.ToInt32();
                    if (childHit is NativeMethods.HtClose or NativeMethods.HtMaxButton or NativeMethods.HtMinButton)
                    {
                        return true;
                    }
                }
            }

            // 2. 自定义自绘标题栏窗口的几何坐标命中判定（适用于 Chrome、Edge、VS Code、微信、QQ 等现代无边框自绘窗口）
            var dpi = NativeMethods.GetDpiForWindow(window);
            var scale = (dpi == 0 ? 96 : dpi) / 96f;

            int visibleRight;
            int visibleTop;
            int buttonsHeight;

            if (NativeMethods.IsZoomed(window))
            {
                // 最大化状态：取所在显示器边界
                var hMonitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
                var monitorInfo = new NativeMethods.MonitorInfo();
                monitorInfo.CbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>();

                if (hMonitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    visibleRight = monitorInfo.RcMonitor.Right;
                    visibleTop = monitorInfo.RcMonitor.Top;
                }
                else if (NativeMethods.GetWindowRect(window, out var maxRect))
                {
                    visibleRight = maxRect.Right - (int)Math.Round(8 * scale);
                    visibleTop = maxRect.Top + (int)Math.Round(8 * scale);
                }
                else
                {
                    return false;
                }

                buttonsHeight = (int)Math.Round(45 * scale);
            }
            else
            {
                // 汉化版修复：直接使用窗口矩形定位右上角按钮区域。
                // 不能优先用 DWM 扩展边框——Chrome/Electron 等自绘标题栏窗口
                // 返回的扩展边框坐标是错误值（实测窗口顶 125px，DWM 却报 156px），
                // 会导致右键命中区域整体偏移，出现"右键无反应"。
                if (NativeMethods.GetWindowRect(window, out var winRect))
                {
                    visibleRight = winRect.Right - (int)Math.Round(7 * scale);
                    visibleTop = winRect.Top;
                }
                else
                {
                    return false;
                }

                buttonsHeight = (int)Math.Round(40 * scale);
            }

            // 右上角系统控制三按钮区域总宽度（约 155 * scale）
            var buttonsWidth = (int)Math.Round(155 * scale);

            if (pt.X >= visibleRight - buttonsWidth && pt.X <= visibleRight + 5 &&
                pt.Y >= visibleTop - 5 && pt.Y <= visibleTop + buttonsHeight)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UninstallHook();
        _toggleTimer.Stop();
        _toggleTimer.Dispose();
        _pendingToggles.Clear();
        GC.SuppressFinalize(this);
    }
}

internal static class WindowFiltering
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const long WsCaption = 0x00C00000L;
    private const long WsExToolWindow = 0x00000080L;

    private const int DwmwaCloaked = 14;

    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "DV2ControlHost",
        "MultitaskingViewFrame",
    };

    public static bool IsEligible(IntPtr window)
    {
        if (
            window == IntPtr.Zero
            || !NativeMethods.IsWindow(window)
            || !NativeMethods.IsWindowVisible(window)
            || NativeMethods.IsIconic(window)
        )
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);

        if (processId == CurrentProcessId)
        {
            return false;
        }

        if (IsCloaked(window))
        {
            return false;
        }

        var className = WindowText.GetClassName(window);

        if (IgnoredClasses.Contains(className))
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(window, GwlExStyle).ToInt64();

        if ((extendedStyle & WsExToolWindow) != 0)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(window, out var rectangle))
        {
            return false;
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;

        return width >= 160 && height >= 60;
    }

    private static bool IsCloaked(IntPtr window)
    {
        int cloaked;

        var result = NativeMethods.DwmGetWindowAttributeInt(
            window,
            DwmwaCloaked,
            out cloaked,
            sizeof(int)
        );

        return result == 0 && cloaked != 0;
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly Action _onHotkey;
    private readonly Action _onShowSettings;
    private HotkeyDefinition _definition;
    private bool _disposed;

    public HotkeyWindow(Action onHotkey, Action onShowSettings, HotkeyDefinition definition)
    {
        _onHotkey = onHotkey;
        _onShowSettings = onShowSettings;
        CreateHandle(new CreateParams());

        NativeMethods.ChangeWindowMessageFilter(Program.WmShowSettings, NativeMethods.MsgfltAdd);

        RegisterOrThrow(definition);
        _definition = definition;
    }

    public void UpdateHotkey(HotkeyDefinition definition)
    {
        if (_definition == definition)
        {
            return;
        }

        var previous = _definition;
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);

        try
        {
            RegisterOrThrow(definition);
            _definition = definition;
        }
        catch
        {
            try
            {
                RegisterOrThrow(previous);
                _definition = previous;
            }
            catch
            {
                // Первоначальная ошибка важнее ошибки восстановления.
            }

            throw;
        }
    }

    private void RegisterOrThrow(HotkeyDefinition definition)
    {
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);

        if (!definition.IsConfigured)
        {
            return; // 快捷键为“无”，无需注册系统热键
        }

        var registered = NativeMethods.RegisterHotKey(
            Handle,
            HotkeyId,
            definition.NativeModifiers | ModNoRepeat,
            (uint)definition.Key
        );

        if (registered)
        {
            return;
        }

        var error = new Win32Exception(Marshal.GetLastWin32Error());

        throw new InvalidOperationException(
            $"Не удалось зарегистрировать {definition.DisplayText}. "
                + "Возможно, это сочетание уже занято другой программой.\n\n"
                + error.Message,
            error
        );
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            _onHotkey();
        }
        else if (message.Msg == (int)Program.WmShowSettings)
        {
            _onShowSettings();
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}

internal static class WindowPinning
{
    private static readonly IntPtr HwndTopmost = new(-1);

    private static readonly IntPtr HwndNotTopmost = new(-2);

    // 汉化版改进：单置顶 —— 记录当前置顶的窗口
    private static IntPtr _pinnedWindow = IntPtr.Zero;

    // 汉化版改进：置顶窗口保持最顶（防其他窗口临时置顶盖住它）
    public static void MaintainPinnedTop()
    {
        try
        {
            if (
                _pinnedWindow != IntPtr.Zero
                && NativeMethods.IsWindow(_pinnedWindow)
                && IsTopMost(_pinnedWindow)
            )
            {
                NativeMethods.SetWindowPos(
                    _pinnedWindow,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate
                );
            }
        }
        catch
        {
        }
    }

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;

    public static ToggleResult ToggleWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return ToggleResult.Error(
                AppLocalization.Text("Окно не найдено.", "Window not found.")
            );
        }

        var className = WindowText.GetClassName(window);

        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return ToggleResult.Error(
                AppLocalization.Text(
                    "Рабочий стол и панель задач закреплять нельзя.",
                    "The desktop and taskbar cannot be pinned."
                )
            );
        }

        var wasTopMost = IsTopMost(window);

        if (wasTopMost)
        {
            if (!SetWindowTopMost(window, topMost: false) || IsTopMost(window))
            {
                return ToggleResult.Error(
                    AppLocalization.Text("Не удалось открепить окно.", "Failed to unpin window.")
                );
            }

            if (_pinnedWindow == window)
            {
                _pinnedWindow = IntPtr.Zero;
            }

            if (!PlaceBehindCurrentForeground(window))
            {
                return ToggleResult.Error(
                    AppLocalization.Text(
                        "Окно откреплено, но не удалось восстановить порядок окон.",
                        "Window unpinned, but the window order could not be restored."
                    )
                );
            }

            var title = WindowText.GetWindowTitle(window);

            return ToggleResult.Ok(
                $"{AppLocalization.Text("Откреплено", "Unpinned")}: {title}"
            );
        }

        // 取消上一个置顶窗口（如果还活着且确实处于置顶状态）
        if (
            _pinnedWindow != IntPtr.Zero
            && _pinnedWindow != window
            && NativeMethods.IsWindow(_pinnedWindow)
            && IsTopMost(_pinnedWindow)
        )
        {
            if (!SetWindowTopMost(_pinnedWindow, topMost: false) || IsTopMost(_pinnedWindow))
            {
                return ToggleResult.Error(
                    AppLocalization.Text("Не удалось открепить предыдущее окно.", "Failed to unpin the previous window.")
                );
            }

            var previousWindow = _pinnedWindow;
            _pinnedWindow = IntPtr.Zero;
            if (!PlaceBehindCurrentForeground(previousWindow))
            {
                return ToggleResult.Error(
                    AppLocalization.Text(
                        "Предыдущее окно откреплено, но не удалось восстановить порядок окон.",
                        "Previous window unpinned, but the window order could not be restored."
                    )
                );
            }
        }

        var changed = SetWindowTopMost(window, topMost: true);

        if (!changed)
        {
            var win32Error = Marshal.GetLastWin32Error();
            var error = new Win32Exception(win32Error);

            var errorMessage = win32Error == 5
                ? AppLocalization.Text(
                    "Отказ в доступе. Запустите PinWindow от имени администратора для управления этим окном.",
                    "Access denied. Run PinWindow as Administrator to control this window."
                )
                : AppLocalization.Text(
                    $"Windows не разрешила изменить окно: {error.Message}",
                    $"Windows did not allow the window to be changed: {error.Message}"
                );

            return ToggleResult.Error(errorMessage);
        }

        _pinnedWindow = window;

        var titlePinned = WindowText.GetWindowTitle(window);

        return ToggleResult.Ok(
            $"{AppLocalization.Text("Закреплено поверх окон", "Pinned on top")}: {titlePinned}"
        );
    }

    private static bool PlaceBehindCurrentForeground(IntPtr window)
    {
        // 右键点击已被鼠标钩子拦截，目标窗口不会因此成为前台窗口。
        // HWND_NOTOPMOST 却会把它放到普通窗口层的顶部。如果另一个普通窗口
        // 仍是前台窗口，用户再点它可能不会发生新的激活，也就不会纠正层级。
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == window)
        {
            return true;
        }

        var root = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        if (root != IntPtr.Zero)
        {
            foreground = root;
        }

        if (
            foreground == window
            || !WindowFiltering.IsEligible(foreground)
            || IsTopMost(foreground)
            || NativeMethods.GetWindow(foreground, NativeMethods.GwOwner) == window
            || NativeMethods.GetWindow(window, NativeMethods.GwOwner) == foreground
        )
        {
            return true;
        }

        // 只参照此刻有效的普通前台窗口；不用置顶前保存的旧 HWND。
        return NativeMethods.SetWindowPos(
            window,
            foreground,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate
        ) && !IsTopMost(window);
    }

    // 汉化版改进：置顶/取消置顶单个窗口
    private static bool SetWindowTopMost(IntPtr window, bool topMost)
    {
        return NativeMethods.SetWindowPos(
            window,
            topMost ? HwndTopmost : HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate
        );
    }

    public static bool IsTopMost(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(window, GwlExStyle).ToInt64();

        return (extendedStyle & WsExTopmost) != 0;
    }
}

internal static class WindowText
{
    public static string GetWindowTitle(IntPtr window)
    {
        var length = NativeMethods.GetWindowTextLength(window);

        if (length <= 0)
        {
            var className = GetClassName(window);

            return string.IsNullOrWhiteSpace(className) ? "окно без названия" : className;
        }

        var buffer = new StringBuilder(length + 1);

        NativeMethods.GetWindowText(window, buffer, buffer.Capacity);

        return buffer.ToString();
    }

    public static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);

        NativeMethods.GetClassName(window, buffer, buffer.Capacity);

        return buffer.ToString();
    }
}

internal readonly record struct ToggleResult(bool Success, string Message)
{
    public static ToggleResult Ok(string message) => new(true, message);

    public static ToggleResult Error(string message) => new(false, message);
}

internal static class NativeMethods
{
    internal static readonly IntPtr HwndTop = IntPtr.Zero;
    internal static readonly IntPtr HwndTopmost = new(-1);
    internal static readonly IntPtr HwndNotTopmost = new(-2);

    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint GwHwndNext = 2;
    internal const uint GwHwndPrev = 3;
    internal const uint GwOwner = 4;

    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventSystemMoveSizeStart = 0x000A;
    internal const uint EventSystemMoveSizeEnd = 0x000B;
    internal const uint EventSystemMinimizeStart = 0x0016;
    internal const uint EventSystemMinimizeEnd = 0x0017;

    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectLocationChange = 0x800B;

    internal const int ObjIdWindow = 0;

    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;

    internal const byte AcSrcOver = 0x00;
    internal const byte AcSrcAlpha = 0x01;
    internal const uint UlwAlpha = 0x00000002;

    internal const int WhMouseLl = 14;
    internal const int WmRButtonDown = 0x0204;
    internal const int WmRButtonUp = 0x0205;
    internal const int WmMButtonDown = 0x0207;
    internal const int WmMButtonUp = 0x0208;
    internal const int WmXButtonDown = 0x020B;
    internal const int WmXButtonUp = 0x020C;
    internal const uint WmNcHitTest = 0x0084;
    internal const int HtMinButton = 8;
    internal const int HtMaxButton = 9;
    internal const int HtClose = 20;
    internal const uint GaRoot = 2;
    internal const int DwmwaCaptionButtonBounds = 5;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int HwndBroadcast = 0xFFFF;
    internal const uint MsgfltAdd = 1;
    internal const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MsllHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    internal const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult
    );

    internal delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Width;
        public int Height;

        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        IntPtr window,
        int id,
        uint modifiers,
        uint virtualKey
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(
        IntPtr window,
        StringBuilder className,
        int maximumCount
    );

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttributeRect(
        IntPtr window,
        int attribute,
        out Rect value,
        int valueSize
    );

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttributeInt(
        IntPtr window,
        int attribute,
        out int value,
        int valueSize
    );

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDeviceContext,
        ref Point destinationPoint,
        ref Size windowSize,
        IntPtr sourceDeviceContext,
        ref Point sourcePoint,
        int colorKey,
        ref BlendFunction blendFunction,
        uint flags
    );

    internal static IntPtr GetWindowLongPtr(IntPtr window, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));
    }

    private const int GwlHwndParent = -8;

    internal static IntPtr SetWindowOwner(IntPtr window, IntPtr owner)
    {
        return SetWindowLongPtr(window, GwlHwndParent, owner);
    }

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, newValue)
            : new IntPtr(SetWindowLong32(window, index, newValue.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int newValue);
}
