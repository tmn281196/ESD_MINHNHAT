# -*- coding: utf-8 -*-
"""Branch 'Sơ đồ' (Diagrams): class overview + sequence diagrams as inline SVG (offline, no Mermaid).
Prose Vietnamese; text inside the drawings is English. Helpers adapted from the dark-noir template."""
from xml.sax.saxutils import escape as esc
from frame import write, section_index

LINE_H = 16


def _defs(p):
    return f'''<defs>
  <marker id="{p}-dia" viewBox="0 0 16 10" refX="15" refY="5" markerWidth="14" markerHeight="9" orient="auto-start-reverse">
    <path d="M1 5 L8 1 L15 5 L8 9 z" fill="currentColor"/></marker>
  <marker id="{p}-ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0 0 L10 5 L0 10 z" fill="currentColor"/></marker>
  <marker id="{p}-open" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
    <path d="M0 0 L10 5 L0 10" fill="none" stroke="currentColor"/></marker>
</defs>'''


def _fig(p, w, h, aria, inner, caption):
    return (f'<figure class="diagram">\n<svg id="{p}" viewBox="0 0 {w} {h}" role="img" aria-label="{esc(aria)}">\n'
            f'{_defs(p)}\n<g font-family="Google Sans, sans-serif" font-size="12">\n{inner}</g>\n</svg>\n'
            f'<figcaption>{caption}</figcaption>\n</figure>\n')


def _halo(x, y, text, anchor="middle", size=11.5, fill="var(--ink)"):
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="JetBrains Mono, monospace" font-size="{size}" '
            f'fill="{fill}" paint-order="stroke" stroke="var(--bg)" stroke-width="5" stroke-linejoin="round">{esc(text)}</text>\n')


# ---------------------------------------------------------------- class boxes

def _class_box(x, y, w, name, members=(), stereo=None):
    h = 26 + (len(members) * LINE_H + 10 if members else 0) + (14 if stereo else 0)
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="var(--surface-2)" stroke="currentColor"/>\n'
    ty = y + 18
    if stereo:
        s += f'<text x="{x + w / 2}" y="{ty - 2}" text-anchor="middle" font-size="10" fill="var(--ink-3)">«{esc(stereo)}»</text>\n'
        ty += 13
    s += f'<text x="{x + w / 2}" y="{ty}" text-anchor="middle" font-weight="600" fill="currentColor">{esc(name)}</text>\n'
    if members:
        sep = ty + 8
        s += f'<line x1="{x}" y1="{sep}" x2="{x + w}" y2="{sep}" stroke="var(--rule-strong)"/>\n'
        for i, m in enumerate(members):
            s += (f'<text x="{x + 10}" y="{sep + 16 + i * LINE_H}" font-family="JetBrains Mono, monospace" font-size="10.5" '
                  f'fill="var(--ink-2)">{esc(m)}</text>\n')
    return s, h


def _own(p, d):
    return f'<path d="{d}" stroke="currentColor" stroke-width="1.3" fill="none" marker-start="url(#{p}-dia)"/>\n'


def _use(p, d):
    return f'<path d="{d}" stroke="var(--accent)" stroke-width="1.4" stroke-dasharray="5 4" fill="none" marker-end="url(#{p}-open)"/>\n'


