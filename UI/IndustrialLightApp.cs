using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Basler.Pylon;
using VM.Core;
using VM.PlatformSDKCS;
using VMControls.Winform.Release;
using LightControllerApp.Core;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace LightControllerApp.UI
{
    public class IndustrialLightApp : Form
    {
        public static IndustrialLightApp Instance;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private Timer _timer;
        private GUICamera _cam = new GUICamera();
        private bool _isScanning = false;
        private int _zoom = 1;
        private readonly string[] _pos = { "Left", "Right", "Top", "Bottom", "Full" };
        private const string AllLightsOffCommand = "$L0=0-0,L1=0-0,L2=0-0,L3=0-0,L4=0-0,L5=0-0#";

        private enum LightOutputMode
        {
            Continuous24V,
            Strobe48V
        }

        private LightOutputMode _outputMode = LightOutputMode.Strobe48V;

        private TextBox[] txtBr = new TextBox[5], txtW = new TextBox[5], txtE = new TextBox[5];
        private TextBox txtIp, txtPort, txtStt, txtDir, txtSol;
        private Label lblTcp, lblRes;
        private Button bC, bD, bNet, bS, bMode24V, bMode48V, bOffAll;
        private RichTextBox rLog;
        private PictureBox pic;
        private TrackBar tbZ;

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Instance = new IndustrialLightApp();
            Application.Run(Instance);
        }

        public IndustrialLightApp()
        {
            Instance = this;
            InitUI();
            InitNet();
            LoadConfig();
            this.FormClosing += async (s, e) => { SaveConfig(); await TurnOffAllLights(); };
        }

        public static void AppLog(string t, string m, Color c)
        {
            if (Instance == null || Instance.IsDisposed) return;
            if (Instance.InvokeRequired) { Instance.Invoke(new Action(() => AppLog(t, m, c))); return; }
            Instance.rLog.SelectionStart = Instance.rLog.TextLength;
            Instance.rLog.SelectionColor = c;
            Instance.rLog.AppendText(string.Format("[{0:HH:mm:ss}] [{1}] {2}\n", DateTime.Now, t, m));
            Instance.rLog.ScrollToCaret();
        }

        private void InitNet()
        {
            _timer = new Timer { Interval = 2000 };
            _timer.Tick += async (s, e) =>
            {
                if (_tcp != null && _tcp.Connected) { lblTcp.Text = "Status: OK"; lblTcp.ForeColor = Color.Green; return; }
                lblTcp.Text = "Status: Error"; lblTcp.ForeColor = Color.Red;
                try
                {
                    _tcp = new TcpClient { NoDelay = true };
                    await _tcp.ConnectAsync(txtIp.Text, int.Parse(txtPort.Text));
                    _stream = _tcp.GetStream();
                    IndustrialLightApp.AppLog("SYS", "TCP Connected", Color.Green);
                    ReadLoop(); // Start async read loop
                }
                catch { }
            };
            _timer.Start();
        }

        private async Task ReadLoop()
        {
            byte[] b = new byte[1024];
            try
            {
                while (_tcp != null && _tcp.Connected)
                {
                    int n = await _stream.ReadAsync(b, 0, b.Length);
                    if (n == 0) break;
                    IndustrialLightApp.AppLog("RX", Encoding.ASCII.GetString(b, 0, n).Trim(), Color.Cyan);
                }
            }
            catch { }
        }

        private async Task Send(string c)
        {
            if (_stream == null || _tcp == null || !_tcp.Connected) return;
            try
            {
                byte[] d = Encoding.ASCII.GetBytes(c);
                await _stream.WriteAsync(d, 0, d.Length);
                await _stream.FlushAsync();
                IndustrialLightApp.AppLog("TX", c, Color.Gray);
            }
            catch { }
        }

        private async Task Light(int idx, string br)
        {
            // idx: 0=Left, 1=Right, 2=Top, 3=Bottom, 4=Full (per user request table)
            StringBuilder sb = new StringBuilder("$");
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(",");
                bool isOn = (idx == 4) || (i == idx);
                sb.Append(string.Format("L{0}=0-{1}", i, isOn ? br : "0"));
            }
            sb.Append("#");
            await Send(sb.ToString());
        }

        private async Task TurnOffAllLights()
        {
            await Send(AllLightsOffCommand);
        }

        private async Task Set24VContinuousMode()
        {
            // Safety reset before switching mode.
            await TurnOffAllLights();
            await Task.Delay(50);
            await Send("$VOL=0#"); // DC22V (24V LED) mode
            await Send("$TR=15#"); // Always-on mode
            _outputMode = LightOutputMode.Continuous24V;
            IndustrialLightApp.AppLog("SYS", "Switched to 24V Continuous Mode", Color.Orange);
        }

        private async Task Set48VStrobeMode()
        {
            // Safety reset before switching mode.
            await TurnOffAllLights();
            await Task.Delay(50);
            await Send("$VOL=1#"); // DC48V boost mode
            await Send("$TR=6#");  // Internal falling-edge trigger mode
            _outputMode = LightOutputMode.Strobe48V;
            IndustrialLightApp.AppLog("SYS", "Set to 48V Strobe Mode", Color.Purple);
        }

        private void SetCameraExposure(double us)
        {
            if (_cam == null || !_cam.IsOpen) return;
            try
            {
                var p = _cam.Parameters;
                if (p != null && p.Contains(PLCamera.ExposureTime))
                {
                    p[PLCamera.ExposureTime].TrySetValue(us);
                    IndustrialLightApp.AppLog("CAM", "Exposure set to " + us + "us", Color.DarkCyan);
                }
            }
            catch (Exception ex) { IndustrialLightApp.AppLog("ERR", "Cam Exp: " + ex.Message, Color.Red); }
        }

        private void LoadConfig()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p);
                if (lines.Length >= 1) txtDir.Text = lines[0];
                if (lines.Length >= 2) txtStt.Text = lines[1];
                if (lines.Length >= 3)
                {
                    string[] brs = lines[2].Split(',');
                    for (int i = 0; i < 5; i++) if (i < brs.Length) txtBr[i].Text = brs[i];
                }
                if (lines.Length >= 4)
                {
                    string[] ws = lines[3].Split(',');
                    for (int i = 0; i < 5; i++) if (i < ws.Length) txtW[i].Text = ws[i];
                }
                if (lines.Length >= 5)
                {
                    string[] es = lines[4].Split(',');
                    for (int i = 0; i < 5; i++) if (i < es.Length) txtE[i].Text = es[i];
                }
                IndustrialLightApp.AppLog("SYS", "Config Loaded", Color.Blue);
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                List<string> lines = new List<string>();
                lines.Add(txtDir.Text);
                lines.Add(txtStt.Text);
                lines.Add(string.Join(",", Array.ConvertAll(txtBr, t => t.Text)));
                lines.Add(string.Join(",", Array.ConvertAll(txtW, t => t.Text)));
                lines.Add(string.Join(",", Array.ConvertAll(txtE, t => t.Text)));
                File.WriteAllLines(p, lines.ToArray());
            }
            catch { }
        }

        private void InitUI()
        {
            this.Text = "Industrial Light & Camera Controller";
            this.Size = new Size(1400, 900);
            this.MinimumSize = new Size(1200, 760);

            var L = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(6)
            };
            L.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            L.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            L.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
            L.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            L.RowStyles.Add(new RowStyle(SizeType.Absolute, 210F));

            // 1. TRẠNG THÁI CAMERA
            var g1 = new GroupBox { Text = "1. TRẠNG THÁI CAMERA", Dock = DockStyle.Fill };
            var g1Grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
            g1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            g1Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            g1Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            g1Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            var bF = new Button { Text = "Tìm Cam", Dock = DockStyle.Fill, FlatStyle = FlatStyle.System };
            var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            bC = new Button { Text = "Kết Nối Cam", Dock = DockStyle.Fill, BackColor = Color.LightSkyBlue };
            bD = new Button { Text = "Ngắt Cam", Dock = DockStyle.Fill, Enabled = false };
            g1Grid.Controls.Add(bF, 0, 0);
            g1Grid.Controls.Add(cb, 1, 0);
            g1Grid.Controls.Add(bC, 0, 1);
            g1Grid.Controls.Add(bD, 1, 1);
            g1.Controls.Add(g1Grid);

            // 2. TRẠNG THÁI ĐIỀU KHIỂN ĐÈN
            var g2 = new GroupBox { Text = "2. TRẠNG THÁI ĐIỀU KHIỂN ĐÈN", Dock = DockStyle.Fill };
            var g2Grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3, Padding = new Padding(8) };
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            g2Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            g2Grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            g2Grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            g2Grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

            txtIp = new TextBox { Text = "192.168.1.2", Dock = DockStyle.Fill };
            txtPort = new TextBox { Text = "1200", Dock = DockStyle.Fill };
            bNet = new Button { Text = "Kết Nối Đèn", Dock = DockStyle.Fill, BackColor = Color.LightSkyBlue };
            lblTcp = new Label { Text = "Disconnected", Dock = DockStyle.Fill, ForeColor = Color.Red, TextAlign = ContentAlignment.MiddleLeft };
            txtDir = new TextBox { Text = @"D:\THH\controller_den\", Dock = DockStyle.Fill };
            var bBr = new Button { Text = "...", Dock = DockStyle.Fill };
            var bO = new Button { Text = "Mở Folder", Dock = DockStyle.Fill };
            txtStt = new TextBox { Text = "1", Dock = DockStyle.Fill };

            g2Grid.Controls.Add(new Label { Text = "IP:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            g2Grid.Controls.Add(txtIp, 1, 0);
            g2Grid.Controls.Add(txtPort, 2, 0);
            g2Grid.Controls.Add(bNet, 3, 0);
            g2Grid.Controls.Add(lblTcp, 4, 0);
            g2Grid.SetColumnSpan(lblTcp, 2);

            g2Grid.Controls.Add(new Label { Text = "Folder:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            g2Grid.Controls.Add(txtDir, 1, 1);
            g2Grid.SetColumnSpan(txtDir, 2);
            g2Grid.Controls.Add(bBr, 3, 1);
            g2Grid.Controls.Add(bO, 4, 1);
            g2Grid.SetColumnSpan(bO, 2);

            g2Grid.Controls.Add(new Label { Text = "STT:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            g2Grid.Controls.Add(txtStt, 1, 2);
            g2.Controls.Add(g2Grid);

            var topLeft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            topLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            topLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            topLeft.Controls.Add(g1, 0, 0);
            topLeft.Controls.Add(g2, 1, 0);

            // 3. THÔNG SỐ CẦN QUÉT CHO 5 BƯỚC
            var g3 = new GroupBox { Text = "3. THÔNG SỐ CẦN QUÉT CHO 5 BƯỚC", Dock = DockStyle.Fill };
            var g3Grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            g3Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            g3Grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));

            var T = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 7 };
            T.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
            T.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            T.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            T.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            T.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
            T.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            for (int r = 0; r < 5; r++) T.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            T.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            T.Controls.Add(new Label { Text = "Vị Trí Đèn", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            T.Controls.Add(new Label { Text = "Độ Sáng Đèn (0-255)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            T.Controls.Add(new Label { Text = "Gia Tốc Chờ Đèn (ms)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            T.Controls.Add(new Label { Text = "Phơi Sáng Cam (us)", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);
            T.Controls.Add(new Label { Text = "Chạy Thử", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);

            for (int i = 0; i < 5; i++)
            {
                int ii = i;
                T.Controls.Add(new Label { Text = _pos[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i + 1);
                txtBr[i] = new TextBox { Text = "150", Dock = DockStyle.Fill };
                txtW[i] = new TextBox { Text = "100", Dock = DockStyle.Fill };
                txtE[i] = new TextBox { Text = "1000", Dock = DockStyle.Fill };
                var bT = new Button { Text = "BẬT THỬ", Dock = DockStyle.Fill, BackColor = Color.LightSkyBlue };
                bT.Click += async (s, e) =>
                {
                    SetCameraExposure(double.Parse(txtE[ii].Text));
                    // Also send it to light controller
                    await Send(string.Format("$T{0}=0-{1}#", ii, txtE[ii].Text));
                    await Light(ii, txtBr[ii].Text);
                    if (_outputMode == LightOutputMode.Strobe48V)
                    {
                        await Task.Delay(2000);
                        await TurnOffAllLights();
                    }
                };
                T.Controls.Add(txtBr[i], 1, i + 1);
                T.Controls.Add(txtW[i], 2, i + 1);
                T.Controls.Add(txtE[i], 3, i + 1);
                T.Controls.Add(bT, 4, i + 1);
            }

            var bApply = new Button { Text = "LƯU & ÁP DỤNG THAM SỐ", Dock = DockStyle.Bottom, Height = 40, BackColor = Color.LightBlue, Font = new Font(this.Font, FontStyle.Bold) };
            bApply.Click += (s, e) => { SaveConfig(); IndustrialLightApp.AppLog("SYS", "Settings Applied & Saved", Color.Green); };
            T.Controls.Add(bApply, 0, 6);
            T.SetColumnSpan(bApply, 5);

            bMode24V = new Button { Text = "CHẾ ĐỘ 24V (THƯỜNG SÁNG)", Dock = DockStyle.Fill, BackColor = Color.LightYellow };
            bMode48V = new Button { Text = "CHẾ ĐỘ 48V (STROBE)", Dock = DockStyle.Fill, BackColor = Color.MediumPurple, ForeColor = Color.White };
            bOffAll = new Button { Text = "TẮT TẤT CẢ ĐÈN", Dock = DockStyle.Fill, BackColor = Color.LightCoral, Font = new Font(this.Font, FontStyle.Bold) };
            bS = new Button { Text = "START SCAN", Dock = DockStyle.Fill, BackColor = Color.LightGreen, Font = new Font(this.Font, FontStyle.Bold) };

            var modePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            modePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            modePanel.Controls.Add(bMode24V, 0, 0);
            modePanel.Controls.Add(bMode48V, 1, 0);
            modePanel.Controls.Add(bOffAll, 0, 1);
            modePanel.Controls.Add(bS, 1, 1);

            g3Grid.Controls.Add(T, 0, 0);
            g3Grid.Controls.Add(modePanel, 0, 1);
            g3.Controls.Add(g3Grid);

            // 4. LIVE
            var g4 = new GroupBox { Text = "4. LIVE", Dock = DockStyle.Fill };
            pic = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            tbZ = new TrackBar { Minimum = 1, Maximum = 10, Value = 1, Dock = DockStyle.Bottom };
            tbZ.Scroll += (s, e) => { _zoom = tbZ.Value; };
            g4.Controls.Add(pic);
            g4.Controls.Add(tbZ);

            // 5. VISION
            var g5 = new GroupBox { Text = "5. VISION", Dock = DockStyle.Fill };
            var g5Grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(8) };
            g5Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            g5Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            g5Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            g5Grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            g5Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var bL = new Button { Text = "Load Sol", Dock = DockStyle.Fill };
            txtSol = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            lblRes = new Label { Text = "Result: ---", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            rLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = Color.White };
            g5Grid.Controls.Add(bL, 0, 0);
            g5Grid.Controls.Add(txtSol, 1, 0);
            g5Grid.Controls.Add(lblRes, 2, 0);
            g5Grid.Controls.Add(rLog, 0, 1);
            g5Grid.SetColumnSpan(rLog, 3);
            g5.Controls.Add(g5Grid);

            L.Controls.Add(topLeft, 0, 0);
            L.Controls.Add(g3, 0, 1);
            L.Controls.Add(g4, 1, 0);
            L.SetRowSpan(g4, 2);
            L.Controls.Add(g5, 0, 2);
            L.SetColumnSpan(g5, 2);
            this.Controls.Add(L);

            // Events
            bF.Click += (s, e) => { cb.Items.Clear(); foreach (var d in CameraFinder.Enumerate()) cb.Items.Add(d[CameraInfoKey.ModelName]); if (cb.Items.Count > 0) cb.SelectedIndex = 0; };
            bC.Click += (s, e) => { if (cb.SelectedIndex >= 0) { _cam.CreateByCameraInfo(CameraFinder.Enumerate()[cb.SelectedIndex]); _cam.OpenCamera(); _cam.GuiCameraFrameReadyForDisplay += OnFrame; _cam.StartContinuousShotGrabbing(); bC.Enabled = false; bD.Enabled = true; } };
            bD.Click += (s, e) => { _cam.CloseCamera(); bC.Enabled = true; bD.Enabled = false; };
            bNet.Click += async (s, e) => { await Send("$?"); }; // Status test
            bBr.Click += (s, e) => { var f = new FolderBrowserDialog(); if (f.ShowDialog() == DialogResult.OK) txtDir.Text = f.SelectedPath; };
            bO.Click += (s, e) => { if (Directory.Exists(txtDir.Text)) Process.Start("explorer.exe", txtDir.Text); };
            bMode24V.Click += async (s, e) => { await Set24VContinuousMode(); };
            bMode48V.Click += async (s, e) => { await Set48VStrobeMode(); };
            bOffAll.Click += async (s, e) =>
            {
                await TurnOffAllLights();
                IndustrialLightApp.AppLog("SYS", "All lights OFF", Color.LightCoral);
            };
            bS.Click += async (s, e) => { await DoScan(); };
            bL.Click += (s, e) => { var f = new OpenFileDialog { Filter = "*.solw|*.solw" }; if (f.ShowDialog() == DialogResult.OK) { txtSol.Text = f.FileName; VmSolution.Load(f.FileName, ""); } };
        }

        private async Task DoScan()
        {
            _isScanning = true;
            int s = int.Parse(txtStt.Text);
            Stopwatch totalSw = Stopwatch.StartNew();
            List<Bitmap> grabbedImages = new List<Bitmap>();
            List<string> logs = new List<string>();

            try
            {
                // Ensure selected output mode before scanning
                if (_outputMode == LightOutputMode.Continuous24V) await Set24VContinuousMode();
                else await Set48VStrobeMode();

                _cam.SetSoftwareTrigger(true); // Switch camera to trigger mode

                for (int i = 0; i < 5; i++)
                {
                    Stopwatch stepSw = Stopwatch.StartNew();
                    // Set Camera Exposure first
                    SetCameraExposure(double.Parse(txtE[i].Text));

                    // Set Brightness and Time for this step - ensuring other lights are OFF
                    StringBuilder sb = new StringBuilder("$");
                    for (int j = 0; j < 4; j++)
                    {
                        if (j > 0) sb.Append(",");
                        bool isOn = (i == 4) || (j == i);
                        sb.Append(string.Format("L{0}=0-{1}", j, isOn ? txtBr[i].Text : "0"));
                        if (isOn) sb.Append(string.Format(",T{0}=0-{1}", j, txtE[i].Text));
                    }
                    sb.Append("#");
                    await Send(sb.ToString());

                    await Task.Delay(int.Parse(txtW[i].Text));

                    if (!_cam.TriggerSoftware())
                    {
                        IndustrialLightApp.AppLog("ERR", string.Format("{0}: Cam Not Ready", _pos[i]), Color.Red);
                    }

                    Bitmap m = null;
                    // Fast polling for frame (5ms delay instead of 100ms)
                    for (int j = 0; j < 100; j++)
                    {
                        m = _cam.GetLatestFrame();
                        if (m != null) break;
                        await Task.Delay(5);
                    }

                    if (m != null)
                    {
                        grabbedImages.Add(m);
                        stepSw.Stop();
                        logs.Add(string.Format("{0}: {1}ms", _pos[i], stepSw.ElapsedMilliseconds));
                    }
                    else
                    {
                        logs.Add(string.Format("{0}: FAILED", _pos[i]));
                    }
                }

                _cam.SetSoftwareTrigger(false); // Switch back to free run

                // In strobe mode, turn off after scan. In 24V mode, keep on as requested.
                if (_outputMode == LightOutputMode.Strobe48V) await TurnOffAllLights();

                // Batch Save from RAM to Disk
                Stopwatch saveSw = Stopwatch.StartNew();
                for (int i = 0; i < grabbedImages.Count; i++)
                {
                    string p = Path.Combine(txtDir.Text, _pos[i], s + ".bmp");
                    if (!Directory.Exists(Path.GetDirectoryName(p))) Directory.CreateDirectory(Path.GetDirectoryName(p));
                    grabbedImages[i].Save(p, ImageFormat.Bmp);
                    grabbedImages[i].Dispose(); // Release RAM immediately after save
                }
                saveSw.Stop();

                totalSw.Stop();
                txtStt.Text = (s + 1).ToString();
                SaveConfig(); // Save STT after increment

                string finalLog = string.Format("Scan Done. Steps: [{0}]. Save: {1}ms. Total: {2}ms", string.Join(" | ", logs.ToArray()), saveSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds);
                IndustrialLightApp.AppLog("SCAN", finalLog, Color.Green);
            }
            catch (Exception ex) { IndustrialLightApp.AppLog("ERR", ex.Message, Color.Red); }
            finally
            {
                _cam.SetSoftwareTrigger(false);
                _isScanning = false;
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (_isScanning) return;
            var m = _cam.GetLatestFrame();
            if (m != null)
            {
                var o = pic.Image;
                if (_zoom > 1)
                {
                    int w = m.Width / _zoom, h = m.Height / _zoom;
                    pic.Image = m.Clone(new Rectangle((m.Width - w) / 2, (m.Height - h) / 2, w, h), m.PixelFormat);
                }
                else pic.Image = (Bitmap)m.Clone();
                if (o != null) o.Dispose(); m.Dispose();
            }
        }
    }
}
