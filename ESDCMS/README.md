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
- Tùy chọn cloud của gateway (`lora_gateway.ino`, `ENABLE_CLOUD 1`): WiFi và tài khoản Firebase nằm trong `secrets.h` của
  firmware, `ENABLE_CLOUD` là công tắc duy nhất; app chỉ **đọc** trạng thái. `Device/GatewayCloudLink.cs` hỏi `0xA5 'S'` (trạng thái) và `'I'` (MAC, router đang thử)
  qua cổng COM; gateway tự gửi khung `'s'` khi trạng thái đổi (bước đang làm, mã lỗi) và khung `'r'` cho mỗi lần gửi lên
  cloud. `Device/GatewayCloudText.cs` dựng tooltip của ☁ (xanh OK, vàng đang kết nối, xám lỗi) và dòng console (tiếng
  Anh, qua `Lang.En`); bấm ☁ (cả khi đang khóa) mở `CloudDialog.cs`, chỉ xem.
- Tên biến cục bộ lấy từ PDB nên khá sát bản gốc; tên tham số lambda và một số cấu trúc `async` có thể khác nguyên bản.
- `Monitoring/MornitorPage.xaml` giữ nguyên tên file gốc (resource `monitoring/mornitorpage.baml`).
- `config.json` (COM port, vị trí thiết bị) và `crash.log` nằm ở `%USERPROFILE%\.esdcms\`. Lần chạy đầu, nếu chưa có
  file ở đó mà còn `config.json` cũ cạnh exe thì tự chép sang (file cũ để nguyên). Xem `Device/ConfigStore.cs`.
- Chạy cùng Windows: khóa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ESDCMS`; khóa cũ `ESDKitSystem` tự bị xóa.
