using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;

namespace EasyHFP.UI
{
    public class OnboardingWizardForm : Form
    {
        private readonly IBluetoothService _bluetoothService;
        private readonly IConfigurationService _configService;
        private readonly IStartMenuShortcutService _shortcutService;
        private readonly IAudioProfileController _profileController;
        private readonly ConfigData _workingConfig;

        private List<BtDeviceItem> _devices = new();
        private BtDeviceItem? _selectedDevice;
        private HeadsetProfileSnapshot? _selectedSnapshot;
        private int _currentStepIndex;

        // Shared Wizard Frame Controls
        private Icon? _windowIcon;
        private Bitmap? _logoBitmap;
        private PictureBox _logoPictureBox = null!;
        private Label _stepTitleLabel = null!;
        private Label _stepIndicatorLabel = null!;
        private Panel _step1Panel = null!;
        private Panel _step2Panel = null!;
        private Panel _step3Panel = null!;

        private Button _backButton = null!;
        private Button _nextOrFinishButton = null!;
        private Button _cancelButton = null!;

        // Step 1 Controls
        private Label _welcomeIntroLabel = null!;
        private Label _selectDeviceCaptionLabel = null!;
        private ComboBox _deviceComboBox = null!;
        private Button _refreshButton = null!;
        private GroupBox _readinessGroup = null!;
        private Label _connectionStatusLabel = null!;
        private Label _endpointReadinessLabel = null!;
        private Label _guidanceLabel = null!;

        // Step 2 Controls
        private CheckBox _addToStartMenuCheck = null!;
        private Label _addToStartMenuDescLabel = null!;
        private CheckBox _startWithWindowsCheck = null!;
        private Label _startWithWindowsDescLabel = null!;
        private CheckBox _closeToTrayCheck = null!;
        private Label _closeToTrayDescLabel = null!;
        private CheckBox _notificationsCheck = null!;
        private Label _notificationsDescLabel = null!;

        // Step 3 Controls
        private Label _finishIntroLabel = null!;
        private GroupBox _summaryGroup = null!;
        private Label _summaryContentLabel = null!;
        private Label _finishNoteLabel = null!;

        public int CurrentStepIndex => _currentStepIndex;
        public BtDeviceItem? SelectedDevice => _selectedDevice;
        public HeadsetProfileSnapshot? SelectedDeviceSnapshot => _selectedSnapshot;
        public ConfigData WorkingConfig => _workingConfig;
        public int WakeupCount { get; private set; }
        public bool OnboardingCompleted { get; private set; }
        public Image? HeaderLogoImage => _logoPictureBox?.Image;

        public bool AddToStartMenuChecked
        {
            get => _addToStartMenuCheck.Checked;
            set => _addToStartMenuCheck.Checked = value;
        }

        public bool StartWithWindowsChecked
        {
            get => _startWithWindowsCheck.Checked;
            set => _startWithWindowsCheck.Checked = value;
        }

        public bool CloseToTrayChecked
        {
            get => _closeToTrayCheck.Checked;
            set => _closeToTrayCheck.Checked = value;
        }

        public bool NotificationsChecked
        {
            get => _notificationsCheck.Checked;
            set => _notificationsCheck.Checked = value;
        }

