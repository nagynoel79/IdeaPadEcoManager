using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using IdeaPadEcoManager.Models;
using IdeaPadEcoManager.Services;

namespace IdeaPadEcoManager.UI
{
    public class DashboardForm : Form
    {
        private readonly ProfileManager _profileManager;
        private readonly bool _startMinimized;
        private readonly IContainer _components;
        private readonly NotifyIcon _notifyIcon;

        // UI Controls
        private Label _lblStatusHeader = null!;
        private Label _lblAdminBadge = null!;
        private Button _btnRestartAdmin = null!;

        // Overview controls
        private Label _lblPowerSource = null!;
        private ProgressBar _pbBattery = null!;
        private Label _lblBatteryPercent = null!;
        private Label _lblCurrentProfile = null!;
        private RadioButton _rbAuto = null!;
        private RadioButton _rbForceEco = null!;
        private RadioButton _rbForcePerf = null!;

        private Label _lblFanMode = null!;
        private Button _btnFanQuiet = null!;
        private Button _btnFanBalanced = null!;
        private Button _btnFanPerf = null!;

        private TrackBar _tbBrightness = null!;
        private Label _lblBrightnessVal = null!;
        private Label _lblActiveScheme = null!;

        // GPU controls
        private ListView _lvIdes = null!;
        private Button _btnPinIdes = null!;
        private ListView _lvGpuProcs = null!;
        private Label _lblGpuStatus = null!;
        private Button _btnRefreshGpu = null!;

        // Logs
        private RichTextBox _rtbLogs = null!;
        private CheckBox _chkAutoStart = null!;

        // Tray menu items
        private ToolStripMenuItem _menuStatusHeader = null!;
        private ToolStripMenuItem _menuAutoMode = null!;
        private ToolStripMenuItem _menuManualEco = null!;
        private ToolStripMenuItem _menuManualPerf = null!;
        private ToolStripMenuItem _menuFanQuiet = null!;
        private ToolStripMenuItem _menuFanBalanced = null!;
        private ToolStripMenuItem _menuFanPerf = null!;
        private ToolStripMenuItem _menuGpuStatus = null!;
        private ToolStripMenuItem _menuAutoStart = null!;

        private readonly System.Windows.Forms.Timer _uiRefreshTimer;

        public DashboardForm(ProfileManager profileManager, bool startMinimized = false)
        {
            _profileManager = profileManager;
            _startMinimized = startMinimized;
            _components = new Container();

            // Set Form Icon to matching icon
            Icon = IconHelper.CreateTrayIcon(_profileManager.CurrentActiveProfile);

            InitializeComponents();
            ApplyDarkTheme();

            // Setup NotifyIcon with explicit Container and top-level Form attachment
            _notifyIcon = new NotifyIcon(_components)
            {
                Icon = Icon,
                Text = "Lenovo IdeaPad Eco Manager",
                Visible = true
            };

            BuildContextMenu();
            UpdateTrayVisuals();

            _notifyIcon.DoubleClick += (s, e) => ShowDashboard();
            _notifyIcon.BalloonTipClicked += (s, e) => ShowDashboard();

            // Timer
            _uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _uiRefreshTimer.Tick += (s, e) =>
            {
                if (Visible && IsHandleCreated && !IsDisposed)
                {
                    RefreshDashboardData();
                }
            };

            // Wire profile manager events
            _profileManager.ProfileApplied += (profile, reason) =>
            {
                UpdateTrayVisuals();

                string title = profile == AppPowerProfile.CodingEco
                    ? "🌿 Coding Eco & Quiet Aktiválva"
                    : "🚀 Full Performance Aktiválva";

                string text = profile == AppPowerProfile.CodingEco
                    ? "Akkumulátor: Csendes ventilátor, CPU 80%, Fényerő 45%, RTX 3050 felügyelet."
                    : "Töltő csatlakoztatva: Maximális CPU/GPU teljesítmény, Lenovo Performance hűtés.";

                try
                {
                    _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
                }
                catch { }

                if (Visible && IsHandleCreated && !IsDisposed)
                {
                    RefreshDashboardData();
                }
            };

            _profileManager.RtxMonitor.OnGpuWakeupAlert += (procs) =>
            {
                string procNames = string.Join(", ", procs.Take(3).Select(p => p.Name));
                if (procs.Count > 3) procNames += $" (+{procs.Count - 3} további)";

                try
                {
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        "⚠️ RTX 3050 Aktív Akkumulátorról!",
                        $"A dGPU felébredt az akkun:\n{procNames}\nKattints a részletekért!",
                        ToolTipIcon.Warning
                    );
                }
                catch { }
            };

