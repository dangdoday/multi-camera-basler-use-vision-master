# Multi-Camera Basler + VisionMaster Integration

App để điều khiển 2 camera Basler cùng lúc với tính năng trigger và tích hợp VisionMaster.

## Tính năng

- ✅ Kết nối 2 camera Basler cùng một lúc
- ✅ Trigger phần mềm cho cả 2 camera (Software Trigger)
- ✅ Grab ảnh liên tục hoặc một lần (Single Shot)
- ✅ Hiển thị ảnh từ cả 2 camera lên giao diện
- ✅ Tích hợp VisionMaster để load ảnh
- ✅ Ghi log tất cả hoạt động
- ✅ Kiến trúc clean với Multi-camera Manager

## Cấu trúc Dự án

```
2camera-basler/
├── 2CameraBasler.csproj          # Project file
├── Core/
│   ├── DualCameraController.cs   # Controller quản lý 1 camera (có trigger)
│   └── MultiCameraManager.cs     # Manager quản lý 2 camera
├── UI/
│   ├── DualCameraApp.cs          # Form chính với UI
│   ├── DualCameraApp.Designer.cs # Designer
│   └── DualCameraApp.resx        # Resources
├── bin/
│   └── Debug/
│       └── MultiCameraBaslerApp.exe  # Executable
└── README.md                     # Documentation
```

## Yêu cầu

- .NET Framework 4.8
- Basler Pylon SDK (phiên bản tương thích)
- VisionMaster 4.4.0 (hoặc tương thích)
- Windows x86/x64

## Build & Chạy

### Build với MSBuild

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "d:\multi-camera-basler-use-vision-master\2camera-basler\2CameraBasler.csproj" `
  /t:Rebuild `
  /p:Configuration=Debug `
  /p:Platform=x64 `
  /p:PlatformTarget=x86
```

### Chạy App

```powershell
Start-Process "d:\multi-camera-basler-use-vision-master\2camera-basler\bin\Debug\MultiCameraBaslerApp.exe"
```

## Lưu ý Phát triển

### 1. **Basler Pylon API Issues** - Cần Sửa

Hiện tại các phương thức sau cần điều chỉnh theo SDK Basler của bạn:

```csharp
// ❌ Cần sửa trong MultiCameraManager.cs:
Pylon.Initialize();      // Tìm method khởi tạo Pylon đúng
Pylon.Terminate();       // Tìm method kết thúc Pylon đúng

// ❌ Cần sửa trong MultiCameraManager.cs (line 45):
ICameraFactory factory = new CameraFactory();  // API khác nhau theo version
List<ICameraInfo> cameras = factory.EnumerateCameras();

// ✅ Thay thế bằng:
// var cameras = CameraFinder.FindDevices();  // Hoặc method tương tự
```

### 2. **Camera Parameter API** - Cần Sửa

```csharp
// ❌ Hiện tại trong DualCameraController.cs (line 277):
camera.Parameters[PLCamera.TriggerSource].TrySetValue(PLCamera.TriggerSource.Software);

// ✅ Phù hợp với API Basler:
var param = camera.Parameters[PLCamera.TriggerSource];
if (param is IEnumParameter enumParam)
{
    enumParam.SetValue(PLCamera.TriggerSource.Software);
}
```

### 3. **PixelDataConverter.Convert()** - Cần Sửa

```csharp
// ❌ Hiện tại (line 369 trong DualCameraController.cs):
converter.Convert(bitmapData, e.GrabResult);

// ✅ Cách đúng (tùy theo API):
converter.OutputPixelFormat = PixelType.BGR8packed;
converter.Convert(bitmapData, e.GrabResult);
// hoặc
converter.Convert<Bgr8Pixel>(pixelArray, e.GrabResult);
```

## Hướng dẫn Sửa Chi Tiết

1. **Mở file** `Core/MultiCameraManager.cs` và `Core/DualCameraController.cs`
2. **Kiểm tra** các method trong Basler Pylon SDK:
   - Xem tài liệu Basler SDK để tìm đúng method
   - Sử dụng IntelliSense trong Visual Studio
3. **Sửa** các lỗi biên dịch theo hướng dẫn ở trên
4. **Test** từng tính năng:
   - Discover Cameras
   - Open/Close
   - Continuous Grab
   - Software Trigger

## Chức năng UI

### Các nút điều khiển:

- **Discover Cameras**: Quét và phát hiện camera kết nối
- **Open All Cameras**: Mở cả 2 camera
- **Close All Cameras**: Đóng cả 2 camera
- **Start Continuous Grab**: Bắt đầu grab ảnh liên tục
- **Software Trigger (All)**: Kích hoạt trigger phần mềm
- **Execute Trigger (Fire!)**: Thực thi trigger sau khi kích hoạt
- **Stop Grabbing**: Dừng grab ảnh

### Giao diện:

```
┌─────────────────────────────────────────┬──────────────────┐
│                                         │                  │
│  Camera 1 Preview  │  Camera 2 Preview  │   Controls       │
│                    │                    │   (Buttons)      │
│                    │                    │                  │
│                    │                    │   Status Log     │
│                    │                    │   (Đen để        │
│ Camera Status Info │ Camera Status Info │    xem logs)     │
└─────────────────────────────────────────┴──────────────────┘
```

## Kiểm tra Lỗi Compile

Sau khi sửa các lỗi API, build lại:

```powershell
# Xóa output cũ
Remove-Item -Recurse "d:\multi-camera-basler-use-vision-master\2camera-basler\bin", `
                     "d:\multi-camera-basler-use-vision-master\2camera-basler\obj" `
  -ErrorAction SilentlyContinue

# Build lại
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "d:\multi-camera-basler-use-vision-master\2camera-basler\2CameraBasler.csproj" `
  /t:Rebuild `
  /p:Configuration=Debug `
  /p:Platform=x64 `
  /p:PlatformTarget=x86
```

## VisionMaster Integration

Phương thức `LoadImageToVisionMaster()` hiện ghi lại trạng thái. Để tích hợp thực:

```csharp
// Trong MultiCameraManager.cs - LoadImageToVisionMaster():
// Thêm code để load ảnh vào VisionMaster UI:
// 
// VisionMasterAPI.LoadImage(frame);
// - hoặc -
// vmMasterControl.SetImage(frame);
```

## Troubleshooting

| Lỗi | Nguyên nhân | Cách sửa |
|-----|-----------|----------|
| `BadImageFormatException` | Kiến trúc DLL không khớp | Build với `/p:PlatformTarget=x86` |
| Camera không kết nối | Serial number sai hoặc driver  không có | Chạy lại "Discover Cameras" |
| Trigger không hoạt động | API gọi sai | Kiểm tra lại API Basler |
| VisionMaster không nhận ảnh | API tích hợp chưa implement | Code `LoadImageToVisionMaster()` đầy đủ |

## Liên hệ

Nếu cần trợ giúp thêm, hãy:
1. Kiểm tra lỗi compile chi tiết
2. Tìm đúng API Basler SDK version của bạn
3. Sửa theo hướng dẫn trên
4. Test từng tính năng riêng

---

**Ghi chú**: Đây là bản v1 cấu trúc cơ bản. Sau khi sửa các lỗi API, app sẽ hoạt động đầy đủ.