        public OnboardingWizardForm(
            IBluetoothService bluetoothService,
            IConfigurationService configService,
            IStartMenuShortcutService shortcutService,
            IAudioProfileController profileController,
            ConfigData? initialConfig = null)
        {
            _bluetoothService = bluetoothService ?? throw new ArgumentNullException(nameof(bluetoothService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _shortcutService = shortcutService ?? throw new ArgumentNullException(nameof(shortcutService));
            _profileController = profileController ?? throw new ArgumentNullException(nameof(profileController));

            _workingConfig = (initialConfig ?? _configService.LoadConfig()).Clone();

            InitializeComponent();
            RefreshDevicesSynchronously();
            ShowStep(0);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "EasyHFP Setup";
            _windowIcon = AppBranding.CreateWindowIcon();
            Icon = _windowIcon;
            ClientSize = new Size(448, 376);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _logoBitmap = AppBranding.CreateLogoBitmap();
            _logoPictureBox = new PictureBox
            {
                Location = new Point(16, 10),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Image = _logoBitmap
            };

            _stepTitleLabel = new Label
            {
                Text = "Welcome to EasyHFP",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(68, 20),
                Size = new Size(272, 24)
            };

            _stepIndicatorLabel = new Label
            {
                Text = "Step 1 of 3",
                ForeColor = SystemColors.GrayText,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(340, 24),
                Size = new Size(92, 18)
            };

            // Step Panels (same bounds)
            var panelLocation = new Point(16, 62);
            var panelSize = new Size(416, 256);

            _step1Panel = new Panel { Location = panelLocation, Size = panelSize };
            _step2Panel = new Panel { Location = panelLocation, Size = panelSize, Visible = false };
            _step3Panel = new Panel { Location = panelLocation, Size = panelSize, Visible = false };

            BuildStep1Panel();
            BuildStep2Panel();
            BuildStep3Panel();

            // Bottom Navigation Buttons
            _backButton = new Button
            {
                Text = "< Back",
                Location = new Point(176, 332),
                Size = new Size(82, 28),
                UseVisualStyleBackColor = true,
                Enabled = false
            };
            _backButton.Click += (s, e) => NavigateBack();

            _nextOrFinishButton = new Button
            {
                Text = "Next >",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(264, 332),
                Size = new Size(82, 28),
                UseVisualStyleBackColor = true
            };
            _nextOrFinishButton.Click += (s, e) =>
            {
                if (_currentStepIndex < 2)
                {
                    NavigateNext();
                }
                else
                {
                    CompleteOnboarding();
                }
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(352, 332),
                Size = new Size(80, 28),
                UseVisualStyleBackColor = true
            };
            _cancelButton.Click += (s, e) =>
            {
                OnboardingCompleted = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            AcceptButton = _nextOrFinishButton;
            CancelButton = _cancelButton;

            Controls.Add(_logoPictureBox);
            Controls.Add(_stepTitleLabel);
            Controls.Add(_stepIndicatorLabel);
            Controls.Add(_step1Panel);
            Controls.Add(_step2Panel);
            Controls.Add(_step3Panel);
            Controls.Add(_backButton);
            Controls.Add(_nextOrFinishButton);
            Controls.Add(_cancelButton);

            FormClosed += OnboardingWizardForm_FormClosed;

            ResumeLayout(false);
            PerformLayout();
        }

        private void OnboardingWizardForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (_logoBitmap != null)
            {
                try { _logoBitmap.Dispose(); } catch { }
                _logoBitmap = null;
            }

            if (_windowIcon != null && _windowIcon != SystemIcons.Application)
            {
                try { _windowIcon.Dispose(); } catch { }
                _windowIcon = null;
            }
        }

        private void BuildStep1Panel()
        {
            _welcomeIntroLabel = new Label
            {
                Text = "EasyHFP switches your Bluetooth headset between high-quality Stereo (A2DP) and Hands-Free microphone mode (HFP) on Windows 11 without restarting Bluetooth or disconnecting your headset.",
                Location = new Point(0, 2),
                Size = new Size(416, 48)
            };

            _selectDeviceCaptionLabel = new Label
            {
                Text = "Select your preferred Bluetooth headset:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(0, 56),
                Size = new Size(320, 18)
            };

            _deviceComboBox = new ComboBox
            {
                Location = new Point(0, 78),
                Size = new Size(332, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _deviceComboBox.SelectedIndexChanged += (s, e) => OnSelectedHeadsetChanged();

            _refreshButton = new Button
            {
                Text = "Refresh",
                Location = new Point(340, 77),
                Size = new Size(76, 25),
                UseVisualStyleBackColor = true
            };
            _refreshButton.Click += (s, e) => RefreshDevicesSynchronously();

            _readinessGroup = new GroupBox
            {
                Text = "Headset & Audio Endpoint Status",
                Location = new Point(0, 112),
                Size = new Size(416, 138)
            };

            _connectionStatusLabel = new Label
            {
                Text = "Connection: Checking...",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(12, 24),
                Size = new Size(392, 20),
                AutoEllipsis = true
            };

            _endpointReadinessLabel = new Label
            {
                Text = "Endpoints: Checking...",
                Location = new Point(12, 48),
                Size = new Size(392, 36)
            };

            _guidanceLabel = new Label
            {
                Text = string.Empty,
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(12, 88),
                Size = new Size(392, 42)
            };

            _readinessGroup.Controls.Add(_connectionStatusLabel);
            _readinessGroup.Controls.Add(_endpointReadinessLabel);
            _readinessGroup.Controls.Add(_guidanceLabel);

            _step1Panel.Controls.Add(_welcomeIntroLabel);
            _step1Panel.Controls.Add(_selectDeviceCaptionLabel);
            _step1Panel.Controls.Add(_deviceComboBox);
            _step1Panel.Controls.Add(_refreshButton);
            _step1Panel.Controls.Add(_readinessGroup);
        }

        private void BuildStep2Panel()
        {
            _addToStartMenuCheck = new CheckBox
            {
                Text = "Add EasyHFP to Windows Start search (Recommended)",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(4, 6),
                Size = new Size(404, 20),
                UseVisualStyleBackColor = true,
                Checked = _workingConfig.AddToStartMenu
            };

            _addToStartMenuDescLabel = new Label
            {
                Text = "Creates a per-user Start Menu shortcut so you can launch EasyHFP from Windows Search. Does not make EasyHFP run automatically at login.",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(24, 28),
                Size = new Size(388, 32)
            };

            _startWithWindowsCheck = new CheckBox
            {
                Text = "Launch EasyHFP when Windows starts",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(4, 68),
                Size = new Size(404, 20),
                UseVisualStyleBackColor = true,
                Checked = _workingConfig.StartWithWindows
            };

            _startWithWindowsDescLabel = new Label
            {
                Text = "Starts EasyHFP minimized in the system tray when you sign in to Windows (independent of Windows Start search).",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(24, 90),
                Size = new Size(388, 32)
            };

            _closeToTrayCheck = new CheckBox
            {
                Text = "Close window to system tray",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(4, 130),
                Size = new Size(404, 20),
                UseVisualStyleBackColor = true,
                Checked = _workingConfig.MinimizeToTray
            };

            _closeToTrayDescLabel = new Label
            {
                Text = "Keeps EasyHFP running in the system tray when you close the main window.",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(24, 152),
                Size = new Size(388, 22)
            };

            _notificationsCheck = new CheckBox
            {
                Text = "Show desktop notifications",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(4, 184),
                Size = new Size(404, 20),
                UseVisualStyleBackColor = true,
                Checked = _workingConfig.ShowNotifications
            };

            _notificationsDescLabel = new Label
            {
                Text = "Displays a brief notification when switching between Stereo (A2DP) and Hands-Free (HFP).",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(24, 206),
                Size = new Size(388, 22)
            };

            _step2Panel.Controls.Add(_addToStartMenuCheck);
            _step2Panel.Controls.Add(_addToStartMenuDescLabel);
            _step2Panel.Controls.Add(_startWithWindowsCheck);
            _step2Panel.Controls.Add(_startWithWindowsDescLabel);
            _step2Panel.Controls.Add(_closeToTrayCheck);
            _step2Panel.Controls.Add(_closeToTrayDescLabel);
            _step2Panel.Controls.Add(_notificationsCheck);
            _step2Panel.Controls.Add(_notificationsDescLabel);
        }

        private void BuildStep3Panel()
        {
            _finishIntroLabel = new Label
            {
                Text = "Review your configuration below. Click Finish to save your settings and open EasyHFP.",
                Location = new Point(0, 4),
                Size = new Size(416, 32)
            };

            _summaryGroup = new GroupBox
            {
                Text = "Setup Summary",
                Location = new Point(0, 40),
                Size = new Size(416, 168)
            };

            _summaryContentLabel = new Label
            {
                Text = string.Empty,
                Location = new Point(14, 24),
                Size = new Size(388, 134)
            };

            _summaryGroup.Controls.Add(_summaryContentLabel);

            _finishNoteLabel = new Label
            {
                Text = "You can change your headset or preferences at any time from the EasyHFP main window.",
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(0, 218),
                Size = new Size(416, 30)
            };

            _step3Panel.Controls.Add(_finishIntroLabel);
            _step3Panel.Controls.Add(_summaryGroup);
            _step3Panel.Controls.Add(_finishNoteLabel);
        }

        public void RefreshDevicesSynchronously()
        {
            try
            {
                _devices = _bluetoothService.EnumerateDevices() ?? new List<BtDeviceItem>();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to enumerate Bluetooth devices during onboarding.", ex);
                _devices = new List<BtDeviceItem>();
            }

            string preferredMac = _selectedDevice?.Mac ?? _workingConfig.DefaultMac;
            int selectIdx = -1;

            if (!string.IsNullOrWhiteSpace(preferredMac))
            {
                selectIdx = _devices.FindIndex(d => d.Mac.Equals(preferredMac, StringComparison.OrdinalIgnoreCase));
            }

            if (selectIdx < 0 && _devices.Count > 0)
            {
                int connectedIdx = _devices.FindIndex(d => d.Connected);
                selectIdx = connectedIdx >= 0 ? connectedIdx : 0;
            }

            _deviceComboBox.BeginUpdate();
            try
            {
                _deviceComboBox.Items.Clear();
                foreach (var dev in _devices)
                {
                    _deviceComboBox.Items.Add(dev);
                }

                if (selectIdx >= 0 && selectIdx < _deviceComboBox.Items.Count)
                {
                    _deviceComboBox.SelectedIndex = selectIdx;
                }
                else
                {
                    _selectedDevice = null;
                    _selectedSnapshot = null;
                    UpdateReadinessUI();
                }
            }
            finally
            {
                _deviceComboBox.EndUpdate();
            }
        }

        private void OnSelectedHeadsetChanged()
        {
            _selectedDevice = _deviceComboBox.SelectedItem as BtDeviceItem;
            _selectedSnapshot = null;

            if (_selectedDevice != null)
            {
                try
                {
                    // Read-only Core Audio inspection: never activates microphone capture during setup.
                    _selectedSnapshot = _profileController.QueryProfileSnapshot(_selectedDevice);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Could not query endpoint snapshot during onboarding for '{_selectedDevice.Name}': {ex.Message}");
                }
            }

            UpdateReadinessUI();
        }

        private void UpdateReadinessUI()
        {
            if (_selectedDevice == null)
            {
                _connectionStatusLabel.Text = "No compatible Bluetooth Classic headset found";
                _connectionStatusLabel.ForeColor = Color.FromArgb(180, 83, 9);
                _endpointReadinessLabel.Text = "A2DP / HFP Endpoints: Not detected";
                _guidanceLabel.Text = "Make sure your Bluetooth headset is paired and connected in Windows Settings > Bluetooth & devices, then click Refresh. You can also continue setup and select a headset later.";
                return;
            }

            bool isConnected = _selectedDevice.Connected || (_selectedSnapshot?.IsBluetoothConnected ?? false);
            bool hasA2dp = _selectedSnapshot?.IsRenderEndpointActive ?? false;
            bool hasHfp = _selectedSnapshot?.IsCaptureEndpointActive ?? false;

            if (isConnected)
            {
                _connectionStatusLabel.Text = $"Connection: Connected ({_selectedDevice.Name})";
                _connectionStatusLabel.ForeColor = Color.FromArgb(16, 124, 65);

                if (hasA2dp && hasHfp)
                {
                    _endpointReadinessLabel.Text = "Endpoints: Stereo (A2DP) playback and Hands-Free (HFP) microphone endpoints are available and ready.";
                    _endpointReadinessLabel.ForeColor = Color.FromArgb(16, 124, 65);
                    _guidanceLabel.Text = "Your headset is ready for one-click A2DP / HFP profile switching.";
                }
                else if (hasA2dp && !hasHfp)
                {
                    _endpointReadinessLabel.Text = "Endpoints: Stereo (A2DP) detected, but Hands-Free (HFP) microphone endpoint is unavailable.";
                    _endpointReadinessLabel.ForeColor = Color.FromArgb(180, 83, 9);
                    _guidanceLabel.Text = "Check Windows Settings > System > Sound > Input to ensure the headset Hands-Free microphone is enabled.";
                }
                else if (!hasA2dp && hasHfp)
                {
                    _endpointReadinessLabel.Text = "Endpoints: Hands-Free (HFP) detected, but Stereo (A2DP) playback endpoint is unavailable.";
                    _endpointReadinessLabel.ForeColor = Color.FromArgb(180, 83, 9);
                    _guidanceLabel.Text = "Check Windows Settings > System > Sound > Output to ensure the headset Stereo playback device is enabled.";
                }
                else
                {
                    _endpointReadinessLabel.Text = "Endpoints: Waiting for Windows audio endpoints to initialize.";
                    _endpointReadinessLabel.ForeColor = Color.FromArgb(180, 83, 9);
                    _guidanceLabel.Text = "If you just connected the headset, wait a moment and click Refresh.";
                }
            }
            else
            {
                _connectionStatusLabel.Text = $"Connection: Paired, currently disconnected ({_selectedDevice.Name})";
                _connectionStatusLabel.ForeColor = SystemColors.GrayText;
                _endpointReadinessLabel.Text = "Endpoints: Will become available when the headset connects.";
                _endpointReadinessLabel.ForeColor = SystemColors.ControlText;
                _guidanceLabel.Text = "You can save this headset as your preferred device now, or power it on and click Refresh to verify its audio endpoints.";
            }
        }

        public void NavigateNext()
        {
            if (_currentStepIndex < 2)
            {
                ShowStep(_currentStepIndex + 1);
            }
        }

        public void NavigateBack()
        {
            if (_currentStepIndex > 0)
            {
                ShowStep(_currentStepIndex - 1);
            }
        }

        private void ShowStep(int stepIndex)
        {
            _currentStepIndex = Math.Clamp(stepIndex, 0, 2);

            _step1Panel.Visible = (_currentStepIndex == 0);
            _step2Panel.Visible = (_currentStepIndex == 1);
            _step3Panel.Visible = (_currentStepIndex == 2);

            _backButton.Enabled = (_currentStepIndex > 0);
            _nextOrFinishButton.Text = (_currentStepIndex == 2) ? "Finish" : "Next >";
            _stepIndicatorLabel.Text = $"Step {_currentStepIndex + 1} of 3";

            switch (_currentStepIndex)
            {
                case 0:
                    _stepTitleLabel.Text = "Welcome to EasyHFP";
                    break;
                case 1:
                    _stepTitleLabel.Text = "Preferences & Windows Integration";
                    break;
                case 2:
                    _stepTitleLabel.Text = "Ready to Use EasyHFP";
                    UpdateSummaryText();
                    break;
            }
        }

        private void UpdateSummaryText()
        {
            string headsetSummary = _selectedDevice != null
                ? $"{_selectedDevice.Name} [{_selectedDevice.Mac}]"
                : "None selected (can be chosen later in the main window)";

            string endpointSummary;
            if (_selectedDevice == null)
            {
                endpointSummary = "No headset selected";
            }
            else if ((_selectedDevice.Connected || (_selectedSnapshot?.IsBluetoothConnected ?? false)) &&
                     (_selectedSnapshot?.IsRenderEndpointActive ?? false) &&
                     (_selectedSnapshot?.IsCaptureEndpointActive ?? false))
            {
                endpointSummary = "Ready (A2DP + HFP available)";
            }
            else if (_selectedDevice.Connected || (_selectedSnapshot?.IsBluetoothConnected ?? false))
            {
                endpointSummary = "Connected (verify audio endpoints in main window)";
            }
            else
            {
                endpointSummary = "Paired (currently disconnected)";
            }

            _summaryContentLabel.Text =
                $"Preferred Headset:  {headsetSummary}\n" +
                $"Device Readiness:   {endpointSummary}\n\n" +
                $"Windows Start Search Shortcut:  {(_addToStartMenuCheck.Checked ? "Enabled (EasyHFP.lnk)" : "Disabled")}\n" +
                $"Launch When Windows Starts:     {(_startWithWindowsCheck.Checked ? "Enabled (minimized in tray)" : "Disabled")}\n" +
                $"Close Window to System Tray:    {(_closeToTrayCheck.Checked ? "Enabled" : "Disabled")}\n" +
                $"Desktop Notifications:          {(_notificationsCheck.Checked ? "Enabled" : "Disabled")}\n" +
                $"Global Toggle Shortcut:         {_workingConfig.Hotkey}";
        }

        public bool CompleteOnboarding()
        {
            if (_selectedDevice != null)
            {
                _workingConfig.DefaultMac = _selectedDevice.Mac;
                _workingConfig.DefaultName = _selectedDevice.Name;
                _workingConfig.KnownDevices[_selectedDevice.Mac] = _selectedDevice.Name;
            }

            _workingConfig.AddToStartMenu = _addToStartMenuCheck.Checked;
            _workingConfig.StartWithWindows = _startWithWindowsCheck.Checked;
            _workingConfig.MinimizeToTray = _closeToTrayCheck.Checked;
            _workingConfig.ShowNotifications = _notificationsCheck.Checked;
            _workingConfig.SetupCompleted = true;

            try
            {
                _shortcutService.SetShortcutEnabled(_workingConfig.AddToStartMenu);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not apply Start Menu shortcut preference during onboarding: {ex.Message}");
            }

            try
            {
                _configService.SetAutoStart(_workingConfig.StartWithWindows);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not apply Windows startup preference during onboarding: {ex.Message}");
            }

            _configService.SaveConfig(_workingConfig);
            AppLogger.Info($"First-run onboarding completed (selectedDevice='{_workingConfig.DefaultName}' [{_workingConfig.DefaultMac}], startMenu={_workingConfig.AddToStartMenu}, autoStart={_workingConfig.StartWithWindows}).");

            OnboardingCompleted = true;
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        public void BringToFrontFromSecondaryInstance()
        {
            WakeupCount++;
            if (IsDisposed) return;

            if (InvokeRequired && IsHandleCreated)
            {
                BeginInvoke(new Action(BringToFrontFromSecondaryInstance));
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Show();
            Activate();
            if (IsHandleCreated)
            {
                NativeUser32.SetForegroundWindow(Handle);
            }
        }
    }
}