def _class_svg():
    p = "svg-classes"
    g = ""
    box = {}

    def add(k, x, y, w, name, members=(), stereo=None):
        nonlocal g
        s, h = _class_box(x, y, w, name, members, stereo)
        g += s
        box[k] = (x, y, w, h)

    def bot(k):
        x, y, w, h = box[k]; return y + h

    add("mw", 370, 20, 250, "MainWindow", ["+GlobalViewModel", "+DeviceManagerPage · MonitorPage", "COM auto-refresh · Lock"])
    add("vm", 20, 170, 310, "ViewModel", ["+ESDBoxes : ESDBox[20]", "+Config", "FeedByte()  // LoRa parser", "conveyor timer 200 ms", "StatusPollLoopAsync()  // query"])
    add("dmp", 370, 170, 270, "DeviceManagerPage", ["+ControlBox · +RFGateway", "ApplyRFConnection()", "ApplyControlBoxConnection()", "OpenControlBoxCore()  // SWON"])
    add("mp", 700, 170, 250, "MonitorPage", ["+RootCanvas"])
    add("esd", 70, 400, 250, "ESDBox", ["ID · Status · ColorState", "StatusChangeAction"])
    add("cb", 370, 400, 130, "ControlBox", ["+SerialPort", "+Channels[5]", "GetFrame()", "SendTX()  // echo=ACK"])
    add("rf", 510, 400, 130, "RFGateway", ["+SerialPort", "OpenGeneration", "Send(frame)", "event RX"])
    add("rc", 700, 360, 250, "RootCanvas", ["+DynamicDevicesList", "   : BlockDevice[20]", "BoxClicked"])
    add("bd", 700, 560, 250, "BlockDevice", ["Status · IsUsed · Position", "click → query, drag → move"])

    # ownership (diamond at the owner)
    g += _own(p, f"M440 {bot('mw')} L440 150 L175 150 L175 170")
    g += _own(p, f"M500 {bot('mw')} L500 170")
    g += _own(p, f"M560 {bot('mw')} L560 150 L825 150 L825 170")
    g += _own(p, f"M175 {bot('vm')} L175 400")
    g += _own(p, f"M470 {bot('dmp')} L435 400")
    g += _own(p, f"M540 {bot('dmp')} L575 400")
    g += _own(p, f"M825 {bot('mp')} L825 360")
    g += _own(p, f"M825 {bot('rc')} L825 560")
    g += _halo(185, 392, "20", anchor="start", size=10)
    g += _halo(835, 552, "20", anchor="start", size=10)
    # the serial receive event is the one cross-cutting link: drawn along the bottom
    g += _use(p, f"M575 {bot('rf')} L575 770 L40 770 L40 {bot('vm')}")
    g += _halo(300, 784, "RFGateway RX event → ViewModel.FeedByte", fill="var(--accent)")
    return _fig(p, 980, 800,
                "Class overview: MainWindow owns the ViewModel, the DeviceManagerPage (ControlBox + RFGateway) and the MonitorPage (RootCanvas + BlockDevice); the RF gateway's serial event feeds the ViewModel parser",
                g, "<b>Cây sở hữu một chiều.</b> <code>MainWindow</code> giữ mọi thứ. Đường nét đứt trắng là liên kết chéo duy nhất: sự kiện nhận serial của <code>RFGateway</code> chạy vào parser <code>ViewModel.FeedByte</code>.")


# ---------------------------------------------------------------- sequence diagrams

