using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Basler.Pylon;

namespace MultiCameraBaslerApp.UI
{
    public partial class CameraSelectionDialog : Form
    {
        private TextBox txtCamera1Serial, txtCamera2Serial;
        private Button btnOK, btnCancel, btnDiscover;
        private Label lblStatus;
        private RichTextBox rtbInfo;

        public string SelectedCamera1SerialNumber { get; set; }
        public string SelectedCamera1Name { get; set; }
        public string SelectedCamera2SerialNumber { get; set; }
        public string SelectedCamera2Name { get; set; }

        public CameraSelectionDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Cameras - Enter Serial Numbers";
            this.Width = 600;
            this.Height = 400;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Main layout
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(10)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            // Title
            Label lblTitle = new Label
            {
                Text = "Enter Camera Serial Numbers",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Arial", 12, FontStyle.Bold)
            };
            mainLayout.Controls.Add(lblTitle, 0, 0);

            // Input layout
            TableLayoutPanel inputLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            inputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            inputLayout.Controls.Add(new Label { Text = "Camera 1:", Dock = DockStyle.Fill }, 0, 0);
            txtCamera1Serial = new TextBox { Dock = DockStyle.Fill };
            inputLayout.Controls.Add(txtCamera1Serial, 1, 0);

            inputLayout.Controls.Add(new Label { Text = "Camera 2:", Dock = DockStyle.Fill }, 0, 1);
            txtCamera2Serial = new TextBox { Dock = DockStyle.Fill };
            inputLayout.Controls.Add(txtCamera2Serial, 1, 1);

            mainLayout.Controls.Add(inputLayout, 0, 1);

            // Info display
            rtbInfo = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Text = "Enter the serial numbers of your Basler cameras.\n\n" +
                       "To find camera serial numbers:\n" +
                       "1. Use Basler Pylon Viewer\n" +
                       "2. Or check the camera physical labels\n\n" +
                       "Example: 40166741"
            };
            mainLayout.Controls.Add(rtbInfo, 0, 2);

            // Status label
            lblStatus = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.Blue
            };
            mainLayout.Controls.Add(lblStatus, 0, 3);

            // Buttons
            FlowLayoutPanel btnLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            btnOK = new Button { Text = "OK", Width = 80, Height = 30 };
            btnOK.Click += (s, e) => OK_Click();

            btnCancel = new Button { Text = "Cancel", Width = 80, Height = 30 };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            btnDiscover = new Button { Text = "Discover", Width = 80, Height = 30 };
            btnDiscover.Click += (s, e) => DiscoverCameras();

            btnLayout.Controls.Add(btnCancel);
            btnLayout.Controls.Add(btnOK);
            btnLayout.Controls.Add(btnDiscover);

            mainLayout.Controls.Add(btnLayout, 0, 4);

            this.Controls.Add(mainLayout);
        }

        private void DiscoverCameras()
        {
            try
            {
                btnDiscover.Enabled = false;
                btnDiscover.Text = "Searching...";
                lblStatus.Text = "Discovering cameras...";
                lblStatus.ForeColor = Color.Blue;

                rtbInfo.Clear();
                rtbInfo.AppendText("Attempting to enumerate connected cameras...\n\n");

                // Try CameraFinder.Enumerate (static method)
                List<ICameraInfo> cameras = new List<ICameraInfo>();

                try
                {
                    cameras = CameraFinder.Enumerate();
                    rtbInfo.AppendText($"Found {cameras.Count} camera(s)\n\n");
                }
                catch (Exception ex)
                {
                    rtbInfo.AppendText($"Discovery failed: {ex.Message}\n\n");
                }

                // Display found cameras
                if (cameras.Count > 0)
                {
                    for (int i = 0; i < cameras.Count; i++)
                    {
                        ICameraInfo cam = cameras[i];
                        string serialNumber = cam[CameraInfoKey.SerialNumber];
                        string modelName = cam[CameraInfoKey.ModelName];
                        
                        rtbInfo.AppendText($"Camera {i + 1}: {modelName}\n");
                        rtbInfo.AppendText($"Serial: {serialNumber}\n\n");

                        // Auto-populate first two cameras
                        if (i == 0)
                            txtCamera1Serial.Text = serialNumber;
                        else if (i == 1)
                            txtCamera2Serial.Text = serialNumber;
                    }

                    lblStatus.Text = $"Found {cameras.Count} camera(s)";
                    lblStatus.ForeColor = Color.Green;
                }
                else
                {
                    rtbInfo.AppendText("No cameras found. Please enter serial numbers manually.\n");
                    lblStatus.Text = "No cameras found";
                    lblStatus.ForeColor = Color.Orange;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error during discovery";
                lblStatus.ForeColor = Color.Red;
                rtbInfo.Clear();
                rtbInfo.AppendText("ERROR: " + ex.Message);
            }
            finally
            {
                btnDiscover.Enabled = true;
                btnDiscover.Text = "Discover";
            }
        }

        private void OK_Click()
        {
            string cam1Serial = txtCamera1Serial.Text.Trim();
            string cam2Serial = txtCamera2Serial.Text.Trim();

            if (string.IsNullOrEmpty(cam1Serial) || string.IsNullOrEmpty(cam2Serial))
            {
                lblStatus.Text = "Please enter both camera serial numbers";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            if (cam1Serial == cam2Serial)
            {
                lblStatus.Text = "Camera 1 and Camera 2 must be different!";
                lblStatus.ForeColor = Color.Red;
                return;
            }

            try
            {
                SelectedCamera1SerialNumber = cam1Serial;
                SelectedCamera1Name = $"Camera 1 (SN: {cam1Serial})";
                
                SelectedCamera2SerialNumber = cam2Serial;
                SelectedCamera2Name = $"Camera 2 (SN: {cam2Serial})";

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error: " + ex.Message;
                lblStatus.ForeColor = Color.Red;
            }
        }
    }
}