            AppLogger.OnLog += AppendLogMessage;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_startMinimized)
            {
                BeginInvoke(new Action(() =>
                {
                    Hide();
                    ShowInTaskbar = false;
                }));
            }
            else
            {
                RefreshDashboardData();
                _uiRefreshTimer.Start();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                ShowInTaskbar = true;
                RefreshDashboardData();
                _uiRefreshTimer.Start();
            }
            else
            {
                _uiRefreshTimer.Stop();
            }
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip(_components);

            var titleItem = new ToolStripMenuItem("Lenovo Eco & Performance Manager")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            menu.Items.Add(titleItem);

            _menuStatusHeader = new ToolStripMenuItem("Állapot: Betöltés...")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic)
            };
            menu.Items.Add(_menuStatusHeader);

            menu.Items.Add(new ToolStripSeparator());

            // Operation Mode submenu
            var menuModes = new ToolStripMenuItem("⚙️ Működési Profil Mód");
            _menuAutoMode = new ToolStripMenuItem("✓ Automatikus (Töltő követése)", null, (s, e) =>
            {
                _profileManager.SetOperationMode(OperationMode.Auto);
                UpdateTrayVisuals();
            });
            _menuManualEco = new ToolStripMenuItem("Kényszerített Coding Eco (Akkus)", null, (s, e) =>
            {
                _profileManager.SetOperationMode(OperationMode.ManualEco);
                UpdateTrayVisuals();
            });
            _menuManualPerf = new ToolStripMenuItem("Kényszerített Full Performance (Töltős)", null, (s, e) =>
            {
                _profileManager.SetOperationMode(OperationMode.ManualPerformance);
                UpdateTrayVisuals();
            });
            menuModes.DropDownItems.AddRange(new ToolStripItem[] { _menuAutoMode, _menuManualEco, _menuManualPerf });
            menu.Items.Add(menuModes);

            // Lenovo Fan Mode submenu
            var menuFan = new ToolStripMenuItem("🌀 Lenovo Hűtési Mód (EC)");
            _menuFanQuiet = new ToolStripMenuItem("🔇 Csendes mód (Quiet - 1)", null, (s, e) =>
            {
                _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Quiet);
                UpdateTrayVisuals();
            });
            _menuFanBalanced = new ToolStripMenuItem("⚖ Kiegyensúlyozott mód (Balanced - 2)", null, (s, e) =>
            {
                _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Balanced);
                UpdateTrayVisuals();
            });
            _menuFanPerf = new ToolStripMenuItem("🚀 Teljesítmény mód (Performance - 3)", null, (s, e) =>
            {
                _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Performance);
                UpdateTrayVisuals();
            });
            menuFan.DropDownItems.AddRange(new ToolStripItem[] { _menuFanQuiet, _menuFanBalanced, _menuFanPerf });
            menu.Items.Add(menuFan);

            menu.Items.Add(new ToolStripSeparator());

            // Pin IDEs
            var menuPinIdes = new ToolStripMenuItem("🔧 IDE-k rögzítése Intel iGPU-ra", null, (s, e) =>
            {
                int count = _profileManager.GpuRegistry.PinAllIdesToIntegratedGpu();
                _notifyIcon.ShowBalloonTip(2500, "IDE GPU Rögzítés", $"{count} fejlesztői környezet konfigurálva Intel iGPU-ra.", ToolTipIcon.Info);
            });
            menu.Items.Add(menuPinIdes);

            // GPU Status
            _menuGpuStatus = new ToolStripMenuItem("🎮 RTX 3050: Ellenőrzés...", null, (s, e) => ShowDashboard());
            menu.Items.Add(_menuGpuStatus);

            menu.Items.Add(new ToolStripSeparator());

            // Open Dashboard
            var menuDashboard = new ToolStripMenuItem("📊 Vezérlőpult megnyitása...", null, (s, e) => ShowDashboard())
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            menu.Items.Add(menuDashboard);

            // Auto-start
            _menuAutoStart = new ToolStripMenuItem("Indítás a Windowsszal", null, (s, e) =>
            {
                bool newState = !StartupService.IsAutoStartEnabled();
                StartupService.SetAutoStart(newState);
                _menuAutoStart.Checked = newState;
            })
            {
                Checked = StartupService.IsAutoStartEnabled()
            };
            menu.Items.Add(_menuAutoStart);

            // Elevation if not admin
            if (!LenovoWmiService.IsAdministrator())
            {
                var menuAdmin = new ToolStripMenuItem("🛡️ Újraindítás Rendszergazdaként", null, (s, e) => RestartElevated());
                menu.Items.Add(menuAdmin);
            }

            menu.Items.Add(new ToolStripSeparator());

            // Exit
            var menuExit = new ToolStripMenuItem("Kilépés", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Application.Exit();
            });
            menu.Items.Add(menuExit);

            _notifyIcon.ContextMenuStrip = menu;
        }

        private void UpdateTrayVisuals()
        {
            try
            {
                var (isAc, percent, isCharging, isSaver) = _profileManager.PowerStatus.GetCurrentStatus();
                var profile = _profileManager.CurrentActiveProfile;

                // Update Icon
                var newIcon = IconHelper.CreateTrayIcon(profile);
                Icon = newIcon;
                _notifyIcon.Icon = newIcon;

                // Update Tooltip
                string profileName = profile == AppPowerProfile.CodingEco ? "Coding Eco" : "Full Perf";
                string pwr = isAc ? $"AC (100%)" : $"Bat ({percent}%)";
                _notifyIcon.Text = $"Lenovo Eco Manager\n{pwr} - {profileName}";

                // Update Menu Checkmarks
                _menuStatusHeader.Text = $"Állapot: {(isAc ? "Hálózatról" : "Akkuról")} ({percent}%) - {profileName}";

                _menuAutoMode.Checked = _profileManager.CurrentOperationMode == OperationMode.Auto;
                _menuManualEco.Checked = _profileManager.CurrentOperationMode == OperationMode.ManualEco;
                _menuManualPerf.Checked = _profileManager.CurrentOperationMode == OperationMode.ManualPerformance;

                // Check dGPU status
                var procs = _profileManager.RtxMonitor.GetRunningProcesses();
                if (procs.Count == 0)
                {
                    _menuGpuStatus.Text = "🎮 RTX 3050: 💤 Alszik (0 app)";
                }
                else
                {
                    _menuGpuStatus.Text = $"🎮 RTX 3050: ⚡ Aktív ({procs.Count} app)";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UpdateTrayVisuals error: {ex.Message}");
            }
        }

        public void ShowDashboard()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowDashboard));
                return;
            }

            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
            Activate();
            RefreshDashboardData();
        }

        private void InitializeComponents()
        {
            Text = "Lenovo IdeaPad Gaming 3 - Eco & Performance Manager";
            Size = new Size(860, 680);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            // Header Panel
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 85,
                Padding = new Padding(15)
            };

            _lblStatusHeader = new Label
            {
                Text = "Lenovo IdeaPad Gaming 3 15IHU6",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 12)
            };

            var lblSub = new Label
            {
                Text = "Intel Core i5-11320H | NVIDIA GeForce RTX 3050 4GB | 60Hz Display",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(16, 45)
            };

            bool isAdmin = LenovoWmiService.IsAdministrator();
            _lblAdminBadge = new Label
            {
                Text = isAdmin ? "✓ Rendszergazda (WMI Aktív)" : "⚠ Korlátozott jog (WMI Adminisztrátort igényel)",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = isAdmin ? Color.LightGreen : Color.Orange,
                AutoSize = true,
                Location = new Point(560, 18)
            };

            _btnRestartAdmin = new Button
            {
                Text = "Futtatás Rendszergazdaként",
                Size = new Size(200, 28),
                Location = new Point(560, 42),
                Visible = !isAdmin,
                Cursor = Cursors.Hand
            };
            _btnRestartAdmin.Click += (s, e) => RestartElevated();

            pnlHeader.Controls.AddRange(new Control[] { _lblStatusHeader, lblSub, _lblAdminBadge, _btnRestartAdmin });

            // Tab Control
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10)
            };

            var tabOverview = new TabPage("⚡ Áttekintés & Profilok");
            var tabGpu = new TabPage("🎮 GPU & IDE Felügyelet");
            var tabLogs = new TabPage("📜 Napló & Beállítások");

            BuildOverviewTab(tabOverview);
            BuildGpuTab(tabGpu);
            BuildLogsTab(tabLogs);

            tabControl.TabPages.AddRange(new[] { tabOverview, tabGpu, tabLogs });

            Controls.Add(tabControl);
            Controls.Add(pnlHeader);
        }

        private void BuildOverviewTab(TabPage tab)
        {
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(15)
            };

            // 1. Power & Profile Box
            var grpPower = new GroupBox
            {
                Text = "Áramellátás és Működési Profil",
                Width = 790,
                Height = 130,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            _lblPowerSource = new Label
            {
                Text = "Tápforrás: Ellenőrzés...",
                Font = new Font("Segoe UI", 11),
                Location = new Point(20, 30),
                AutoSize = true
            };

            _pbBattery = new ProgressBar
            {
                Location = new Point(20, 60),
                Size = new Size(280, 22),
                Style = ProgressBarStyle.Continuous
            };

            _lblBatteryPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 10),
                Location = new Point(310, 62),
                AutoSize = true
            };

            _lblCurrentProfile = new Label
            {
                Text = "Aktív Profil: ...",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(20, 92),
                AutoSize = true
            };

            _rbAuto = new RadioButton
            {
                Text = "Automatikus (Töltő követése)",
                Location = new Point(460, 28),
                AutoSize = true,
                Checked = _profileManager.CurrentOperationMode == OperationMode.Auto,
                Font = new Font("Segoe UI", 9.5f)
            };
            _rbAuto.CheckedChanged += (s, e) => { if (_rbAuto.Checked) _profileManager.SetOperationMode(OperationMode.Auto); RefreshDashboardData(); };

            _rbForceEco = new RadioButton
            {
                Text = "Kényszerített Coding Eco (Akkus)",
                Location = new Point(460, 58),
                AutoSize = true,
                Checked = _profileManager.CurrentOperationMode == OperationMode.ManualEco,
                Font = new Font("Segoe UI", 9.5f)
            };
            _rbForceEco.CheckedChanged += (s, e) => { if (_rbForceEco.Checked) _profileManager.SetOperationMode(OperationMode.ManualEco); RefreshDashboardData(); };

            _rbForcePerf = new RadioButton
            {
                Text = "Kényszerített Full Performance (Töltős)",
                Location = new Point(460, 88),
                AutoSize = true,
                Checked = _profileManager.CurrentOperationMode == OperationMode.ManualPerformance,
                Font = new Font("Segoe UI", 9.5f)
            };
            _rbForcePerf.CheckedChanged += (s, e) => { if (_rbForcePerf.Checked) _profileManager.SetOperationMode(OperationMode.ManualPerformance); RefreshDashboardData(); };

            grpPower.Controls.AddRange(new Control[] { _lblPowerSource, _pbBattery, _lblBatteryPercent, _lblCurrentProfile, _rbAuto, _rbForceEco, _rbForcePerf });

            // 2. Lenovo Cooling Box
            var grpCooling = new GroupBox
            {
                Text = "Lenovo Vantage / EC Hűtési Profil (Smart Fan)",
                Width = 790,
                Height = 110,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            _lblFanMode = new Label
            {
                Text = "EC Mód: Lekérdezés...",
                Font = new Font("Segoe UI", 10.5f),
                Location = new Point(20, 32),
                AutoSize = true
            };

            _btnFanQuiet = new Button
            {
                Text = "🔇 Csendes (Quiet - 1)",
                Location = new Point(20, 65),
                Size = new Size(180, 32),
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _btnFanQuiet.Click += (s, e) => { _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Quiet); RefreshDashboardData(); };

            _btnFanBalanced = new Button
            {
                Text = "⚖ Kiegyensúlyozott (2)",
                Location = new Point(210, 65),
                Size = new Size(180, 32),
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _btnFanBalanced.Click += (s, e) => { _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Balanced); RefreshDashboardData(); };

            _btnFanPerf = new Button
            {
                Text = "🚀 Teljesítmény (3)",
                Location = new Point(400, 65),
                Size = new Size(180, 32),
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _btnFanPerf.Click += (s, e) => { _profileManager.LenovoWmi.SetSmartFanMode(LenovoFanMode.Performance); RefreshDashboardData(); };

            grpCooling.Controls.AddRange(new Control[] { _lblFanMode, _btnFanQuiet, _btnFanBalanced, _btnFanPerf });

            // 3. Brightness & Windows Power Plan Box
            var grpPlan = new GroupBox
            {
                Text = "Kijelző Fényerő & Windows Energiagazdálkodás",
                Width = 790,
                Height = 150,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            var lblB = new Label { Text = "Fényerő:", Location = new Point(20, 30), AutoSize = true, Font = new Font("Segoe UI", 9.5f) };
            _tbBrightness = new TrackBar
            {
                Minimum = 10,
                Maximum = 100,
                TickFrequency = 10,
                Location = new Point(85, 25),
                Size = new Size(240, 45)
            };
            _tbBrightness.Scroll += (s, e) =>
            {
                _lblBrightnessVal.Text = $"{_tbBrightness.Value}%";
            };
            _tbBrightness.MouseUp += (s, e) =>
            {
                _profileManager.Brightness.SetBrightness(_tbBrightness.Value);
            };

            _lblBrightnessVal = new Label { Text = "...%", Location = new Point(335, 30), AutoSize = true, Font = new Font("Segoe UI", 9.5f) };

            var btnEcoBright = new Button { Text = "45% (Eco)", Location = new Point(390, 26), Size = new Size(85, 30), Font = new Font("Segoe UI", 9) };
            btnEcoBright.Click += (s, e) => { _profileManager.Brightness.SetBrightness(45); RefreshDashboardData(); };

            var btnNormBright = new Button { Text = "70% (AC)", Location = new Point(485, 26), Size = new Size(85, 30), Font = new Font("Segoe UI", 9) };
            btnNormBright.Click += (s, e) => { _profileManager.Brightness.SetBrightness(70); RefreshDashboardData(); };

            var btnMaxBright = new Button { Text = "100%", Location = new Point(580, 26), Size = new Size(75, 30), Font = new Font("Segoe UI", 9) };
            btnMaxBright.Click += (s, e) => { _profileManager.Brightness.SetBrightness(100); RefreshDashboardData(); };

            _lblActiveScheme = new Label
            {
                Text = "Windows Energiagazdálkodási séma: Lekérdezés...",
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(20, 80),
                AutoSize = true
            };

            var lblTuningInfo = new Label
            {
                Text = "• Coding Eco: CPU max 80% | Turbo Boost: KI | PCIe: Max Savings | Intel GPU: Max Battery\n• Full Performance: CPU 100% | Turbo Boost: BE (Aggressive) | PCIe: KI | Intel GPU: Max Perf",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.DarkGray,
                Location = new Point(20, 105),
                AutoSize = true
            };

            grpPlan.Controls.AddRange(new Control[] { lblB, _tbBrightness, _lblBrightnessVal, btnEcoBright, btnNormBright, btnMaxBright, _lblActiveScheme, lblTuningInfo });

            pnl.Controls.AddRange(new Control[] { grpPower, grpCooling, grpPlan });
            tab.Controls.Add(pnl);
        }

        private void BuildGpuTab(TabPage tab)
        {
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(15)
            };

            // 1. IDEs Box
            var grpIdes = new GroupBox
            {
                Text = "Fejlesztői Környezetek (IDE) Integrált GPU-hoz Rögzítése",
                Width = 790,
                Height = 220,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            _lvIdes = new ListView
            {
                Location = new Point(15, 30),
                Size = new Size(760, 135),
                View = View.Details,
                FullRowSelect = true,
                Font = new Font("Segoe UI", 9)
            };
            _lvIdes.Columns.Add("IDE Név", 140);
            _lvIdes.Columns.Add("Állapot", 170);
            _lvIdes.Columns.Add("Fájl Elérési Út", 420);

            _btnPinIdes = new Button
            {
                Text = "🔧 Minden IDE Rögzítése Intel iGPU-ra (HKCU DirectX Beállítás)",
                Location = new Point(15, 175),
                Size = new Size(420, 32),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnPinIdes.Click += (s, e) =>
            {
                int c = _profileManager.GpuRegistry.PinAllIdesToIntegratedGpu();
                MessageBox.Show($"{c} fejlesztői környezet sikeresen rögzítve az Intel integrált GPU-ra (GpuPreference=1;).",
                    "IDE GPU Rögzítés", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshDashboardData();
            };

            grpIdes.Controls.AddRange(new Control[] { _lvIdes, _btnPinIdes });

            // 2. RTX 3050 Oversight Box
            var grpGpu = new GroupBox
            {
                Text = "NVIDIA GeForce RTX 3050 Valós Idejű Felügyelet",
                Width = 790,
                Height = 230,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            _lblGpuStatus = new Label
            {
                Text = "RTX 3050 Állapot: Lekérdezés...",
                Font = new Font("Segoe UI", 10.5f),
                Location = new Point(15, 30),
                AutoSize = true
            };

            _btnRefreshGpu = new Button
            {
                Text = "🔄 Frissítés",
                Location = new Point(660, 24),
                Size = new Size(115, 30),
                Font = new Font("Segoe UI", 9)
            };
            _btnRefreshGpu.Click += (s, e) => RefreshGpuProcesses();

            _lvGpuProcs = new ListView
            {
                Location = new Point(15, 65),
                Size = new Size(760, 150),
                View = View.Details,
                FullRowSelect = true,
                Font = new Font("Segoe UI", 9)
            };
            _lvGpuProcs.Columns.Add("PID", 80);
            _lvGpuProcs.Columns.Add("Folyamat Név", 200);
            _lvGpuProcs.Columns.Add("GPU Memória", 120);
            _lvGpuProcs.Columns.Add("Elérési Út", 330);

            grpGpu.Controls.AddRange(new Control[] { _lblGpuStatus, _btnRefreshGpu, _lvGpuProcs });

            pnl.Controls.AddRange(new Control[] { grpIdes, grpGpu });
            tab.Controls.Add(pnl);
        }

        private void BuildLogsTab(TabPage tab)
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15)
            };

            var grpSettings = new GroupBox
            {
                Text = "Rendszer Beállítások",
                Dock = DockStyle.Top,
                Height = 65,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            _chkAutoStart = new CheckBox
            {
                Text = "Automatikus indítás a Windows betöltésekor (HKCU Run & Startup)",
                Location = new Point(20, 25),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                Checked = StartupService.IsAutoStartEnabled()
            };
            _chkAutoStart.CheckedChanged += (s, e) =>
            {
                StartupService.SetAutoStart(_chkAutoStart.Checked);
            };

            grpSettings.Controls.Add(_chkAutoStart);

            var grpLogs = new GroupBox
            {
                Text = "Eseménynapló",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Padding = new Padding(10)
            };

            _rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None
            };

            var pnlLogActions = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(0, 5, 0, 0)
            };

            var btnClearLogs = new Button
            {
                Text = "Napló Törlése",
                Size = new Size(120, 30),
                Dock = DockStyle.Left
            };
            btnClearLogs.Click += (s, e) => _rtbLogs.Clear();

            var btnOpenLogDir = new Button
            {
                Text = "Napló Mappa Megnyitása",
                Size = new Size(180, 30),
                Dock = DockStyle.Right
            };
            btnOpenLogDir.Click += (s, e) =>
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IdeaPadEcoManager");
                    if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
                }
                catch { }
            };

            pnlLogActions.Controls.AddRange(new Control[] { btnClearLogs, btnOpenLogDir });

            grpLogs.Controls.Add(_rtbLogs);
            grpLogs.Controls.Add(pnlLogActions);

            pnl.Controls.Add(grpLogs);
            pnl.Controls.Add(grpSettings);

            tab.Controls.Add(pnl);

            // Populate existing logs
            foreach (var log in AppLogger.GetRecentLogs())
            {
                AppendLogMessage(log);
            }
        }

        private void ApplyDarkTheme()
        {
            BackColor = Color.FromArgb(32, 33, 36);
            ForeColor = Color.WhiteSmoke;

            void StyleControls(Control.ControlCollection controls)
            {
                foreach (Control c in controls)
                {
                    if (c is Button btn)
                    {
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.BackColor = Color.FromArgb(50, 52, 56);
                        btn.ForeColor = Color.WhiteSmoke;
                        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                    }
                    else if (c is GroupBox grp)
                    {
                        grp.ForeColor = Color.FromArgb(230, 230, 230);
                        StyleControls(grp.Controls);
                    }
                    else if (c is ListView lv)
                    {
                        lv.BackColor = Color.FromArgb(24, 25, 27);
                        lv.ForeColor = Color.WhiteSmoke;
                    }
                    else if (c is TabControl tc)
                    {
                        foreach (TabPage page in tc.TabPages)
                        {
                            page.BackColor = Color.FromArgb(32, 33, 36);
                            page.ForeColor = Color.WhiteSmoke;
                            StyleControls(page.Controls);
                        }
                    }
                    else if (c is Panel pnl)
                    {
                        StyleControls(pnl.Controls);
                    }
                }
            }

            StyleControls(Controls);
        }

        public void RefreshDashboardData()
        {
            if (!IsHandleCreated || IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshDashboardData));
                return;
            }

            try
            {
                // 1. Power status
                var (isAc, percent, isCharging, isSaver) = _profileManager.PowerStatus.GetCurrentStatus();
                _lblPowerSource.Text = isAc
                    ? $"Tápforrás: 🔌 Töltő (Hálózat) {(isCharging ? "[Töltés...]" : "[Feltöltve]")}"
                    : $"Tápforrás: 🔋 Akkumulátor {(isSaver ? "[Akkukímélő Aktív]" : "")}";
                _pbBattery.Value = Math.Clamp(percent, 0, 100);
                _lblBatteryPercent.Text = $"{percent}%";

                _lblCurrentProfile.Text = _profileManager.CurrentActiveProfile == AppPowerProfile.CodingEco
                    ? "Aktív Profil: 🌿 Coding Eco & Quiet"
                    : "Aktív Profil: 🚀 Full Performance";
                _lblCurrentProfile.ForeColor = _profileManager.CurrentActiveProfile == AppPowerProfile.CodingEco
                    ? Color.LightGreen
                    : Color.Salmon;

                // 2. Lenovo WMI Mode
                var fanResult = _profileManager.LenovoWmi.GetSmartFanMode();
                if (fanResult.Success)
                {
                    _lblFanMode.Text = $"EC Mód: {fanResult.CurrentMode} ({(uint)fanResult.CurrentMode}) - Vantage Szinkronban";
                    _lblFanMode.ForeColor = fanResult.CurrentMode switch
                    {
                        LenovoFanMode.Quiet => Color.LightSkyBlue,
                        LenovoFanMode.Balanced => Color.WhiteSmoke,
                        LenovoFanMode.Performance => Color.Salmon,
                        _ => Color.WhiteSmoke
                    };
                }
                else
                {
                    _lblFanMode.Text = $"EC Mód: {fanResult.Message}";
                    _lblFanMode.ForeColor = Color.Orange;
                }

                // 3. Brightness
                int bright = _profileManager.Brightness.GetBrightness();
                if (bright >= 0)
                {
                    _tbBrightness.Value = Math.Clamp(bright, 10, 100);
                    _lblBrightnessVal.Text = $"{bright}%";
                }

                // 4. Windows Scheme
                var activeScheme = _profileManager.PowerPlan.GetActiveScheme();
                _lblActiveScheme.Text = $"Windows Energiagazdálkodási séma: {activeScheme.Name} ({activeScheme.Guid})";

                // 5. IDEs
                RefreshIdeList();

                // 6. GPU
                RefreshGpuProcesses();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Dashboard refresh error: {ex.Message}");
            }
        }

        private void RefreshIdeList()
        {
            if (!IsHandleCreated || IsDisposed) return;
            var ides = _profileManager.GpuRegistry.CheckIdePreferences();
            _lvIdes.Items.Clear();

            foreach (var ide in ides)
            {
                var item = new ListViewItem(ide.Name);
                item.SubItems.Add(ide.IsConfigured ? "✓ Intel iGPU (GpuPref=1)" : "⚠ Nincs beállítva");
                item.SubItems.Add(ide.Path);
                item.ForeColor = ide.IsConfigured ? Color.LightGreen : Color.Orange;
                _lvIdes.Items.Add(item);
            }
        }

        private void RefreshGpuProcesses()
        {
            if (!IsHandleCreated || IsDisposed) return;
            var procs = _profileManager.RtxMonitor.GetRunningProcesses();
            _lvGpuProcs.Items.Clear();

            if (procs.Count == 0)
            {
                _lblGpuStatus.Text = "RTX 3050 Állapot: 💤 Alszik (0 futó alkalmazás - Minimális fogyasztás)";
                _lblGpuStatus.ForeColor = Color.LightGreen;
            }
            else
            {
                _lblGpuStatus.Text = $"RTX 3050 Állapot: ⚡ Aktív ({procs.Count} folyamat használja a dGPU-t!)";
                _lblGpuStatus.ForeColor = Color.Salmon;

                foreach (var p in procs)
                {
                    var item = new ListViewItem(p.Pid.ToString());
                    item.SubItems.Add(p.Name);
                    string mem = p.MemoryBytes > 0 && p.MemoryBytes < 1000000000000
                        ? $"{(p.MemoryBytes / (1024 * 1024))} MB"
                        : "N/A";
                    item.SubItems.Add(mem);
                    item.SubItems.Add(p.FullPath);
                    _lvGpuProcs.Items.Add(item);
                }
            }
        }

        private void AppendLogMessage(string line)
        {
            if (!IsHandleCreated || IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLogMessage), line);
                return;
            }

            try
            {
                if (_rtbLogs.Lines.Length > 300)
                {
                    _rtbLogs.Clear();
                }

                _rtbLogs.AppendText(line + Environment.NewLine);
                _rtbLogs.SelectionStart = _rtbLogs.Text.Length;
                _rtbLogs.ScrollToCaret();
            }
            catch { }
        }

        private void RestartElevated()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(startInfo);
                _notifyIcon.Visible = false;
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nem sikerült a rendszergazdai indítás: {ex.Message}", "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                try
                {
                    _notifyIcon.ShowBalloonTip(2000, "Lenovo Eco Manager", "Az alkalmazás a tálcán fut tovább. Kattints az ikonra a megnyitáshoz!", ToolTipIcon.Info);
                }
                catch { }
            }
            else
            {
                _notifyIcon.Visible = false;
                base.OnFormClosing(e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _notifyIcon?.Dispose();
                _components?.Dispose();
                _uiRefreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
