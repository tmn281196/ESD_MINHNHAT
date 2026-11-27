# -*- coding: utf-8 -*-
"""Branch 'Giao thức' (Protocols): the LoRa status frame and the Control Box command frame.
Byte layouts use the .bytes / .meta / .ex helpers from arch.css."""
from frame import write, section_index


def lora():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một khung AA…BB cho cả hai chiều; CRC8 bảo vệ, Network ID lọc đúng line.</em></p>
<p>RF Gateway nối PC bằng USB-serial <strong>9600 8N1</strong>. Mọi khung — hộp báo trạng thái lên, hay PC query xuống — dùng chung một bố cục:</p>

<div class="meta"><span><b>Serial</b> 9600 8N1</span><span><b>STX</b> 0xAA</span><span><b>ETX</b> 0xBB</span><span><b>CRC8</b> poly 0x31, init 0x00</span><span><b>Network ID</b> 0x03 (mặc định)</span></div>

<div class="bytes">
  <span class="v hdr">AA<small>STX</small></span>
  <span class="v typ">NetID<small>line</small></span>
  <span class="v typ">TxID<small>box 1..20</small></span>
  <span class="v dat">LEN<small>số byte DATA</small></span>
  <span class="v dat">DATA…<small>LEN byte</small></span>
  <span class="v typ">CRC8<small>của 4 vùng giữa</small></span>
  <span class="v ftr">BB<small>ETX</small></span>
</div>
<p class="tnote">CRC8 tính trên <b>NetID + TxID + LEN + DATA</b> (không gồm STX, CRC, ETX). Cùng một hàm CRC8 (poly <code>0x31</code>) ở hộp, gateway và PC.</p>

<h2 id="status">Khung trạng thái (hộp → PC)</h2>
<p>Hộp gửi <code>LEN = 1</code>, <code>DATA[0]</code> là một ký tự trạng thái: <code>'A'</code> (0x41), <code>'W'</code> (0x57), <code>'I'</code> (0x49).
<code>TxID</code> là số hộp. Ví dụ hộp 09:</p>
<div class="ex"><span class="dir rx">RX</span> <b>AA 03 09 01 41 70 BB</b> &nbsp;— box 09 = Active (<code>'A'</code>)</div>
<div class="ex"><span class="dir rx">RX</span> <b>AA 03 09 01 57 95 BB</b> &nbsp;— box 09 = Warning (<code>'W'</code>)</div>
<div class="ex"><span class="dir rx">RX</span> <b>AA 03 09 01 49 C9 BB</b> &nbsp;— box 09 = Inactive (<code>'I'</code>)</div>
<p>Phía PC phân tích khung bằng một máy trạng thái byte-một (<code>ViewModel.FeedByte</code>): chờ <code>STX</code> → NetID → TxID → LEN → DATA → CRC → <code>ETX</code>. Khi khung xong:</p>
<ul>
<li><code>NetID</code> ≠ Network ID đang đặt → bỏ ngay tại đó, quay về chờ <code>STX</code> (lọc nhiễu line khác).</li>
<li><code>TxID</code> ngoài 1..20 → ghi log "invalid Box ID", bỏ.</li>
<li>CRC sai → hộp đó thành <strong>Error</strong> (chỉ đếm, giữ màu cũ).</li>
<li>CRC đúng → đổi trạng thái hộp theo <code>DATA[0]</code>, và đánh dấu hộp đã trả lời (dùng cho <a href="../kien_truc/04_poll.html">polling</a>).</li>
</ul>

