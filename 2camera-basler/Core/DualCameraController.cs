using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Basler.Pylon;

namespace MultiCameraBaslerApp.Core
{
    public class DualCameraController : IDisposable
    {
        private const double RENDERFPS = 100;
        
        private Camera camera;
        private string cameraSerialNumber;
        private string cameraName;
        
        private PixelDataConverter converter = new PixelDataConverter();
        private System.Diagnostics.Stopwatch stopwatch;
        private int frameDurationTicks;

        private Object monitor = new Object();
        private Bitmap latestFrame = null;
        private int imageCount = 0;
        private int errorCount = 0;
        private bool isTriggered = false;

        // Events for UI updates
        public event EventHandler CameraOpened;
        public event EventHandler CameraClosed;
        public event EventHandler GrabStarted;
        public event EventHandler GrabStopped;
        public event EventHandler FrameReady;
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;

        public DualCameraController(string serialNumber, string name)
        {
            cameraSerialNumber = serialNumber;
            cameraName = name;
            
            stopwatch = new System.Diagnostics.Stopwatch();
            double frametime = 1 / RENDERFPS;
            frameDurationTicks = (int)(System.Diagnostics.Stopwatch.Frequency * frametime);
        }

        public string CameraName
        {
            get { return cameraName; }
        }

        public string SerialNumber
        {
            get { return cameraSerialNumber; }
        }

        public bool IsCreated
        {
            get { return camera != null; }
        }

        public bool IsOpen
        {
            get { return IsCreated && camera.IsOpen; }
        }

        public bool IsGrabbing
        {
            get { return IsOpen && camera.StreamGrabber.IsGrabbing; }
        }

        public int ImageCount
        {
            get { return imageCount; }
        }

        public int ErrorCount
        {
            get { return errorCount; }
        }

