"""The site map, page titles and cross-links for the ESD Compliance Monitoring
System docs, in the dark-noir-docs style: a home page, four branch hubs (Vietnamese
names), pages with English titles and a Vietnamese summary. assets/site.js is
generated from this file by tools/build.py."""

SITE = "ESD Compliance Monitoring System"
UPDATED = "2026-11-26"

# path -> (English title, Vietnamese summary)
PAGES = {
    "index.html": (SITE, "Giám sát tuân thủ ESD cho một line sản xuất: các hộp ESD báo trạng thái về PC qua LoRa, PC dừng băng tải qua một Control Box khi có cảnh báo tĩnh điện."),

    "he_thong/he_thong.html": ("Hệ thống", "ESD, các thiết bị trong hệ và vai trò của chúng, mô hình trạng thái của một hộp. Đọc trước phần Kiến trúc."),
    "he_thong/00_tong_quan.html": ("System Overview", "Toàn chuỗi: hộp ESD → LoRa → RF Gateway → ứng dụng PC → Control Box → contactor băng tải; bài toán cần giải và cách hệ giải nó."),
    "he_thong/01_thiet_bi.html": ("Devices & Roles", "Bốn khối phần cứng: hộp ESD, RF Gateway, Control Box, PC; ai nói với ai, trên đường truyền nào, với tốc độ nào."),
    "he_thong/02_trang_thai.html": ("Box Status Model", "Sáu trạng thái Active / Warning / Inactive / Unknown / Error / Offline: màu, ý nghĩa, và cách hộp suy ra A/W/I từ hai chân IN1/IN2."),

    "giao_thuc/giao_thuc.html": ("Giao thức", "Hai đường serial, hai khung tin khác nhau: khung trạng thái LoRa (hộp ↔ PC) và khung lệnh Control Box (PC ↔ Arduino)."),
    "giao_thuc/01_lora.html": ("LoRa Status Protocol", "Khung AA..BB qua RF Gateway: byte, CRC8, Network ID, khung trạng thái hộp gửi lên và khung query ‘?’ PC gửi xuống."),
    "giao_thuc/02_control_box.html": ("Control Box Protocol", "Khung 44 45 .. 56 tới Arduino: opcode kênh, STATE, checksum XOR, echo làm ACK; bản đồ chân và logic relay NC."),

    "kien_truc/kien_truc.html": ("Kiến trúc", "Ứng dụng WPF được dựng thế nào: các lớp, hai cổng COM, các luồng, và luồng dữ liệu đi đâu làm gì."),
    "kien_truc/01_boi_canh.html": ("System Context", "ESDCMS.exe: một tiến trình WPF .NET 4.8 giữ hai link serial (RF Gateway, Control Box) và tất cả logic; single-instance theo thư mục, chống treo cổng COM."),
    "kien_truc/02_phan_tang.html": ("Layers & File Map", "Bản đồ file: Device/, Monitoring/, cửa sổ chính; mỗi file làm gì, và quy ước Fody/PropertyChanged."),
    "kien_truc/03_ket_noi.html": ("Connections & COM", "Mở/đóng cổng an toàn (SerialPortSafe), tự nhận cắm/rút USB, tự nối lại, thử lại lúc khởi động cùng Windows."),
    "kien_truc/04_poll.html": ("Status Polling", "Vòng hỏi trạng thái: query mỗi hộp lúc mở cổng, phát hiện Offline, hỏi lại định kỳ, và click vào ô hộp để hỏi ngay."),
    "kien_truc/05_conveyor.html": ("Conveyor Control", "Luật dừng băng tải: bất kỳ hộp đang dùng ở Warning → dừng; timer 200 ms, retry/gap, xung SWON lúc kết nối, chế độ Lock/manual."),
    "kien_truc/06_cau_hinh.html": ("Config & Settings", "%USERPROFILE%\\.esdcms\\config.json: tất cả khóa và giá trị mặc định, chuyển từ bản cũ, lưu an toàn, layout màn hình, auto-start cùng Windows."),
    "kien_truc/07_giao_dien.html": ("User Interface", "Một cửa sổ: nút ngôn ngữ English / 한국어 / ไทย, bản đồ băng tải, cảnh báo hộp Offline, console log, và thanh dưới một hàng chia group theo thiết bị."),
    "kien_truc/08_gioi_han.html": ("Limits & Open Work", "Hệ khác hệ hoàn chỉnh ở đâu: query cần nạp firmware v2 + NODE_ID riêng cho mọi hộp, L2 là bản sao L1, vài điểm cần dọn."),

    "so_do/so_do.html": ("Sơ đồ", "Sơ đồ lớp và sơ đồ tuần tự, vẽ bằng SVG ngay trong trang, chữ trong hình tiếng Anh, xem được khi không có mạng."),
    "so_do/00_cach_doc.html": ("Reading the Drawings", "Cách đọc sơ đồ: ký hiệu quan hệ lớp và mũi tên tuần tự dùng trong các trang sau."),
    "so_do/01_lop.html": ("Class Overview", "Các lớp chính và quan hệ: MainWindow, ViewModel, ControlBox, RFGateway, RootCanvas/BlockDevice, ConfigStore."),
    "so_do/02_canh_bao.html": ("Warning → Stop", "Một hộp chuyển Warning: từ khung LoRa, qua parser, timer băng tải, tới khung lệnh Control Box và relay — đúng theo mã nguồn hiện tại."),
    "so_do/03_khoi_dong.html": ("Startup & Connect", "Thứ tự lúc khởi động: mở hai cổng COM, xung SWON của Control Box, và vòng query trạng thái lần đầu."),
}

