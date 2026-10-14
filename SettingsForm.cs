using System.Drawing;
using System.Windows.Forms;

namespace PinWindow;

internal sealed class SettingsForm : Form
{
    // 汉化版：项目主页与问题反馈，跳转到用户自己的 GitHub
    private const string ProjectUrl = "https://github.com/yyfaded/PinWindow";

    private const string IssueUrl = ProjectUrl + "/issues/new";

    private readonly Func<AppSettings, string?> _applySettings;

    private readonly ComboBox _mouseTriggerButtonComboBox;
    private readonly CheckBox _controlCheckBox;
    private readonly CheckBox _altCheckBox;
    private readonly CheckBox _shiftCheckBox;
    private readonly CheckBox _winCheckBox;
    private readonly ComboBox _keyComboBox;

    private readonly CheckBox _showTrayIconCheckBox;
    private readonly CheckBox _autostartCheckBox;
    private readonly ComboBox _languageComboBox;

    public SettingsForm(AppSettings settings, Func<AppSettings, string?> applySettings)
    {
        _applySettings = applySettings;

        Text = AppLocalization.Text("Настройки PinWindow", "PinWindow Settings");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 520);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = false,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var heading = new Label
        {
            Text = AppLocalization.Text("Настройки PinWindow", "PinWindow Settings"),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        root.Controls.Add(heading);

        var subtitle = new Label
        {
            Text = AppLocalization.Text(
                "Изменения сохраняются в %AppData%\\PinWindow\\settings.json",
                "Settings are saved to %AppData%\\PinWindow\\settings.json"
            ),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 14),
        };
        root.Controls.Add(subtitle);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoScroll = true,
            Margin = Padding.Empty,
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content);

        // --- 快捷键与鼠标按键设置分组 ---
        var triggerGroup = new GroupBox
        {
            Text = AppLocalization.Text("Сочетание клавиш и мышь", "Shortcuts & Mouse Button"),
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 12),
        };
        content.Controls.Add(triggerGroup);

        var triggerLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        triggerGroup.Controls.Add(triggerLayout);

        // 1. 鼠标触发按键行
        var mouseRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
        };
        var mouseLabel = new Label
        {
            Text = AppLocalization.Text("Кнопка мыши:", "Trigger mouse button:"),
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0),
        };
        _mouseTriggerButtonComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230,
            Margin = new Padding(0, 2, 0, 2),
        };
        _mouseTriggerButtonComboBox.Items.Add(AppLocalization.Text("Правая кнопка мыши (рекомендуется)", "Right button (Recommended)"));
        _mouseTriggerButtonComboBox.Items.Add(AppLocalization.Text("Средняя кнопка мыши (колесико)", "Middle button (Wheel click)"));
        _mouseTriggerButtonComboBox.Items.Add(AppLocalization.Text("Боковая кнопка 1 (Назад)", "Side button 1 (Back)"));
        _mouseTriggerButtonComboBox.Items.Add(AppLocalization.Text("Боковая кнопка 2 (Вперед)", "Side button 2 (Forward)"));
        _mouseTriggerButtonComboBox.Items.Add(AppLocalization.Text("Любая кнопка (Правая/Средняя/Боковая)", "Any button (Right/Middle/Side)"));
        _mouseTriggerButtonComboBox.SelectedIndex = Math.Clamp(settings.MouseTriggerButton, 0, 4);

        mouseRow.Controls.Add(mouseLabel);
        mouseRow.Controls.Add(_mouseTriggerButtonComboBox);
        triggerLayout.Controls.Add(mouseRow);

        // 2. 全局键盘快捷键行
        var hotkeyLabel = new Label
        {
            Text = AppLocalization.Text("Глобальная горячая клавиша:", "Global keyboard shortcut:"),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        };
        triggerLayout.Controls.Add(hotkeyLabel);

        var hotkeyLayout = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        _controlCheckBox = CreateModifierCheckBox("Ctrl", settings.HotkeyControl);
        _altCheckBox = CreateModifierCheckBox("Alt", settings.HotkeyAlt);
        _shiftCheckBox = CreateModifierCheckBox("Shift", settings.HotkeyShift);
        _winCheckBox = CreateModifierCheckBox("Win", settings.HotkeyWin);

        hotkeyLayout.Controls.Add(_controlCheckBox);
        hotkeyLayout.Controls.Add(_altCheckBox);
        hotkeyLayout.Controls.Add(_shiftCheckBox);
        hotkeyLayout.Controls.Add(_winCheckBox);

        _keyComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
            Margin = new Padding(8, 3, 0, 3),
        };
        foreach (var option in CreateKeyOptions())
        {
            _keyComboBox.Items.Add(option);
        }
        var selectedKeyIndex = FindKeyIndex((Keys)settings.HotkeyKey);
        _keyComboBox.SelectedIndex =
            selectedKeyIndex >= 0 ? selectedKeyIndex : FindKeyIndex(Keys.None);

        hotkeyLayout.Controls.Add(_keyComboBox);
        triggerLayout.Controls.Add(hotkeyLayout);

        // --- 常规行为设置分组 ---
        var behaviorGroup = new GroupBox
        {
            Text = AppLocalization.Text("Поведение", "Behavior"),
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = new Padding(0),
        };
        content.Controls.Add(behaviorGroup);

        var behaviorLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        behaviorGroup.Controls.Add(behaviorLayout);

        var languageLayout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };

        var languageLabel = new Label
        {
            Text = "界面语言：",
            AutoSize = true,
            Margin = new Padding(0, 7, 10, 0),
        };

        _languageComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 125,
            Margin = new Padding(0, 3, 0, 3),
        };

        _languageComboBox.Items.Add("中文(简体)");
        _languageComboBox.Items.Add("English");
        _languageComboBox.Items.Add("Русский");

        _languageComboBox.SelectedIndex = settings.Language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? 0
            : settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 2;

        languageLayout.Controls.Add(languageLabel);
        languageLayout.Controls.Add(_languageComboBox);
        behaviorLayout.Controls.Add(languageLayout);

        _showTrayIconCheckBox = CreateBehaviorCheckBox(
            AppLocalization.Text(
                "Показывать значок в области уведомлений",
                "Show icon in system tray"
            ),
            settings.ShowTrayIcon
        );

        var trayTipLabel = new Label
        {
            Text = AppLocalization.Text(
                "Подсказка: если значок скрыт, запустите программу повторно, чтобы открыть настройки.",
                "Tip: If tray icon is hidden, run this program again to open settings."
            ),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 8.25F),
            Margin = new Padding(20, 0, 0, 8),
        };

        _autostartCheckBox = CreateBehaviorCheckBox(
            AppLocalization.Text(
                "Запускать PinWindow вместе с Windows",
                "Run PinWindow with Windows"
            ),
            settings.StartWithWindows
        );

        behaviorLayout.Controls.Add(_showTrayIconCheckBox);
        behaviorLayout.Controls.Add(trayTipLabel);
        behaviorLayout.Controls.Add(_autostartCheckBox);

        var assemblyVersion = typeof(SettingsForm).Assembly.GetName().Version;
        var versionText = assemblyVersion is null
            ? "未知"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";

        var versionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0),
        };

        var versionLabel = new Label
        {
            Text = $"PinWindow v{versionText}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 3, 0, 0),
        };

        var projectLink = new LinkLabel
        {
            Text = "GitHub",
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(10, 3, 0, 0),
        };

        projectLink.LinkClicked += (_, _) =>
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ProjectUrl,
                    UseShellExecute = true,
                }
            );
        };

        var bugReportLink = new LinkLabel
        {
            Text = AppLocalization.Text("Сообщить об ошибке", "Bug report"),
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(10, 3, 0, 0),
        };

        bugReportLink.LinkClicked += (_, _) =>
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = IssueUrl,
                    UseShellExecute = true,
                }
            );
        };

        versionPanel.Controls.Add(versionLabel);
        versionPanel.Controls.Add(projectLink);
        versionPanel.Controls.Add(bugReportLink);

        root.Controls.Add(versionPanel);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
        };
        root.Controls.Add(buttonPanel);

        var saveButton = new Button
        {
            Text = AppLocalization.Text("Сохранить", "Save"),
            AutoSize = true,
            Padding = new Padding(12, 3, 12, 3),
        };
        saveButton.Click += (_, _) => SaveSettings();

        var cancelButton = new Button
        {
            Text = AppLocalization.Text("Отмена", "Cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Padding = new Padding(12, 3, 12, 3),
        };

        var defaultsButton = new Button
        {
            Text = AppLocalization.Text("По умолчанию", "Defaults"),
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, 16, 0),
        };
        defaultsButton.Click += (_, _) => LoadValues(AppSettings.CreateDefault());

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(defaultsButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static CheckBox CreateModifierCheckBox(string text, bool isChecked) =>
        new()
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Margin = new Padding(0, 6, 12, 3),
        };

    private static CheckBox CreateBehaviorCheckBox(string text, bool isChecked) =>
        new()
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 6),
        };

    private static IReadOnlyList<HotkeyKeyOption> CreateKeyOptions()
    {
        var options = new List<HotkeyKeyOption>
        {
            new(Keys.None),
        };

        for (var key = Keys.A; key <= Keys.Z; key++)
        {
            options.Add(new HotkeyKeyOption(key));
        }

        for (var key = Keys.D0; key <= Keys.D9; key++)
        {
            options.Add(new HotkeyKeyOption(key));
        }

        options.Add(new HotkeyKeyOption(Keys.Space));
        options.Add(new HotkeyKeyOption(Keys.Tab));
        options.Add(new HotkeyKeyOption(Keys.Insert));
        options.Add(new HotkeyKeyOption(Keys.Home));
        options.Add(new HotkeyKeyOption(Keys.End));
        options.Add(new HotkeyKeyOption(Keys.PageUp));
        options.Add(new HotkeyKeyOption(Keys.PageDown));

        return options;
    }

    private int FindKeyIndex(Keys key)
    {
        for (var i = 0; i < _keyComboBox.Items.Count; i++)
        {
            if (_keyComboBox.Items[i] is HotkeyKeyOption option && option.Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    private void LoadValues(AppSettings settings)
    {
        _languageComboBox.SelectedIndex = settings.Language.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase
        )
            ? 0
            : settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 2;

        _mouseTriggerButtonComboBox.SelectedIndex = Math.Clamp(settings.MouseTriggerButton, 0, 4);

        _controlCheckBox.Checked = settings.HotkeyControl;
        _altCheckBox.Checked = settings.HotkeyAlt;
        _shiftCheckBox.Checked = settings.HotkeyShift;
        _winCheckBox.Checked = settings.HotkeyWin;

        var index = FindKeyIndex((Keys)settings.HotkeyKey);
        _keyComboBox.SelectedIndex = index >= 0 ? index : FindKeyIndex(Keys.None);

        _showTrayIconCheckBox.Checked = settings.ShowTrayIcon;
        _autostartCheckBox.Checked = settings.StartWithWindows;
    }

    private void SaveSettings()
    {
        var hasModifier =
            _controlCheckBox.Checked
            || _altCheckBox.Checked
            || _shiftCheckBox.Checked
            || _winCheckBox.Checked;

        var selectedKey = (_keyComboBox.SelectedItem as HotkeyKeyOption)?.Key ?? Keys.None;

        if (hasModifier && selectedKey == Keys.None)
        {
            MessageBox.Show(
                this,
                AppLocalization.Text("Выберите клавишу.", "Select a key."),
                AppLocalization.Text("Горячая клавиша", "Keyboard shortcut"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        if (!hasModifier && selectedKey != Keys.None)
        {
            MessageBox.Show(
                this,
                AppLocalization.Text(
                    "Выберите хотя бы один модификатор: Ctrl, Alt, Shift или Win.",
                    "Select at least one modifier: Ctrl, Alt, Shift, or Win."
                ),
                AppLocalization.Text("Горячая клавиша", "Keyboard shortcut"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        var isHotkeyConfigured = hasModifier && selectedKey != Keys.None;

        var settings = new AppSettings
        {
            Language = _languageComboBox.SelectedIndex switch { 0 => "zh", 1 => "en", _ => "ru" },
            MouseTriggerButton = _mouseTriggerButtonComboBox.SelectedIndex,
            ShowTrayIcon = _showTrayIconCheckBox.Checked,
            StartWithWindows = _autostartCheckBox.Checked,
            PinSize = 27,
            OffsetX = 0,
            OffsetY = 0,
            ActiveColor = "#0078D7",
            HotkeyControl = isHotkeyConfigured && _controlCheckBox.Checked,
            HotkeyAlt = isHotkeyConfigured && _altCheckBox.Checked,
            HotkeyShift = isHotkeyConfigured && _shiftCheckBox.Checked,
            HotkeyWin = isHotkeyConfigured && _winCheckBox.Checked,
            HotkeyKey = isHotkeyConfigured ? (int)selectedKey : (int)Keys.None,
        };

        var error = _applySettings(settings);

        if (error is not null)
        {
            MessageBox.Show(
                this,
                error,
                AppLocalization.Text("Не удалось сохранить настройки", "Failed to save settings"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class HotkeyKeyOption
    {
        public HotkeyKeyOption(Keys key)
        {
            Key = key;
        }

        public Keys Key { get; }

        public override string ToString() => HotkeyKeyNames.ToDisplayName(Key);
    }
}
