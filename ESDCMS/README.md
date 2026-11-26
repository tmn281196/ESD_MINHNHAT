# ESDCMS — ESD Compliance Monitoring System (mã nguồn khôi phục)

Mã nguồn này được dịch ngược từ `Debug/ESDKitSystem.exe` (kèm `ESDKitSystem.pdb`) ngày 2026-09-15
bằng ILSpy 11 (`ilspycmd` + `ICSharpCode.BamlDecompiler`), sau đó dọn lại để build được.
Ngày 2026-11-26 đổi tên project từ `ESDKitSystem` thành **`ESDCMS`** (namespace, exe, `.csproj`, `.slnx`, thư mục);
tên hiển thị là **ESD Compliance Monitoring System**.

- Target: .NET Framework 4.8, WPF, x64. Build bằng `dotnet build ESDCMS.csproj` hoặc mở `ESDCMS.slnx` bằng Visual Studio 2022 → ra `ESDCMS.exe`.
- Dùng **Fody + PropertyChanged.Fody** (xem `FodyWeavers.xml`): các property trong `ViewModel`, `ESDBox`
  là auto-property, Fody tự dệt `OnPropertyChanged` khi build. Không tự viết thêm `OnPropertyChanged` vào setter.
- Gói NuGet: FontAwesome.Sharp 6.6.0, Newtonsoft.Json 13.0.3, Fody 6.9.2, PropertyChanged.Fody 4.1.0.
- Tên biến cục bộ lấy từ PDB nên khá sát bản gốc; tên tham số lambda và một số cấu trúc `async` có thể khác nguyên bản.
- `Monitoring/MornitorPage.xaml` giữ nguyên tên file gốc (resource `monitoring/mornitorpage.baml`).
- `config.json` (COM port, vị trí thiết bị) và `crash.log` nằm ở `%USERPROFILE%\.esdcms\`. Lần chạy đầu, nếu chưa có
  file ở đó mà còn `config.json` cũ cạnh exe thì tự chép sang (file cũ để nguyên). Xem `Device/ConfigStore.cs`.
- Chạy cùng Windows: khóa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ESDCMS`; khóa cũ `ESDKitSystem` tự bị xóa.
