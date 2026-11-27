# ESDCloudViewer — xem cloud của các line ESD

App WPF xem dữ liệu mà ESDCMS (Cloud, `Device/CloudUploader.cs`) ghi lên Firebase Realtime
Database, chạy trên máy bất kỳ có internet (văn phòng, ở nhà), không cần PC ở xưởng. Xem **nhiều line cùng lúc**,
mỗi line một bản đồ băng tải như trang Monitor của ESDCMS. Có thêm **bản web** (`web/esd-cloud-viewer.html`).

- Target: .NET Framework 4.8, WPF, x64. Build: `dotnet build ESDCloudViewer.slnx` → `ESDCloudViewer.exe`
  (hoặc mở `ESDCMS/ESDCMS.slnx`, solution có cả hai project).
- Giao diện theo template của ESDCMS: thanh tiêu đề `#323F4E` có nút ngôn ngữ (English → 한국어 → ไทย), chú thích màu
  giống trang Monitor, Console Log nền xanh (luôn tiếng Anh, thu gọn được), thanh dưới có group **Firebase**. Font
  Google Sans / JetBrains Mono và icon lấy thẳng từ `../ESDCMS` (link trong `.csproj`, không chép).
- Gói NuGet: FontAwesome.Sharp 6.6.0, Newtonsoft.Json 13.0.3. Không dùng Firebase SDK: gọi REST như ESDCMS.

## Dùng

1. **Settings**: nhập Database URL, Web API key, email, mật khẩu (như cài đặt Cloud của ESDCMS) và các line cần xem
   (Net ID 2 chữ số hex, cách nhau bằng dấu phẩy: `03, 02, 01`). Tick *Remember on this PC* thì lưu vào
   `%USERPROFILE%\.esdcms\cloudviewer.json`; mật khẩu Firebase mã hóa bằng DPAPI theo user Windows (user hoặc PC khác
   không giải mã được, phải nhập lại).
2. **Check settings**: định dạng, đăng nhập, đọc được từng line, ghi thử vào `lines/<line đầu>/pcs/viewer-check` rồi
   xóa (như ESDCMS ghi). Kết quả `[ OK ]` / `[WARN]` / `[FAIL]` ở Console Log. Đăng nhập được thì tự chuyển sang xem.
3. **Watch**: đăng nhập rồi xem trực tiếp. Mở app mà đã lưu đủ cài đặt thì tự xem luôn; chưa đủ thì hộp Settings tự
   hiện, bấm OK là xem. Trạng thái hiện tại của mọi hộp hiện ngay (sự kiện đầu tiên của stream là toàn bộ dữ liệu).
   - Mỗi line: từng PC (ESDCMS) ghi line đó: ghi lần cuối (xanh nếu trong 150 s), RF Gateway / Control Box có kết nối
     không, băng tải chạy hay dừng; rê chuột xem số hộp đang dùng, chạy từ lúc nào, phiên bản.
   - Bản đồ băng tải 20 ô: hộp nằm đúng ô `pos`, màu như Monitor của ESDCMS — Inactive, Warning, Active, Offline
     (cả hộp chưa nghe lần nào, `U`), Not in use (xám, `N`). Line có dữ liệu mà thiếu hộp (ESDCMS bản cũ chỉ gửi hộp
     đang dùng) thì ô đó xám; line chưa có dữ liệu thì ô trắng.
   - 30 lần đổi trạng thái gần nhất, gộp mọi line.
   - Console Log ghi mỗi lần box đổi trạng thái và mỗi lần một PC ghi lên cloud.

## Bản web

`web/esd-cloud-viewer.html`: một file, mở bằng trình duyệt (double-click, hoặc GitHub Pages / web server). Cùng chức
năng với app (trừ *Check settings*), có en/ko/th; trên điện thoại băng tải chia 2 hàng 10 ô. File không chứa tài khoản
hay địa chỉ database: người dùng **gõ tay** trong Settings, hoặc **nạp `key.json`** (nút *Load key.json* trong Settings,
hoặc kéo-thả file vào trang). `key.json` có dạng `{"databaseUrl", "apiKey", "email", "password"}` (như
`Firebase/key.json`), thêm `"lines": "03, 02"` nếu chỉ muốn xem vài line. Ô *Lines* để trống = mọi line có trong
database (dò lại mỗi 60 s).

- Database URL, key, email, line lưu trong trình duyệt (localStorage); **mật khẩu chỉ giữ trong tab** (sessionStorage:
  F5 vẫn xem tiếp, đóng tab là mất). Mở trang là tự xem; thiếu gì thì hộp Settings tự hiện.
- Chrome / Edge nhớ file `key.json` đã chọn bằng nút: lần sau mở trang chỉ cần bấm nút một lần (chọn *Allow on every
  visit* thì tự đọc, không cần bấm). Trang chạy trên web server thì cũng tự đọc `key.json` nằm cạnh nó.
- `web/.gitignore` chặn `key.json` trong thư mục này: không bao giờ commit file có mật khẩu (repo đang public).
- **GitHub Pages**: workflow `.github/workflows/cloud-viewer-pages.yml` đăng riêng file HTML này (thành `index.html`)
  mỗi khi nó đổi trên `main`. Làm một lần: repo Settings → Pages → Source: *GitHub Actions*. Trang:
  `https://<owner>.github.io/<repo>/`.

Firebase Realtime Database chỉ dùng HTTP/1.1 và trình duyệt mở tối đa 6 kết nối tới một host, nên bản web chỉ stream
`boxes` của 4 line đầu; line thứ 5 trở đi đọc lại mỗi 5 s. `pcs` và `events` đọc mỗi 10 s (`pcs` đọc ngay khi
`boxes` đổi, vì ESDCMS ghi cả hai cùng lúc). Đừng mở trang ở nhiều tab cùng lúc.

## Cách chạy bên trong

- `Firebase/FirebaseRest.cs`: đăng nhập (`identitytoolkit … signInWithPassword`), gọi REST, và đọc **stream** của
  database (`Accept: text/event-stream`, sự kiện `put` / `patch` / `keep-alive` / `auth_revoked` / `cancel`).
- `Firebase/LineWatcher.cs`: một watcher cho mỗi line — một stream cho `lines/<line>/pcs`, một cho `.../boxes`, hỏi
  `events` (30 cái cuối) mỗi 10 s. Tự nối lại khi mất mạng; 90 s không nhận gì (kể cả keep-alive) thì coi kết nối đã
  chết và mở lại; token sống 60 phút nên cứ 50 phút đăng nhập lại. `App.xaml.cs` nâng giới hạn kết nối của .NET
  Framework (mặc định chỉ 2 kết nối tới một host, không đủ cho 2 stream mỗi line).
- `Firebase/ConfigCheck.cs`: các bước kiểm tra.
- `MainWindow.xaml(.cs)`: dựng lại phần xem mỗi giây từ bản chụp của các watcher; `SettingsDialog.cs`: hộp thoại tạo
  bằng code như các hộp thoại của ESDCMS; `ViewerSettings.cs`: lưu cài đặt + DPAPI; `Lang/`: chuỗi en/ko/th (cùng cơ
  chế ESDCMS).
- Lỗi không lường trước ghi vào `%USERPROFILE%\.esdcms\cloudviewer-crash.log`, app vẫn chạy tiếp.
