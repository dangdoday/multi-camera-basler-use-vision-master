using System;
using System.Collections.Generic;
using System.Drawing;
using Basler.Pylon;
using VM.Core;
using VM.PlatformSDKCS;

namespace MultiCameraBaslerApp.Core
{
    public class MultiCameraManager : IDisposable
    {
        private List<DualCameraController> cameras = new List<DualCameraController>();
        private int maxCameras = 2;

        public event EventHandler<string> CameraDiscovered;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<ExceptionEventArgs> ErrorOccurred;

        public MultiCameraManager(int maxCameraCount = 2)
        {
            maxCameras = maxCameraCount;
            // Pylon is initialized automatically when first Camera is created
            InitializeVisionMaster();
        }

        private void InitializeVisionMaster()
        {
            try
            {
                // VisionMaster initialization placeholder
                StatusChanged?.Invoke(this, "Camera manager initialized");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
        }

        public List<ICameraInfo> DiscoverAvailableCameras()
        {
            List<ICameraInfo> availableCameras = new List<ICameraInfo>();
            try
            {
                // Note: Auto-discovery of cameras requires specific Basler SDK methods
                // For now, return empty list and use manual serial number creation
                // User should create cameras using CreateCameras() with known serial numbers
                StatusChanged?.Invoke(this, "Camera discovery requires manual serial number input");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }

            return availableCameras;
        }

        public void CreateCameraFromSerialNumber(string serialNumber, string displayName)
        {
            try
            {
                List<ICameraInfo> cameraList = new List<ICameraInfo>();
                // Create a dummy ICameraInfo for the given serial number
                // This will be passed to CreateCamera method
                DualCameraController controller = new DualCameraController(serialNumber, displayName);
                controller.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
                controller.CameraOpened += (s, e) => StatusChanged?.Invoke(s, $"{displayName} opened");
                controller.CameraClosed += (s, e) => StatusChanged?.Invoke(s, $"{displayName} closed");
                controller.GrabStarted += (s, e) => StatusChanged?.Invoke(s, $"{displayName} grabbing started");
                controller.GrabStopped += (s, e) => StatusChanged?.Invoke(s, $"{displayName} grabbing stopped");

                if (!controller.CreateCamera())
                {
                    StatusChanged?.Invoke(this, $"Failed to create {displayName}");
                    return;
                }

                cameras.Add(controller);
                CameraDiscovered?.Invoke(this, $"Created camera: {displayName} (SN: {serialNumber})");
                StatusChanged?.Invoke(this, $"{displayName} created successfully");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
        }

        public bool CreateCameras(List<ICameraInfo> cameraInfoList)
        {
            try
            {
                // Clean up existing cameras
                DisposeCameras();

                int cameraNumber = 1;
                foreach (ICameraInfo cameraInfo in cameraInfoList)
                {
                    if (cameraNumber > maxCameras)
                        break;

                    string serialNumber = cameraInfo[CameraInfoKey.SerialNumber];
                    string modelName = cameraInfo[CameraInfoKey.ModelName];
                    string cameraDisplayName = $"Camera {cameraNumber} ({modelName})";

                    DualCameraController controller = new DualCameraController(serialNumber, cameraDisplayName);
                    controller.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
                    controller.CameraOpened += (s, e) => StatusChanged?.Invoke(s, $"{cameraDisplayName} opened");
                    controller.CameraClosed += (s, e) => StatusChanged?.Invoke(s, $"{cameraDisplayName} closed");
                    controller.GrabStarted += (s, e) => StatusChanged?.Invoke(s, $"{cameraDisplayName} grabbing started");
                    controller.GrabStopped += (s, e) => StatusChanged?.Invoke(s, $"{cameraDisplayName} grabbing stopped");

                    if (!controller.CreateCamera())
                    {
                        StatusChanged?.Invoke(this, $"Failed to create {cameraDisplayName}");
                        continue;
                    }

                    cameras.Add(controller);
                    StatusChanged?.Invoke(this, $"{cameraDisplayName} created successfully");
                    cameraNumber++;
                }

                return cameras.Count > 0;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public DualCameraController GetCamera(int index)
        {
            if (index >= 0 && index < cameras.Count)
                return cameras[index];
            return null;
        }

        public int CameraCount
        {
            get { return cameras.Count; }
        }

        public bool OpenAllCameras()
        {
            try
            {
                bool allOpened = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.OpenCamera())
                        allOpened = false;
                }
                return allOpened;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool CloseAllCameras()
        {
            try
            {
                bool allClosed = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.CloseCamera())
                        allClosed = false;
                }
                return allClosed;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool StartContinuousGrabbingAll()
        {
            try
            {
                bool allStarted = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.StartContinuousGrabbing())
                        allStarted = false;
                }
                StatusChanged?.Invoke(this, "Continuous grabbing started on all cameras");
                return allStarted;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool SetExposureTimeForCamera(int cameraIndex, double exposureTimeMicroseconds)
        {
            try
            {
                DualCameraController cam = GetCamera(cameraIndex);
                if (cam == null)
                    return false;

                return cam.SetExposureTime(exposureTimeMicroseconds);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool StopGrabbingAll()
        {
            try
            {
                bool allStopped = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.StopGrabbing())
                        allStopped = false;
                }
                StatusChanged?.Invoke(this, "Grabbing stopped on all cameras");
                return allStopped;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool StartSoftwareTriggerAll()
        {
            try
            {
                bool allStarted = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.StartSoftwareTriggerMode())
                        allStarted = false;
                }
                StatusChanged?.Invoke(this, "Software trigger mode activated on all cameras");
                return allStarted;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool TriggerAllCameras()
        {
            try
            {
                bool allTriggered = true;
                foreach (DualCameraController cam in cameras)
                {
                    if (!cam.ExecuteTrigger())
                        allTriggered = false;
                }
                return allTriggered;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public bool LoadImageToVisionMaster(int cameraIndex, string outputImagePath = null)
        {
            try
            {
                if (cameraIndex < 0 || cameraIndex >= cameras.Count)
                    return false;

                DualCameraController cam = cameras[cameraIndex];
                Bitmap frame = cam.GetLatestFrame();

                if (frame == null)
                {
                    StatusChanged?.Invoke(this, $"No frame available from Camera {cameraIndex + 1}");
                    return false;
                }

                // Save image if path specified
                if (!string.IsNullOrEmpty(outputImagePath))
                {
                    frame.Save(outputImagePath);
                    StatusChanged?.Invoke(this, $"Image saved to {outputImagePath}");
                }

                // Load to VisionMaster if available
                // This depends on your VisionMaster API
                // You may need to adjust based on your VisionMaster version
                StatusChanged?.Invoke(this, $"Image from Camera {cameraIndex + 1} ready for VisionMaster processing");

                frame.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
                return false;
            }
        }

        public void DisposeCameras()
        {
            try
            {
                foreach (DualCameraController cam in cameras)
                {
                    cam.Dispose();
                }
                cameras.Clear();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
        }

        public void Dispose()
        {
            try
            {
                // Pylon terminates automatically when last Camera is disposed
                DisposeCameras();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ExceptionEventArgs { Exception = ex });
            }
        }
    }
}
