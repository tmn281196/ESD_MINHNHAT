# ESDCloudViewer — xem cloud của line ESD

App WPF xem dữ liệu mà ESDCMS (Cloud, `Device/CloudUploader.cs`) ghi lên Firebase Realtime
Database, chạy trên máy bất kỳ có internet (văn phòng, ở nhà), không cần PC ở xưởng. Giao diện giống ESDCMS.

- Target: .NET Framework 4.8, WPF, x64. Build: `dotnet build ESDCloudViewer.csproj` → `ESDCloudViewer.exe`
  (hoặc mở `ESDCMS/ESDCMS.slnx`, solution có cả hai project).
- Giao diện theo template của ESDCMS: thanh tiêu đề `#323F4E` có nút ngôn ngữ (English → 한국어 → ไทย), chú thích màu
  giống trang Monitor, Console Log nền xanh (luôn tiếng Anh), thanh dưới có group **Firebase**. Font Google Sans /
  JetBrains Mono và icon lấy thẳng từ `../ESDCMS` (link trong `.csproj`, không chép).
- Gói NuGet: FontAwesome.Sharp 6.6.0, Newtonsoft.Json 13.0.3. Không dùng Firebase SDK: gọi REST như ESDCMS.

## Dùng

1. **Settings**: nhập Database URL, Web API key, email, mật khẩu (như cài đặt Cloud của ESDCMS) và line (Net ID, 2 số).
   Tick *Remember on this PC* thì lưu vào `%USERPROFILE%\.esdcms\cloudviewer.json`; mật khẩu Firebase mã hóa bằng DPAPI
   theo user Windows (user hoặc PC khác không giải mã được, phải nhập lại).
2. **Check settings**: kiểm tra y như ESDCMS dùng — định dạng, đăng nhập, `gateways/<UID>` (phải là chữ `03`,
   không có dấu ngoặc kép), ghi thử vào `lines/03/pcs/viewer-check` rồi xóa, và rules phải chặn line khác. Kết quả `[ OK ]` / `[WARN]` /
   `[FAIL]` ở Console Log. Đăng nhập được thì tự chuyển sang xem trực tiếp.
3. **Watch**: đăng nhập rồi xem trực tiếp. Mở app mà đã lưu cài đặt thì tự xem luôn.
   - Từng PC (ESDCMS) ghi line: ghi lần cuối (xanh nếu trong 150 s), RF Gateway / Control Box có kết nối không,
     băng tải chạy hay dừng, số hộp đang dùng, chạy từ lúc nào, phiên bản.
   - Box: màu như Monitor của ESDCMS (Active / Warning / Inactive / Offline khi PC báo `O`).
   - 30 lần đổi trạng thái gần nhất.
   - Console Log ghi mỗi lần box đổi trạng thái và mỗi lần một PC ghi lên cloud.

## Cách chạy bên trong

- `Firebase/FirebaseRest.cs`: đăng nhập (`identitytoolkit … signInWithPassword`), gọi REST, và đọc **stream** của
  database (`Accept: text/event-stream`, sự kiện `put` / `patch` / `keep-alive` / `auth_revoked` / `cancel`).
- `Firebase/LineWatcher.cs`: một stream cho `lines/<line>/pcs`, một cho `.../boxes`, hỏi `events` (30 cái cuối)
  mỗi 10 s. Tự nối lại khi mất mạng; 90 s không nhận gì (kể cả keep-alive) thì coi kết nối đã chết và mở lại; token
  sống 60 phút nên cứ 50 phút đăng nhập lại.
- `Firebase/ConfigCheck.cs`: các bước kiểm tra.
- `MainWindow.xaml(.cs)`: dựng lại phần xem mỗi giây từ bản chụp của watcher; `SettingsDialog.cs`: hộp thoại tạo bằng code
  như các hộp thoại của ESDCMS; `ViewerSettings.cs`: lưu cài đặt + DPAPI; `Lang/`: chuỗi en/ko/th (cùng cơ chế ESDCMS).
- Lỗi không lường trước ghi vào `%USERPROFILE%\.esdcms\cloudviewer-crash.log`, app vẫn chạy tiếp.
