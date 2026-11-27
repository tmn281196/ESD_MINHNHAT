# -*- coding: utf-8 -*-
"""Branch 'Hệ thống' (Foundations): what the system is, the devices, the box status model.
Prose Vietnamese, diagram text English."""
from frame import write, section_index
import svg


def _overview_svg():
    W, H = 1200, 244
    bw, bh, y = 150, 58, 94
    xs = [24, 274, 524, 774, 1024]            # 100 px gaps, room for a label in each
    yc = y + bh / 2
    boxes = [("ESD Box", "A · W · I"), ("RF Gateway", "LoRa → USB"), ("PC · app", "ESDCMS.exe"),
             ("Control Box", "Arduino"), ("Conveyor", "relays")]
    glabels = ["LoRa 433 MHz", "USB · 9600", "USB · 9600", "relay (NC)"]

    def halo(x, yy, text, fill="var(--ink-2)", size=10, anchor="middle"):
        return (f'<text x="{x}" y="{yy}" text-anchor="{anchor}" font-family="JetBrains Mono, monospace" '
                f'font-size="{size}" fill="{fill}" paint-order="stroke" stroke="var(--bg)" stroke-width="6" '
                f'stroke-linejoin="round">{text}</text>\n')

    g = ""
    g += svg.heading(24, 44, "STATUS  (box → PC)", size=13)
    g += svg.heading(778, 44, "COMMAND  (PC → conveyor)", size=13)
    g += '<rect x="14" y="60" width="668" height="118" fill="none" stroke="var(--rule)" stroke-dasharray="6 4"/>\n'
    g += '<rect x="766" y="60" width="420" height="118" fill="none" stroke="var(--rule)" stroke-dasharray="6 4"/>\n'
    for (nm, sub), x, strong in zip(boxes, xs, [False, False, True, False, False]):
        g += svg.box(x, y, bw, bh, nm, sub, strong=strong, center=True)
    for i in range(4):                        # arrow first, label (with halo) on top of it
        x1, x2 = xs[i] + bw, xs[i + 1]
        g += svg.arrow(f"M{x1} {yc} L{x2} {yc}")
        g += halo((x1 + x2) / 2, yc - 11, glabels[i])
    g += halo(W / 2, 224, "any used box = Warning → STOP   ·   all clear → RUN", fill="var(--accent)", size=11.5)
    return svg.figure(W, H, "System chain: ESD box over LoRa to the RF gateway, over USB to the PC app, over a second USB link to the control box and the conveyor relays",
                      g, "<b>Hai nửa gặp nhau ở PC.</b> Nửa trái là trạng thái hộp đi lên; nửa phải là lệnh dừng/chạy băng tải đi xuống. PC là chỗ duy nhất nối hai nửa.")


def tong_quan():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Các hộp ESD báo trạng thái về PC qua LoRa; PC dừng băng tải khi có cảnh báo tĩnh điện.</em></p>
''' + _overview_svg() + '''
<h2 id="bai-toan">Bài toán</h2>
<p>Trên một line lắp ráp điện tử, mỗi người thao tác phải nối đất qua dây đeo cổ tay hoặc thảm ESD để phóng tĩnh điện an toàn.
Nếu một vị trí bị <strong>hở tĩnh điện</strong> (tuột dây, đứt mạch nối đất) mà băng tải vẫn chạy, bo mạch đi qua vị trí đó có nguy cơ hỏng vì phóng tĩnh điện.
Hệ này giám sát mọi vị trí theo thời gian thực và <strong>dừng băng tải</strong> ngay khi có vị trí hở, rồi cho chạy lại khi mọi thứ bình thường.</p>