def _sequence(p, parts, steps, caption, aria, col=170, row=32):
    xs = {k: 20 + col / 2 + i * col for i, (k, _, _) in enumerate(parts)}
    W = int(40 + len(parts) * col)
    top = 16
    g = ""
    for k, name, sub in parts:
        x = xs[k]
        g += f'<rect x="{x - col / 2 + 8}" y="{top}" width="{col - 16}" height="42" fill="var(--surface-2)" stroke="currentColor"/>\n'
        g += f'<text x="{x}" y="{top + 18}" text-anchor="middle" font-weight="600" font-size="12.5" fill="currentColor">{esc(name)}</text>\n'
        g += f'<text x="{x}" y="{top + 34}" text-anchor="middle" font-size="10.5" fill="var(--ink-3)">{esc(sub)}</text>\n'
    y = top + 42 + 34
    frames = []
    body = ""
    for st in steps:
        kind = st[0]
        if kind == "frame":
            y += 10
            frames.append((st[1], y - 22, st[2]))
            y += 12
            continue
        if kind == "self":
            _, a, text = st
            x = xs[a]
            d = -1 if x > W - col else 1
            body += (f'<path d="M{x} {y - 6} L{x + 26 * d} {y - 6} L{x + 26 * d} {y + 8} L{x + 2 * d} {y + 8}" '
                     f'stroke="currentColor" stroke-width="1.2" fill="none" marker-end="url(#{p}-ar)"/>\n')
            body += _halo(x + 32 * d, y + 2, text, anchor="start" if d > 0 else "end")
            y += row + 4
            continue
        _, a, b, text = st
        x1, x2 = xs[a], xs[b]
        dash = ' stroke-dasharray="5 4"' if kind == "reply" else ""
        mk = f"url(#{p}-open)" if kind == "reply" else f"url(#{p}-ar)"
        body += f'<path d="M{x1} {y} L{x2 - (6 if x2 > x1 else -6)} {y}" stroke="currentColor" stroke-width="1.2"{dash} fill="none" marker-end="{mk}"/>\n'
        body += _halo((x1 + x2) / 2, y - 6, text)
        y += row
    for text, fy, n in frames:
        fh = n * row + 30
        g += f'<rect x="30" y="{fy}" width="{W - 60}" height="{fh}" fill="none" stroke="var(--rule-strong)" stroke-dasharray="4 3"/>\n'
        g += f'<rect x="30" y="{fy}" width="{len(text) * 6.9 + 16}" height="18" fill="var(--surface-2)" stroke="var(--rule-strong)"/>\n'
        g += f'<text x="38" y="{fy + 13}" font-family="JetBrains Mono, monospace" font-size="10.5" fill="var(--ink-2)">{esc(text)}</text>\n'
    end = y + 4
    for k, _, _ in parts:
        g += f'<line x1="{xs[k]}" y1="{top + 42}" x2="{xs[k]}" y2="{end}" stroke="var(--rule-strong)" stroke-dasharray="3 4"/>\n'
    g += body
    return _fig(p, W, end + 16, aria, g, caption)


def _warning_svg():
    parts = [("box", "ESD Box", "IN1/IN2"), ("gw", "RF Gateway", "LoRa→USB"),
             ("vm", "ViewModel", "parser+timer"), ("cb", "ControlBox", "serial"), ("ard", "Arduino", "relays")]
    steps = [
        ("self", "box", "IN1=1, IN2=0 → status 'W'"),
        ("call", "box", "gw", "LoRa: AA 03 ID 01 57 95 BB"),
        ("call", "gw", "vm", "forward over USB (same frame)"),
        ("self", "vm", "FeedByte: CRC ok → box.Status = Warning"),
        ("self", "vm", "Enqueue: a used box is Warning → desired = STOP"),
        ("frame", "conveyor timer 200 ms: desired != last", 1),
        ("call", "vm", "cb", "AssertMotorChannels(STOP)"),
        ("frame", "each Motor 1..3: TX gap apart, retry up to 4x", 3),
        ("call", "cb", "ard", "44 45 03 01 01 02 56  (Motor 1 STOP)"),
        ("reply", "ard", "cb", "echo = ACK (byte-for-byte)"),
        ("self", "ard", "pin HIGH → relay ON → motor 1 stops"),
        ("self", "cb", "then Motor 2, 3, 4 the same way"),
    ]
    return _sequence("svg-warn", parts, steps,
                     "<b>Một cảnh báo đi hết chuỗi.</b> Hộp báo <code>'W'</code>, parser đổi trạng thái, timer 200 ms thấy khác lần trước và gửi lệnh STOP cho từng kênh; echo của Arduino là xác nhận.",
                     "Sequence: an ESD box reports Warning over LoRa, the gateway forwards it, the ViewModel parser updates the box and the 200 ms timer sends a STOP command per motor channel to the Arduino")