<h2 id="query">Khung query (PC → hộp)</h2>
<p>Để biết trạng thái mà không phải đợi hộp tự đổi, PC gửi lệnh <code>'?'</code> (0x3F) tới một hộp; gateway kiểm khung rồi phát ra LoRa, hộp đáp lại bằng chính khung trạng thái ở trên.
<code>TxID</code> là hộp đích; <code>0xFF</code> nghĩa là "mọi hộp".</p>
<div class="table-wrap"><table>
<thead><tr><th>Hộp đích</th><th>Hộp trả lời sau</th><th>Vì sao</th></tr></thead>
<tbody>
<tr><td class="mono">TxID của nó</td><td>20&nbsp;ms</td><td>Cho gateway kịp chuyển từ phát sang thu</td></tr>
<tr><td class="mono">0xFF (tất cả)</td><td><code>NODE_ID × 60&nbsp;ms</code></td><td>Mỗi hộp một khe thời gian, các câu trả lời không đè nhau</td></tr>
</tbody></table></div>
<div class="ex"><span class="dir">TX</span> <b>AA 03 09 01 3F 97 BB</b> &nbsp;— hỏi box 09</div>
<div class="ex"><span class="dir">TX</span> <b>AA 03 FF 01 3F C8 BB</b> &nbsp;— hỏi tất cả</div>
<p class="tnote">Nếu PC <em>nhận</em> được một khung có <code>DATA[0] = '?'</code> thì đó là query của một PC khác trên cùng Network ID — PC bỏ qua, không coi là trạng thái hộp. Gateway nhận khung từ PC theo từng byte: bỏ khung dở nếu im lặng quá 50&nbsp;ms, khung tối đa 64 byte, sai Network ID hoặc CRC thì không phát.</p>
<div class="callout warn"><p class="label">Cả hộp lẫn gateway phải chạy firmware v2</p>
<p>Chiều query cần <b>gateway</b> chuyển tiếp PC→LoRa và <b>hộp</b> biết đáp <code>'?'</code> — cả hai có trong <code>Firmware_LoRA/</code> hiện tại (<code>lora_gateway</code>, <code>lora_node</code> bản v2). Hộp còn nạp firmware cũ sẽ không trả lời và hiện <strong>Offline</strong> cho tới khi nó tự gửi một thay đổi. PC hiện hỏi <em>từng</em> hộp; chế độ <code>0xFF</code> có ở firmware nhưng PC chưa dùng. Xem <a href="../kien_truc/08_gioi_han.html">Limits &amp; Open Work</a>.</p></div>
<p class="tnote"><b>Repeater (tùy chọn).</b> Một <code>lora_node</code> nạp với <code>ENABLE_REPEATER = 1</code> sẽ phát lại mỗi khung hợp lệ một lần, để hộp nằm ngoài tầm gateway — chỉ nghe được node-repeater — vẫn nhận được query và gửi được trả lời. Khung phát lại là khung <em>hơi khác</em>: cùng bố cục AA…BB nhưng nối thêm một byte <em>hop-marker</em> <code>0x52</code> ở cuối DATA nên <code>LEN</code> tăng 1 và CRC tính lại; khung đã mang marker thì không phát lại nữa (chống lặp vô hạn và chống hai repeater khuếch đại nhau). Gateway và PC chỉ đọc <code>DATA[0]</code> và kiểm CRC theo <code>LEN</code> nên hiểu khung phát lại y như khung gốc — không phải sửa gì ở gateway hay PC.</p>

<h2 id="cfg">Khung cấu hình 0xA5 (PC ↔ gateway, tùy chọn cloud)</h2>
<p>Chỉ dùng khi gateway nạp với <code>ENABLE_CLOUD 1</code> (xem <a href="../he_thong/01_thiet_bi.html#cloud">Devices &amp; Roles</a>). Đi trên cùng cổng USB nhưng bắt đầu bằng <code>0xA5</code> thay cho <code>0xAA</code> và <strong>không bao giờ phát ra LoRa</strong>; gateway thường và bản app cũ đều bỏ qua loại khung này.</p>
<div class="bytes">
  <span class="v hdr">A5<small>STX</small></span>
  <span class="v typ">TYPE<small>W · S · w · s</small></span>
  <span class="v dat">LEN<small>số byte DATA</small></span>
  <span class="v dat">DATA…<small>LEN byte</small></span>
  <span class="v typ">CRC8<small>TYPE+LEN+DATA</small></span>
  <span class="v ftr">BB<small>ETX</small></span>