        public bool SetExposureTime(double exposureTimeMicroseconds)
        {
            try
            {
                if (!IsOpen)
                    return false;

                camera.Parameters[PLCamera.ExposureTime].TrySetValue(exposureTimeMicroseconds);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public Bitmap GetLatestFrame()
        {
            lock (monitor)
            {
                if (latestFrame != null)
                {
                    Bitmap returnedBitmap = latestFrame;
                    latestFrame = null;
                    return returnedBitmap;
                }
                return null;
            }
        }

        public bool CreateCamera()
        {
            try
            {
                if (IsCreated)
                {
                    DestroyCamera();
                }
                
                camera = new Camera(cameraSerialNumber);
                ConnectToCameraEvents();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool OpenCamera()
        {
            try
            {
                if (!IsCreated)
                    return false;

                if (!IsOpen)
                {
                    camera.Open();
                    CameraOpened?.Invoke(this, EventArgs.Empty);
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool CloseCamera()
        {
            try
            {
                if (IsGrabbing)
                {
                    StopGrabbing();
                }
                if (IsOpen)
                {
                    camera.Close();
                    CameraClosed?.Invoke(this, EventArgs.Empty);
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public void DestroyCamera()
        {
            try
            {
                if (IsGrabbing)
                {
                    StopGrabbing();
                }
                if (IsOpen)
                {
                    CloseCamera();
                }
                if (IsCreated)
                {
                    DisconnectFromCameraEvents();
                    camera.Dispose();
                    camera = null;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
        }

        protected void ConnectToCameraEvents()
        {
            if (IsCreated)
            {
                camera.CameraOpened += OnCameraOpened;
                camera.CameraClosed += OnCameraClosed;
                camera.StreamGrabber.GrabStarted += OnGrabStarted;
                camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
                camera.StreamGrabber.GrabStopped += OnGrabStopped;
            }
        }

        protected void DisconnectFromCameraEvents()
        {
            if (IsCreated)
            {
                camera.CameraOpened -= OnCameraOpened;
                camera.CameraClosed -= OnCameraClosed;
                camera.StreamGrabber.GrabStarted -= OnGrabStarted;
                camera.StreamGrabber.ImageGrabbed -= OnImageGrabbed;
                camera.StreamGrabber.GrabStopped -= OnGrabStopped;
            }
        }

        public bool StartContinuousGrabbing()
        {
            try
            {
                if (!IsOpen)
                    return false;

                ResetGrabStatistics();
                Configuration.AcquireContinuous(camera, null);
                camera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool StartSingleShotGrabbing()
        {
            try
            {
                if (!IsOpen)
                    return false;

                ResetGrabStatistics();
                Configuration.AcquireSingleFrame(camera, null);
                camera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public Bitmap GrabSingleFrameSynchronously()
        {
            IGrabResult grabResult = null;
            try
            {
                if (!IsOpen) 
                {
                    ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = new Exception($"{cameraName}: Camera chưa mở.") });
                    return null;
                }
                
                // Disconnect event to avoid interference during sync grab
                camera.StreamGrabber.ImageGrabbed -= OnImageGrabbed;

                if (camera.StreamGrabber.IsGrabbing)
                {
                    camera.StreamGrabber.Stop();
                }

                // Explicitly set Software Trigger mode
                camera.Parameters[PLCamera.TriggerSelector].TrySetValue(PLCamera.TriggerSelector.FrameStart);
                camera.Parameters[PLCamera.TriggerMode].TrySetValue(PLCamera.TriggerMode.On);
                camera.Parameters[PLCamera.TriggerSource].TrySetValue(PLCamera.TriggerSource.Software);
                camera.Parameters[PLCamera.AcquisitionMode].TrySetValue(PLCamera.AcquisitionMode.SingleFrame);
                
                camera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);
                
                // Wait for camera to be ready for trigger
                if (camera.WaitForFrameTriggerReady(1000, TimeoutHandling.Return))
                {
                    camera.ExecuteSoftwareTrigger();
                }

                using (grabResult = camera.StreamGrabber.RetrieveResult(5000, TimeoutHandling.Return))
                {
                    if (grabResult != null && grabResult.GrabSucceeded)
                    {
                        Interlocked.Increment(ref imageCount);
                        Bitmap bitmap;
                        
                        // Check camera parameters instead of grabResult
                        string camPixelFormat = camera.Parameters[PLCamera.PixelFormat].GetValue();
                        bool isMono = camPixelFormat.Contains("Mono");

                        if (isMono)
                        {
                            bitmap = new Bitmap(grabResult.Width, grabResult.Height, PixelFormat.Format8bppIndexed);
                            ColorPalette palette = bitmap.Palette;
                            for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
                            bitmap.Palette = palette;
                            converter.OutputPixelFormat = PixelType.Mono8;
                        }
                        else
                        {
                            bitmap = new Bitmap(grabResult.Width, grabResult.Height, PixelFormat.Format24bppRgb);
                            converter.OutputPixelFormat = PixelType.BGR8packed;
                        }

                        BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                        IntPtr ptrBmp = bmpData.Scan0;
                        converter.Convert(ptrBmp, bmpData.Stride * bitmap.Height, grabResult);
                        bitmap.UnlockBits(bmpData);
                        return bitmap;
                    }
                    else
                    {
                        string errorMsg = (grabResult == null) ? "GrabResult null (Timeout)" : "Grab failed";
                        ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = new Exception($"{cameraName}: {errorMsg}") });
                    }
                }
                
                Interlocked.Increment(ref errorCount);
                return null;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errorCount);
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = new Exception($"{cameraName} Grab Error: {ex.Message}") });
                return null;
            }
            finally
            {
                if (grabResult != null) grabResult.Dispose();
                try { if (camera.StreamGrabber.IsGrabbing) camera.StreamGrabber.Stop(); } catch {}
                // Reconnect event
                camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
            }
        }

        public bool StartSoftwareTriggerMode()
        {
            try
            {
                if (!IsOpen)
                    return false;

                ResetGrabStatistics();
                Configuration.AcquireContinuous(camera, null);
                
                // Configure software trigger - using correct Basler API
                camera.Parameters[PLCamera.TriggerSelector].TrySetValue(PLCamera.TriggerSelector.FrameStart);
                camera.Parameters[PLCamera.TriggerMode].TrySetValue(PLCamera.TriggerMode.On);
                camera.Parameters[PLCamera.TriggerSource].TrySetValue(PLCamera.TriggerSource.Software);
                
                camera.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
                isTriggered = true;
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool ExecuteTrigger()
        {
            try
            {
                if (!IsGrabbing || !isTriggered)
                    return false;

                // Use correct Basler API for software trigger
                if (camera.WaitForFrameTriggerReady(100, TimeoutHandling.Return))
                {
                    camera.ExecuteSoftwareTrigger();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool StopGrabbing()
        {
            try
            {
                if (IsGrabbing)
                {
                    camera.StreamGrabber.Stop();
                    isTriggered = false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        protected void ResetGrabStatistics()
        {
            Interlocked.Exchange(ref imageCount, 0);
            Interlocked.Exchange(ref errorCount, 0);
        }

        protected void ClearLatestFrame()
        {
            lock (monitor)
            {
                if (latestFrame != null)
                {
                    latestFrame.Dispose();
                    latestFrame = null;
                }
            }
        }

        private void OnCameraOpened(object sender, EventArgs e)
        {
            CameraOpened?.Invoke(this, e);
        }

        private void OnCameraClosed(object sender, EventArgs e)
        {
            CameraClosed?.Invoke(this, e);
        }

        private void OnGrabStarted(object sender, EventArgs e)
        {
            stopwatch.Restart();
            GrabStarted?.Invoke(this, e);
        }

        private void OnGrabStopped(object sender, EventArgs e)
        {
            GrabStopped?.Invoke(this, e);
        }

        private void OnImageGrabbed(object sender, ImageGrabbedEventArgs e)
        {
            try
            {
                if (e.GrabResult.GrabSucceeded)
                {
                    Interlocked.Increment(ref imageCount);

                    stopwatch.Stop();
                    if (stopwatch.ElapsedTicks < frameDurationTicks)
                    {
                        int sleepMs = (int)((frameDurationTicks - stopwatch.ElapsedTicks) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
                        if (sleepMs > 0)
                            System.Threading.Thread.Sleep(sleepMs);
                    }

                    lock (monitor)
                    {
                        if (latestFrame != null)
                            latestFrame.Dispose();
 
                        Bitmap bitmap;
                        string camPixelFormat = camera.Parameters[PLCamera.PixelFormat].GetValue();
                        bool isMono = camPixelFormat.Contains("Mono");

                        if (isMono)
                        {
                            bitmap = new Bitmap(e.GrabResult.Width, e.GrabResult.Height, PixelFormat.Format8bppIndexed);
                            ColorPalette palette = bitmap.Palette;
                            for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
                            bitmap.Palette = palette;
                            converter.OutputPixelFormat = PixelType.Mono8;
                        }
                        else
                        {
                            bitmap = new Bitmap(e.GrabResult.Width, e.GrabResult.Height, PixelFormat.Format24bppRgb);
                            converter.OutputPixelFormat = PixelType.BGR8packed;
                        }

                        BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                        IntPtr ptrBmp = bmpData.Scan0;
                        converter.Convert(ptrBmp, bmpData.Stride * bitmap.Height, e.GrabResult);
                        bitmap.UnlockBits(bmpData);
                        latestFrame = bitmap;
 
                        FrameReady?.Invoke(this, EventArgs.Empty);
                    }
                }
                else
                {
                    Interlocked.Increment(ref errorCount);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errorCount);
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
            finally
            {
                e.GrabResult.Dispose();
                stopwatch.Restart();
            }
        }

        public void Dispose()
        {
            DestroyCamera();
            converter?.Dispose();
        }

        public IParameterCollection Parameters
        {
            get { return IsCreated ? camera.Parameters : null; }
        }
    }

    public class ExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
    }
}