<h2 id="chuoi">Chuỗi thiết bị</h2>
<p>Trạng thái đi từ trái sang phải trong hình trên:</p>
<div class="table-wrap"><table>
<thead><tr><th>Chặng</th><th>Thiết bị</th><th>Làm gì</th></tr></thead>
<tbody>
<tr><td class="num">1</td><td class="nowrap">Hộp ESD</td><td>Đo hai đầu vào (dây đeo / thảm) ở một vị trí, rút ra trạng thái <code>A</code>/<code>W</code>/<code>I</code>, gửi bằng LoRa khi trạng thái đổi (lặp lại vài lần cho chắc) và khi PC hỏi.</td></tr>
<tr><td class="num">2</td><td class="nowrap">RF Gateway</td><td>Nhận gói LoRa của mọi hộp cùng Network ID, chuyển tiếp qua USB-serial về PC dưới dạng khung <code>AA … BB</code>; chiều ngược lại phát lệnh query của PC ra LoRa.</td></tr>
<tr><td class="num">3</td><td class="nowrap">PC · ESDCMS</td><td>Phân tích khung, cập nhật màu ô của hộp trên bản đồ băng tải; nếu có hộp Warning thì ra lệnh dừng.</td></tr>
<tr><td class="num">4</td><td class="nowrap">Control Box</td><td>Arduino nhận khung lệnh qua USB-serial, bật/tắt các chân relay tương ứng, echo lại khung làm xác nhận.</td></tr>
<tr><td class="num">5</td><td class="nowrap">Băng tải</td><td>Relay (tiếp điểm thường đóng) ngắt/đóng mạch động cơ: cảnh báo → dừng, hết cảnh báo → chạy.</td></tr>
</tbody>
</table></div>

