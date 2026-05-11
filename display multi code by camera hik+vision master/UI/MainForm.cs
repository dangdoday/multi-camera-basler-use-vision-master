using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;
using VM.Core;
using VM.PlatformSDKCS;
using VMControls.Interface;
using VMControls.Winform.Release;

namespace DisplayMultiCodeApp.UI
{
    public partial class MainForm : Form
    {
        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        #region Constants & UI Theme
        private static readonly Color PRIMARY_COLOR = Color.FromArgb(45, 45, 48);
        private static readonly Color ACCENT_COLOR = Color.FromArgb(0, 122, 204);
        private static readonly Color TEXT_COLOR = Color.FromArgb(241, 241, 241);
        private static readonly Color ERROR_COLOR = Color.FromArgb(204, 51, 51);
        #endregion

        #region UI Components
        private TabControl tabMain;
        private TabPage tabHome, tabSettings;
        private Panel pnlRight;
        private PictureBox picStitched;
        private Button btnRun;
        private RichTextBox rtbLog;
        private DataGridView dgvResults; // Bảng danh sách mã code bên trái
        private TextBox txtSolPath, txtTcpIp;
        private NumericUpDown nudTcpPort;
        private NumericUpDown nudRows, nudCols; // Cấu hình lưới
        private CheckBox chkShowGrid;
        private string _lastSolPath = "";
        private Button btnBrowse, btnLoad, btnSetup;
        #endregion

        #region Grid & ROI State
        private System.Drawing.RectangleF _roiRect = new System.Drawing.RectangleF(100, 100, 1000, 1000); // Tọa độ trên ảnh thực
        private int _gridRows = 2, _gridCols = 2;
        private bool _isDrawingRoi = false;
        private bool _allowDrawingRoi = false; 
        private bool _showGridBoxes = true; // Flag hiện/ẩn ô lưới
        private System.Drawing.PointF _roiStartPoint;
        private List<GridCellResult> _gridResults = new List<GridCellResult>(); 
        #endregion

        #region State Management
        private System.ComponentModel.BackgroundWorker _vmLoader;
        private Bitmap _lastStitchedImage = null;
        private List<CodeResult> _currentResults = new List<CodeResult>();
        private readonly object _imageLock = new object();
        #endregion

        public class CodeResult
        {
            public string CodeStr { get; set; } = string.Empty;
            public float CenterX { get; set; }
            public float CenterY { get; set; }
            public float BoxWidth { get; set; }
            public float BoxHeight { get; set; }
            public float ScaledWidth { get; set; }
            public float ScaledHeight { get; set; }
            public float Angle { get; set; }
            public int CameraIndex { get; set; }
            public float MappedX { get; set; }
            public float MappedY { get; set; }
        }

        public class GridCellResult
        {
            public bool Found { get; set; }
            public string Code { get; set; } = "";
        }

        public MainForm()
        {
            InitializeComponent();
            ApplyTheme();
            this.FormClosing += (s, e) => {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            };
            LoadAppConfig();
        }

        private void InitializeComponent()
        {
            this.Text = "VisionMaster Multi-Camera Code Reader | Optimized";
            this.Size = new Size(1366, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = PRIMARY_COLOR;

            tabMain = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.Normal };
            tabHome = new TabPage("HOME");
            tabSettings = new TabPage("SETTINGS");
            
            tabMain.TabPages.Add(tabHome);
            tabMain.TabPages.Add(tabSettings);
            this.Controls.Add(tabMain);

            SetupHomeTab();
            SetupSettingsTab();

            picStitched.MouseDown += PicStitched_MouseDown;
            picStitched.MouseMove += PicStitched_MouseMove;
            picStitched.MouseUp += PicStitched_MouseUp;

            _vmLoader = new System.ComponentModel.BackgroundWorker();
            _vmLoader.DoWork += (s, e) => {
                try {
                    string solPathFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visionmaster.last.txt");
                    if (System.IO.File.Exists(solPathFile)) {
                        string lastSol = System.IO.File.ReadAllText(solPathFile);
                        if (!string.IsNullOrEmpty(lastSol) && System.IO.File.Exists(lastSol)) {
                            this.Invoke(new Action(() => { txtSolPath.Text = lastSol; LoadSolution(); }));
                        }
                    }
                } catch { }
            };
            _vmLoader.RunWorkerAsync();
        }

        private void PicStitched_MouseDown(object sender, MouseEventArgs e)
        {
            // Cho phép vẽ nếu giữ Ctrl HOẶC nếu đã bật nút trong Settings
            if (e.Button == MouseButtons.Left && (Control.ModifierKeys == Keys.Control || _allowDrawingRoi))
            {
                _isDrawingRoi = true;
                _roiStartPoint = GetImageCoords(e.Location);
                _roiRect = new System.Drawing.RectangleF(_roiStartPoint.X, _roiStartPoint.Y, 1, 1);
            }
        }