# branch hub -> children, in reading order
TREE = [
    ("he_thong/he_thong.html", [
        "he_thong/00_tong_quan.html", "he_thong/01_thiet_bi.html", "he_thong/02_trang_thai.html"]),
    ("giao_thuc/giao_thuc.html", [
        "giao_thuc/01_lora.html", "giao_thuc/02_control_box.html"]),
    ("kien_truc/kien_truc.html", [
        "kien_truc/01_boi_canh.html", "kien_truc/02_phan_tang.html", "kien_truc/03_ket_noi.html",
        "kien_truc/04_poll.html", "kien_truc/05_conveyor.html", "kien_truc/06_cau_hinh.html",
        "kien_truc/07_giao_dien.html", "kien_truc/08_gioi_han.html"]),
    ("so_do/so_do.html", [
        "so_do/00_cach_doc.html", "so_do/01_lop.html", "so_do/02_canh_bao.html", "so_do/03_khoi_dong.html"]),
]

# page -> pages it is related to (shown as "Liên quan" in the kv block)
RELATED = {
    "he_thong/00_tong_quan.html": ["he_thong/01_thiet_bi.html", "kien_truc/01_boi_canh.html", "so_do/02_canh_bao.html"],
    "he_thong/01_thiet_bi.html": ["giao_thuc/01_lora.html", "giao_thuc/02_control_box.html"],
    "he_thong/02_trang_thai.html": ["giao_thuc/01_lora.html", "kien_truc/05_conveyor.html"],
    "giao_thuc/01_lora.html": ["he_thong/02_trang_thai.html", "kien_truc/04_poll.html", "kien_truc/08_gioi_han.html"],
    "giao_thuc/02_control_box.html": ["kien_truc/05_conveyor.html", "so_do/02_canh_bao.html"],
    "kien_truc/01_boi_canh.html": ["he_thong/00_tong_quan.html", "kien_truc/02_phan_tang.html", "so_do/01_lop.html"],
    "kien_truc/02_phan_tang.html": ["so_do/01_lop.html"],
    "kien_truc/03_ket_noi.html": ["kien_truc/07_giao_dien.html", "so_do/03_khoi_dong.html"],
    "kien_truc/04_poll.html": ["giao_thuc/01_lora.html", "he_thong/02_trang_thai.html", "kien_truc/08_gioi_han.html"],
    "kien_truc/05_conveyor.html": ["giao_thuc/02_control_box.html", "so_do/02_canh_bao.html", "he_thong/02_trang_thai.html"],
    "kien_truc/06_cau_hinh.html": ["kien_truc/07_giao_dien.html", "kien_truc/04_poll.html", "kien_truc/05_conveyor.html"],
    "kien_truc/07_giao_dien.html": ["kien_truc/06_cau_hinh.html", "kien_truc/03_ket_noi.html"],
    "kien_truc/08_gioi_han.html": ["giao_thuc/01_lora.html", "kien_truc/04_poll.html"],
    "so_do/01_lop.html": ["kien_truc/02_phan_tang.html"],
    "so_do/02_canh_bao.html": ["kien_truc/05_conveyor.html", "giao_thuc/02_control_box.html"],
    "so_do/03_khoi_dong.html": ["kien_truc/03_ket_noi.html", "kien_truc/05_conveyor.html"],
}


def parent_of(path):
    for hub, children in TREE:
        if path in children:
            return hub
    return "index.html" if path != "index.html" else None


def children_of(path):
    if path == "index.html":
        return [hub for hub, _ in TREE]
    for hub, children in TREE:
        if hub == path:
            return children
    return []
