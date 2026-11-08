using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;

namespace EasyHFP.UI
{
    public class MainForm : Form
    {
        private readonly IBluetoothService _bluetoothService;
        private readonly IConfigurationService _configService;
        private readonly IAudioProfileController _profileController;
        private readonly bool _ownsProfileController;

        private ConfigData _config;
        private List<BtDeviceItem> _currentDevices = new();
        private BtDeviceItem? _selectedDevice;
        private HeadsetProfileSnapshot? _currentProfileSnapshot;

        private bool _allowVisible;
        private bool _isBusy;
        private bool _isExiting;
        private bool _isDisposing;
        private bool _trayBalloonShownOnce;
        private int _isRefreshing;

        // Tray icon state cache to avoid GDI handle leaks and redundant redrawing
        private (AudioProfileMode profileMode, bool isConnected) _lastTrayIconState = (AudioProfileMode.Unknown, false);
        private bool _trayIconInitialized;

        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private ToolStripMenuItem? _trayToggleMenuItem;
        private HotkeyWindow? _hotkeyWindow;
        private System.Windows.Forms.Timer? _refreshTimer;

        // UI Controls (Minimal Native Windows Utility Layout)
        private Label _deviceLabel = null!;
        private ComboBox _deviceComboBox = null!;
        private Button _refreshButton = null!;

        private Label _connectionCaptionLabel = null!;
        private Label _connectionValueLabel = null!;
        private Label _modeCaptionLabel = null!;
        private Label _modeValueLabel = null!;

        private Button _toggleButton = null!;
        private Label _statusMessageLabel = null!;

        private GroupBox _settingsGroup = null!;
        private CheckBox _autoStartCheck = null!;
        private CheckBox _minToTrayCheck = null!;
        private CheckBox _toastCheck = null!;
        private Label _hotkeyInfoLabel = null!;

        public MainForm(
            IBluetoothService bluetoothService,
            IConfigurationService configService,
            bool startInTray,
            IAudioProfileController? profileController = null)
        {
            _bluetoothService = bluetoothService ?? throw new ArgumentNullException(nameof(bluetoothService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _ownsProfileController = (profileController == null);
            _profileController = profileController ?? new WindowsAudioProfileController();
            _profileController.ProfileStateChanged += OnControllerProfileStateChanged;

            _config = _configService.LoadConfig();
            _allowVisible = !startInTray;

            InitializeComponent();
            InitializeTray();
            InitializeHotkey();

            if (startInTray)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
            }
        }

        private void OnControllerProfileStateChanged()
        {
            if (_isDisposing || IsDisposed || !IsHandleCreated) return;
            ReloadDeviceState();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowVisible)
            {
                value = false;
                if (!IsHandleCreated)
                {
                    CreateHandle();
                }
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_refreshTimer == null)
            {
                _refreshTimer = new System.Windows.Forms.Timer
                {
                    Interval = 3000
                };
                _refreshTimer.Tick += (s, args) => ReloadDeviceState();
                _refreshTimer.Start();
            }

            ReloadDeviceState();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray && _allowVisible && !_isExiting)
            {
                HideToTray();
            }
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "EasyHFP";
            ClientSize = new Size(364, 316);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. Device Selection Row
            _deviceLabel = new Label
            {
                Text = "Device:",
                Location = new Point(14, 16),
                AutoSize = true
            };