</div>
<div class="table-wrap"><table>
<thead><tr><th>Chiều</th><th>TYPE</th><th>DATA</th><th>Ý nghĩa</th></tr></thead>
<tbody>
<tr><td class="nowrap">PC → gateway</td><td class="mono">'W'</td><td>SSID, <code>0x00</code>, mật khẩu</td><td>Vào WiFi này. Gateway lưu vào flash, chỉ ghi khi khác giá trị cũ.</td></tr>
<tr><td class="nowrap">PC → gateway</td><td class="mono">'W'</td><td>(trống)</td><td>Tắt WiFi: quên SSID, tắt radio WiFi.</td></tr>
<tr><td class="nowrap">PC → gateway</td><td class="mono">'S'</td><td>(trống)</td><td>Hỏi trạng thái cloud.</td></tr>
<tr><td class="nowrap">gateway → PC</td><td class="mono">'w'</td><td>0 / 1 / 2</td><td>Đã lưu / không đổi / sai định dạng (SSID 1–32 byte, mật khẩu tối đa 63).</td></tr>
<tr><td class="nowrap">gateway → PC</td><td class="mono">'s'</td><td>state, −RSSI, tuổi (7 bit thấp), tuổi (7 bit cao)</td><td>state 0 chưa có WiFi, 1 đang vào WiFi, 2 đã vào WiFi nhưng chưa ghi được cloud, 3 cloud OK. Tuổi = số giây từ lần ghi cloud thành công gần nhất (16383 = chưa bao giờ).</td></tr>
</tbody></table></div>
<p class="tnote">Mọi byte DATA của khung trả lời đều nhỏ hơn <code>0x80</code>, nên parser của PC không bao giờ nhầm nó với đầu một khung <code>0xAA</code>. Gateway không có tùy chọn cloud thì không trả lời: app báo "không trả lời" và biểu tượng ☁ chuyển đỏ.</p>
'''
    write("giao_thuc/01_lora.html", body=body)


def control_box():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một khung 7 byte đặt một kênh relay; Arduino echo lại nguyên khung làm xác nhận.</em></p>
<p>Control Box nối PC bằng USB-serial <strong>9600 8N1</strong>. Mỗi lệnh đặt <em>một</em> kênh về mức mong muốn:</p>

<div class="meta"><span><b>Serial</b> 9600 8N1</span><span><b>Prefix</b> 0x44 0x45</span><span><b>Suffix</b> 0x56</span><span><b>LEN</b> 3</span><span><b>CS</b> XOR</span><span><b>ACK</b> echo nguyên khung</span></div>

<div class="bytes">
  <span class="v hdr">44<small>'D'</small></span>
  <span class="v hdr">45<small>'E'</small></span>
  <span class="v dat">03<small>LEN</small></span>
  <span class="v typ">OP<small>kênh 1..5</small></span>
  <span class="v typ">STATE<small>0 / 1</small></span>
  <span class="v typ">XOR<small>của 5 byte trước</small></span>
  <span class="v ftr">56<small>'V'</small></span>
</div>
<p class="tnote">XOR tính trên cả năm byte trước nó: <code>44 ^ 45 ^ 03 ^ OP ^ STATE</code>. <code>LEN = 3</code> là số byte sau nó (OP, STATE, XOR); firmware chỉ nhận khung có <code>LEN = 3</code>.</p>

<h2 id="opcode">Opcode và STATE</h2>
<div class="table-wrap"><table>
<thead><tr><th class="mono">OP</th><th>Kênh (app)</th><th class="mono">STATE=1</th><th class="mono">STATE=0</th></tr></thead>
<tbody>
<tr><td class="num">1..4</td><td>Motor 1..4</td><td>chân HIGH → relay ON → <b>motor STOP</b></td><td>chân LOW → relay OFF → <b>motor RUN</b></td></tr>
<tr><td class="num">5</td><td>Start All (SWON)</td><td>chân SWON HIGH</td><td>chân SWON LOW</td></tr>
</tbody>
</table></div>
<p>Relay dùng tiếp điểm <strong>thường đóng (NC)</strong> nối tiếp mạch động cơ: cấp điện relay (chân HIGH) thì NC hở ra và động cơ dừng; thả (chân LOW) thì NC đóng lại và động cơ chạy. Vì vậy "cảnh báo" = đặt STATE 1.</p>
<div class="ex"><span class="dir">TX</span> <b>44 45 03 01 01 02 56</b> &nbsp;— Motor 1 STOP</div>
<div class="ex"><span class="dir">TX</span> <b>44 45 03 01 00 03 56</b> &nbsp;— Motor 1 RUN</div>
<div class="ex"><span class="dir">TX</span> <b>44 45 03 05 01 06 56</b> &nbsp;— Start All = HIGH</div>
<div class="ex"><span class="dir rx">RX</span> <b>44 45 03 01 01 02 56</b> &nbsp;— echo y hệt = ACK</div>

<h2 id="echo">Echo làm ACK</h2>
<p>Arduino nhận khung (máy trạng thái <code>WAIT_P1 → P2 → LEN → BODY</code>), kiểm XOR và suffix, rồi nếu <code>LEN = 3</code> thì gọi <code>SetChannel(OP, STATE)</code> và <strong>ghi lại nguyên khung</strong> ra serial. PC đọc khung trả về, so <em>từng byte</em> với khung đã gửi: khớp = thành công. Khung sai CRC ở Arduino thì không echo → PC coi là thất bại và thử lại (xem <a href="../kien_truc/05_conveyor.html">Conveyor Control</a>, các khóa Retry / RX timeout).</p>
<p class="tnote">Arduino cũng bỏ khung nhận dở nếu im lặng quá 100&nbsp;ms (chống dính nửa khung).</p>

<h2 id="pin">Bản đồ chân (Arduino Mega)</h2>
<div class="table-wrap"><table>
<thead><tr><th>Kênh</th><th class="mono">OP</th><th class="mono">Chân</th><th>Nhãn</th></tr></thead>
<tbody>
<tr><td>Motor 1</td><td class="num">1</td><td class="num">28</td><td>MI</td></tr>
<tr><td>Motor 2</td><td class="num">2</td><td class="num">24</td><td>SOLDER</td></tr>
<tr><td>Motor 3</td><td class="num">3</td><td class="num">22</td><td>PACKING</td></tr>
<tr><td>Motor 4</td><td class="num">4</td><td class="num">22</td><td>FCT</td></tr>
<tr><td>Start All</td><td class="num">5</td><td class="num">30</td><td>SWON</td></tr>
</tbody>
</table></div>
<div class="callout warn"><p class="label">Motor 3 và Motor 4 trùng chân</p>
<p>Trong <code>Pin.h</code> hiện tại <code>MOTOR3</code> và <code>MOTOR4</code> đều là chân <code>22</code>, nên hai kênh này điều khiển cùng một relay. Nếu line cần 4 động cơ độc lập thì phải đổi <code>MOTOR4</code> sang chân khác. Xem <a href="../kien_truc/08_gioi_han.html">Limits</a>.</p></div>
'''
    write("giao_thuc/02_control_box.html", body=body)


def build():
    lora(); control_box()
    section_index("giao_thuc/giao_thuc.html",
                  "<p>Hai trang khung tin: khung trạng thái LoRa qua RF Gateway (cả chiều query), và khung lệnh Control Box có echo xác nhận cùng bản đồ chân relay.</p>")