def _startup_svg():
    parts = [("mw", "MainWindow", "Loaded"), ("dmp", "DeviceManagerPage", "connect"),
             ("rf", "RFGateway", "COM"), ("cb", "ControlBox", "COM"), ("vm", "ViewModel", "poll loop")]
    steps = [
        ("call", "mw", "dmp", "InitComConnections()"),
        ("call", "dmp", "rf", "ApplyRFConnection(open)"),
        ("self", "rf", "open COM · OpenGeneration++"),
        ("call", "dmp", "cb", "ApplyControlBoxConnection(open)"),
        ("self", "cb", "open COM"),
        ("frame", "SWON pulse, under _controlBoxGate", 2),
        ("self", "cb", "Motor 1..3 = RUN, TX gap apart"),
        ("self", "cb", "Start All = HIGH, wait 500 ms, = LOW"),
        ("self", "mw", "retry open up to 12x / 5 s if a port is missing"),
        ("self", "vm", "poll sees OpenGeneration changed · wait 2.5 s"),
        ("call", "vm", "rf", "query each used box: AA 03 ID 01 3F .. BB"),
        ("reply", "rf", "vm", "box status, else timeout → Offline"),
    ]
    return _sequence("svg-start", parts, steps,
                     "<b>Mở cổng, rồi hỏi.</b> Control Box được đưa về RUN và phát một xung Start All khi mở; vòng polling đợi 2,5 s rồi query từng hộp; gateway phát query ra LoRa, hộp (firmware v2) trả lời sau 20 ms.",
                     "Sequence: on load the app opens both COM ports, pulses the control box SWON line, then the poll loop waits and queries each used box",
                     col=196)


# ---------------------------------------------------------------- pages

def cach_doc():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p>Các sơ đồ dùng ký hiệu UML: <b>sơ đồ lớp</b> cho biết lớp nào giữ lớp nào, <b>sơ đồ tuần tự</b> cho biết ai gọi ai theo thứ tự thời gian.
Tên trong hình là tên lớp và member thật trong mã nguồn. Chữ trong hình để tiếng Anh.</p>
<h2 id="so-do-lop">Sơ đồ lớp</h2>
<div class="table-wrap"><table>
<thead><tr><th>Ký hiệu</th><th>Nghĩa</th></tr></thead>
<tbody>
<tr><td>Đường có hình thoi đặc ở đầu A</td><td>A <b>sở hữu</b> B: A tạo ra B, B sống cùng A</td></tr>
<tr><td>Đường nét đứt màu trắng, mũi tên mở</td><td>A <b>dùng</b> B (ở đây: sự kiện serial, đi qua ranh giới luồng)</td></tr>
<tr><td class="mono">+</td><td>thành phần công khai; <code>[20]</code> = có 20 cái</td></tr>
</tbody></table></div>
<h2 id="so-do-tuan-tu">Sơ đồ tuần tự</h2>
<div class="table-wrap"><table>
<thead><tr><th>Ký hiệu</th><th>Nghĩa</th></tr></thead>
<tbody>
<tr><td>Cột có hộp tên ở trên</td><td>Một đối tượng hoặc một luồng; dòng chấm đi xuống là thời gian</td></tr>
<tr><td>Mũi tên liền, đầu đặc</td><td>Gọi hoặc gửi khung</td></tr>
<tr><td>Mũi tên nét đứt, đầu mở</td><td>Trả về / echo / thông báo</td></tr>
<tr><td>Mũi tên quay về chính nó</td><td>Việc làm bên trong đối tượng đó</td></tr>
<tr><td>Khung nét đứt có nhãn</td><td>Nhóm bước lặp hoặc có điều kiện</td></tr>
</tbody></table></div>
'''
    write("so_do/00_cach_doc.html", body=body)


def lop():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>MainWindow giữ tất cả; hai phần cứng móc vào qua DeviceManagerPage.</em></p>
''' + _class_svg() + '''
<h2 id="doc-hinh">Đọc hình</h2>
<ul>
<li><code>MainWindow</code> tạo và giữ <code>ViewModel</code> (logic), <code>DeviceManagerPage</code> (hai cổng) và <code>MonitorPage</code> (bản đồ).</li>
<li><code>ViewModel</code> giữ 20 <code>ESDBox</code>, timer băng tải và vòng polling; nó cũng là nơi phân tích khung LoRa.</li>
<li><code>DeviceManagerPage</code> giữ <code>ControlBox</code> và <code>RFGateway</code>, mỗi lớp bọc một <code>SerialPort</code>.</li>
<li><code>MonitorPage</code> giữ <code>RootCanvas</code>, canvas giữ 20 <code>BlockDevice</code> (ô hộp trên băng tải).</li>
<li>Liên kết chéo duy nhất là sự kiện nhận serial của <code>RFGateway</code> gọi vào <code>ViewModel.FeedByte</code> — chạy trên luồng serial, nên luôn <code>BeginInvoke</code> về UI.</li>
</ul>
'''
    write("so_do/01_lop.html", body=body)


