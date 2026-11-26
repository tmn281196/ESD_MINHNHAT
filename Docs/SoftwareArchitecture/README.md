# ESD Compliance Monitoring System — tài liệu kiến trúc

Một site HTML tĩnh theo template **dark-noir-docs**: mở **`index.html`** bằng trình duyệt (double-click),
không cần server, không cần mạng. Mục lục dạng cây bên trái (CONTENTS), mục lục của trang bên phải,
nút Previous / Next cuối mỗi trang.

Tài liệu mô tả **hành vi hiện tại** của ứng dụng WPF `ESDCMS` — ESD Compliance Monitoring System (bản v2, dịch ngược,
trước tên là `ESDKitSystem`) cùng firmware Control Box và LoRA, bám sát mã nguồn trong `ESDCMS/` và `Firmware_*/`.

| Đường dẫn | Nội dung |
|------|----------|
| `index.html` | Trang chủ: bốn nhánh, số liệu, trạng thái |
| `he_thong/` | 3 trang: System Overview, Devices & Roles, Box Status Model |
| `giao_thuc/` | 2 trang: LoRa Status Protocol, Control Box Protocol |
| `kien_truc/` | 8 trang: System Context, Layers & File Map, Connections & COM, Status Polling, Conveyor Control, Config & Settings, User Interface, Limits & Open Work |
| `so_do/` | 4 trang: Reading the Drawings, Class Overview, Warning → Stop, Startup & Connect (SVG, không Mermaid) |
| `assets/` | `noir.css` + `noir.js` (theme), `site.css`, `arch.css` (màu SVG + khung byte), `diagram-view.js`, `site.js` (sinh ra), `fonts/` |
| `tools/` | bộ sinh site: `sections.py` (tiêu đề, cây, liên kết), `frame.py` (khung trang), `svg.py` (helper SVG), `p_*.py` (nội dung), `build.py` |

## Quy ước

- Tiêu đề trang (h1, mục lục) tiếng Anh; dòng tóm tắt và nội dung tiếng Việt; chữ trong hình tiếng Anh.
- Mỗi trang mở đầu bằng khối **Thuộc / Trang con / Liên quan** (`dl.kv`), sinh từ `sections.py`.
- Đầu trang có chip **Updated**; đổi ngày ở `UPDATED` trong `sections.py`.
- Sửa nội dung ở `tools/p_*.py` rồi chạy `python tools/build.py`; sửa thẳng file HTML thì lần dựng sau mất.
