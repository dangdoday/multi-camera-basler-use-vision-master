using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Basler.Pylon;
using MultiCameraBaslerApp.Core;
using VM.Core;
using VMControls.Interface;
using VMControls.Winform.Release;
using VM.PlatformSDKCS;
using ImageSourceModuleCs;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Linq;

namespace MultiCameraBaslerApp.UI
{
    public partial class DualCameraApp : Form
    {
        private sealed class CameraOption
        {
            public string SerialNumber { get; }
            public string DisplayName { get; }

            public CameraOption(string serialNumber, string displayName)
            {
                SerialNumber = serialNumber;
                DisplayName = displayName;
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private MultiCameraManager cameraManager;
        private Timer displayTimer;
        private PictureBox picCamera1, picCamera2;
        private ComboBox cboCamera1, cboCamera2;
        private Button btnDiscover, btnConnect, btnDisconnect, btnLoadVisionMaster, btnShowConfig, btnTriggerVM;
        private Button btnBrowseVisionMaster;
        private TextBox txtVisionMasterPath;
        private OpenFileDialog openVisionMasterDialog;
        private NumericUpDown nudExposure1, nudExposure2;
        private Button btnToggleLive;
        private Label lblLiveStatus;
        private bool liveEnabled;
        private RichTextBox rtbLog;
        private Label lblCamera1Info, lblCamera2Info;
        private Label lblVisionMasterStatus;
        private Panel pnlVisionMaster, pnlHomeVisionContainer;
        private Panel pnlHome, pnlSettings;
        private Button btnGoToSettings, btnHomeTrigger, btnBackToHome;
        private VmRenderControl vmRenderControl;
        private bool suppressSelectionEvents;
        
        // TCP Settings
        private TextBox txtTcpIp;
        private NumericUpDown nudTcpPort;
        private Button btnTestTcp;

        // Zoom and Pan variables for Camera 1
        private float zoom1 = 1.0f;
        private System.Drawing.PointF offset1 = new System.Drawing.PointF(0, 0);
        private System.Drawing.Point lastMouse1;
        private bool isDragging1 = false;

        // Zoom and Pan variables for Camera 2
        private float zoom2 = 1.0f;
        private System.Drawing.PointF offset2 = new System.Drawing.PointF(0, 0);
        private System.Drawing.Point lastMouse2;
        private bool isDragging2 = false;

        [STAThread]
        public static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogStartupException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception exception = e.ExceptionObject as Exception;
                if (exception != null)
                {
                    LogStartupException(exception);
                }
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                DualCameraApp form = new DualCameraApp();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                LogStartupException(ex);
            }
        }

        private static void LogStartupException(Exception exception)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.error.txt");
                System.IO.File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + exception + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }

            MessageBox.Show(exception.Message, "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public DualCameraApp()
        {
            InitializeComponent();
            cameraManager = new MultiCameraManager(2);
            cameraManager.StatusChanged += (s, m) => AppLog("SYS", (string)m, Color.Blue);
            cameraManager.CameraDiscovered += (s, m) => AppLog("DISCOVERY", m, Color.Green);
            cameraManager.ErrorOccurred += (s, e) => AppLog("ERROR", ((ExceptionEventArgs)e).Exception.Message, Color.Red);
            
            SetupDisplayTimer();
            this.Load += (s, e) => OnAppLoaded();
        }

        private void InitializeComponent()
        {
            this.Text = "Dual Camera Basler + VisionMaster";
            this.Width = 1500;
            this.Height = 900;
            this.StartPosition = FormStartPosition.CenterScreen;

            openVisionMasterDialog = new OpenFileDialog
            {
                FileName = "Select Solution File",
                Filter = "solw files (*.solw)|*.solw|All files (*.*)|*.*"
            };

            // --- SETTINGS PANEL ---
            pnlSettings = new Panel { Dock = DockStyle.Fill, Visible = false };
            
            TableLayoutPanel mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            Panel leftPanel = new Panel { Dock = DockStyle.Fill };
            TableLayoutPanel cameraLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            
            picCamera1 = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black };
            picCamera2 = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black };

