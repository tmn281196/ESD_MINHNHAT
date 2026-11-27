# ESDCMS — ESD Compliance Monitoring System (mã nguồn khôi phục)

Mã nguồn này được dịch ngược từ `Debug/ESDKitSystem.exe` (kèm `ESDKitSystem.pdb`) ngày 2026-09-15
bằng ILSpy 11 (`ilspycmd` + `ICSharpCode.BamlDecompiler`), sau đó dọn lại để build được.
Ngày 2026-11-26 đổi tên project từ `ESDKitSystem` thành **`ESDCMS`** (namespace, exe, `.csproj`, `.slnx`, thư mục);
tên hiển thị là **ESD Compliance Monitoring System**.

- Target: .NET Framework 4.8, WPF, x64. Build bằng `dotnet build ESDCMS.csproj` hoặc mở `ESDCMS.slnx` bằng Visual Studio 2022 → ra `ESDCMS.exe`.
- Dùng **Fody + PropertyChanged.Fody** (xem `FodyWeavers.xml`): các property trong `ViewModel`, `ESDBox`
  là auto-property, Fody tự dệt `OnPropertyChanged` khi build. Không tự viết thêm `OnPropertyChanged` vào setter.
- Gói NuGet: FontAwesome.Sharp 6.6.0, Newtonsoft.Json 13.0.3, Fody 6.9.2, PropertyChanged.Fody 4.1.0.
- Font giống docs: **Google Sans** (chữ giao diện) + **JetBrains Mono** (console log), cả hai SIL OFL 1.1, nhúng làm
  `Resource` từ `Fonts/`, khai báo `AppFont` / `MonoFont` trong `App.xaml` (license `Fonts/LICENSE-*` được chép ra cạnh exe).
  Các `.ttf` tạo từ bản woff2 *latin* trong `Docs/SoftwareArchitecture/assets/fonts/` vì WPF (.NET 4.8) không đọc `.woff2`
  và không dùng được variable font: Google Sans tách 3 bản tĩnh 400/600/700 bằng `fontTools.varLib.instancer` rồi **bỏ bảng
  `STAT`** (còn STAT thì WPF đọc SemiBold thành weight 1 và vẽ bằng bản Bold); JetBrains Mono chỉ đổi woff2 → ttf. Chữ Hàn/Thái
  không có trong font tự fallback sang font hệ thống. `Frame` (trang Monitor) và cửa sổ tạo bằng code (`UnlockDialog`) không
  kế thừa font của Window nên được gắn `AppFont` riêng.
- WiFi cho tùy chọn cloud của gateway (`lora_gateway.ino`, `ENABLE_CLOUD 1`): nút ☁ ở group RF Gateway mở `WifiDialog.cs`;
  SSID/mật khẩu gửi xuống gateway qua cổng COM bằng khung `0xA5` (`Device/GatewayCloudLink.cs`), mật khẩu trong
  `config.json` mã hóa DPAPI theo user Windows (`Config.WifiPasswordProtected`, cần tham chiếu `System.Security`).
  Biểu tượng ☁ xanh khi gateway báo đã vào WiFi (ViewModel hỏi mỗi 5 s, chỉ khi đã tick).
- Tên biến cục bộ lấy từ PDB nên khá sát bản gốc; tên tham số lambda và một số cấu trúc `async` có thể khác nguyên bản.
- `Monitoring/MornitorPage.xaml` giữ nguyên tên file gốc (resource `monitoring/mornitorpage.baml`).
- `config.json` (COM port, vị trí thiết bị) và `crash.log` nằm ở `%USERPROFILE%\.esdcms\`. Lần chạy đầu, nếu chưa có
  file ở đó mà còn `config.json` cũ cạnh exe thì tự chép sang (file cũ để nguyên). Xem `Device/ConfigStore.cs`.
- Chạy cùng Windows: khóa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ESDCMS`; khóa cũ `ESDKitSystem` tự bị xóa.