        private void PicStitched_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawingRoi)
            {
                System.Drawing.PointF current = GetImageCoords(e.Location);
                float x = Math.Min(_roiStartPoint.X, current.X);
                float y = Math.Min(_roiStartPoint.Y, current.Y);
                float w = Math.Abs(_roiStartPoint.X - current.X);
                float h = Math.Abs(_roiStartPoint.Y - current.Y);
                _roiRect = new System.Drawing.RectangleF(x, y, w, h);
                picStitched.Invalidate();
            }
        }

        private void PicStitched_MouseUp(object sender, MouseEventArgs e)
        {
            _isDrawingRoi = false;
            picStitched.Invalidate();
        }

        private System.Drawing.PointF GetImageCoords(System.Drawing.Point p)
        {
            if (_lastStitchedImage == null) return p;
            
            // Tính toán tỷ lệ hiển thị giống hệt trong hàm Paint
            float sc = Math.Min((float)picStitched.Width / _lastStitchedImage.Width, (float)picStitched.Height / _lastStitchedImage.Height);
            float ox = (picStitched.Width - _lastStitchedImage.Width * sc) / 2;
            float oy = (picStitched.Height - _lastStitchedImage.Height * sc) / 2;

            // Bù trừ Offset rồi mới chia cho Scale
            return new System.Drawing.PointF((p.X - ox) / sc, (p.Y - oy) / sc);
        }

        private void ApplyTheme()
        {
            foreach (TabPage tab in tabMain.TabPages)
            {
                tab.BackColor = PRIMARY_COLOR;
                tab.ForeColor = TEXT_COLOR;
            }
        }

        private void SetupHomeTab()
        {
            tabHome.BackColor = PRIMARY_COLOR;
            Panel pnlHome = new Panel { Dock = DockStyle.Fill };

            // 1. Panel Kết quả (Bên trái)
            Panel pnlResults = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(5), BackColor = Color.FromArgb(35, 35, 35) };
            Label lblResults = new Label { Text = "DETECTION LIST", Dock = DockStyle.Top, Height = 30, ForeColor = Color.Yellow, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
            
            dgvResults = new DataGridView {
                Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White,
                BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false,
                ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            dgvResults.DefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40);
            dgvResults.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 40, 40); // Trùng màu nền để mất trỏ xanh
            dgvResults.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvResults.Columns.Add("colId", "ID");
            dgvResults.Columns.Add("colStatus", "STATUS");
            dgvResults.Columns.Add("colContent", "CODE CONTENT");
            dgvResults.Columns[0].Width = 40;
            dgvResults.Columns[1].Width = 70;
            dgvResults.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            pnlResults.Controls.AddRange(new Control[] { dgvResults, lblResults });

            // 2. Vùng hiển thị ảnh (Chính giữa)
            Panel pnlImageContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            
            btnRun = new Button {
                Text = "READ CODE", Width = 220, Height = 55,
                FlatStyle = FlatStyle.Flat, BackColor = ACCENT_COLOR, ForeColor = Color.White,
                Font = new Font("Segoe UI", 14, FontStyle.Bold), Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRun.Location = new Point(500, 15);
            btnRun.Click += async (s, e) => await RunInspectionAsync();
            
            picStitched = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            picStitched.Paint += PicStitched_Paint;
            picStitched.SizeChanged += (s, e) => {
                btnRun.Left = pnlImageContainer.Width - btnRun.Width - 25;
                picStitched.Invalidate();
            };

            pnlImageContainer.Controls.Add(btnRun);
            pnlImageContainer.Controls.Add(picStitched);
            btnRun.BringToFront();

            pnlHome.Controls.AddRange(new Control[] { pnlImageContainer, pnlResults });
            tabHome.Controls.Add(pnlHome);
        }

        private void SetupSettingsTab()
        {
            Panel pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };

            // 1. Solution Section
            GroupBox gbSol = new GroupBox { Text = "VisionMaster Solution", Dock = DockStyle.Top, Height = 130, ForeColor = TEXT_COLOR };
            txtSolPath = new TextBox { Left = 20, Top = 40, Width = 550, Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(60, 60, 60), ForeColor = TEXT_COLOR };
            btnBrowse = new Button { Text = "...", Left = 580, Top = 39, Width = 45, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 80, 80) };
            btnLoad = new Button { Text = "LOAD SOLUTION", Left = 20, Top = 80, Width = 150, Height = 35, FlatStyle = FlatStyle.Flat, BackColor = ACCENT_COLOR };
            btnSetup = new Button { Text = "OPEN VM CONFIGURATOR", Left = 180, Top = 80, Width = 220, Height = 35, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), Enabled = false };
            gbSol.Controls.AddRange(new Control[] { txtSolPath, btnBrowse, btnLoad, btnSetup });

            // 2. TCP Section
            GroupBox gbTcp = new GroupBox { Text = "TCP Output Settings", Dock = DockStyle.Top, Height = 130, ForeColor = TEXT_COLOR };
            Label lblIp = new Label { Text = "Target IP:", Left = 20, Top = 45, Width = 80 };
            txtTcpIp = new TextBox { Text = "127.0.0.1", Left = 100, Top = 42, Width = 150, BackColor = Color.FromArgb(60, 60, 60), ForeColor = TEXT_COLOR };
            Label lblPort = new Label { Text = "Port:", Left = 270, Top = 45, Width = 50 };
            nudTcpPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 8080, Left = 320, Top = 42, Width = 100, BackColor = Color.FromArgb(60, 60, 60), ForeColor = TEXT_COLOR };
            Button btnSaveTcp = new Button { Text = "SAVE CONFIG", Left = 20, Top = 80, Width = 150, Height = 35, FlatStyle = FlatStyle.Flat, BackColor = Color.SeaGreen };
            gbTcp.Controls.AddRange(new Control[] { lblIp, txtTcpIp, lblPort, nudTcpPort, btnSaveTcp });

            // 3. Grid Section
            GroupBox gbGrid = new GroupBox { Text = "Grid QC Configuration", Dock = DockStyle.Top, Height = 150, ForeColor = TEXT_COLOR };
            Label lblRows = new Label { Text = "Rows:", Left = 20, Top = 40, Width = 50 };
            nudRows = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 2, Left = 75, Top = 37, Width = 60, BackColor = Color.FromArgb(60, 60, 60), ForeColor = TEXT_COLOR };
            Label lblCols = new Label { Text = "Cols:", Left = 160, Top = 40, Width = 50 };
            nudCols = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 2, Left = 215, Top = 37, Width = 60, BackColor = Color.FromArgb(60, 60, 60), ForeColor = TEXT_COLOR };
            
            Button btnToggleRoi = new Button { Text = "ENABLE ROI DRAWING", Left = 20, Top = 80, Width = 200, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.DarkSlateBlue, ForeColor = Color.White };
            chkShowGrid = new CheckBox { Text = "Show Grid Cells", Checked = true, Left = 240, Top = 88, Width = 150, ForeColor = TEXT_COLOR };
            
            nudRows.ValueChanged += (s, e) => { _gridRows = (int)nudRows.Value; picStitched.Invalidate(); };
            nudCols.ValueChanged += (s, e) => { _gridCols = (int)nudCols.Value; picStitched.Invalidate(); };
            btnToggleRoi.Click += (s, e) => {
                _allowDrawingRoi = !_allowDrawingRoi;
                btnToggleRoi.Text = _allowDrawingRoi ? "DISABLE ROI DRAWING" : "ENABLE ROI DRAWING";
                btnToggleRoi.BackColor = _allowDrawingRoi ? Color.Crimson : Color.DarkSlateBlue;
                if (_allowDrawingRoi) tabMain.SelectedTab = tabHome;
            };
            chkShowGrid.CheckedChanged += (s, e) => { _showGridBoxes = chkShowGrid.Checked; picStitched.Invalidate(); };
            gbGrid.Controls.AddRange(new Control[] { lblRows, nudRows, lblCols, nudCols, btnToggleRoi, chkShowGrid });

            // 4. Log Section
            GroupBox gbLog = new GroupBox { Text = "Activity Log", Dock = DockStyle.Fill, MinimumSize = new Size(0, 250), ForeColor = TEXT_COLOR };
            rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 20), ForeColor = TEXT_COLOR, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            gbLog.Controls.Add(rtbLog);

            pnlContent.Controls.Add(gbLog);
            pnlContent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
            pnlContent.Controls.Add(gbGrid);
            pnlContent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
            pnlContent.Controls.Add(gbTcp);
            pnlContent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
            pnlContent.Controls.Add(gbSol);

            tabSettings.Controls.Add(pnlContent);

            btnBrowse.Click += (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog { Filter = "VM Solution|*.solw" }) 
                    if (ofd.ShowDialog() == DialogResult.OK) txtSolPath.Text = ofd.FileName;
            };
            btnLoad.Click += (s, e) => LoadSolution();
            btnSetup.Click += (s, e) => OpenVMSetup();
            btnSaveTcp.Click += (s, e) => SaveTcpConfig();
        }

        #region VisionMaster Core Logic
        private void LoadSolution()
        {
            try
            {
                string solPath = txtSolPath.Text.Trim();
                if (string.IsNullOrEmpty(solPath) || !System.IO.File.Exists(solPath)) {
                    Log("Error", "Invalid solution path.");
                    return;
                }

                Log("VM", "Loading solution, please wait...");
                VmSolution.Load(solPath, "");
                
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visionmaster.last.txt"), solPath);
                
                btnSetup.Enabled = true;
                Log("VM", "Solution loaded successfully.");
            }
            catch (Exception ex)
            {
                Log("Error", $"Load failed: {ex.Message}");
            }
        }

        private void OpenVMSetup()
        {
            try
            {
                Form configForm = new Form { Text = "VisionMaster Configurator", Size = new Size(1400, 900), WindowState = FormWindowState.Maximized };
                VmMainViewConfigControl configControl = new VmMainViewConfigControl { Dock = DockStyle.Fill };
                configForm.Controls.Add(configControl);
                configForm.Show();
            }
            catch (Exception ex) { Log("Error", "Could not open config: " + ex.Message); }
        }

        private async Task RunInspectionAsync()
        {
            if (btnRun.Enabled == false) return;
            
            try
            {
                btnRun.Enabled = false;
                btnRun.Text = "◌ PROCESSING...";
                btnRun.BackColor = Color.FromArgb(100, 100, 100);

                Log("Vision", "Triggering Flow1...");
                
                await Task.Run(() => {
                    var proc = VmSolution.Instance["Flow1"] as VmProcedure;
                    if (proc == null) throw new Exception("Procedure 'Flow1' not found.");

                    var trigger = VmSolution.Instance["Flow1.Trigger1"];
                    if (trigger != null) ((dynamic)trigger).Run();
                    else proc.Run();
                });

                await Task.Run(() => {
                    ExtractStitchedImage();
                    ExtractResults();
                });

                picStitched.Invalidate();
                UpdateResultsList();

                // Lọc chỉ lấy các mã code nằm TRONG vùng ROI để gửi đi
                var resultsInRoi = _currentResults.Where(r => _roiRect.Contains(r.MappedX, r.MappedY)).ToList();
                
                string resultStr = resultsInRoi.Count > 0 
                    ? string.Join(";", resultsInRoi.Select(r => r.CodeStr)) 
                    : "NO_CODE";
                
                Log("Result", $"Found {_currentResults.Count} unique codes.");
                
                foreach (var r in _currentResults)
                {
                    Log("Code", $"[{r.CodeStr}] Cam{r.CameraIndex} -> XY:({r.CenterX:F1}, {r.CenterY:F1}) | Global:({r.MappedX:F1}, {r.MappedY:F1})");
                }

                await Task.Run(() => SendTcpData(resultStr));
            }
            catch (Exception ex)
            {
                Log("Error", $"READ CODE failed: {ex.Message}");
            }
            finally
            {
                btnRun.Enabled = true;
                btnRun.Text = "▶ READ CODE";
                btnRun.BackColor = ACCENT_COLOR;
            }
        }

        private void ExtractStitchedImage()
        {
            try
            {
                var pin = VmSolution.Instance["Flow1.Image Stitch1.(StitchImage)"];
                if (pin == null) { Log("Debug", "Stitch Pin not found!"); return; }

                dynamic imgData = GetPinValue(pin, true);

                if (imgData != null) {
                    Log("Debug", "Image data type: " + imgData.GetType().Name);
                    
                    int w = Convert.ToInt32(GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(StitchImageWidth)"], true) ?? 0);
                    int h = Convert.ToInt32(GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(StitchImageHeight)"], true) ?? 0);
                    int fmt = Convert.ToInt32(GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(StitchImagePixelFormat)"], true) ?? 1);

                    Bitmap bmp = ImageBaseDataToBitmap(imgData, w, h, fmt);
                    if (bmp != null) {
                        lock (_imageLock) {
                            _lastStitchedImage?.Dispose();
                            _lastStitchedImage = (Bitmap)bmp.Clone();
                        }
                        bmp.Dispose();
                        Log("Debug", $"Image updated: {w}x{h}");
                    }
                }
            }
            catch (Exception ex) { Log("Debug", "Stitch extract error: " + ex.Message); }
        }

        private void ExtractResults()
        {
            lock (_currentResults)
            {
                _currentResults.Clear();
                HashSet<string> seenCodes = new HashSet<string>();
                
                var modules = new[] { 
                    new { Path = "Flow1.DL Code Reading CPU1", CamIdx = 1 },
                    new { Path = "Flow1.DL Code Reading CPU2", CamIdx = 1 }, 
                    new { Path = "Flow1.DL Code Reading CPU3", CamIdx = 2 }
                };

                foreach (var m in modules) {
                    try {
                        var codesObj = VmSolution.Instance[m.Path + ".(CodeStr)"];
                        if (codesObj == null) continue;
                        
                        object codesVal = GetPinValue(codesObj, false);
                        if (!(codesVal is Array arrCodes) || arrCodes.Length == 0) continue;

                        Array cxs = GetPinValue(VmSolution.Instance[m.Path + ".(CenterX)"], false) as Array;
                        Array cys = GetPinValue(VmSolution.Instance[m.Path + ".(CenterY)"], false) as Array;
                        Array bws = GetPinValue(VmSolution.Instance[m.Path + ".(BoxWidth)"], false) as Array;
                        Array bhs = GetPinValue(VmSolution.Instance[m.Path + ".(BoxHeight)"], false) as Array;
                        Array angs = GetPinValue(VmSolution.Instance[m.Path + ".(BoxAngle)"], false) as Array;

                        for (int i = 0; i < arrCodes.Length; i++) {
                            string code = GetStringFromObj(arrCodes.GetValue(i));
                            
                            if (string.IsNullOrWhiteSpace(code) || code.Contains("IMVS_") || seenCodes.Contains(code)) continue;

                            var res = new CodeResult {
                                CodeStr = code,
                                CameraIndex = m.CamIdx,
                                CenterX = GetFloatFromArray(cxs, i),
                                CenterY = GetFloatFromArray(cys, i),
                                BoxWidth = GetFloatFromArray(bws, i),
                                BoxHeight = GetFloatFromArray(bhs, i),
                                Angle = GetFloatFromArray(angs, i)
                            };

                            MapToGlobalCoordinates(res);
                            _currentResults.Add(res);
                            seenCodes.Add(code);
                        }
                    } catch { }
                }

                _gridResults.Clear();
                float cellW = _roiRect.Width / _gridCols;
                float cellH = _roiRect.Height / _gridRows;

                for (int r = 0; r < _gridRows; r++)
                {
                    for (int c = 0; c < _gridCols; c++)
                    {
                        System.Drawing.RectangleF cell = new System.Drawing.RectangleF(_roiRect.X + c * cellW, _roiRect.Y + r * cellH, cellW, cellH);
                        var codeInCell = _currentResults.FirstOrDefault(code => cell.Contains(code.MappedX, code.MappedY));
                        
                        _gridResults.Add(new GridCellResult { 
                            Found = codeInCell != null, 
                            Code = codeInCell?.CodeStr ?? "" 
                        });
                    }
                }

                _currentResults = _currentResults
                    .OrderBy(r => Math.Floor(r.MappedY / 150))
                    .ThenBy(r => r.MappedX)
                    .ToList();

                if (_currentResults.Count == 0) {
                    List<string> fallbackCodes = new List<string>();
                    VmProcedure proc = VmSolution.Instance["Flow1"] as VmProcedure;
                    if (proc != null) ExtractResultsRecursive(proc, fallbackCodes);
                    foreach (var c in fallbackCodes.Distinct()) _currentResults.Add(new CodeResult { CodeStr = c });
                }
                UpdateResultsList();
            }
        }

        #region UI Update Helpers
        private void UpdateResultsList()
        {
            if (dgvResults.InvokeRequired) { dgvResults.Invoke(new Action(UpdateResultsList)); return; }
            dgvResults.Rows.Clear();
            
            for (int i = 0; i < _gridResults.Count; i++)
            {
                var res = _gridResults[i];
                string status = res.Found ? "OK" : "NG";
                int rowIndex = dgvResults.Rows.Add(i + 1, status, res.Code);
                dgvResults.Rows[rowIndex].DefaultCellStyle.ForeColor = res.Found ? Color.LimeGreen : Color.Red;
                dgvResults.Rows[rowIndex].DefaultCellStyle.SelectionForeColor = res.Found ? Color.LimeGreen : Color.Red; // Giữ màu chữ khi click
            }
            dgvResults.ClearSelection();
            dgvResults.CurrentCell = null;
        }

        private object GetPinValue(object pinObj, bool takeFirst = false)
        {
            if (pinObj == null) return null;
            try { 
                dynamic val = null;
                try { val = ((dynamic)pinObj).Value; } catch { val = pinObj; }
                
                if (takeFirst && val is Array arr && arr.Length > 0 && !(val is byte[])) {
                    return arr.GetValue(0);
                }
                return val;
            } catch { return pinObj; }
        }

        private float GetFloatFromArray(object arrObj, int index)
        {
            if (!(arrObj is Array arr) || index >= arr.Length) return 0;
            try { 
                object val = arr.GetValue(index);
                if (val == null) return 0;
                return Convert.ToSingle(val); 
            } catch { return 0; }
        }

        private string GetStringFromObj(object obj)
        {
            if (obj == null) return "";
            try { return ((dynamic)obj).strValue; } catch { return obj.ToString(); }
        }
        #endregion

        private void ExtractResultsRecursive(object container, List<string> allFoundCodes)
        {
            try {
                dynamic c = container;
                var list = c.GetAllModuleList();
                if ((int)list.nNum == 0) return;

                for (int i = 0; i < (int)list.nNum; i++) {
                    string mName = list.astModuleInfo[i].strModuleName;
                    var module = c[mName];
                    if (module == null) continue;

                    if (mName.Contains("Code Reading") || mName.Contains("CodeRecg")) {
                        dynamic res = module.ModuResult;
                        if (res != null) {
                            try {
                                var val = res.CodeStr;
                                if (val is Array arr) {
                                    for (int j = 0; j < arr.Length; j++) allFoundCodes.Add(arr.GetValue(j)?.ToString());
                                } else { allFoundCodes.Add(val?.ToString()); }
                            } catch { }
                        }
                    }
                    if (module.GetType().Name.Contains("Group")) ExtractResultsRecursive(module, allFoundCodes);
                }
            } catch { }
        }

        private void MapToGlobalCoordinates(CodeResult res)
        {
            try {
                int camIdx = res.CameraIndex - 1; 
                if (camIdx < 0) camIdx = 0;

                Array st_cxs = GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(CenterX)"], false) as Array;
                Array st_cys = GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(CenterY)"], false) as Array;
                Array st_bws = GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(BoxWidth)"], false) as Array;
                Array st_bhs = GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(BoxHeight)"], false) as Array;
                Array st_angs = GetPinValue(VmSolution.Instance["Flow1.Image Stitch1.(Angle)"], false) as Array;

                if (st_cxs != null && camIdx < st_cxs.Length) {
                    float s_cx = GetFloatFromArray(st_cxs, camIdx);
                    float s_cy = GetFloatFromArray(st_cys, camIdx);
                    float s_bw = GetFloatFromArray(st_bws, camIdx);
                    float s_bh = GetFloatFromArray(st_bhs, camIdx);
                    float s_ang = GetFloatFromArray(st_angs, camIdx);

                    float srcW = 5472f, srcH = 3648f, roiX = 0f, roiY = 0f;

                    try {
                        string toolName = $"DL Code Reading CPU{res.CameraIndex + 1}";
                        dynamic tool = VmSolution.Instance["Flow1." + toolName];
                        if (tool != null) {
                            var img = GetPinValue(tool.InputImage);
                            if (img != null) { srcW = (float)((dynamic)img).Width; srcH = (float)((dynamic)img).Height; }
                            try { var roi = tool.ROI; roiX = (float)roi.X; roiY = (float)roi.Y; } catch { }
                        }
                    } catch { }

                    float scale_x = (srcW > 0) ? s_bw / srcW : 1.0f;
                    float scale_y = (srcH > 0) ? s_bh / srcH : 1.0f;
                    float rel_x = (res.CenterX + roiX - srcW / 2f) * scale_x;
                    float rel_y = (res.CenterY + roiY - srcH / 2f) * scale_y;
                    double rad = s_ang * Math.PI / 180.0;
                    float rot_x = (float)(rel_x * Math.Cos(rad) - rel_y * Math.Sin(rad));
                    float rot_y = (float)(rel_x * Math.Sin(rad) + rel_y * Math.Cos(rad));

                    res.MappedX = rot_x + s_cx;
                    res.MappedY = rot_y + s_cy;
                    res.Angle += s_ang;
                    res.ScaledWidth = res.BoxWidth * scale_x;
                    res.ScaledHeight = res.BoxHeight * scale_y;
                }
                else {
                    res.MappedX = res.CenterX;
                    res.MappedY = res.CenterY;
                }
            } catch { 
                res.MappedX = res.CenterX;
                res.MappedY = res.CenterY;
            }
        }
        #endregion

        #region Helper Methods
        private Bitmap ImageBaseDataToBitmap(dynamic imgData, int overrideWidth = 0, int overrideHeight = 0, int overrideFmt = -1)
        {
            try
            {
                if (imgData == null) return null;
                if (imgData is Array arr && arr.Length > 0 && !(imgData is byte[])) imgData = arr.GetValue(0);
                
                Type t = imgData.GetType();
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                Func<string, object> getProp = (name) => {
                    try {
                        var p = t.GetProperty(name, flags); if (p != null) return p.GetValue(imgData);
                        var f = t.GetField(name, flags); if (f != null) return f.GetValue(imgData);
                    } catch { }
                    return null;
                };

                int width = overrideWidth > 0 ? overrideWidth : Convert.ToInt32(getProp("Width") ?? getProp("nWidth") ?? getProp("nImageWidth") ?? 0);
                int height = overrideHeight > 0 ? overrideHeight : Convert.ToInt32(getProp("Height") ?? getProp("nHeight") ?? getProp("nImageHeight") ?? 0);
                if (width <= 0 || height <= 0) return null;

                int format = overrideFmt != -1 ? overrideFmt : Convert.ToInt32(getProp("enPixelFormat") ?? getProp("nPixelFormat") ?? 1);
                var pixelFormat = (format == (int)VMPixelFormat.VM_PIXEL_MONO_08 || format == 1) 
                    ? System.Drawing.Imaging.PixelFormat.Format8bppIndexed 
                    : System.Drawing.Imaging.PixelFormat.Format24bppRgb;

                Bitmap bmp = new Bitmap(width, height, pixelFormat);
                var rect = new System.Drawing.Rectangle(0, 0, width, height);
                var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, pixelFormat);
                try {
                    IntPtr pSrc = IntPtr.Zero;
                    
                    try { pSrc = (IntPtr)imgData.MemoryPtr; } catch { }
                    if (pSrc == IntPtr.Zero) try { pSrc = (IntPtr)imgData.pData; } catch { }
                    if (pSrc == IntPtr.Zero) try { pSrc = (IntPtr)imgData.pBuffer; } catch { }

                    if (pSrc == IntPtr.Zero) {
                        var reflectionFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        foreach (string propName in new[] { "MemoryPtr", "pData", "pBuffer", "ImageData", "pDataPtr" }) {
                            var p = t.GetProperty(propName, reflectionFlags);
                            if (p != null) {
                                object val = p.GetValue(imgData);
                                if (val is IntPtr ip && ip != IntPtr.Zero) { pSrc = ip; break; }
                                if (val is long l && l != 0) { pSrc = (IntPtr)l; break; }
                            }
                            var f = t.GetField(propName, reflectionFlags);
                            if (f != null) {
                                object val = f.GetValue(imgData);
                                if (val is IntPtr ip && ip != IntPtr.Zero) { pSrc = ip; break; }
                                if (val is long l && l != 0) { pSrc = (IntPtr)l; break; }
                            }
                        }
                    }
                    
                    if (pSrc == IntPtr.Zero && imgData is byte[] rawBytes) {
                        System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, bmpData.Scan0, Math.Min(rawBytes.Length, bmpData.Stride * height));
                    }
                    else if (pSrc != IntPtr.Zero) {
                        try {
                            int bpp = (pixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed) ? 1 : 3;
                            int stride = width * bpp;
                            try { stride = Convert.ToInt32(getProp("nStride") ?? getProp("Stride") ?? (width * bpp)); } catch { }
                            for (int y = 0; y < height; y++) {
                                CopyMemory((IntPtr)((long)bmpData.Scan0 + y * bmpData.Stride), (IntPtr)((long)pSrc + y * stride), (uint)(width * bpp));
                            }
                        } catch { /* Suppress memory access errors to prevent dmp creation */ }
                    }
                }
                finally { bmp.UnlockBits(bmpData); }

                if (pixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed) {
                    var pal = bmp.Palette;
                    for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
                    bmp.Palette = pal;
                }
                return bmp;
            }
            catch { return null; }
        }

        private void SendTcpData(string data)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(txtTcpIp.Text, (int)nudTcpPort.Value, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2))) return;
                    client.EndConnect(result);
                    byte[] bytes = Encoding.UTF8.GetBytes(data + "\r\n");
                    client.GetStream().Write(bytes, 0, bytes.Length);
                }
            }
            catch { }
        }

        private void Log(string tag, string msg)
        {
            if (rtbLog.InvokeRequired) { rtbLog.Invoke(new Action(() => Log(tag, msg))); return; }
            rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] [{tag}] {msg}\n");
            rtbLog.ScrollToCaret();
        }

        private void LoadAppConfig()
        {
            try
            {
                string solFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visionmaster.last.txt");
                if (System.IO.File.Exists(solFile)) {
                    txtSolPath.Text = System.IO.File.ReadAllText(solFile).Trim();
                    if (!string.IsNullOrEmpty(txtSolPath.Text)) LoadSolution();
                }

                string tcpFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tcp.settings.txt");
                if (System.IO.File.Exists(tcpFile)) {
                    var lines = System.IO.File.ReadAllLines(tcpFile);
                    if (lines.Length >= 2) {
                        txtTcpIp.Text = lines[0];
                        if (decimal.TryParse(lines[1], out decimal port)) nudTcpPort.Value = port;
                    }
                }

                string calibFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grid.settings.txt");
                if (System.IO.File.Exists(calibFile)) {
                    var lines = System.IO.File.ReadAllLines(calibFile);
                    if (lines.Length >= 6) {
                        float.TryParse(lines[0], out float x);
                        float.TryParse(lines[1], out float y);
                        float.TryParse(lines[2], out float w);
                        float.TryParse(lines[3], out float h);
                        int.TryParse(lines[4], out _gridRows);
                        int.TryParse(lines[5], out _gridCols);
                        _roiRect = new System.Drawing.RectangleF(x, y, w, h);
                        nudRows.Value = _gridRows;
                        nudCols.Value = _gridCols;
                        if (lines.Length >= 7) {
                            bool.TryParse(lines[6], out _showGridBoxes);
                            chkShowGrid.Checked = _showGridBoxes;
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveTcpConfig()
        {
            try {
                string tcpFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tcp.settings.txt");
                System.IO.File.WriteAllLines(tcpFile, new[] { txtTcpIp.Text, nudTcpPort.Value.ToString() });
                
                string calibFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grid.settings.txt");
                System.IO.File.WriteAllLines(calibFile, new[] { 
                    _roiRect.X.ToString(), _roiRect.Y.ToString(), 
                    _roiRect.Width.ToString(), _roiRect.Height.ToString(),
                    _gridRows.ToString(), _gridCols.ToString(),
                    chkShowGrid.Checked.ToString()
                });
                Log("Config", "Settings saved.");
            } catch (Exception ex) { Log("Error", "Save failed: " + ex.Message); }
        }

        private void DisposeResources()
        {
            try
            {
                // 1. Dừng nạp nếu đang chạy
                if (_vmLoader != null && _vmLoader.IsBusy) _vmLoader.CancelAsync();

                // 2. Giải phóng bộ nhớ ảnh
                lock (_imageLock) {
                    if (_lastStitchedImage != null) {
                        _lastStitchedImage.Dispose();
                        _lastStitchedImage = null;
                    }
                }

                // 3. Ép buộc giải phóng tài nguyên và COM objects để tránh dmp khi thoát
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                Log("System", "Resources released successfully.");
            }
            catch { }
        }

        private void PicStitched_Paint(object sender, PaintEventArgs e)
        {
            Bitmap bmp;
            lock (_imageLock) { bmp = _lastStitchedImage != null ? new Bitmap(_lastStitchedImage) : null; }
            if (bmp == null) {
                e.Graphics.Clear(Color.Black);
                e.Graphics.DrawString("Chưa có ảnh. Hãy nhấn READ CODE", this.Font, Brushes.Gray, 10, 10);
                return;
            }

            using (bmp)
            {
                // 1. Tính toán tỷ lệ hiển thị
                float sc = Math.Min((float)picStitched.Width / bmp.Width, (float)picStitched.Height / bmp.Height);
                float ox = (picStitched.Width - bmp.Width * sc) / 2;
                float oy = (picStitched.Height - bmp.Height * sc) / 2;

                // 2. Thiết lập Transform chuẩn (Dịch chuyển và Scale)
                e.Graphics.TranslateTransform(ox, oy);
                e.Graphics.ScaleTransform(sc, sc);

                // 3. Vẽ ảnh thực tế
                e.Graphics.DrawImage(bmp, 0, 0);

                // 4. Vẽ Grid QC
                using (Pen roiPen = new Pen(Color.Yellow, 3 / sc) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    // e.Graphics.DrawRectangle(roiPen, _roiRect.X, _roiRect.Y, _roiRect.Width, _roiRect.Height);
                    float cellW = _roiRect.Width / _gridCols;
                    float cellH = _roiRect.Height / _gridRows;

                    for (int r = 0; r < _gridRows; r++)
                    {
                        for (int c = 0; c < _gridCols; c++)
                        {
                            int id = r * _gridCols + c;
                            System.Drawing.RectangleF cell = new System.Drawing.RectangleF(_roiRect.X + c * cellW, _roiRect.Y + r * cellH, cellW, cellH);
                            
                            bool isOk = id < _gridResults.Count && _gridResults[id].Found;
                            
                            // Chỉ vẽ ô màu nếu được bật
                            if (_showGridBoxes) {
                                Color cellColor = isOk ? Color.FromArgb(80, 0, 255, 0) : Color.FromArgb(80, 255, 0, 0);
                                if (_gridResults.Count == 0) cellColor = Color.FromArgb(40, 255, 255, 255);
                                using (Brush b = new SolidBrush(cellColor)) e.Graphics.FillRectangle(b, cell);
                                e.Graphics.DrawRectangle(new Pen(isOk ? Color.Lime : Color.Red, 1 / sc), cell.X, cell.Y, cell.Width, cell.Height);
                            }
                            
                            // Luôn vẽ số thứ tự
                            float fontSize = 18 / sc;
                            using (Font f = new Font("Arial", fontSize, FontStyle.Bold))
                                e.Graphics.DrawString((id + 1).ToString(), f, isOk ? Brushes.Lime : Brushes.Red, cell.X + 5/sc, cell.Y + 5/sc);
                        }
                    }
                }

                // 5. Vẽ BBox NGUYÊN BẢN (rotated boxes) bao quanh mã code
                lock (_currentResults)
                {
                    using (Pen p = new Pen(Color.Lime, 3 / sc))
                    {
                        foreach (var res in _currentResults)
                        {
                            var state = e.Graphics.Save();
                            e.Graphics.TranslateTransform(res.MappedX, res.MappedY);
                            e.Graphics.RotateTransform(res.Angle);
                            
                            // Vẽ khung bao với kích thước ĐÃ ĐƯỢC SCALE để khớp với ảnh gộp
                            e.Graphics.DrawRectangle(p, -res.ScaledWidth/2, -res.ScaledHeight/2, res.ScaledWidth, res.ScaledHeight);
                            
                            e.Graphics.Restore(state);
                        }
                    }
                }
            }
        }
        #endregion
    }
}