<h2 id="hai-duong">Hai đường serial tách biệt</h2>
<p>PC giữ <strong>hai cổng COM riêng</strong>, hai khung tin khác nhau:</p>
<ul>
<li><strong>RF Gateway</strong> — giám sát hộp, hai chiều: trạng thái hộp đi lên, lệnh query <code>'?'</code> của PC đi xuống. Xem <a href="../giao_thuc/01_lora.html">LoRa Status Protocol</a>.</li>
<li><strong>Control Box</strong> — đường ra, điều khiển relay băng tải, có echo xác nhận. Xem <a href="../giao_thuc/02_control_box.html">Control Box Protocol</a>.</li>
</ul>
<div class="callout note"><p class="label">Vì sao tách hai đường</p>
<p>Hai chức năng độc lập: giám sát (không dây, nhiều hộp) và điều khiển cơ cấu (có dây, một hộp Arduino, cần phản hồi chắc chắn).
Tách hai cổng cho phép rút/cắm, nối lại, hỏng một bên mà bên kia vẫn chạy.</p></div>
'''
    write("he_thong/00_tong_quan.html", body=body)


def thiet_bi():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Bốn khối phần cứng, mỗi khối một vai trò, nối với nhau trên hai đường truyền.</em></p>
<div class="table-wrap"><table>
<thead><tr><th>Thiết bị</th><th>Phần cứng</th><th>Vai trò</th><th>Nối với</th></tr></thead>
<tbody>
<tr><td class="nowrap"><strong>Hộp ESD</strong></td><td>Arduino + SX1278 (LoRa 433&nbsp;MHz), 2 đầu vào, 3 đèn</td><td>Đo một vị trí, phát trạng thái, trả lời query</td><td class="nowrap">LoRa ↔ RF Gateway</td></tr>
<tr><td class="nowrap"><strong>RF Gateway</strong></td><td>ESP32 + SX1278, cổng USB-serial</td><td>Thu gói LoRa đúng Network ID đẩy lên PC; phát lệnh của PC ra LoRa</td><td class="nowrap">USB 9600 ↔ PC</td></tr>
<tr><td class="nowrap"><strong>Control Box</strong></td><td>Arduino Mega + bo relay</td><td>Đóng/mở relay băng tải theo lệnh PC</td><td class="nowrap">USB 9600 ↔ PC</td></tr>
<tr><td class="nowrap"><strong>PC</strong></td><td>Windows + <code>ESDCMS.exe</code></td><td>Gom trạng thái, hiển thị, ra lệnh dừng/chạy</td><td class="nowrap">hai cổng COM</td></tr>
</tbody>
</table></div>

<h2 id="firmware">Firmware trong repo</h2>
<p>Ba loại firmware Arduino, trong <code>Firmware_LoRA/</code> và <code>Firmware_ControBox/</code>:</p>
<div class="table-wrap"><table>
<thead><tr><th>File</th><th>Chạy trên</th><th>Nội dung</th></tr></thead>
<tbody>
<tr><td class="mono">Firmware_LoRA/lora_node</td><td class="nowrap">Hộp ESD</td><td>Đọc IN1/IN2 → <code>A</code>/<code>W</code>/<code>I</code>, bật đèn; gửi khi đổi (lặp 5 lần, giãn dần), lúc bật nguồn, và khi được hỏi <code>'?'</code>. <code>NODE_ID</code> đặt riêng cho từng hộp trước khi nạp (trong code đang là <code>0x04</code>), <code>NETWORK_ID 0x03</code>. Bật thêm vai trò repeater (phát lại giúp hộp ngoài tầm gateway) bằng <code>#define ENABLE_REPEATER 1</code>, mặc định 0. Phát <strong>17&nbsp;dBm</strong> (<code>TX_POWER_DBM</code>), cả lúc bật nguồn lẫn sau khi tự reset module LoRa (gửi không xong trong 500&nbsp;ms hoặc module báo bận).</td></tr>
<tr><td class="mono">Firmware_LoRA/lora_gateway</td><td class="nowrap">RF Gateway</td><td>Hai chiều: LoRa→USB (lọc Network ID + CRC, đẩy khung <code>AA…BB</code> lên PC) và USB→LoRa (kiểm khung của PC rồi phát ra LoRa). Phát 17&nbsp;dBm (mặc định thư viện).</td></tr>
<tr><td class="mono">Firmware_ControBox/Line1, Line2, Line3</td><td class="nowrap">Control Box</td><td>Nhận khung lệnh USB, set chân relay, echo làm ACK. Ba line cùng một code, chỉ khác <code>Pin.h</code> (chân theo dây của từng line). <code>Line3</code> mới lấy chân từ firmware cũ, chưa kiểm dây thật.</td></tr>
</tbody>
</table></div>
<div class="callout warn"><p class="label">Lưu ý ghép đôi</p>
<p>Hộp (<code>lora_node</code>) và Gateway (<code>lora_gateway</code>) phải cùng <strong>Network ID</strong> (hiện <code>0x03</code>) và cùng tần số (433&nbsp;MHz) thì mới nói chuyện được; Network ID trên PC phải khớp để khung không bị bỏ. Mỗi hộp mang một <strong>TxID</strong> riêng (1..20) chính là số thứ tự hộp trên PC — sửa <code>#define NODE_ID</code> trong <code>lora_node.ino</code> trước khi nạp cho từng hộp; hai hộp trùng ID sẽ đè trạng thái của nhau.</p></div>

<h2 id="radio">Thông số radio và build</h2>
<p>Hộp và gateway dùng thư viện <code>LoRa</code> 0.8.0 (Sandeep Mistry). <code>LoRa.begin(433E6)</code> reset cứng chip SX1278 rồi đặt tần số, LNA boost, AGC tự động và công suất 17&nbsp;dBm; các thông số còn lại giữ mặc định của chip:</p>
<div class="meta"><span><b>Tần số</b> 433&nbsp;MHz</span><span><b>SF</b> 7</span><span><b>BW</b> 125&nbsp;kHz</span><span><b>CR</b> 4/5</span><span><b>Sync word</b> 0x12</span><span><b>CRC LoRa</b> tắt (khung tự có CRC8)</span><span><b>Công suất</b> 17&nbsp;dBm</span></div>
<p class="tnote">Đổi SF hoặc BW phải nạp lại gateway và mọi hộp cùng lúc, và phải tính lại các khe thời gian (<code>SLOT_MS</code>, <code>REPLY_DELAY_MS</code>), vì thời gian phát một khung tăng theo SF.</p>
<p>Build bằng Arduino IDE hoặc <code>arduino-cli</code>. <code>lora_node</code> đã biên dịch thử với board <code>arduino:avr:uno</code> (khoảng 6,9&nbsp;KB flash; 7,3&nbsp;KB khi bật repeater), <code>lora_gateway</code> với <code>esp32:esp32:esp32</code>. Thư mục <code>libraries/LoRa/src</code> chỉ được có <code>LoRa.cpp</code> và <code>LoRa.h</code>: một bản sao kiểu <em>"LoRa - Copy.cpp"</em> nằm cạnh sẽ làm build lỗi "multiple definition".</p>

<h2 id="cloud">Cloud: ESDCMS ghi lên Firebase</h2>
<p>PC chạy <code>ESDCMS</code> ghi trạng thái line lên <strong>Firebase Realtime Database</strong>, nên PC cần có internet. RF gateway chỉ là cầu LoRa ↔ USB, không có WiFi. Việc ghi chạy nền (<code>Device/CloudUploader.cs</code>), không làm chậm vòng hỏi hộp hay việc điều khiển băng tải. Mất mạng thì line vẫn chạy bình thường: app giữ tối đa 500 lần đổi trạng thái trong bộ nhớ rồi gửi bù khi có mạng lại.</p>
<ul>
<li><strong>Cài đặt:</strong> group <strong>Cloud</strong> ở thanh dưới, nút bánh răng (khi đã mở khóa): tick <em>Send the line to the cloud</em>, nhập <em>Database URL</em>, <em>Web API key</em>, <em>Email</em>, <em>Password</em> của tài khoản Firebase. Lưu trong <code>config.json</code>, mật khẩu mã hóa DPAPI theo user Windows. Line ghi vào theo Net ID (<code>lines/03</code> khi Net ID là 0x03).</li>
<li><strong>Khi nào ghi:</strong> mỗi giây app so trạng thái các hộp đang dùng; hộp đổi trạng thái thì ghi trong khoảng 1&nbsp;s. Ngoài ra cứ 60&nbsp;s ghi đủ một lần (PC, mọi hộp, lần nghe cuối). Lỗi thì thử lại sau 5&nbsp;s, 10&nbsp;s, … tối đa 5&nbsp;phút. Token đăng nhập dùng 50&nbsp;phút rồi đăng nhập lại.</li>
<li><strong>Icon ☁ của group Cloud:</strong> <strong>xanh</strong> = ghi OK; <strong>vàng</strong> = đang kết nối; <strong>xám</strong> = tắt, chưa đủ cài đặt, hoặc lỗi. Rê chuột để xem mã và tên lỗi (<a href="#cloud-loi">Mã lỗi</a>).</li>
<li><strong>Console log:</strong> mỗi lần đăng nhập và mỗi lần ghi, app in một dòng: ghi gì (hộp nào, mấy lần đổi trạng thái, bao nhiêu byte), mất bao lâu, OK hay lỗi gì.</li>
<li><strong>Firebase:</strong> tài khoản Email/Password trong Authentication. Rules mẫu ở <code>Firebase/firebase.rules.json</code>: ai đã đăng nhập cũng đọc được <code>lines</code>, nhưng một tài khoản chỉ ghi được line khai báo ở <code>gateways/&lt;UID&gt; = 03</code> (kiểu <em>string</em>, gõ <code>03</code> không kèm dấu ngoặc kép). Hai PC của cùng một line dùng chung tài khoản; mỗi PC có mục riêng trong <code>pcs/</code>.</li>
<li><strong>App <code>ESDCloudViewer</code></strong> (thư mục cạnh <code>ESDCMS</code>, giao diện giống ESDCMS, xem <a href="../kien_truc/03_ket_noi.html#viewer">Connections &amp; COM</a>): xem line từ máy bất kỳ có internet. <em>Check settings</em> kiểm tra tài khoản và rules y như ESDCMS dùng; <em>Watch</em> xem trực tiếp: từng PC (ghi lần cuối, RF Gateway, Control Box, băng tải), màu từng hộp, các lần đổi trạng thái gần nhất.</li>
</ul>
<div class="code"><div class="code-bar">Realtime Database · một line = một Net ID</div><pre><code>lines/03/pcs/&lt;PC&gt;            lastSeen (giờ server), app, startedAt, rf, controlBox, conveyor (RUN/STOP), boxesInUse, droppedEvents
lines/03/boxes/09             s (A/W/I/O), t (từ lúc), seen (nghe lần cuối), pc
lines/03/events/&lt;t&gt;_&lt;PC&gt;_&lt;n&gt;  { box, s, t, pc }   mỗi lần đổi trạng thái</code></pre></div>
<div class="code"><div class="code-bar">Console log của app · ví dụ</div><pre><code>Cloud: on, line 03 as LINE03-PC1 -&gt; esdlora-default-rtdb.asia-southeast1.firebasedatabase.app
Cloud -&gt; Firebase: sign-in OK (0.6 s)
Cloud -&gt; Firebase: wrote pc info, 3 boxes [01 A, 02 W, 05 O], 2 status changes, 752 bytes (0.4 s)
Cloud -&gt; Firebase: write FAILED (0.3 s): HTTP 401 Unauthorized, Permission denied; tried pc info, 3 boxes [01 A, 02 A, 05 O], 1 status change, 698 bytes</code></pre></div>
<p class="tnote">Thời gian là Unix epoch tính bằng ms theo đồng hồ PC (riêng <code>lastSeen</code> theo giờ server). <code>O</code> = Offline: hộp không trả lời PC sau các vòng hỏi. Tên PC là tên máy Windows. Firmware gateway không còn tùy chọn cloud/WiFi.</p>

<h3 id="cloud-loi">Mã lỗi</h3>
<p>Tooltip của icon ☁ và Console Log chỉ hiện mã và tên lỗi; ý nghĩa ở bảng dưới.</p>
<div class="table-wrap"><table>
<thead><tr><th>Mã / tên</th><th>Ý nghĩa</th></tr></thead>
<tbody>
<tr><td class="mono">HTTP 400 Bad Request, INVALID_LOGIN_CREDENTIALS</td><td>Sai email hoặc mật khẩu (so với Authentication → Users).</td></tr>
<tr><td class="mono">HTTP 400 Bad Request, API_KEY_INVALID</td><td>Sai Web API key, hoặc key bị giới hạn.</td></tr>
<tr><td class="mono">HTTP 400 …, PASSWORD_LOGIN_DISABLED / CONFIGURATION_NOT_FOUND</td><td>Chưa bật đăng nhập Email/Password, hoặc project chưa bật Authentication.</td></tr>
<tr><td class="mono">HTTP 400 …, USER_DISABLED / TOO_MANY_ATTEMPTS_TRY_LATER / INVALID_EMAIL</td><td>User bị khóa / sai quá nhiều lần, Firebase tạm chặn / email sai định dạng.</td></tr>
<tr><td class="mono">HTTP 401 Unauthorized, Permission denied</td><td>Không có quyền ghi line: rules chưa Publish, hoặc <code>gateways/&lt;UID&gt;</code> không phải chữ của line (thừa dấu ngoặc kép, là số…). Token hết hạn cũng ra 401; app tự đăng nhập lại.</td></tr>
<tr><td class="mono">HTTP 404 Not Found</td><td>Sai Database URL (không phải database của project).</td></tr>
<tr><td class="mono">HTTP 423 Locked</td><td>Database đang bị tắt trong Firebase console.</td></tr>
<tr><td class="mono">HTTP 429 / 5xx</td><td>Firebase tạm chặn vì gửi nhiều / lỗi phía Google; app tự thử lại.</td></tr>
<tr><td class="mono">NameResolutionFailure</td><td>Không phân giải được tên server: PC không có internet, hoặc sai URL.</td></tr>
<tr><td class="mono">ConnectFailure</td><td>Có tên nhưng không kết nối được (tường lửa chặn cổng 443).</td></tr>
<tr><td class="mono">Timeout</td><td>Server không trả lời trong 20&nbsp;s.</td></tr>
<tr><td class="mono">TrustFailure / SecureChannelFailure</td><td>Lỗi HTTPS: giờ của PC sai, hoặc proxy/tường lửa can thiệp vào HTTPS.</td></tr>
</tbody></table></div>
'''
    write("he_thong/01_thiet_bi.html", body=body)