            // Setup Zoom/Pan for Camera 1
            picCamera1.MouseWheel += (s, e) => {
                float oldZoom = zoom1;
                if (e.Delta > 0) zoom1 *= 1.15f; else zoom1 /= 1.15f;
                zoom1 = Math.Max(0.1f, Math.Min(20f, zoom1));
                picCamera1.Invalidate();
            };
            picCamera1.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isDragging1 = true; lastMouse1 = e.Location; } };
            picCamera1.MouseMove += (s, e) => {
                if (isDragging1) {
                    offset1.X += (e.X - lastMouse1.X);
                    offset1.Y += (e.Y - lastMouse1.Y);
                    lastMouse1 = e.Location;
                    picCamera1.Invalidate();
                }
            };
            picCamera1.MouseUp += (s, e) => { isDragging1 = false; };
            picCamera1.Paint += (s, e) => {
                if (picCamera1.Image != null) {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.TranslateTransform(offset1.X, offset1.Y);
                    e.Graphics.ScaleTransform(zoom1, zoom1);
                    e.Graphics.DrawImage(picCamera1.Image, 0, 0);
                }
            };

            // Setup Zoom/Pan for Camera 2
            picCamera2.MouseWheel += (s, e) => {
                float oldZoom = zoom2;
                if (e.Delta > 0) zoom2 *= 1.15f; else zoom2 /= 1.15f;
                zoom2 = Math.Max(0.1f, Math.Min(20f, zoom2));
                picCamera2.Invalidate();
            };
            picCamera2.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isDragging2 = true; lastMouse2 = e.Location; } };
            picCamera2.MouseMove += (s, e) => {
                if (isDragging2) {
                    offset2.X += (e.X - lastMouse2.X);
                    offset2.Y += (e.Y - lastMouse2.Y);
                    lastMouse2 = e.Location;
                    picCamera2.Invalidate();
                }
            };
            picCamera2.MouseUp += (s, e) => { isDragging2 = false; };
            picCamera2.Paint += (s, e) => {
                if (picCamera2.Image != null) {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.TranslateTransform(offset2.X, offset2.Y);
                    e.Graphics.ScaleTransform(zoom2, zoom2);
                    e.Graphics.DrawImage(picCamera2.Image, 0, 0);
                }
            };

            cameraLayout.Controls.Add(picCamera1, 0, 0);
            cameraLayout.Controls.Add(picCamera2, 1, 0);
            
            Panel infoPanel = new Panel { Dock = DockStyle.Bottom, Height = 60 };
            lblCamera1Info = new Label { Text = "Camera 1: N/A", Dock = DockStyle.Top, AutoSize = true };
            lblCamera2Info = new Label { Text = "Camera 2: N/A", Dock = DockStyle.Top, AutoSize = true };
            infoPanel.Controls.Add(lblCamera2Info);
            infoPanel.Controls.Add(lblCamera1Info);
            
            leftPanel.Controls.Add(cameraLayout);
            leftPanel.Controls.Add(infoPanel);

            Panel rightPanel = new Panel { Dock = DockStyle.Fill };
            TableLayoutPanel rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(4)
            };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 380F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 280F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140F)); // For TCP Group
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // For Back button

            GroupBox cameraGroup = new GroupBox { Text = "1. TRẠNG THÁI CAMERA", Dock = DockStyle.Fill };
            TableLayoutPanel cameraGroupLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
                Padding = new Padding(8)
            };
            cameraGroupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            cameraGroupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            cameraGroupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            btnDiscover = new Button { Text = "Tìm Cam", Dock = DockStyle.Fill };
            btnDiscover.Click += (s, e) => RefreshCameraList();

            cboCamera1 = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCamera2 = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCamera1.SelectedIndexChanged += (s, e) => UpdateSelectionLabels();
            cboCamera2.SelectedIndexChanged += (s, e) => UpdateSelectionLabels();

            btnConnect = new Button { Text = "Kết Nối Cam", Dock = DockStyle.Fill, BackColor = Color.LightSkyBlue };
            btnConnect.Click += (s, e) => ConnectSelectedCameras();
            btnConnect.MinimumSize = new Size(0, 32);

            btnDisconnect = new Button { Text = "Ngắt Cam", Dock = DockStyle.Fill };
            btnDisconnect.Click += (s, e) => DisconnectCameras();
            btnDisconnect.MinimumSize = new Size(0, 32);

            nudExposure1 = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 1000000,
                Increment = 100,
                Value = 10000
            };
            nudExposure1.ValueChanged += (s, e) => ApplyExposure(0, nudExposure1);

            nudExposure2 = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 1000000,
                Increment = 100,
                Value = 10000
            };
            nudExposure2.ValueChanged += (s, e) => ApplyExposure(1, nudExposure2);

            btnToggleLive = new Button { Text = "Cam Off", Dock = DockStyle.Fill, BackColor = Color.LightGray };
            btnToggleLive.Click += (s, e) => ToggleLive();
            lblLiveStatus = new Label { Text = "Live: OFF", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            cameraGroupLayout.Controls.Add(btnDiscover, 0, 0);
            cameraGroupLayout.Controls.Add(new Label { Text = "Camera 1:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            cameraGroupLayout.Controls.Add(cboCamera1, 1, 1);
            cameraGroupLayout.Controls.Add(new Label { Text = "Camera 2:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            cameraGroupLayout.Controls.Add(cboCamera2, 1, 2);

            cameraGroupLayout.Controls.Add(btnConnect, 0, 3);
            cameraGroupLayout.SetColumnSpan(btnConnect, 2);
            cameraGroupLayout.Controls.Add(btnDisconnect, 0, 4);
            cameraGroupLayout.SetColumnSpan(btnDisconnect, 2);
            cameraGroupLayout.Controls.Add(new Label { Text = "Exposure Cam 1 (us):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            cameraGroupLayout.Controls.Add(nudExposure1, 1, 5);
            cameraGroupLayout.Controls.Add(new Label { Text = "Exposure Cam 2 (us):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
            cameraGroupLayout.Controls.Add(nudExposure2, 1, 6);
            cameraGroupLayout.Controls.Add(btnToggleLive, 0, 7);
            cameraGroupLayout.SetColumnSpan(btnToggleLive, 2);
            cameraGroupLayout.Controls.Add(lblLiveStatus, 0, 8);
            cameraGroupLayout.SetColumnSpan(lblLiveStatus, 2);
            cameraGroup.Controls.Add(cameraGroupLayout);

            GroupBox visionMasterGroup = new GroupBox { Text = "2. LOAD VISIONMASTER", Dock = DockStyle.Fill };
            TableLayoutPanel visionLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(8)
            };
            visionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            visionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            visionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            visionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            visionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            visionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            visionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            pnlVisionMaster = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke
            };
            Label lblVisionPlaceholder = new Label
            {
                Text = "VisionMaster area",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            pnlVisionMaster.Controls.Add(lblVisionPlaceholder);

            btnLoadVisionMaster = new Button { Text = "Load Sol", Dock = DockStyle.Fill };
            btnLoadVisionMaster.Click += (s, e) => LoadVisionMasterSolution();

            btnShowConfig = new Button { Text = "Setup", Dock = DockStyle.Fill, Enabled = false };
            btnShowConfig.Click += (s, e) => ShowVisionMasterConfig();

            btnTriggerVM = new Button { Text = "Trigger VM", Dock = DockStyle.Fill, Enabled = false, BackColor = Color.LightGreen };
            btnTriggerVM.Click += (s, e) => TriggerVisionMaster();

            txtVisionMasterPath = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Text = "Select VisionMaster solution (.sol)"
            };

            btnBrowseVisionMaster = new Button { Text = "Browse", Dock = DockStyle.Fill };
            btnBrowseVisionMaster.Click += (s, e) => BrowseVisionMasterSolution();

            lblVisionMasterStatus = new Label
            {
                Text = "VisionMaster: chưa load ảnh",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft
            };

            visionLayout.Controls.Add(txtVisionMasterPath, 0, 1);
            visionLayout.Controls.Add(btnBrowseVisionMaster, 1, 1);
            visionLayout.Controls.Add(btnLoadVisionMaster, 0, 2);
            visionLayout.Controls.Add(btnShowConfig, 1, 2);
            visionLayout.Controls.Add(btnTriggerVM, 0, 3);
            visionLayout.SetColumnSpan(btnTriggerVM, 2);
            visionLayout.Controls.Add(lblVisionMasterStatus, 0, 4);
            visionLayout.SetColumnSpan(lblVisionMasterStatus, 2);
            visionMasterGroup.Controls.Add(visionLayout);

            GroupBox tcpGroup = new GroupBox { Text = "3. CẤU HÌNH TCP", Dock = DockStyle.Fill };
            TableLayoutPanel tcpLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
            tcpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            tcpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            tcpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            txtTcpIp = new TextBox { Text = "127.0.0.1", Dock = DockStyle.Fill };
            nudTcpPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 8080, Dock = DockStyle.Fill };
            
            // Auto-save when changed
            txtTcpIp.TextChanged += (s, e) => SaveTcpSettings();
            nudTcpPort.ValueChanged += (s, e) => SaveTcpSettings();

            btnTestTcp = new Button { Text = "TEST CONNECTION", Dock = DockStyle.Fill, BackColor = Color.LightYellow };
            btnTestTcp.Click += (s, e) => SendTcpData("TEST_CONNECTION_FROM_APP");

            tcpLayout.Controls.Add(new Label { Text = "IP Address:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            tcpLayout.Controls.Add(txtTcpIp, 1, 0);
            tcpLayout.Controls.Add(new Label { Text = "Port:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            tcpLayout.Controls.Add(nudTcpPort, 1, 1);
            tcpLayout.Controls.Add(btnTestTcp, 0, 2);
            tcpLayout.SetColumnSpan(btnTestTcp, 2);
            tcpGroup.Controls.Add(tcpLayout);

            GroupBox logGroup = new GroupBox { Text = "LOG", Dock = DockStyle.Fill };
            rtbLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true };
            logGroup.Controls.Add(rtbLog);

            btnBackToHome = new Button { Text = "VỀ TRANG CHỦ", Dock = DockStyle.Fill, BackColor = Color.LightGray, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnBackToHome.Click += (s, e) => { pnlSettings.Visible = false; pnlHome.Visible = true; };

            rightLayout.Controls.Add(cameraGroup, 0, 0);
            rightLayout.Controls.Add(visionMasterGroup, 0, 1);
            rightLayout.Controls.Add(tcpGroup, 0, 2);
            rightLayout.Controls.Add(logGroup, 0, 3);
            rightLayout.Controls.Add(btnBackToHome, 0, 4);

            rightPanel.Controls.Add(rightLayout);

            mainLayout.Controls.Add(leftPanel, 0, 0);
            mainLayout.Controls.Add(rightPanel, 1, 0);

            pnlSettings.Controls.Add(mainLayout);

            // --- HOME PANEL ---
            pnlHome = new Panel { Dock = DockStyle.Fill, Visible = true };
            
            TableLayoutPanel homeLayout = new TableLayoutPanel { Dock = DockStyle.Fill };
            homeLayout.ColumnCount = 1;
            homeLayout.RowCount = 2;
            homeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            homeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel homeButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            btnGoToSettings = new Button { Text = "SETTING", Width = 150, Height = 40, BackColor = Color.LightSteelBlue, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnGoToSettings.Click += (s, e) => { pnlHome.Visible = false; pnlSettings.Visible = true; };
            
            btnHomeTrigger = new Button { Text = "TRIGGER SOFTWARE", Width = 200, Height = 40, BackColor = Color.LightGreen, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Enabled = false };
            btnHomeTrigger.Click += (s, e) => TriggerVisionMaster();

            homeButtons.Controls.Add(btnGoToSettings);
            homeButtons.Controls.Add(btnHomeTrigger);

            homeLayout.Controls.Add(homeButtons, 0, 0);
            
            pnlHomeVisionContainer = new Panel { Dock = DockStyle.Fill, Name = "homeVisionContainer" };
            pnlHomeVisionContainer.Controls.Add(pnlVisionMaster);
            homeLayout.Controls.Add(pnlHomeVisionContainer, 0, 1);
            
            pnlHome.Controls.Add(homeLayout);

            this.Controls.Add(pnlHome);
            this.Controls.Add(pnlSettings);
        }

        private void SetupDisplayTimer()
        {
            displayTimer = new Timer { Interval = 100 };
            displayTimer.Tick += (s, e) => UpdateCameraDisplays();
            displayTimer.Start();
        }

        private void UpdateCameraDisplays()
        {
            for (int i = 0; i < cameraManager.CameraCount; i++)
            {
                DualCameraController cam = cameraManager.GetCamera(i);
                if (cam == null) continue;

                Bitmap frame = cam.GetLatestFrame();
                if (frame != null)
                {
                    PictureBox pic = (i == 0) ? picCamera1 : picCamera2;
                    Label lbl = (i == 0) ? lblCamera1Info : lblCamera2Info;
                    
                    pic.Image?.Dispose();
                    pic.Image = frame;
                    
                    lbl.Text = $"{cam.CameraName}: {cam.ImageCount} images, {cam.ErrorCount} errors";
                }
            }
        }

        private ImageBaseData ConvertBitmapToImageBaseData(Bitmap bitmap)
        {
            if (bitmap == null) return null;
            
            int width = bitmap.Width;
            int height = bitmap.Height;

            try
            {
                if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
                {
                    int imgSize = width * height;
                    byte[] buffer = new byte[imgSize];
                    System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
                    
                    try
                    {
                        int stride = bmpData.Stride;
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(bmpData.Scan0 + (y * stride), buffer, y * width, width);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bmpData);
                    }
                    return new ImageBaseData(buffer, (uint)imgSize, width, height, (int)VMPixelFormat.VM_PIXEL_MONO_08);
                }
                else if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                {
                    int imgSize = width * height * 3;
                    byte[] buffer = new byte[imgSize];
                    System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
                    
                    try
                    {
                        int stride = bmpData.Stride;
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(bmpData.Scan0 + (y * stride), buffer, y * width * 3, width * 3);
                        }

                        // BGR to RGB
                        for (int i = 0; i < buffer.Length - 2; i += 3)
                        {
                            byte temp = buffer[i];
                            buffer[i] = buffer[i + 2];
                            buffer[i + 2] = temp;
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bmpData);
                    }
                    return new ImageBaseData(buffer, (uint)imgSize, width, height, (int)VMPixelFormat.VM_PIXEL_RGB24_C3);
                }
            }
            catch (Exception ex)
            {
                AppLog("CONVERT", "Lỗi convert ảnh: " + ex.Message, Color.Red);
            }
            
            return null;
        }

        private async void TriggerVisionMaster()
        {
            try
            {
                btnTriggerVM.Enabled = false;
                btnHomeTrigger.Enabled = false;
                AppLog("VM", "Bắt đầu tiến trình Trigger...", Color.DarkGray);

                var processInfoList = VmSolution.Instance.GetAllProcedureList();
                if (processInfoList.nNum <= 0)
                {
                    AppLog("VM", "Chưa có procedure nào trong file Sol.", Color.OrangeRed);
                    return;
                }
                
                string firstProcessName = processInfoList.astProcessInfo[0].strProcessName;
                VmProcedure procedure = VmSolution.Instance[firstProcessName] as VmProcedure;
                if (procedure == null) 
                {
                    AppLog("VM", "Không thể lấy Procedure: " + firstProcessName, Color.Red);
                    return;
                }

                bool hasImage = false;
                var grabTasks = new Dictionary<int, Task<Bitmap>>();

                for (int i = 0; i < cameraManager.CameraCount; i++)
                {
                    DualCameraController cam = cameraManager.GetCamera(i);
                    if (cam == null) continue;

                    if (!liveEnabled)
                    {
                        AppLog("CAM", $"Đang chụp ảnh Camera {i+1}...", Color.DarkCyan);
                        grabTasks[i] = Task.Run(() => cam.GrabSingleFrameSynchronously());
                    }
                    else
                    {
                        PictureBox pic = (i == 0) ? picCamera1 : picCamera2;
                        grabTasks[i] = Task.FromResult(pic.Image != null ? (Bitmap)pic.Image.Clone() : null);
                    }
                }

                await Task.WhenAll(grabTasks.Values);
                AppLog("VM", "Đã lấy xong ảnh từ các camera. Bắt đầu xử lý...", Color.DarkCyan);

                // Phase 1: Heavy image processing in background
                await Task.Run(() =>
                {
                    try
                    {
                        int receivedCount = 0;
                        foreach (var entry in grabTasks)
                        {
                            int cameraIdx = entry.Key;
                            Bitmap bmp = entry.Value.Result;

                            if (bmp != null)
                            {
                                receivedCount++;
                                string moduleName = (cameraIdx == 0) ? "Image Source1" : "Image Source2";
                                ImageSourceModuleTool imageSourceModule = procedure[moduleName] as ImageSourceModuleTool;
                                
                                if (imageSourceModule != null)
                                {
                                    ImageBaseData imageBaseData = ConvertBitmapToImageBaseData(bmp);
                                    if (imageBaseData != null)
                                    {
                                        imageSourceModule.SetImageData(imageBaseData);
                                        hasImage = true;
                                    }
                                }
                                else
                                {
                                    AppLog("VM", $"Không tìm thấy module: {moduleName}", Color.Orange);
                                }

                                if (!liveEnabled)
                                {
                                    this.BeginInvoke(new Action(() =>
                                    {
                                        PictureBox pic = (cameraIdx == 0) ? picCamera1 : picCamera2;
                                        pic.Image?.Dispose();
                                        pic.Image = (Bitmap)bmp.Clone();
                                        pic.Invalidate(); // Force repaint for zoom/pan
                                    }));
                                }
                            }
                        }
                        if (receivedCount > 0) AppLog("VM", $"Đã nạp {receivedCount} ảnh vào modules.", Color.DarkCyan);
                    }
                    catch (Exception ex)
                    {
                        AppLog("ERROR", "Lỗi xử lý ảnh ngầm: " + ex.Message, Color.Red);
                    }
                });

                // Phase 2: Run procedure on UI thread to avoid crash
                if (hasImage)
                {
                    AppLog("VM", "Đang chạy thuật toán Vision Master...", Color.DarkCyan);
                    procedure.Run();
                    
                    // Force refresh Home controls
                    vmRenderControl?.Refresh();
                    
                    AppLog("VM", "==> TRIGGER THÀNH CÔNG <==", Color.Green);

                    // 4. TRÍCH XUẤT KẾT QUẢ VÀ GỬI TCP (Hợp nhất CPU2 & CPU3 + Lọc trùng)
                    List<string> allFoundCodes = new List<string>();
                    string[] targetPaths = { 
                        "Flow1.DL Code Reading CPU2.(CodeStr)", 
                        "Flow1.DL Code Reading CPU3.(CodeStr)" 
                    };

                    foreach (var path in targetPaths) {
                        try {
                            dynamic directObj = VmSolution.Instance[path];
                            if (directObj != null) {
                                dynamic val = null;
                                try { val = directObj.Value; } catch { val = directObj; }

                                if (val != null) {
                                    if (val is Array arr) {
                                        for (int i = 0; i < arr.Length; i++) {
                                            dynamic item = arr.GetValue(i);
                                            try { allFoundCodes.Add(item.strValue); } catch { allFoundCodes.Add(item.ToString()); }
                                        }
                                    } else if (val.GetType().GetProperty("astStringVal") != null) {
                                        for (int i = 0; i < (int)val.nNum; i++) allFoundCodes.Add(val.astStringVal[i].strValue);
                                    } else {
                                        allFoundCodes.Add(val.ToString());
                                    }
                                }
                            }
                        } catch { }
                    }

                    // Lọc trùng lặp (De-duplicate)
                    allFoundCodes = allFoundCodes.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

                    // Nếu chưa thấy code qua đường dẫn trực tiếp, mới dùng đến quét đệ quy dự phòng
                    if (allFoundCodes.Count == 0) {
                        var procInfoList = VmSolution.Instance.GetAllProcedureList();
                        for (int i = 0; i < procInfoList.nNum; i++) {
                            string pName = procInfoList.astProcessInfo[i].strProcessName;
                            VmProcedure proc = VmSolution.Instance[pName] as VmProcedure;
                            if (proc != null) ExtractResultsRecursive(proc, pName, allFoundCodes);
                        }
                        allFoundCodes = allFoundCodes.Distinct().ToList();
                    }

                    // Gửi dữ liệu đi
                    string finalData = allFoundCodes.Count > 0 ? string.Join(";", allFoundCodes) : "NO_CODE";
                    SendTcpData(finalData);
                    
                    if (allFoundCodes.Count > 0)
                        AppLog("VM-RES", $"==> GỬI THÀNH CÔNG {allFoundCodes.Count} MÃ: {finalData}", Color.LimeGreen);
                    else
                        AppLog("VM-RES", "Không tìm thấy mã code nào.", Color.Yellow);
                }
                else
                {
                    AppLog("VM", "LỖI: Không nhận được ảnh nào từ camera.", Color.OrangeRed);
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Lỗi khi trigger: " + ex.Message, Color.Red);
            }
            finally
            {
                btnTriggerVM.Enabled = true;
                btnHomeTrigger.Enabled = true;
            }
        }

        private void DiscoverCameras()
        {
            RefreshCameraList();
        }

        private void OnAppLoaded()
        {
            RefreshCameraList();
            LoadRememberedVisionMasterPathOnly();
            
            // Use BeginInvoke to allow UI to settle before auto-connecting
            this.BeginInvoke(new Action(() => {
                LoadCameraSettings(); 
                LoadTcpSettings();
            }));
            
            // Auto-load if path is valid
            string solPath = txtVisionMasterPath.Text.Trim();
            if (!string.IsNullOrEmpty(solPath) && System.IO.File.Exists(solPath))
            {
                LoadVisionMasterSolution();
            }
        }

        private void EnsureVisionMasterControls()
        {
            if (vmRenderControl == null)
            {
                try
                {
                    vmRenderControl = new VmRenderControl 
                    { 
                        Dock = DockStyle.Fill,
                        BackColor = Color.Black,
                        CoordinateInfoVisible = true
                    };
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to create VisionMaster render control: " + ex.Message, ex);
                }
            }

            pnlVisionMaster.Controls.Clear();
            pnlVisionMaster.Controls.Add(vmRenderControl);

            if (pnlHomeVisionContainer != null && pnlVisionMaster.Parent != pnlHomeVisionContainer)
            {
                pnlHomeVisionContainer.Controls.Add(pnlVisionMaster);
            }
        }

        private string GetVisionMasterRememberedFilePath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visionmaster.last.txt");
        }

        private void RefreshCameraList()
        {
            try
            {
                suppressSelectionEvents = true;

                List<ICameraInfo> cameras = CameraFinder.Enumerate();
                List<CameraOption> options = new List<CameraOption>();

                foreach (ICameraInfo camera in cameras)
                {
                    string serialNumber = camera[CameraInfoKey.SerialNumber];
                    string modelName = camera[CameraInfoKey.ModelName];
                    options.Add(new CameraOption(serialNumber, string.Format("{0} ({1})", modelName, serialNumber)));
                }

                cboCamera1.BeginUpdate();
                cboCamera2.BeginUpdate();
                cboCamera1.Items.Clear();
                cboCamera2.Items.Clear();

                foreach (CameraOption option in options)
                {
                    cboCamera1.Items.Add(option);
                    cboCamera2.Items.Add(option);
                }

                cboCamera1.SelectedIndex = options.Count > 0 ? 0 : -1;
                cboCamera2.SelectedIndex = options.Count > 1 ? 1 : -1;

                AppLog("DISCOVERY", string.Format("Found {0} camera(s)", options.Count), Color.Green);
                if (options.Count == 0)
                {
                    AppLog("DISCOVERY", "No cameras found. Connect cameras and press Tìm Cam again.", Color.Orange);
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Camera discovery failed: " + ex.Message, Color.Red);
            }
            finally
            {
                cboCamera1.EndUpdate();
                cboCamera2.EndUpdate();
                suppressSelectionEvents = false;
                UpdateSelectionLabels();
            }
        }

        private void UpdateSelectionLabels()
        {
            if (suppressSelectionEvents)
            {
                return;
            }

            CameraOption cam1 = cboCamera1.SelectedItem as CameraOption;
            CameraOption cam2 = cboCamera2.SelectedItem as CameraOption;

            lblCamera1Info.Text = cam1 != null ? string.Format("Camera 1: {0}", cam1.DisplayName) : "Camera 1: N/A";
            lblCamera2Info.Text = cam2 != null ? string.Format("Camera 2: {0}", cam2.DisplayName) : "Camera 2: N/A";
        }

        private void ExtractResultsRecursive(object container, string path, List<string> allFoundCodes)
        {
            try {
                dynamic c = container;
                var list = c.GetAllModuleList();
                if (list == null || list.nNum == 0) {
                    try { list = c.GetModuleList(); } catch { }
                }
                if (list == null) return;

                for (int i = 0; i < (int)list.nNum; i++) {
                    string mName = list.astModuleInfo[i].strModuleName;
                    var module = c[mName];
                    if (module == null) continue;

                    string currentPath = string.IsNullOrEmpty(path) ? mName : $"{path} > {mName}";
                    if (mName.Contains("Code Reading") || mName.Contains("CodeRecg")) {
                        ExtractFromModule(module, currentPath, allFoundCodes);
                    }
                    if (module.GetType().Name.Contains("Group")) ExtractResultsRecursive(module, currentPath, allFoundCodes);
                }
            } catch { }
        }

        private void ExtractFromModule(object module, string path, List<string> allFoundCodes)
        {
            try {
                dynamic tool = module;
                dynamic res = tool.ModuResult;
                if (res == null) return;

                int count = 0;
                try { count = (int)res.CodeNum; } catch { try { count = (int)res.nCodeNum; } catch { } }

                if (count > 0) {
                    try {
                        var val = res.CodeStr;
                        if (val is Array arr) {
                            for (int i = 0; i < arr.Length; i++) allFoundCodes.Add(arr.GetValue(i)?.ToString());
                        } else {
                            allFoundCodes.Add(val?.ToString());
                        }
                    } catch {
                        try {
                            var info = res.astCodeInfo;
                            for (int i = 0; i < count; i++) {
                                try { allFoundCodes.Add(info[i].strCode); } catch { }
                            }
                        } catch { }
                    }
                }

                if (allFoundCodes.Count == 0) {
                    string[] names = { "Code", "out", "CodeStr" };
                    foreach (var n in names) {
                        try {
                            dynamic outS = res.GetOutputString(n);
                            if (outS != null && (int)outS.nNum > 0) {
                                for (int i = 0; i < (int)outS.nNum; i++) allFoundCodes.Add(outS.astStringVal[i].strValue);
                                break;
                            }
                        } catch { }
                    }
                }
            } catch { }
        }

        private void SendTcpData(string data)
        {
            string ip = "127.0.0.1";
            int port = 8080;

            if (txtTcpIp != null) this.Invoke(new Action(() => { ip = txtTcpIp.Text; port = (int)nudTcpPort.Value; }));

            Task.Run(() => {
                try {
                    using (TcpClient client = new TcpClient()) {
                        var connectTask = client.ConnectAsync(ip, port);
                        if (connectTask.Wait(1000)) {
                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data + "\r\n");
                            client.GetStream().Write(bytes, 0, bytes.Length);
                            AppLog("TCP", "Đã gửi: " + data, Color.DarkGreen);
                        } else {
                            AppLog("TCP-ERROR", "Không thể kết nối đến " + ip + ":" + port, Color.Red);
                        }
                    }
                } catch (Exception ex) {
                    AppLog("TCP-ERROR", "Lỗi gửi TCP: " + ex.Message, Color.Red);
                }
            });
        }

        private void ConnectSelectedCameras()
        {
            CameraOption cam1 = cboCamera1.SelectedItem as CameraOption;
            CameraOption cam2 = cboCamera2.SelectedItem as CameraOption;

            if (cam1 == null || cam2 == null)
            {
                AppLog("UI", "Vui lòng chọn đủ Camera 1 và Camera 2", Color.OrangeRed);
                return;
            }

            if (cam1.SerialNumber == cam2.SerialNumber)
            {
                AppLog("UI", "Camera 1 và Camera 2 phải khác nhau", Color.Red);
                return;
            }

            try
            {
                cameraManager.DisposeCameras();
                cameraManager.CreateCameraFromSerialNumber(cam1.SerialNumber, "Camera 1");
                cameraManager.CreateCameraFromSerialNumber(cam2.SerialNumber, "Camera 2");

                if (cameraManager.OpenAllCameras())
                {
                    liveEnabled = false;
                    UpdateLiveUi(false);
                    AppLog("UI", "Đã kết nối và mở 2 camera", Color.Green);
                    SaveCameraSettings(); // Save these cameras as preferred
                }
                else
                {
                    AppLog("UI", "Không mở được cả 2 camera", Color.Red);
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Connect failed: " + ex.Message, Color.Red);
            }
        }

        private void DisconnectCameras()
        {
            try
            {
                cameraManager.StopGrabbingAll();
                cameraManager.CloseAllCameras();
                cameraManager.DisposeCameras();
                liveEnabled = false;
                UpdateLiveUi(false);
                ClearPreviewImages();
                AppLog("UI", "Đã ngắt camera", Color.Gray);
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Disconnect failed: " + ex.Message, Color.Red);
            }
        }

        private void BrowseVisionMasterSolution()
        {
            try
            {
                if (openVisionMasterDialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtVisionMasterPath.Text = openVisionMasterDialog.FileName;
                    lblVisionMasterStatus.Text = "VisionMaster: file selected";
                    AppLog("VM", "Selected solution: " + openVisionMasterDialog.FileName, Color.DarkCyan);
                    SaveRememberedVisionMasterSolution(openVisionMasterDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Browse VisionMaster file failed: " + ex.Message, Color.Red);
            }
        }

        private void SaveRememberedVisionMasterSolution(string solPath)
        {
            try
            {
                System.IO.File.WriteAllText(GetVisionMasterRememberedFilePath(), solPath ?? string.Empty);
            }
            catch
            {
            }
        }

        private void LoadRememberedVisionMasterPathOnly()
        {
            try
            {
                string rememberPath = GetVisionMasterRememberedFilePath();
                if (!System.IO.File.Exists(rememberPath))
                {
                    return;
                }

                string rememberedSol = System.IO.File.ReadAllText(rememberPath).Trim();
                if (string.IsNullOrEmpty(rememberedSol) || !System.IO.File.Exists(rememberedSol))
                {
                    return;
                }

                txtVisionMasterPath.Text = rememberedSol;
                lblVisionMasterStatus.Text = "VisionMaster: file remembered";
                AppLog("VM", "Remembered solution path loaded: " + rememberedSol, Color.DarkCyan);
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Load remembered VisionMaster failed: " + ex.Message, Color.Red);
            }
        }

        private string GetCameraSettingsFilePath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "camera.settings.txt");
        }

        private void SaveCameraSettings()
        {
            try
            {
                CameraOption cam1 = cboCamera1.SelectedItem as CameraOption;
                CameraOption cam2 = cboCamera2.SelectedItem as CameraOption;
                
                List<string> lines = new List<string>();
                if (cam1 != null) lines.Add("Camera1SN=" + cam1.SerialNumber);
                if (cam2 != null) lines.Add("Camera2SN=" + cam2.SerialNumber);
                lines.Add("Camera1Exp=" + nudExposure1.Value);
                lines.Add("Camera2Exp=" + nudExposure2.Value);

                System.IO.File.WriteAllLines(GetCameraSettingsFilePath(), lines);
            }
            catch { }
        }

        private void LoadCameraSettings()
        {
            try
            {
                string path = GetCameraSettingsFilePath();
                if (!System.IO.File.Exists(path)) return;

                string[] lines = System.IO.File.ReadAllLines(path);
                string sn1 = "", sn2 = "";
                double exp1 = 10000, exp2 = 10000;

                foreach (string line in lines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    if (key == "Camera1SN") sn1 = val;
                    else if (key == "Camera2SN") sn2 = val;
                    else if (key == "Camera1Exp") double.TryParse(val, out exp1);
                    else if (key == "Camera2Exp") double.TryParse(val, out exp2);
                }

                suppressSelectionEvents = true; // Avoid double triggering
                
                // Match in ComboBoxes
                bool found1 = false, found2 = false;
                if (!string.IsNullOrEmpty(sn1))
                {
                    for (int i = 0; i < cboCamera1.Items.Count; i++)
                    {
                        if (((CameraOption)cboCamera1.Items[i]).SerialNumber == sn1)
                        {
                            cboCamera1.SelectedIndex = i;
                            found1 = true;
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(sn2))
                {
                    for (int i = 0; i < cboCamera2.Items.Count; i++)
                    {
                        if (((CameraOption)cboCamera2.Items[i]).SerialNumber == sn2)
                        {
                            cboCamera2.SelectedIndex = i;
                            found2 = true;
                            break;
                        }
                    }
                }
                
                suppressSelectionEvents = false;
                UpdateSelectionLabels();

                // If both matched, connect
                if (found1 && found2)
                {
                    AppLog("SYS", string.Format("Đang tự động kết nối: {0} & {1}", sn1, sn2), Color.Blue);
                    ConnectSelectedCameras();
                    
                    // Set exposures
                    nudExposure1.Value = (decimal)Math.Max(nudExposure1.Minimum, Math.Min(nudExposure1.Maximum, (decimal)exp1));
                    nudExposure2.Value = (decimal)Math.Max(nudExposure2.Minimum, Math.Min(nudExposure2.Maximum, (decimal)exp2));
                    
                    ApplyExposure(0, nudExposure1);
                    ApplyExposure(1, nudExposure2);
                }
                else
                {
                    if (!string.IsNullOrEmpty(sn1) || !string.IsNullOrEmpty(sn2))
                    {
                        AppLog("SYS", "Không tìm thấy đủ 2 camera đã lưu trong danh sách thiết bị.", Color.Orange);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Tự động load cấu hình camera thất bại: " + ex.Message, Color.Red);
                suppressSelectionEvents = false;
            }
        }

        private void SaveTcpSettings()
        {
            try {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tcp.settings.txt");
                System.IO.File.WriteAllLines(path, new[] { txtTcpIp.Text, nudTcpPort.Value.ToString() });
            } catch { }
        }

        private void LoadTcpSettings()
        {
            try {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tcp.settings.txt");
                if (System.IO.File.Exists(path)) {
                    string[] lines = System.IO.File.ReadAllLines(path);
                    if (lines.Length >= 2) {
                        txtTcpIp.Text = lines[0];
                        nudTcpPort.Value = decimal.Parse(lines[1]);
                    }
                }
            } catch { }
        }

        private void LoadVisionMasterSolution()
        {
            try
            {
                string solPath = txtVisionMasterPath.Text.Trim();
                if (string.IsNullOrEmpty(solPath) || !solPath.EndsWith(".solw", StringComparison.OrdinalIgnoreCase))
                {
                    AppLog("VM", "Vui lòng chọn file VisionMaster .solw", Color.OrangeRed);
                    return;
                }

                EnsureVisionMasterControls();
                VmSolution.Load(solPath, string.Empty);
                BindFirstVisionMasterProcedure();
                lblVisionMasterStatus.Text = "VisionMaster: loaded " + System.IO.Path.GetFileName(solPath);
                AppLog("VM", "Loaded VisionMaster solution: " + solPath, Color.Purple);
                SaveRememberedVisionMasterSolution(solPath);

                if (btnShowConfig != null) btnShowConfig.Enabled = true;
                if (btnTriggerVM != null) btnTriggerVM.Enabled = true;
                if (btnHomeTrigger != null) btnHomeTrigger.Enabled = true;
                // ShowVisionMasterConfig(); // Đã tắt tự động mở cửa sổ Setup
            }
            catch (Exception ex)
            {
                lblVisionMasterStatus.Text = "VisionMaster: lỗi khi load file";
                AppLog("ERROR", "VisionMaster solution load failed: " + ex.Message, Color.Red);
            }
        }

        private void ShowVisionMasterConfig()
        {
            try
            {
                var processInfoList = VmSolution.Instance.GetAllProcedureList();
                if (processInfoList.nNum <= 0) return;
                string firstProcessName = processInfoList.astProcessInfo[0].strProcessName;
                VmProcedure procedure = VmSolution.Instance[firstProcessName] as VmProcedure;

                Form configForm = new Form
                {
                    Text = "Vision Master Configuration - " + firstProcessName,
                    WindowState = FormWindowState.Maximized,
                    Icon = this.Icon
                };

                VmMainViewConfigControl configControl = new VmMainViewConfigControl
                {
                    Dock = DockStyle.Fill
                };
                try { ((dynamic)configControl).ModuleSource = procedure; } catch { }

                VmGlobalToolControl globalTool = new VmGlobalToolControl
                {
                    Dock = DockStyle.Top,
                    Height = 35
                };
                try { ((dynamic)globalTool).ModuleSource = procedure; } catch { }

                configForm.Controls.Add(configControl);
                configForm.Controls.Add(globalTool);
                
                configForm.FormClosed += (s, e) => 
                {
                    // Re-bind to ensure Home display matches latest changes
                    BindFirstVisionMasterProcedure();
                    vmRenderControl?.Refresh();
                };
                configForm.Show(this);
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Show config failed: " + ex.Message, Color.Red);
            }
        }

        private void BindFirstVisionMasterProcedure()
        {
            try
            {
                if (vmRenderControl == null)
                {
                    EnsureVisionMasterControls();
                }

                var processInfoList = VmSolution.Instance.GetAllProcedureList();
                if (processInfoList.nNum <= 0)
                {
                    throw new InvalidOperationException("VisionMaster solution has no procedure.");
                }

                string firstProcessName = processInfoList.astProcessInfo[0].strProcessName;
                IVmModule procedure = VmSolution.Instance[firstProcessName] as IVmModule;
                if (procedure == null)
                {
                    throw new InvalidOperationException("Cannot resolve the first VisionMaster procedure.");
                }

                // Link controls to the procedure
                vmRenderControl.ModuleSource = procedure;

                vmRenderControl.Refresh();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("VisionMaster binding failed: " + ex.Message, ex);
            }
        }

        private void ApplyExposure(int cameraIndex, NumericUpDown input)
        {
            try
            {
                double exposure = (double)input.Value;
                if (cameraManager.SetExposureTimeForCamera(cameraIndex, exposure))
                {
                    AppLog("CAM", string.Format("Camera {0} exposure set to {1} us", cameraIndex + 1, exposure), Color.DarkCyan);
                    SaveCameraSettings(); // Save the latest exposure
                }
                else
                {
                    AppLog("CAM", string.Format("Failed to set exposure for Camera {0}", cameraIndex + 1), Color.Red);
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Apply exposure failed: " + ex.Message, Color.Red);
            }
        }

        private void ToggleLive()
        {
            try
            {
                if (cameraManager.CameraCount == 0)
                {
                    AppLog("UI", "Vui lòng kết nối camera trước", Color.OrangeRed);
                    return;
                }

                if (!liveEnabled)
                {
                    if (cameraManager.StartContinuousGrabbingAll())
                    {
                        liveEnabled = true;
                        UpdateLiveUi(true);
                        AppLog("UI", "Live camera đã bật", Color.Green);
                    }
                    else
                    {
                        AppLog("UI", "Không bật được live camera", Color.Red);
                    }
                }
                else
                {
                    if (cameraManager.StopGrabbingAll())
                    {
                        liveEnabled = false;
                        UpdateLiveUi(false);
                        AppLog("UI", "Live camera đã tắt", Color.Gray);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog("ERROR", "Toggle live failed: " + ex.Message, Color.Red);
            }
        }

        private void UpdateLiveUi(bool enabled)
        {
            btnToggleLive.Text = enabled ? "Cam On" : "Soft Trigger";
            lblLiveStatus.Text = enabled ? "Live: ON" : "Live: OFF";
            btnToggleLive.BackColor = enabled ? Color.LightGreen : Color.LightGray;
        }

        private void ClearPreviewImages()
        {
            if (picCamera1.Image != null)
            {
                picCamera1.Image.Dispose();
                picCamera1.Image = null;
            }

            if (picCamera2.Image != null)
            {
                picCamera2.Image.Dispose();
                picCamera2.Image = null;
            }
        }

        public void AppLog(string tag, string message, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppLog(tag, message, color)));
                return;
            }

            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(string.Format("[{0:HH:mm:ss}] [{1}] {2}\n", DateTime.Now, tag, message));
            rtbLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            displayTimer?.Stop();
            cameraManager?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
