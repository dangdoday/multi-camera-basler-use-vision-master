using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using MultiCameraBaslerApp.Core;

namespace MultiCameraBaslerApp.UI.Controls
{
    // Image window containing a live view and counters for images, errors, and frame rate.
    public partial class CameraDisplayPanel : UserControl
    {
        private int FPS_AGGREGATION_TIME_S = 5;
        private int imageCountOld = 0;
        private List<int> imageCounts = new List<int>();
        private DualCameraController cameraController;
        private Bitmap image;

        public CameraDisplayPanel()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
        }

        public void SetCamera(DualCameraController cam)
        {
            cameraController = cam;
            if (cam != null)
            {
                cam.FrameReady += OnImageReady;
            }
        }

        public void Clear()
        {
            if (fpsLabel != null) fpsLabel.Text = "";
            if (ErrorsCount != null) ErrorsCount.Text = "";
            if (ImagesCount != null) ImagesCount.Text = "";
            imageCountOld = 0;
            imageCounts.Clear();
            if (image != null)
            {
                image.Dispose();
            }
            image = null;
            Invalidate();
        }

        public void InitCounters()
        {
            imageCountOld = 0;
            imageCounts.Clear();
            if (ImagesCount != null) ImagesCount.Text = "Images: 0";
            if (ErrorsCount != null) ErrorsCount.Text = "Errors: 0";
            if (fpsLabel != null) fpsLabel.Text = "Frame rate: 0.0";
        }

        public void StartFpsCounter()
        {
            InitCounters();
            if (fpsTimer != null)
                fpsTimer.Start();
        }

        public void StopFpsCounter()
        {
            if (fpsTimer != null)
                fpsTimer.Stop();
        }

        private void FpsTimerTick(object sender, EventArgs e)
        {
            if (cameraController == null)
                return;

            int imageCount = cameraController.ImageCount;
            int errorCount = cameraController.ErrorCount;

            double fpsApproximation = 0.0;
            if (cameraController.IsGrabbing)
            {
                if (imageCounts.Count > 0)
                {
                    int imageCountCurrent = imageCount - imageCountOld;
                    imageCounts.Add(imageCountCurrent);
                    while (imageCounts.Count > FPS_AGGREGATION_TIME_S)
                    {
                        imageCounts.RemoveAt(0);
                    }
                    int sum = imageCounts.Sum();
                    fpsApproximation = (double)sum / (double)imageCounts.Count;
                }
                else
                {
                    int imageCountCurrent = imageCount - imageCountOld;
                    if (imageCountOld != 0)
                    {
                        imageCounts.Add(imageCountCurrent);
                    }
                    fpsApproximation = (double)imageCountCurrent;
                }
            }

            if (fpsLabel != null) fpsLabel.Text = String.Format("Frame rate: {0:0.0}", fpsApproximation);
            if (ImagesCount != null) ImagesCount.Text = "Images: " + imageCount;
            if (ErrorsCount != null) ErrorsCount.Text = "Errors: " + errorCount;
            imageCountOld = imageCount;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (image != null)
            {
                double aspectRatioImage = (double)(image.Width) / (double)(image.Height);
                int availableDisplayWidth = this.Width;
                int availableDisplayHeight = this.Height - 50; // Reserve space for labels
                int requiredDisplayHeight = (int)((double)(this.Width) / aspectRatioImage);
                int drawingAreaWidth = availableDisplayWidth;
                int drawingAreaHeight = requiredDisplayHeight;

                if (availableDisplayHeight < requiredDisplayHeight)
                {
                    drawingAreaWidth = (int)(availableDisplayHeight * aspectRatioImage);
                    drawingAreaHeight = availableDisplayHeight;
                }

                Point drawingPosition = new Point(0, 0);
                Size drawingSize = new Size(drawingAreaWidth, drawingAreaHeight);
                Rectangle drawingArea = new Rectangle(drawingPosition, drawingSize);
                e.Graphics.DrawImage(image, drawingArea, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }
            else
            {
                e.Graphics.Clear(this.BackColor);
            }
        }

        private void OnImageReady(Object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new EventHandler<EventArgs>(OnImageReady), sender, e);
                return;
            }

            if (cameraController != null)
            {
                Bitmap newImage = cameraController.GetLatestFrame();
                if (newImage != null)
                {
                    if (image != null)
                    {
                        image.Dispose();
                    }
                    image = newImage;
                }
            }

            this.Invalidate();
            this.Update();
        }

        private void CameraDisplayPanelResize(object sender, EventArgs e)
        {
            this.Invalidate();
        }
    }
}