def trang_thai():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Sáu trạng thái; ba do hộp báo, ba do PC tự đặt.</em></p>
<p>Mỗi hộp trên PC là một đối tượng <code>ESDBox</code> mang một <code>DeviceStatus</code>. Ba trạng thái đầu đến thẳng từ hộp qua LoRa;
ba trạng thái sau do phía PC đặt khi khung lỗi hoặc hộp im lặng.</p>
<div class="table-wrap"><table>
<thead><tr><th>Trạng thái</th><th>Màu ô</th><th>Nghĩa</th><th>Ai đặt</th></tr></thead>
<tbody>
<tr><td class="nowrap"><strong>Active</strong></td><td class="nowrap">xanh lá <code>#45F248</code></td><td>Vị trí đạt: cả hai đầu vào tốt</td><td>hộp (<code>'A'</code>)</td></tr>
<tr><td class="nowrap"><strong>Warning</strong></td><td class="nowrap">đỏ cam (OrangeRed)</td><td>Hở tĩnh điện — <strong>kích hoạt dừng băng tải</strong></td><td>hộp (<code>'W'</code>)</td></tr>
<tr><td class="nowrap"><strong>Inactive</strong></td><td class="nowrap">vàng</td><td>Không có người / chưa nối</td><td>hộp (<code>'I'</code>)</td></tr>
<tr><td class="nowrap"><strong>Unknown</strong></td><td class="nowrap">xanh dương <code>#037EF3</code></td><td>Chưa nghe gì từ hộp (lúc mới mở / reset)</td><td>PC</td></tr>
<tr><td class="nowrap"><strong>Offline</strong></td><td class="nowrap">xanh dương <code>#037EF3</code></td><td>Hộp không trả lời query (mất điện, ngoài vùng, firmware cũ) — giữ màu xanh mặc định, <em>không</em> phải xám</td><td>PC</td></tr>
<tr><td class="nowrap"><strong>Error</strong></td><td class="nowrap">(giữ màu cũ)</td><td>Khung nhận được sai CRC; chỉ tăng bộ đếm lỗi, không đổi ô</td><td>PC</td></tr>
</tbody>
</table></div>
<p>Ô "không dùng" (vị trí trống trên băng tải) luôn vẽ màu xám bất kể trạng thái, xem <a href="../kien_truc/07_giao_dien.html">User Interface</a>.</p>