def canh_bao():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một hộp chuyển Warning, từ chân vào tới relay băng tải.</em></p>
''' + _warning_svg() + '''
<h2 id="doc-hinh">Đọc hình</h2>
<ol>
<li>Hộp đọc hai chân, suy ra <code>'W'</code>, gửi khung LoRa; gateway chuyển tiếp nguyên khung qua USB.</li>
<li><code>ViewModel.FeedByte</code> ghép byte thành khung, CRC đúng thì đặt <code>box.Status = Warning</code> và tính lại "mong muốn": có hộp dùng nào Warning → cần STOP.</li>
<li>Timer băng tải 200&nbsp;ms thấy "mong muốn" khác lần gửi trước nên gọi <code>AssertMotorChannels(STOP)</code>.</li>
<li>Mỗi kênh Motor 1..3 gửi một khung lệnh, chờ echo; thử lại tới 4 lần, cách nhau TX gap giữa các kênh.</li>
<li>Arduino echo khung (ACK) và kéo chân HIGH: relay đóng, động cơ dừng. Hết Warning thì lặp lại với STATE 0 (RUN).</li>
</ol>
'''
    write("so_do/02_canh_bao.html", body=body)


def khoi_dong():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Thứ tự lúc khởi động: mở hai cổng, xung SWON, rồi query trạng thái lần đầu.</em></p>
''' + _startup_svg() + '''
<h2 id="doc-hinh">Đọc hình</h2>
<ul>
<li>Khi cửa sổ <code>Loaded</code>, <code>InitComConnections</code> mở lần lượt RF Gateway rồi Control Box (đều trên luồng nền).</li>
<li>Mở cổng RF thành công thì <code>OpenGeneration</code> tăng; mở Control Box thì chạy xung SWON (Motor 1..3 = RUN, rồi Start All HIGH 500&nbsp;ms → LOW) dưới khóa <code>_controlBoxGate</code>.</li>
<li>Nếu một cổng chưa lên (driver USB chưa sẵn sàng lúc khởi động cùng Windows), <code>MainWindow</code> thử lại tới 12 lần, mỗi 5&nbsp;s.</li>
<li>Vòng polling thấy <code>OpenGeneration</code> đổi, đợi 2,5&nbsp;s (gateway có thể reboot) rồi query mọi hộp đang dùng theo vòng (xem <a href="../kien_truc/04_poll.html">Status Polling</a>). Hộp chạy firmware v2 trả lời sau khoảng 20&nbsp;ms; hộp vẫn im sau <code>PollRetries</code> vòng (mỗi vòng ít nhất <code>PollRoundWaitMs</code> = 5&nbsp;s) thì hiện Offline.</li>
</ul>
'''
    write("so_do/03_khoi_dong.html", body=body)


def build():
    cach_doc(); lop(); canh_bao(); khoi_dong()
    section_index("so_do/so_do.html",
                  "<p>Bốn trang: cách đọc ký hiệu, sơ đồ lớp của toàn ứng dụng, và hai sơ đồ tuần tự (cảnh báo → dừng, và khởi động). Mọi hình vẽ bằng SVG nằm sẵn trong trang.</p>")