            _deviceComboBox = new ComboBox
            {
                Location = new Point(14, 36),
                Size = new Size(264, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;

            _refreshButton = new Button
            {
                Text = "Refresh",
                Location = new Point(284, 35),
                Size = new Size(66, 25),
                UseVisualStyleBackColor = true
            };
            _refreshButton.Click += (s, e) =>
            {
                SetStatusMessage("Refreshing devices...", isError: false);
                ReloadDeviceState(userInitiated: true);
            };

            // 2. Connection & Inferred Active Audio Mode Row
            _connectionCaptionLabel = new Label
            {
                Text = "Connection:",
                Location = new Point(14, 72),
                AutoSize = true
            };

            _connectionValueLabel = new Label
            {
                Text = "Checking...",
                Location = new Point(96, 72),
                Size = new Size(254, 18),
                AutoEllipsis = true
            };

            _modeCaptionLabel = new Label
            {
                Text = "Audio Mode:",
                Location = new Point(14, 96),
                AutoSize = true
            };

            _modeValueLabel = new Label
            {
                Text = "Checking...",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(96, 96),
                Size = new Size(254, 18),
                AutoEllipsis = true
            };

            // 3. Primary Switch Control
            _toggleButton = new Button
            {
                Text = "Switch Audio Mode",
                Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(14, 124),
                Size = new Size(336, 38),
                UseVisualStyleBackColor = true,
                Enabled = false
            };
            _toggleButton.Click += (s, e) => TriggerToggle();

            // 4. Concise Status Message
            _statusMessageLabel = new Label
            {
                Text = "Ready",
                ForeColor = SystemColors.GrayText,
                Location = new Point(14, 168),
                Size = new Size(336, 18),
                AutoEllipsis = true
            };

            // 5. Minimal Settings Area
            _settingsGroup = new GroupBox
            {
                Text = "Settings",
                Location = new Point(14, 194),
                Size = new Size(336, 104)
            };

            _autoStartCheck = new CheckBox
            {
                Text = "Start with Windows (in tray)",
                Location = new Point(12, 24),
                Size = new Size(165, 20),
                UseVisualStyleBackColor = true,
                Checked = _configService.IsAutoStartEnabled()
            };
            _autoStartCheck.CheckedChanged += AutoStartCheck_CheckedChanged;

            _minToTrayCheck = new CheckBox
            {
                Text = "Close to tray",
                Location = new Point(184, 24),
                Size = new Size(140, 20),
                UseVisualStyleBackColor = true,
                Checked = _config.MinimizeToTray
            };
            _minToTrayCheck.CheckedChanged += (s, e) =>
            {
                _config.MinimizeToTray = _minToTrayCheck.Checked;
                TrySaveConfig();
            };

            _toastCheck = new CheckBox
            {
                Text = "Show desktop notifications on switch",
                Location = new Point(12, 50),
                Size = new Size(312, 20),
                UseVisualStyleBackColor = true,
                Checked = _config.ShowNotifications
            };
            _toastCheck.CheckedChanged += (s, e) =>
            {
                _config.ShowNotifications = _toastCheck.Checked;
                TrySaveConfig();
            };

            _hotkeyInfoLabel = new Label
            {
                Text = $"Global shortcut: {_config.Hotkey}",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(12, 76),
                Size = new Size(312, 16),
                AutoEllipsis = true
            };

            _settingsGroup.Controls.Add(_autoStartCheck);
            _settingsGroup.Controls.Add(_minToTrayCheck);
            _settingsGroup.Controls.Add(_toastCheck);
            _settingsGroup.Controls.Add(_hotkeyInfoLabel);

            Controls.Add(_deviceLabel);
            Controls.Add(_deviceComboBox);
            Controls.Add(_refreshButton);
            Controls.Add(_connectionCaptionLabel);
            Controls.Add(_connectionValueLabel);
            Controls.Add(_modeCaptionLabel);
            Controls.Add(_modeValueLabel);
            Controls.Add(_toggleButton);
            Controls.Add(_statusMessageLabel);
            Controls.Add(_settingsGroup);

            FormClosing += MainForm_FormClosing;
            ResumeLayout(false);
            PerformLayout();
        }

        private void AutoStartCheck_CheckedChanged(object? sender, EventArgs e)
        {
            try
            {
                _configService.SetAutoStart(_autoStartCheck.Checked);
                SetStatusMessage(_autoStartCheck.Checked ? "Startup with Windows enabled." : "Startup with Windows disabled.", isError: false);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to toggle auto-start.", ex);
                _autoStartCheck.CheckedChanged -= AutoStartCheck_CheckedChanged;
                _autoStartCheck.Checked = _configService.IsAutoStartEnabled();
                _autoStartCheck.CheckedChanged += AutoStartCheck_CheckedChanged;
                SetStatusMessage("Could not update Windows startup setting.", isError: true);
            }
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();

            _trayToggleMenuItem = new ToolStripMenuItem($"Switch Audio Mode ({_config.Hotkey})", null, (s, e) => TriggerToggle())
            {
                Font = new Font(Font, FontStyle.Bold)
            };
            var openItem = new ToolStripMenuItem("Open EasyHFP", null, (s, e) => RestoreFromTray());
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

            _trayMenu.Items.Add(_trayToggleMenuItem);
            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Text = "EasyHFP",
                Icon = SystemIcons.Application,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            UpdateTrayIcon(AudioProfileMode.Unknown, false);

            // Left-click or double-click restores the window predictably without mutating audio state.
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void InitializeHotkey()
        {
            try
            {
                _hotkeyWindow = new HotkeyWindow(_config.Hotkey);
                _hotkeyWindow.HotkeyPressed += () => TriggerToggle();
                _hotkeyWindow.RegistrationFailed += (err) =>
                {
                    AppLogger.Warn($"Hotkey registration failed: {err}");
                    _hotkeyInfoLabel.Text = $"Shortcut unavailable ({_config.Hotkey} in use)";
                    _hotkeyInfoLabel.ForeColor = Color.Firebrick;
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to initialize global hotkey window.", ex);
                _hotkeyInfoLabel.Text = "Shortcut unavailable";
                _hotkeyInfoLabel.ForeColor = Color.Firebrick;
            }
        }

        private void SetStatusMessage(string message, bool isError)
        {
            if (_isDisposing || IsDisposed) return;
            _statusMessageLabel.Text = message;
            _statusMessageLabel.ForeColor = isError ? Color.Firebrick : SystemColors.GrayText;
        }

        private void TrySaveConfig()
        {
            try
            {
                _configService.SaveConfig(_config);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save configuration from UI.", ex);
                SetStatusMessage("Could not save settings.", isError: true);
            }
        }

        public void ReloadDeviceState(bool userInitiated = false)
        {
            if (_isBusy || _isDisposing || IsDisposed) return;
            if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0) return;

            string preferredMac = _selectedDevice?.Mac ?? _config.DefaultMac;

            Task.Run(() =>
            {
                try
                {
                    var list = _bluetoothService.EnumerateDevices();

                    BtDeviceItem? targetForSnapshot = null;
                    if (!string.IsNullOrEmpty(preferredMac))
                    {
                        targetForSnapshot = list.Find(d => d.Mac.Equals(preferredMac, StringComparison.OrdinalIgnoreCase));
                    }
                    if (targetForSnapshot == null && list.Count > 0)
                    {
                        targetForSnapshot = list[0];
                    }

                    HeadsetProfileSnapshot? snapshot = null;
                    if (targetForSnapshot != null)
                    {
                        snapshot = _profileController.QueryProfileSnapshot(targetForSnapshot);
                    }

                    if (_isDisposing || IsDisposed || !IsHandleCreated) return;

                    BeginInvoke(new Action(() =>
                    {
                        if (_isDisposing || IsDisposed) return;
                        _currentDevices = list;
                        _currentProfileSnapshot = snapshot;
                        UpdateDeviceListUI();
                        UpdateDeviceStatusUI();
                        if (userInitiated)
                        {
                            SetStatusMessage(_selectedDevice != null ? "Device status updated." : "No paired Bluetooth audio devices found.", isError: false);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Device state refresh failed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _isRefreshing, 0);
                }
            });
        }

        private bool HasDeviceListChanged(List<BtDeviceItem> newList)
        {
            if (_deviceComboBox.Items.Count != newList.Count) return true;
            for (int i = 0; i < newList.Count; i++)
            {
                if (_deviceComboBox.Items[i] is not BtDeviceItem existing) return true;
                var updated = newList[i];
                if (!string.Equals(existing.Mac, updated.Mac, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.Name, updated.Name, StringComparison.Ordinal) ||
                    existing.Connected != updated.Connected)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateDeviceListUI()
        {
            // Never reset ComboBox items while user has the dropdown open
            if (_deviceComboBox.DroppedDown)
            {
                if (_selectedDevice != null)
                {
                    var match = _currentDevices.Find(d => d.Mac.Equals(_selectedDevice.Mac, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _selectedDevice = match;
                    }
                }
                return;
            }

            int selIdx = -1;
            for (int i = 0; i < _currentDevices.Count; i++)
            {
                if (_currentDevices[i].Mac.Equals(_config.DefaultMac, StringComparison.OrdinalIgnoreCase))
                {
                    selIdx = i;
                    break;
                }
            }

            if (selIdx < 0 && _currentDevices.Count > 0)
            {
                selIdx = 0;
            }

            _selectedDevice = selIdx >= 0 ? _currentDevices[selIdx] : null;

            if (!HasDeviceListChanged(_currentDevices))
            {
                for (int i = 0; i < _currentDevices.Count; i++)
                {
                    if (_deviceComboBox.Items[i] is BtDeviceItem existing)
                    {
                        existing.HfpState = _currentDevices[i].HfpState;
                        existing.ActiveProfileMode = _currentDevices[i].ActiveProfileMode;
                        existing.Info = _currentDevices[i].Info;
                    }
                }
                return;
            }

            _deviceComboBox.SelectedIndexChanged -= DeviceComboBox_SelectedIndexChanged;
            _deviceComboBox.BeginUpdate();
            try
            {
                _deviceComboBox.Items.Clear();
                foreach (var dev in _currentDevices)
                {
                    _deviceComboBox.Items.Add(dev);
                }

                if (selIdx >= 0 && selIdx < _deviceComboBox.Items.Count)
                {
                    _deviceComboBox.SelectedIndex = selIdx;
                }
            }
            finally
            {
                _deviceComboBox.EndUpdate();
                _deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;
            }
        }

        private void DeviceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_deviceComboBox.SelectedItem is BtDeviceItem dev)
            {
                string? previousHoldMac = _profileController.ActiveHoldDeviceMac;
                if (!string.IsNullOrEmpty(previousHoldMac) &&
                    !string.Equals(previousHoldMac, dev.Mac, StringComparison.OrdinalIgnoreCase))
                {
                    _profileController.ReleaseAllHolds();
                }

                _selectedDevice = dev;
                _config.DefaultMac = dev.Mac;
                _config.DefaultName = dev.Name;
                _config.KnownDevices[dev.Mac] = dev.Name;
                TrySaveConfig();

                SetStatusMessage($"Selected {dev.Name}.", isError: false);
                ReloadDeviceState();
            }
        }

        private void UpdateDeviceStatusUI()
        {
            if (_selectedDevice != null)
            {
                bool isConnected = _selectedDevice.Connected ||
                                   (_currentProfileSnapshot?.IsBluetoothConnected ?? false);

                _connectionValueLabel.Text = isConnected ? "Connected" : "Disconnected";
                _connectionValueLabel.ForeColor = isConnected
                    ? Color.FromArgb(16, 124, 65)
                    : SystemColors.GrayText;

                var mode = _currentProfileSnapshot?.InferredMode ?? _selectedDevice.ActiveProfileMode;
                string modeText = _currentProfileSnapshot?.ModeDisplayText ?? string.Empty;

                switch (mode)
                {
                    case AudioProfileMode.HfpHeldByEasyHfp:
                        _modeValueLabel.Text = !string.IsNullOrEmpty(modeText) ? modeText : "Hands-Free (HFP — Active)";
                        _modeValueLabel.ForeColor = Color.FromArgb(180, 83, 9);
                        _toggleButton.Text = "Switch to Stereo (A2DP)";
                        break;

                    case AudioProfileMode.HfpHeldByExternalApp:
                        _modeValueLabel.Text = !string.IsNullOrEmpty(modeText) ? modeText : "Hands-Free (HFP — In Use)";
                        _modeValueLabel.ForeColor = Color.FromArgb(180, 83, 9);
                        _toggleButton.Text = "Switch to Stereo (A2DP)";
                        break;

                    case AudioProfileMode.A2dp:
                        _modeValueLabel.Text = !string.IsNullOrEmpty(modeText) ? modeText : "Stereo (A2DP)";
                        _modeValueLabel.ForeColor = Color.FromArgb(16, 124, 65);
                        _toggleButton.Text = "Switch to Hands-Free (HFP)";
                        break;

                    case AudioProfileMode.Disconnected:
                        _modeValueLabel.Text = "Disconnected";
                        _modeValueLabel.ForeColor = SystemColors.GrayText;
                        _toggleButton.Text = "Switch Audio Mode";
                        break;

                    default:
                        _modeValueLabel.Text = !string.IsNullOrEmpty(modeText) ? modeText : "Unavailable";
                        _modeValueLabel.ForeColor = SystemColors.GrayText;
                        _toggleButton.Text = "Switch Audio Mode";
                        break;
                }

                bool canToggle = !_isBusy && isConnected;
                _toggleButton.Enabled = canToggle;
                if (_trayToggleMenuItem != null)
                {
                    _trayToggleMenuItem.Enabled = canToggle;
                }

                UpdateTrayStatus(_selectedDevice.Name, mode, isConnected);
            }
            else
            {
                _connectionValueLabel.Text = "No paired device found";
                _connectionValueLabel.ForeColor = SystemColors.GrayText;
                _modeValueLabel.Text = "Unavailable";
                _modeValueLabel.ForeColor = SystemColors.GrayText;
                _toggleButton.Text = "Switch Audio Mode";
                _toggleButton.Enabled = false;
                if (_trayToggleMenuItem != null)
                {
                    _trayToggleMenuItem.Enabled = false;
                }

                UpdateTrayStatus(null, AudioProfileMode.Unknown, false);
            }
        }

        private void UpdateTrayStatus(string? deviceName, AudioProfileMode mode, bool isConnected)
        {
            if (_trayIcon == null) return;

            if (!string.IsNullOrEmpty(deviceName))
            {
                string shortName = deviceName.Length > 24 ? deviceName.Substring(0, 21) + "..." : deviceName;
                string modeText = mode switch
                {
                    AudioProfileMode.HfpHeldByEasyHfp => "Hands-Free (HFP)",
                    AudioProfileMode.HfpHeldByExternalApp => "Hands-Free (Ext Mic)",
                    AudioProfileMode.A2dp => "Stereo (A2DP)",
                    AudioProfileMode.Disconnected => "Disconnected",
                    _ => "Mode Unknown"
                };
                string rawTooltip = $"EasyHFP: {shortName}\n{modeText}";
                _trayIcon.Text = rawTooltip.Length > 63 ? rawTooltip.Substring(0, 63) : rawTooltip;
            }
            else
            {
                _trayIcon.Text = "EasyHFP: No device";
            }

            if (!_trayIconInitialized ||
                _lastTrayIconState.profileMode != mode ||
                _lastTrayIconState.isConnected != isConnected)
            {
                UpdateTrayIcon(mode, isConnected);
                _lastTrayIconState = (mode, isConnected);
                _trayIconInitialized = true;
            }
        }

        public void TriggerToggle()
        {
            if (_isBusy || _selectedDevice == null || _isDisposing || IsDisposed) return;
            _isBusy = true;

            _refreshTimer?.Stop();

            _toggleButton.Text = "Switching...";
            _toggleButton.Enabled = false;
            if (_trayToggleMenuItem != null)
            {
                _trayToggleMenuItem.Enabled = false;
            }
            SetStatusMessage("Switching audio mode...", isError: false);

            var targetDevice = _selectedDevice;
            AppLogger.Info($"Initiating native WASAPI audio mode toggle for '{targetDevice.Name}' [{targetDevice.Mac}].");

            Task.Run(async () =>
            {
                ProfileTransitionResult result;
                HeadsetProfileSnapshot? postSnapshot = null;

                try
                {
                    result = await _profileController.ToggleProfileAsync(targetDevice).ConfigureAwait(false);
                    postSnapshot = _profileController.QueryProfileSnapshot(targetDevice);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Unhandled exception during WASAPI profile switch for '{targetDevice.Name}'.", ex);
                    result = new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.TransitionFailed,
                        ObservedMode = AudioProfileMode.Unknown,
                        UserMessage = $"Unexpected error: {ex.Message}"
                    };
                }

                if (result.Success)
                {
                    AppLogger.Info(
                        $"WASAPI switch succeeded for '{targetDevice.Name}': outcome={result.Outcome}, " +
                        $"observed={result.ObservedMode}, duration={result.Duration.TotalMilliseconds:F0}ms, " +
                        $"framesDiscarded={result.FramesDiscarded}, details='{result.DiagnosticDetails}'.");
                }
                else
                {
                    AppLogger.Warn(
                        $"WASAPI switch not completed for '{targetDevice.Name}': outcome={result.Outcome}, " +
                        $"observed={result.ObservedMode}, msg='{result.UserMessage}', details='{result.DiagnosticDetails}'.");
                }

                if (_isDisposing || IsDisposed || !IsHandleCreated) return;

                BeginInvoke(new Action(() =>
                {
                    if (_isDisposing || IsDisposed) return;

                    _isBusy = false;
                    if (postSnapshot != null)
                    {
                        _currentProfileSnapshot = postSnapshot;
                    }

                    UpdateDeviceStatusUI();

                    if (result.Success)
                    {
                        SetStatusMessage(result.UserMessage, isError: false);

                        if (_config.ShowNotifications)
                        {
                            string modeDesc = result.ObservedMode == AudioProfileMode.HfpHeldByEasyHfp
                                ? "Hands-Free (HFP)"
                                : "Stereo (A2DP)";
                            NotificationHelper.ShowToast(
                                $"EasyHFP — {modeDesc}",
                                $"{targetDevice.Name} is now in {modeDesc} mode.",
                                _trayIcon);
                        }
                    }
                    else
                    {
                        SetStatusMessage(result.UserMessage, isError: true);

                        if (_trayIcon != null && _config.ShowNotifications)
                        {
                            _trayIcon.ShowBalloonTip(3500, "EasyHFP", result.UserMessage, ToolTipIcon.Warning);
                        }
                    }

                    _refreshTimer?.Start();
                }));
            });
        }

        private void UpdateTrayIcon(AudioProfileMode mode, bool isConnected)
        {
            if (_trayIcon == null) return;

            try
            {
                bool isHfpActive = mode == AudioProfileMode.HfpHeldByEasyHfp ||
                                   mode == AudioProfileMode.HfpHeldByExternalApp;

                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.Clear(Color.Transparent);

                    Color bgColor = !isConnected || mode == AudioProfileMode.Disconnected
                        ? Color.FromArgb(120, 120, 128)
                        : (isHfpActive ? Color.FromArgb(217, 119, 6) : Color.FromArgb(16, 124, 65));

                    using (var bgBrush = new SolidBrush(bgColor))
                    {
                        g.FillEllipse(bgBrush, 2, 2, 28, 28);
                    }

                    using (var pen = new Pen(Color.FromArgb(40, 40, 48), 2))
                    {
                        g.DrawEllipse(pen, 2, 2, 28, 28);
                    }

                    string glyph = mode switch
                    {
                        AudioProfileMode.HfpHeldByEasyHfp => "H",
                        AudioProfileMode.HfpHeldByExternalApp => "H",
                        AudioProfileMode.A2dp => "S",
                        _ => "?"
                    };
                    using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
                    var size = g.MeasureString(glyph, font);
                    float x = (32 - size.Width) / 2f;
                    float y = (32 - size.Height) / 2f;
                    using var textBrush = new SolidBrush(Color.White);
                    g.DrawString(glyph, font, textBrush, x, y);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using var tempIcon = Icon.FromHandle(hIcon);
                    var managedIcon = (Icon)tempIcon.Clone();

                    var oldIcon = _trayIcon.Icon;
                    _trayIcon.Icon = managedIcon;

                    if (oldIcon != null && oldIcon != SystemIcons.Application)
                    {
                        oldIcon.Dispose();
                    }
                }
                finally
                {
                    NativeUser32.DestroyIcon(hIcon);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to update tray icon: {ex.Message}");
                _trayIcon.Icon = SystemIcons.Application;
            }
        }

        private void HideToTray()
        {
            if (_isDisposing || IsDisposed) return;

            ShowInTaskbar = false;
            Hide();

            if (_config.ShowNotifications && _trayIcon != null && !_trayBalloonShownOnce)
            {
                _trayBalloonShownOnce = true;
                _trayIcon.ShowBalloonTip(
                    2000,
                    "EasyHFP Running in Tray",
                    $"Running in the background. Press {_config.Hotkey} to switch modes.",
                    ToolTipIcon.Info);
            }
        }

        public void RestoreFromTray()
        {
            if (_isDisposing || IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RestoreFromTray));
                return;
            }

            _allowVisible = true;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            if (IsHandleCreated)
            {
                NativeUser32.SetForegroundWindow(Handle);
            }
            ReloadDeviceState();
        }

        public void ExitApplication()
        {
            if (_isDisposing) return;
            _isExiting = true;
            Close();
            Application.Exit();
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!_isExiting && _config.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _isDisposing = true;

            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
                _refreshTimer = null;
            }

            _profileController.ProfileStateChanged -= OnControllerProfileStateChanged;
            if (_ownsProfileController)
            {
                try
                {
                    _profileController.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Error disposing profile controller on exit: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    _profileController.ReleaseAllHolds();
                }
                catch { }
            }

            if (_hotkeyWindow != null)
            {
                _hotkeyWindow.Dispose();
                _hotkeyWindow = null;
            }

            if (_trayIcon != null)
            {
                var iconToDispose = _trayIcon.Icon;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;

                if (iconToDispose != null && iconToDispose != SystemIcons.Application)
                {
                    try { iconToDispose.Dispose(); } catch { }
                }
            }

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
        }
    }
}