<h2 id="hop-suy-ra">Hộp suy ra A/W/I thế nào</h2>
<p>Firmware hộp (<code>lora_node.ino</code>) đọc hai chân số <code>IN1</code>, <code>IN2</code> và rút ra trạng thái theo đúng bảng này:</p>
<div class="table-wrap"><table>
<thead><tr><th class="mono">IN1</th><th class="mono">IN2</th><th>Trạng thái</th><th>Đèn</th></tr></thead>
<tbody>
<tr><td class="num">1</td><td class="num">1</td><td><code>'A'</code> Active</td><td>xanh</td></tr>
<tr><td class="num">1</td><td class="num">0</td><td><code>'W'</code> Warning</td><td>đỏ</td></tr>
<tr><td class="num">0</td><td class="num">—</td><td><code>'I'</code> Inactive</td><td>vàng</td></tr>
</tbody>
</table></div>
<p>Đầu vào được chống dội 200&nbsp;ms (phải giữ nguyên 200&nbsp;ms mới nhận; không dùng <code>delay()</code> nên hộp vẫn nghe được LoRa). Hộp gửi khung <strong>khi trạng thái đổi</strong> — gửi rồi lặp thêm 5 lần cách 100, 200, 400, 800, 1600&nbsp;ms (+ ngẫu nhiên tới 50&nbsp;ms) vì PC không gửi ACK; <strong>lúc bật nguồn</strong> — đợi <code>NODE_ID × 60&nbsp;ms</code> để các hộp không cùng phát sau khi mất điện; và <strong>khi PC hỏi</strong> (xem <a href="../giao_thuc/01_lora.html#query">query</a>).
Việc <strong>bất kỳ</strong> hộp đang dùng ở Warning sẽ làm băng tải dừng được xử lý ở phía PC, xem <a href="../kien_truc/05_conveyor.html">Conveyor Control</a>.</p>
<div class="callout note"><p class="label">Error ≠ Offline</p>
<p><strong>Error</strong> là có nghe thấy khung nhưng CRC sai (nhiễu đường truyền) — PC chỉ đếm, không đổi màu, vì khung kế thường lại đúng.
<strong>Offline</strong> là hỏi mà <em>không</em> nghe thấy gì sau vài lần query — ô giữ màu <strong>xanh dương mặc định</strong> (như lúc mới mở, Unknown). Màu xám chỉ dành cho ô <em>không dùng</em>.</p></div>
'''
    write("he_thong/02_trang_thai.html", body=body)


def build():
    tong_quan(); thiet_bi(); trang_thai()
    section_index("he_thong/he_thong.html",
                  "<p>Ba trang: toàn cảnh chuỗi thiết bị và bài toán, vai trò từng thiết bị, và mô hình sáu trạng thái của một hộp ESD.</p>")
