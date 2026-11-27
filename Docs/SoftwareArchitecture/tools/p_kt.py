# -*- coding: utf-8 -*-
"""Branch 'Kiến trúc' (Architecture): the WPF app in depth — context, file map, COM handling,
status polling, conveyor control, config, UI, and limits. Prose Vietnamese."""
from frame import write, section_index


def boi_canh():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một tiến trình WPF giữ hai cổng COM và toàn bộ logic; nhiều luồng, nhưng mọi thứ UI đi qua Dispatcher.</em></p>
<p><code>ESDCMS</code> — <em>ESD Compliance Monitoring System</em> — là ứng dụng <strong>.NET Framework 4.8, WPF, x64</strong> (WinExe <code>ESDCMS.exe</code>, bản v2.0; trước tên là <code>ESDKitSystem</code>). Một cửa sổ duy nhất chứa trang giám sát, console log và thanh điều khiển.
Khi chạy, nó mở hai cổng COM — RF Gateway (vào) và Control Box (ra) — và giữ trạng thái của tối đa 20 hộp.</p>

<h2 id="luong">Các luồng</h2>
<div class="table-wrap"><table>
<thead><tr><th>Luồng</th><th>Việc</th><th>Ghi chú</th></tr></thead>
<tbody>
<tr><td class="nowrap">UI Dispatcher</td><td>Vẽ, đổi màu ô, chạy vòng polling, timer băng tải</td><td>Mọi thay đổi UI phải ở đây</td></tr>
<tr><td class="nowrap">Serial RX (RF)</td><td><code>DataReceived</code> của RF Gateway → parser byte</td><td>Dùng <code>BeginInvoke</code>, không bao giờ chờ UI (tránh deadlock khi đóng cổng)</td></tr>
<tr><td class="nowrap"><code>Task.Run</code> nền</td><td>Mở/đóng cổng, ghi/đọc Control Box (chờ echo)</td><td>Port <code>Open()</code>/<code>Write()</code> có thể treo lâu, không để trên UI</td></tr>
<tr><td class="nowrap">Watchdog thoát</td><td>Giết tiến trình nếu driver treo 5&nbsp;s sau khi đóng cửa sổ</td><td>Tránh exe "ma" giữ cổng COM</td></tr>
</tbody>
</table></div>
<p>Hai khóa đồng bộ giữ cho phần cứng không bị hai luồng giành: <code>_controlBoxGate</code> (một thao tác Control Box tại một thời điểm) và <code>_radioLock</code> (một query LoRa trên không tại một thời điểm).</p>

<h2 id="ben-bi">Thiết kế để bền</h2>
<p>Nhiều chi tiết sinh ra từ việc chạy liên tục trên sàn máy với cáp USB hay bị rút:</p>
<ul>
<li><strong>Single-instance theo thư mục:</strong> mutex <code>Local\\ESDCMS_&lt;hash đường dẫn exe&gt;</code>. Mở bản thứ hai <em>cùng thư mục</em> bị chặn (sẽ tranh cổng COM). Bản ở <em>thư mục khác</em> vẫn mở được, nhưng mọi bản của cùng một user Windows dùng chung <code>%USERPROFILE%\\.esdcms\\config.json</code> — cùng cổng COM — nên thực tế chỉ một bản nắm được cổng.</li>
<li><strong>Không chết vì lỗi lẻ:</strong> lỗi Dispatcher được bắt và ghi <code>%USERPROFILE%\\.esdcms\\crash.log</code>, cửa sổ vẫn sống thay vì tắt với cổng COM còn mở.</li>
<li><strong>Đóng cổng dứt khoát khi thoát</strong> + watchdog giết tiến trình nếu driver không nhả.</li>
</ul>
<p>Chi tiết mở/đóng cổng ở <a href="03_ket_noi.html">Connections &amp; COM</a>.</p>
'''
    write("kien_truc/01_boi_canh.html", body=body)


def phan_tang():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Thư mục project <code>ESDCMS/</code>; ba nhóm file: thiết bị &amp; logic (Device/), bản đồ băng tải (Monitoring/), và cửa sổ chính.</em></p>
<div class="table-wrap"><table>
<thead><tr><th class="mono">File</th><th>Vai trò</th></tr></thead>
<tbody>
<tr><td class="mono">ESDCMS.csproj / ESDCMS.slnx</td><td>Project .NET Framework 4.8 · WPF · x64, ra <code>ESDCMS.exe</code>; mở solution bằng Visual Studio 2022 hoặc <code>dotnet build</code>.</td></tr>
<tr><td class="mono">App.xaml.cs</td><td>Khởi động: tên hiển thị <code>AppDisplayName</code>, single-instance mutex, bắt lỗi toàn cục → <code>.esdcms\\crash.log</code>, watchdog thoát.</td></tr>
<tr><td class="mono">MainWindow.xaml(.cs)</td><td>Cửa sổ: nút ngôn ngữ, khung trang giám sát, console log, thanh dưới (group RF Gateway / Control Box / Cloud với nút cài đặt ⚙, group Motors), Lock, auto refresh COM khi cắm/rút USB.</td></tr>
<tr><td class="mono">SettingsForm.cs</td><td>Hộp thoại cài đặt dựng bằng code cho các nút ⚙ của thanh dưới (COM, Net ID, các thời gian, Firebase).</td></tr>
<tr><td class="mono">Device/CloudUploader.cs, Device/FirebaseRest.cs</td><td>Ghi line lên Firebase (đăng nhập, PATCH, gửi bù khi mất mạng) và tên lỗi kiểu mã + tên, xem <a href="../he_thong/01_thiet_bi.html#cloud">Devices &amp; Roles</a>.</td></tr>
<tr><td class="mono">Lang/Strings.{en,ko,th}.xaml, Lang/Lang.cs</td><td>Chữ giao diện cho English / 한국어 / ไทย (cùng bộ khóa) và lớp đổi ngôn ngữ lúc chạy.</td></tr>
<tr><td class="mono">Device/PortNames.cs</td><td>Giá trị của combo COM: tên cổng thật thì mở, <code>Close</code> (và giá trị cũ <code>Unused</code>/<code>Unset</code>) thì không.</td></tr>
<tr><td class="mono">Images/icon/</td><td>Icon ESD dạng vector <code>esd.svg</code>, file <code>esd.ico</code> và script <code>make_esd_icon.ps1</code> sinh ra <code>app.ico</code>.</td></tr>
<tr><td class="mono">UnlockDialog.cs</td><td>Hộp nhập mật khẩu khi mở khóa; nhập đúng một lần thì nhớ trong bộ nhớ tới khi tắt app (<code>EnteredThisSession</code>). Exe chỉ giữ mã băm SHA-256 (có salt) của mật khẩu; đổi mật khẩu = thay hằng <code>PasswordHash</code>. Chống lỡ tay, không phải bảo mật mạnh.</td></tr>
<tr><td class="mono">Device/ViewModel.cs</td><td>Trái tim: parser khung LoRa, 20 <code>ESDBox</code>, vòng polling, timer + luật dừng băng tải.</td></tr>
<tr><td class="mono">Device/ControlBox.cs</td><td>Cổng Control Box: dựng khung lệnh, ghi, chờ echo, kiểm ACK.</td></tr>
<tr><td class="mono">Device/RFGateway.cs</td><td>Cổng RF Gateway: mở/đóng, gửi khung query, phát sự kiện nhận.</td></tr>
<tr><td class="mono">Device/DeviceManagerPage.xaml.cs</td><td>Giữ <code>ControlBox</code> + <code>RFGateway</code>; áp/ngắt kết nối, refresh danh sách COM, xung SWON lúc mở.</td></tr>
<tr><td class="mono">Device/SerialPortSafe.cs</td><td>Mở/đóng cổng an toàn chống treo (xem trang Connections).</td></tr>
<tr><td class="mono">Device/Config.cs, AppConfig.cs</td><td>Các khóa cấu hình + giá trị hiệu lực (clamp). Xem <a href="06_cau_hinh.html">Config</a>.</td></tr>
<tr><td class="mono">Device/ConfigStore.cs</td><td>Đọc/ghi <code>%USERPROFILE%\\.esdcms\\config.json</code> (lần đầu chép config cũ cạnh exe sang), an toàn với mất điện giữa chừng.</td></tr>
<tr><td class="mono">Device/ESDBox.cs, DeviceStatus.cs</td><td>Một hộp và enum trạng thái; màu theo trạng thái.</td></tr>
<tr><td class="mono">Device/AutoStart.cs</td><td>Bật/tắt chạy cùng Windows (giá trị <code>ESDCMS</code> trong HKCU\\…\\Run); xóa giá trị cũ <code>ESDKitSystem</code>.</td></tr>
<tr><td class="mono">Device/UiLogWriter.cs</td><td>Chuyển <code>Console.WriteLine</code> thành dòng trong panel log.</td></tr>
<tr><td class="mono">Monitoring/MornitorPage.xaml(.cs)</td><td>Trang giám sát: tạo <code>RootCanvas</code>, nối click ô → query hộp; đặt dải cảnh báo Offline ngay dưới hàng ô, rộng bằng dải băng tải.</td></tr>
<tr><td class="mono">Monitoring/RootCanvas.cs</td><td>Vẽ băng tải: 20 ô vị trí + 20 ô hộp, nạp layout từ config.</td></tr>
<tr><td class="mono">Monitoring/BlockDevice.cs</td><td>Một ô hộp: kéo đổi vị trí, phải-chuột bật/tắt dùng, click để hỏi.</td></tr>
<tr><td class="mono">HexByteConverter.cs</td><td>Hiển thị/nhập Network ID dạng hex hai chữ số.</td></tr>
</tbody>
</table></div>
<div class="callout note"><p class="label">Fody / PropertyChanged</p>
<p>Project dùng <strong>Fody + PropertyChanged.Fody</strong>: các auto-property trong <code>ViewModel</code>, <code>ESDBox</code> tự được dệt <code>OnPropertyChanged</code> lúc build. Không tự viết <code>OnPropertyChanged</code> trong setter. Mã nguồn là bản <strong>dịch ngược</strong> từ exe rồi dọn lại để build, tên biến cục bộ lấy từ PDB nên khá sát bản gốc.</p></div>
'''
    write("kien_truc/02_phan_tang.html", body=body)


def ket_noi():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Cổng USB-serial hay bị rút giữa chừng; phần lớn code kết nối là để không bị treo vì điều đó.</em></p>

<h2 id="safe">SerialPortSafe</h2>
<p>Mọi lần mở cổng dùng một đối tượng <code>SerialPort</code> <strong>mới toanh</strong>, và đóng có timeout:</p>
<ul>
<li><strong>Đối tượng mới mỗi lần mở:</strong> một <code>SerialPort</code> đóng hụt (cáp bị rút) có thể báo <code>IsOpen = true</code> mãi mãi và mọi <code>Open()</code> sau đều lỗi tới khi khởi động lại — nên luôn tạo mới.</li>
<li><strong>Chặn finalizer của BaseStream:</strong> khi thiết bị USB biến mất, finalizer ném lỗi trên luồng GC và <em>giết cả tiến trình</em>; <code>GC.SuppressFinalize</code> sau khi mở để tránh.</li>
<li><strong>Đóng có timeout 2&nbsp;s:</strong> driver treo không bao giờ làm đơ UI hay giữ tiến trình sống.</li>
<li><strong>ReadTimeout / WriteTimeout 500&nbsp;ms:</strong> mặc định là vô hạn — một driver chết sẽ chặn <code>Write()</code> mãi và giữ khóa TX.</li>
</ul>
<p>Lỗi mở được dịch sang lời khuyên cụ thể: <em>busy (access denied)</em> → chương trình khác đang giữ cổng, rút/cắm cáp rồi Refresh; <em>not found</em> → kiểm cáp.</p>

<h2 id="auto">Tự nhận cắm / rút</h2>
<p>Cửa sổ nghe thông điệp Windows <code>WM_DEVICECHANGE</code>. Mỗi lần tập cổng COM đổi (chống dội 500&nbsp;ms):</p>
<ul>
<li>Làm tươi danh sách COM trong hai ComboBox.</li>
<li>Cổng vừa <strong>biến mất</strong> mà là cổng đang dùng → đóng handle ngay (giữ handle trên thiết bị đã rút chính là thứ làm cổng "access denied" tới khi reboot).</li>
<li>Cổng vừa <strong>trở lại</strong> mà người dùng muốn mở → đợi 1,5&nbsp;s cho driver liệt kê xong rồi tự nối lại.</li>
</ul>

<h2 id="chon-cong">Chọn cổng = kết nối</h2>
<p>Không có nút OPEN/CLOSE. Chọn cổng trong hộp thoại cài đặt (nút ⚙) của group RF Gateway hay Control Box rồi bấm <strong>OK</strong> là app mở cổng đó ngay (đang nối cổng khác thì đóng cổng cũ trước); chọn <strong>Close</strong> là đóng và lưu lại. Việc làm tươi danh sách khi cắm/rút USB không tự đổi cổng. Trạng thái kết nối hiện ở màu icon ở đầu group.</p>

<h2 id="startup">Thử lại lúc khởi động</h2>
<p>Khi chạy cùng Windows, driver USB-serial thường chưa sẵn sàng và lần mở đầu thất bại. App thử lại <strong>12 lần, mỗi 5&nbsp;s</strong> (khoảng một phút) thay vì nằm im mất kết nối. Nút <strong>Refresh Connection</strong> trên thanh điều khiển ép đóng rồi mở lại cả hai cổng, hoạt động cả khi đang khóa.</p>
<h2 id="sim">Thử không cần phần cứng</h2>
<p>Thư mục <code>Simulators/</code> có hai app WPF giả lập phần cứng theo đúng giao thức: <code>ControlBoxSim</code> (nhận khung Control Box, echo làm ACK, có thể tắt/làm chậm echo) và <code>RfGatewaySim</code> (gateway + 20 hộp: bấm A/W/I để đổi trạng thái, bỏ Online để hộp im lặng, trả lời query <code>'?'</code>). Nối với ESDCMS bằng cặp cổng ảo com0com, ví dụ ESDCMS mở <code>COM15</code>/<code>COM16</code>, simulator mở <code>COM18</code>/<code>COM17</code>. Hướng dẫn ở <code>Simulators/README.md</code>.</p>
<h2 id="viewer">ESDCloudViewer: xem cloud từ xa</h2>
<p>Thư mục <code>ESDCloudViewer/</code> (cạnh <code>ESDCMS/</code>, cùng trong <code>ESDCMS.slnx</code>) là app WPF .NET Framework 4.8 thứ hai, đọc những gì ESDCMS ghi lên Firebase. Giao diện theo đúng template ESDCMS: thanh tiêu đề có nút ngôn ngữ (en/ko/th), chú thích màu như trang Monitor, Console Log nền xanh (tiếng Anh), thanh dưới có group <strong>Firebase</strong> với <em>Settings</em>, <em>Check settings</em>, <em>Watch</em>, <em>Stop</em>. Font và icon link thẳng từ <code>ESDCMS/</code>.</p>
<ul>
<li><strong>Cài đặt:</strong> nhập Database URL, Web API key, email, mật khẩu (như cài đặt Cloud của ESDCMS) và chọn line; lưu ở <code>%USERPROFILE%\.esdcms\cloudviewer.json</code>, mật khẩu Firebase mã hóa DPAPI theo user Windows. Mở app mà đã lưu thì tự đăng nhập và xem.</li>
<li><strong>Xem trực tiếp:</strong> stream REST của Realtime Database (<code>lines/&lt;line&gt;/pcs</code> và <code>.../boxes</code>, sự kiện <code>put</code>/<code>patch</code>) cộng 30 lần đổi trạng thái cuối, hỏi lại mỗi 10&nbsp;s. Hộp đổi màu ngay khi gateway ghi; hộp PC báo <code>O</code> hiện Offline. Tự nối lại khi mất mạng, 50 phút đăng nhập lại một lần (token sống 60 phút).</li>
<li><strong>Kiểm tra cấu hình:</strong> đăng nhập, <code>gateways/&lt;UID&gt;</code>, ghi thử vào <code>pcs/</code> rồi xóa, rules chặn line khác; kết quả <code>[ OK ]</code>/<code>[WARN]</code>/<code>[FAIL]</code> ở Console Log.</li>
</ul>
<div class="callout note"><p class="label">OpenGeneration</p>
<p>Mỗi lần cổng RF mở thành công, <code>OpenGeneration</code> tăng một. Vòng polling thấy số này đổi là biết cổng vừa (re)mở và sẽ hỏi lại trạng thái tất cả hộp — xem <a href="04_poll.html">Status Polling</a>.</p></div>
'''
    write("kien_truc/03_ket_noi.html", body=body)


def poll():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Hộp tự báo khi đổi; polling lấp chỗ trống: biết trạng thái đầu, và phát hiện hộp im lặng.</em></p>
<p>Hộp chỉ tự gửi <em>khi trạng thái đổi</em> và lúc bật nguồn, nên nếu chỉ nghe thụ động thì PC mở sau các hộp không biết chúng đang thế nào, và không phân biệt được "đang Active" với "đã tắt nguồn". Vòng polling (<code>StatusPollLoopAsync</code>) giải việc đó bằng khung query <code>'?'</code>.</p>

<h2 id="vong">Vòng polling</h2>
<ul>
<li><strong>Lúc cổng RF (re)mở</strong> (<code>OpenGeneration</code> đổi): đợi <strong>2,5&nbsp;s</strong> (gateway ESP32 có thể reboot khi mở cổng), rồi hỏi <em>mọi hộp đang dùng</em>.</li>
<li><strong>Hỏi theo vòng</strong> (từ v2.1, <code>AskBoxesInRoundsAsync</code>): mỗi vòng gửi một query cho từng hộp chưa nghe thấy — sau mỗi query nghe <strong>PollReplyTimeoutMs</strong> (mặc định 400&nbsp;ms) rồi mới gửi query kế — rồi chờ cho đủ <strong>PollRoundWaitMs</strong> (mặc định 5&nbsp;s) kể từ đầu vòng để gom câu trả lời. Vòng sau chỉ hỏi lại các hộp còn im; tối đa <strong>PollRetries</strong> vòng (mặc định 3).</li>
<li>Hộp vẫn im sau tất cả các vòng → đặt <strong>Offline</strong> (ô giữ màu xanh dương mặc định, không phải xám), và dải cảnh báo vàng ngay dưới hàng ô hộp hiện số hộp kèm lời nhắc kiểm tra nguồn điện và kết nối không dây (xem <a href="07_giao_dien.html">User Interface</a>). Rải các lần hỏi lại ra vài giây để một đợt nhiễu sóng ngắn không làm hộp bị báo Offline nhầm; đổi lại, một hộp tắt thật phải tới khoảng 15&nbsp;s mới thành Offline.</li>
<li>Sau đó vòng polling xét lần lượt, việc nào có thì làm trước:
<ol>
<li><strong>Xác nhận Warning:</strong> hộp vừa chuyển sang Warning và đã im <strong>4&nbsp;s</strong> (tức là đã tự lặp xong) thì được hỏi lại. Hộp trả lời thì trạng thái theo câu trả lời; im thì thành <strong>Offline</strong> (log ghi "switched off?") và không còn tính là Warning để dừng băng tải.</li>
<li><strong>Hộp chưa có trạng thái</strong> (Unknown, ví dụ vừa bật dùng trên bản đồ): hỏi ngay, mỗi hộp tối đa 30&nbsp;s một lần.</li>
<li>Mỗi <strong>AskAgainSec</strong> giây (mặc định <strong>120</strong>; 0 = không): hỏi <em>lần lượt mọi hộp đang dùng</em>. Khung trạng thái không có ACK, nên lượt này sửa ô nào hiện sai vì PC lỡ khung đổi trạng thái; hộp đang Offline mà trả lời thì "back online". Hai PC cùng Network ID nên đặt chu kỳ lệch nhau (ví dụ 120 và 130) để không hỏi cùng lúc.</li>
</ol></li>
</ul>
<p>Chỉ <strong>một query trên không tại một thời điểm</strong> (<code>_radioLock</code>): các lượt hỏi của vòng polling và cú click vào ô hộp thay nhau dùng sóng.</p>

<h2 id="khop">Khớp hỏi với trả lời</h2>
<p>Mỗi hộp có một "người chờ" (<code>TaskCompletionSource</code>). Gửi query xong, vòng hỏi chờ <em>hoặc</em> hộp trả lời (parser gọi <code>NotifyBoxAnswered</code> khi khung CRC đúng) <em>hoặc</em> hết timeout. Khung trạng thái bình thường của hộp cũng được tính là "đã trả lời". Ngoài ra mỗi hộp lưu thời điểm được nghe thấy gần nhất (<code>_lastAnswerTicks</code>); hỏi theo vòng dựa vào đó để biết hộp nào đã trả lời kể từ đầu lượt, kể cả khi câu trả lời tới sau thời gian nghe.</p>

<h2 id="click">Click để hỏi ngay</h2>
<p>Click (không kéo) vào một ô hộp trên bản đồ gọi <code>RequestBoxStatus</code>: hỏi riêng hộp đó ngay, cũng theo vòng như trên (nên một hộp im phải tới khoảng 15&nbsp;s mới thành Offline). Nếu hộp không trong danh sách dùng, hoặc cổng RF chưa nối, chỉ ghi log. Một hộp đang có query dở thì click thêm bị bỏ qua (không xếp hàng).</p>
<div class="callout warn"><p class="label">Phụ thuộc firmware</p>
<p>Query cần firmware v2 ở cả gateway (chuyển tiếp PC→LoRa) và hộp (đáp <code>'?'</code>, sau 20&nbsp;ms) — đều có trong <code>Firmware_LoRA/</code>. Hộp chạy firmware cũ không trả lời nên sẽ hiện <strong>Offline</strong> cho tới khi nó tự gửi một thay đổi trạng thái. Xem <a href="08_gioi_han.html">Limits &amp; Open Work</a>.</p></div>
'''
    write("kien_truc/04_poll.html", body=body)


def conveyor():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Bất kỳ hộp đang dùng ở Warning → dừng băng tải; hết Warning → chạy lại. Một timer lo việc đó.</em></p>

<h2 id="luat">Luật</h2>
<p>Mỗi khi một hộp đổi trạng thái, <code>EnqueueControlBoxUpdate</code> tính lại: có <em>bất kỳ</em> ô đang dùng nào ở <strong>Warning</strong> không? Kết quả (dừng / chạy) được đặt làm "mong muốn", và nhãn băng tải đổi màu (đỏ "Conveyor Stopped" / xanh "Conveyor Moving").</p>
<p>Một <strong>DispatcherTimer 200&nbsp;ms</strong> (<code>ConveyorTimer_Tick</code>) so "mong muốn" với trạng thái đã gửi lần trước; chỉ khi khác mới gửi lệnh, nên không spam Control Box. Gửi hụt (không đủ echo) thì giữ nguyên "lần trước" để lần tick sau <strong>thử lại</strong>.</p>

<h2 id="gui">Gửi một trạng thái</h2>
<p><code>AssertMotorChannels(on)</code> đặt ba kênh Motor 1..3 (<code>ControlBox.MotorCount</code>) về cùng mức, lần lượt, cách nhau <strong>TX gap</strong>:</p>
<ul>
<li>Mỗi kênh: <code>AssertChannel</code> gửi tới <strong>Retry</strong> lần (mặc định 4), cách nhau <strong>Retry gap</strong> (100&nbsp;ms), dừng ngay khi có echo.</li>
<li>Mỗi lần gửi: <code>SendChannel → SendTX</code> ghi khung rồi chờ echo trong <strong>RX timeout</strong> (mặc định 500&nbsp;ms), so từng byte.</li>
<li>Giữa hai kênh liên tiếp chờ <strong>TX gap</strong> (mặc định 500&nbsp;ms).</li>
</ul>
<p class="tnote">Vì vậy thời gian phản ứng xấu nhất (hộp báo Warning → cả 3 kênh đã gửi) khi Control Box im lặng vào cỡ <code>3 × Retry × (RX timeout + Retry gap) + 2 × TX gap</code> — hàng giây; khi Control Box đáp bình thường thì gần như chỉ còn <code>2 × TX gap</code>.</p>

<h2 id="ket-noi">Xung SWON lúc kết nối</h2>
<p>Khi mở cổng Control Box (<code>OpenControlBoxCore</code>), app đưa Motor 1..3 về <strong>RUN</strong> (cách nhau TX gap) rồi phát một xung <strong>Start All</strong> (opcode 5): HIGH, đợi 500&nbsp;ms, LOW. Toàn bộ chạy dưới khóa <code>_controlBoxGate</code> nên không đụng timer băng tải.</p>

<h2 id="lock">Lock / manual</h2>
<p>Mở khóa bằng nút <strong>Lock</strong> (phải nhập mật khẩu) sẽ bật chế độ manual: timer băng tải <em>ngừng</em> tự điều khiển (<code>_controlsUnlocked</code>), người vận hành tự bấm các nút Start All / Stop 1..3 (group Motors), và sửa được các ô cài đặt. Khóa lại thì tự động hoạt động trở lại. Bản đồ hộp cũng chỉ cho kéo/đổi khi mở khóa (xem <a href="07_giao_dien.html">User Interface</a>).</p>
'''
    write("kien_truc/05_conveyor.html", body=body)


def cau_hinh():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một file <code>config.json</code> trong thư mục của user Windows giữ cổng COM, các thông số thời gian và layout màn hình.</em></p>
<p>Đọc/ghi qua <code>ConfigStore</code>, tại <code>%USERPROFILE%\\.esdcms\\config.json</code> (cùng chỗ với <code>crash.log</code>). Không đặt cạnh exe — thư mục cài có thể chỉ đọc (Program Files) hoặc bị thay khi cập nhật; cũng không theo thư mục hiện hành — chạy từ shortcut/auto-start thì thư mục có thể khác và cấu hình "biến mất". File không có sẵn trong repo; app tạo lần chạy đầu.</p>
<div class="callout note"><p class="label">Chuyển từ bản cũ</p>
<p>Bản trước (<code>ESDKitSystem.exe</code>) để <code>config.json</code> cạnh exe. Lần đầu <code>ESDCMS</code> chạy, nếu trong <code>.esdcms</code> chưa có file mà cạnh exe còn file cũ, nó đọc file cũ rồi ghi sang chỗ mới (file cũ để nguyên) — cổng COM và layout được giữ.</p></div>
<div class="callout warn"><p class="label">Một user, một cấu hình</p>
<p>Mọi bản <code>ESDCMS</code> chạy dưới cùng một user Windows dùng chung file này, dù exe nằm ở thư mục khác nhau. Muốn hai line trên một PC với hai bộ cổng COM khác nhau thì cần hai user Windows.</p></div>

<div class="code"><div class="code-bar">json · %USERPROFILE%\\.esdcms\\config.json</div><pre><code>{
  "Com": {
    "RFGatewayCOM": "COM5",
    "ControlBoxCOM": "COM6",
    "CloudEnabled": false,
    "CloudDbUrl": "",
    "CloudApiKey": "",
    "CloudEmail": "",
    "CloudPasswordProtected": "",
    "AutoStart": true,
    "LogCollapsed": false,
    "Language": "en",
    "PollReplyTimeoutMs": 400,
    "PollRoundWaitMs": 5000,
    "PollRetries": 3,
    "AskAgainSec": 120,
    "TxGapMs": 500,
    "NetworkID": 3,
    "TxTimeoutMs": 500,
    "TxRetries": 4,
    "TxRetryDelayMs": 100
  },
  "Monitoring": {
    "DevicePosition":  [0,1,2, ... ,19],
    "DevicesIsActive": [true,true,true,true,true,true,true,true,false, ... ]
  }
}</code></pre></div>

<h2 id="khoa">Các khóa (mục Com)</h2>
<div class="table-wrap"><table>
<thead><tr><th>Khóa</th><th>Mặc định</th><th>Hiệu lực (clamp)</th><th>Ý nghĩa</th></tr></thead>
<tbody>
<tr><td class="mono">RFGatewayCOM / ControlBoxCOM</td><td class="nowrap">—</td><td>—</td><td>Tên cổng COM, chọn trong cài đặt ⚙ của group (OK là kết nối); <code>Close</code> = không mở. Giá trị cũ <code>Unused</code>/<code>Unset</code> cũng được hiểu là Close.</td></tr>
<tr><td class="mono">CloudEnabled</td><td class="nowrap">false</td><td>—</td><td>Ghi line lên Firebase (xem <a href="../he_thong/01_thiet_bi.html#cloud">Devices &amp; Roles</a>) — tick trong cài đặt ⚙ của group Cloud</td></tr>
<tr><td class="mono">CloudDbUrl / CloudApiKey / CloudEmail</td><td class="nowrap">—</td><td>—</td><td>Database URL, Web API key và email của tài khoản Firebase — cài đặt ⚙ của group Cloud</td></tr>
<tr><td class="mono">CloudPasswordProtected</td><td class="nowrap">—</td><td>—</td><td>Mật khẩu Firebase, mã hóa DPAPI theo user Windows (user hoặc PC khác không giải mã được, phải nhập lại)</td></tr>
<tr><td class="mono">Language</td><td class="num">en</td><td>en / ko / th</td><td>Ngôn ngữ giao diện, đổi bằng nút ngôn ngữ trên thanh tiêu đề; console log vẫn tiếng Anh</td></tr>
<tr><td class="mono">NetworkID</td><td class="num">3 (0x03)</td><td>—</td><td>Line ID chấp nhận từ RF Gateway; khung ID khác bị bỏ — ô <em>Net ID</em> trong cài đặt ⚙ của group RF Gateway</td></tr>
<tr><td class="mono">PollReplyTimeoutMs</td><td class="num">400</td><td>≥ 100</td><td>Thời gian nghe trả lời sau một query, trước khi gửi query kế</td></tr>
<tr><td class="mono">PollRoundWaitMs</td><td class="num">5000</td><td>≥ 1000</td><td>Một vòng hỏi kéo dài ít nhất chừng này (trừ khi mọi hộp đã trả lời) rồi mới hỏi lại các hộp còn im. Không có ô trên giao diện, chỉ sửa trong file. Từ v2.1</td></tr>
<tr><td class="mono">PollRetries</td><td class="num">3</td><td>≥ 1</td><td>Số vòng hỏi trước khi cho Offline (gồm vòng đầu)</td></tr>
<tr><td class="mono">AskAgainSec</td><td class="num">120</td><td>0, hoặc ≥ 5</td><td>Chu kỳ hỏi lại mọi hộp đang dùng; 0 = không — ô <em>Ask again</em> trong cài đặt ⚙ của group RF Gateway. Trước v2.1 mặc định là 300; giá trị đã lưu trong file được giữ nguyên nên khi cập nhật phải tự đặt lại</td></tr>
<tr><td class="mono">TxGapMs</td><td class="num">500</td><td>≥ 0</td><td>Khoảng cách giữa hai lệnh Control Box liên tiếp — ô <em>TX gap</em> trong cài đặt ⚙ của group Control Box</td></tr>
<tr><td class="mono">TxTimeoutMs</td><td class="num">500</td><td>≥ 50</td><td>Chờ echo Control Box trước khi coi lệnh thất bại — ô <em>RX timeout</em> trong cài đặt ⚙ của group Control Box</td></tr>
<tr><td class="mono">TxRetries</td><td class="num">4</td><td>≥ 1</td><td>Số lần gửi một lệnh Control Box trước khi bỏ (gồm lần đầu) — ô <em>Retry</em> trong cài đặt ⚙ của group Control Box</td></tr>
<tr><td class="mono">TxRetryDelayMs</td><td class="num">100</td><td>≥ 0</td><td>Khoảng cách giữa hai lần gửi cùng một lệnh Control Box — ô <em>Retry gap</em> trong cài đặt ⚙ của group Control Box</td></tr>
<tr><td class="mono">AutoStart / LogCollapsed</td><td class="nowrap">true / false</td><td>—</td><td>Chạy cùng Windows; ẩn panel log</td></tr>
</tbody>
</table></div>
<p class="tnote">Giá trị trong file được "clamp" khi dùng (ví dụ <code>PollReplyTimeoutMs</code> nhỏ hơn 100 vẫn coi là 100), nên không đặt được giá trị nguy hiểm. Các ô trên thanh cài đặt ghi lại file khi mất focus và log đúng khóa vừa đổi.</p>

<h2 id="monitoring">Mục Monitoring &amp; layout</h2>
<p><code>DevicePosition[20]</code> là vị trí (0..19) của từng hộp trên băng tải, <code>DevicesIsActive[20]</code> là hộp nào đang dùng. Mặc định 8 hộp đầu bật, còn lại tắt. <code>ConfigStore</code> tự chuẩn hóa: đủ 20 phần tử và vị trí không trùng.</p>

<h2 id="an-toan">Ghi an toàn</h2>
<ul>
<li><strong>Ghi nguyên tử:</strong> ghi ra <code>config.json.tmp</code> rồi thay (<code>File.Replace</code>) — mất điện giữa chừng không để lại file hỏng.</li>
<li><strong>File hỏng:</strong> đọc lỗi thì sao lưu thành <code>config.json.bad-&lt;thời gian&gt;</code> và chạy bằng mặc định, không chặn khởi động.</li>
<li><strong>Auto-start:</strong> ghi giá trị <code>ESDCMS</code> trong <code>HKCU\\…\\Run</code>, viết lại mỗi lần chạy nên theo đúng exe nếu đổi thư mục; giá trị cũ <code>ESDKitSystem</code> (bản trước) bị xóa để Windows không mở exe cũ.</li>
</ul>
'''
    write("kien_truc/06_cau_hinh.html", body=body)


def giao_dien():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Một cửa sổ: thanh tiêu đề, bản đồ băng tải (kèm dải cảnh báo Offline ngay dưới hàng ô), console log, và thanh dưới chia group theo thiết bị. Font nhúng trong exe, giống docs: Google Sans cho chữ, JetBrains Mono cho log.</em></p>
<div class="table-wrap"><table>
<thead><tr><th>Vùng</th><th>Nội dung</th></tr></thead>
<tbody>
<tr><td class="nowrap">Thanh tiêu đề</td><td>"ESD Compliance Monitoring System" và <strong>nút ngôn ngữ</strong> bên phải: nút ghi tên ngôn ngữ đang dùng; mỗi lần bấm chuyển <em>English → 한국어 → ไทย → English</em>, đổi ngay không cần khởi động lại, lưu vào <code>config.json</code>. Bấm được cả khi đang khóa.</td></tr>
<tr><td class="nowrap">Bản đồ băng tải</td><td>Một dải (băng tải) với 20 ô vị trí; mỗi hộp là một ô mang số 01..20, tô màu theo trạng thái. Chú thích màu (theo ngôn ngữ): Inactive (vàng), Warning (đỏ cam), Active (xanh lá), <strong>Offline (xanh dương)</strong>, Not in use (xám).</td></tr>
<tr><td class="nowrap">Cảnh báo Offline</td><td>Dải vàng nhạt nằm trên bản đồ, <strong>ngay dưới hàng ô hộp</strong> và rộng bằng dải băng tải (vị trí cố định), chữ lớn đậm, icon ⚠ nhấp nháy nhẹ, <strong>chỉ hiện khi có hộp đang dùng ở Offline</strong>: "Box 03, 07 offline — please check the power supply and the wireless connection." (theo ngôn ngữ). Tự ẩn khi các hộp trả lời lại. Hộp Unknown lúc mới mở (chưa hỏi tới) không tính.</td></tr>
<tr><td class="nowrap">Console Log</td><td>Nền xanh kiểu PowerShell, chữ JetBrains Mono trắng; mọi <code>Console.WriteLine</code> hiện ở đây (giữ 1000 dòng). Xóa / lưu .txt / ẩn-hiện. Log luôn bằng <strong>tiếng Anh</strong> dù giao diện đang ở ngôn ngữ nào.</td></tr>
<tr><td class="nowrap">Thanh dưới</td><td>Một hàng duy nhất. Sát lề trái: nút 🔒 và <strong>Start with Windows</strong>. Sát lề phải: <strong>Refresh Connection</strong>, rồi liền ngay sau là bốn group có khung, tên (kèm icon trạng thái) ở đầu mỗi group:
<strong>RF Gateway</strong>, <strong>Control Box</strong> và <strong>Cloud</strong> chỉ có icon (màu = đã kết nối / trạng thái ghi cloud) và một nút vuông ⚙ mở hộp thoại cài đặt (COM, Net ID, Ask again / COM, TX gap, RX timeout, Retry, Retry gap / Firebase); rê chuột lên ⚙ để xem cài đặt hiện tại ·
<strong>Motors</strong> (Start All, Stop 1..3, nút xanh = đang bật).</td></tr>
</tbody>
</table></div>

<h2 id="com">Chọn cổng COM</h2>
<ul>
<li>Không có nút OPEN/CLOSE. Chọn một cổng trong combo của group là <strong>kết nối ngay</strong> (đang nối cổng khác thì đóng cổng cũ trước); chọn <strong>Close</strong> là ngắt thiết bị đó và nhớ lại lựa chọn.</li>
<li>Icon ở đầu group (📶 RF Gateway, ⏩ Control Box) <strong>xanh lá</strong> khi đã kết nối, trắng khi chưa.</li>
<li>Chỉ lựa chọn của người dùng mới gây kết nối. Lúc app làm tươi danh sách cổng (cắm/rút USB) hay gán lại giá trị đã lưu thì không. Khởi động, cắm lại USB, hay bấm Refresh Connection thì app tự mở lại cổng đã chọn (xem <a href="03_ket_noi.html">Connections &amp; COM</a>).</li>
</ul>

<h2 id="o-hop">Thao tác trên ô hộp</h2>
<div class="table-wrap"><table>
<thead><tr><th>Thao tác</th><th>Tác dụng</th><th>Điều kiện</th></tr></thead>
<tbody>
<tr><td class="nowrap">Click (không kéo)</td><td>Hỏi trạng thái hộp đó ngay</td><td>luôn được</td></tr>
<tr><td class="nowrap">Kéo</td><td>Đổi vị trí ô (hoán đổi với ô đích)</td><td>chỉ khi mở khóa</td></tr>
<tr><td class="nowrap">Phải-chuột</td><td>Bật/tắt "đang dùng" ô (vào/ra khỏi line)</td><td>chỉ khi mở khóa</td></tr>
</tbody>
</table></div>
<p>Ô "không dùng" luôn màu xám. Vị trí và trạng thái dùng được lưu vào <code>config.json</code> (mục Monitoring) khi đóng cửa sổ. Màu trạng thái lấy từ <code>ESDBox</code> (xem <a href="../he_thong/02_trang_thai.html">Box Status Model</a>).</p>
<h2 id="lock">Khóa</h2>
<p>Để phòng lỡ tay, mặc định cửa sổ <strong>khóa</strong>. Khi khóa: combo COM, các nút Motors và ô Start with Windows không bấm được; các ô cài đặt chỉ đọc (nhìn như thường, vẫn chọn/copy được); không kéo/đổi ô hộp. <strong>Refresh Connection</strong> và <strong>nút ngôn ngữ</strong> thì luôn dùng được.</p>
<p>Mở khóa (biểu tượng ổ khóa) phải <strong>nhập mật khẩu</strong>; đúng thì mới sửa được, đồng thời bật chế độ điều khiển motor bằng tay và tắt tự-động dừng băng tải cho tới khi khóa lại. Nhập sai thì hộp báo "Wrong password" và cho nhập lại; Cancel thì vẫn khóa. Khóa lại không cần mật khẩu. Mật khẩu nhập đúng một lần thì được nhớ <strong>tới khi tắt chương trình</strong> (khóa rồi mở lại không hỏi nữa); tắt app mở lại thì phải nhập lại. Không ghi mật khẩu ra đâu cả.</p>

<h2 id="ngon-ngu">Ngôn ngữ</h2>
<p>Mọi chữ trên giao diện nằm trong <code>Lang/Strings.en.xaml</code>, <code>Strings.ko.xaml</code>, <code>Strings.th.xaml</code> (cùng bộ khóa <code>S.…</code>); XAML dùng <code>{DynamicResource S.…}</code> nên đổi ngôn ngữ là cả cửa sổ đổi theo ngay. Thêm hay sửa một chữ thì sửa ở cả ba file. Bản tiếng Hàn và tiếng Thái nên được người bản ngữ rà lại. Console log và hộp báo "already running" lúc khởi động vẫn bằng tiếng Anh.</p>

<h2 id="icon">Icon</h2>
<p>Icon ứng dụng là ký hiệu ESD gốc (đĩa vàng viền đen, tam giác đen, bàn tay và vạch chéo), vẽ lại dạng vector, không chữ, nền trong suốt: <code>Images/icon/esd.svg</code>. <code>app.ico</code> (7 cỡ 16–256&nbsp;px) sinh từ cùng bộ tọa độ bằng <code>Images/icon/make_esd_icon.ps1</code>.</p>
'''
    write("kien_truc/07_giao_dien.html", body=body)


def gioi_han():
    body = '''
<h2 id="tong-quan">Tổng quan</h2>
<p class="lede"><em>Những chỗ hệ khác hệ hoàn chỉnh, và việc còn để ngỏ — đọc kèm mã nguồn.</em></p>

<h2 id="query-fw">Query cần nạp firmware v2 cho mọi hộp</h2>
<p>Hỏi trạng thái chủ động và phát hiện Offline (<a href="04_poll.html">Status Polling</a>, <a href="../giao_thuc/01_lora.html#query">LoRa query</a>) chạy đủ hai đầu: PC, và firmware v2 trong <code>Firmware_LoRA/</code> (gateway chuyển tiếp hai chiều, hộp đáp <code>'?'</code>). Khi triển khai cần để ý:</p>
<ul>
<li><strong>Mọi hộp và gateway phải nạp bản v2.</strong> Hộp còn firmware cũ không đáp query → hiện <strong>Offline</strong> tới khi nó tự gửi một thay đổi.</li>
<li><strong><code>NODE_ID</code> nằm cứng trong <code>lora_node.ino</code></strong> (đang là <code>0x04</code>): phải sửa rồi nạp riêng cho từng hộp. Hai hộp trùng ID sẽ đè trạng thái của nhau và trùng khe trả lời.</li>
<li><strong>PC chưa dùng query <code>0xFF</code></strong> (hỏi tất cả, trả lời theo khe <code>NODE_ID × 60&nbsp;ms</code>) dù firmware hỗ trợ; PC đang hỏi lần lượt từng hộp.</li>
<li><strong>Khung trạng thái không có ACK:</strong> hộp bù bằng cách lặp 5 lần khi đổi. Nếu PC lỡ cả 6 lần, ô hiện sai tới lượt PC hỏi lại mọi hộp (mỗi <code>AskAgainSec</code>, mặc định 120&nbsp;s), tới thay đổi kế tiếp, hoặc tới khi người dùng click ô.</li>
<li><strong><code>PollRoundWaitMs</code> chưa có ô trên giao diện</strong> (thêm từ v2.1): muốn đổi phải sửa <code>config.json</code>.</li>
</ul>

<h2 id="firmware-khac">Khác biệt firmware khác</h2>
<ul>
<li><strong>Ba line cùng code, khác chân:</strong> <code>Firmware_ControBox/Line1/L1</code>, <code>Line2/L2</code>, <code>Line3/L3</code> chạy đúng một code đọc khung; chỉ <code>Pin.h</code> khác, theo dây của từng line (xem <a href="../giao_thuc/02_control_box.html#pin">Control Box Protocol</a>). Hai Control Box của Line 2 (PC1, PC2) dùng chung một firmware.</li>
<li><strong>Chân Line 3 chưa kiểm:</strong> <code>Line3/L3/Pin.h</code> lấy từ firmware cũ (Start All = 24, Motor 1..3 = cùng chân 22) vì chưa có sơ đồ dây; phải đối chiếu hộp thật trước khi nạp.</li>
<li><strong>Chỉ còn 3 motor:</strong> Motor 4 (opcode 4) đã bỏ ở firmware, app và simulator vì không line nào nối nó. Cần thêm motor thì phải thêm lại cả ba nơi.</li>
<li><strong>Hộp nạp firmware trước 2026-11-27 phát yếu dần:</strong> sau lần đầu tự reset module LoRa, hộp tụt từ 17 xuống 10&nbsp;dBm (tầm phát còn khoảng một nửa) cho tới khi rút điện. Bản hiện tại giữ 17&nbsp;dBm (<code>TX_POWER_DBM</code>); phải nạp lại từng hộp.</li>
<li><strong>Chưa đo được cường độ sóng:</strong> gateway không gửi RSSI/SNR lên PC (khung <code>AA…BB</code> không có chỗ, và cổng USB của gateway dành cho khung đó), nên chưa phân biệt được hộp Offline vì nhiễu / bị chắn sóng (ví dụ cạnh máy reflow) hay vì mất nguồn. Muốn đo thì phải dùng một hộp test in RSSI ra cổng USB riêng.</li>
<li><strong>Cloud cần PC có internet:</strong> ESDCMS ghi lên Firebase; PC không có mạng thì app giữ tối đa 500 lần đổi trạng thái để gửi bù, quá thì bỏ cái cũ nhất (đếm ở <code>droppedEvents</code>). Các PC của cùng một line dùng chung một tài khoản Firebase; chưa có tài khoản chỉ-đọc riêng cho người xem.</li>
</ul>

<h2 id="khac">Chỗ khác cần biết</h2>
<ul>
<li><strong>Dừng cả băng tải, không theo từng động cơ:</strong> luật hiện tại là "bất kỳ hộp Warning → đặt cả Motor 1..3 = STOP". Không có ánh xạ "hộp X dừng riêng động cơ Y".</li>
<li><strong>Danh sách MAC trong <code>App.xaml.cs</code>:</strong> có tính nhưng kết quả không dùng (một kiểu kiểm tra giấy phép còn ngủ).</li>
<li><strong>Mã nguồn dịch ngược:</strong> tên tham số lambda và vài cấu trúc <code>async</code> có thể khác bản gốc; hành vi thì bám theo exe đã chạy.</li>
<li><strong>Một PC, một line:</strong> Network ID phân biệt line, và cấu hình nằm theo user Windows (<code>%USERPROFILE%\\.esdcms</code>), nên mọi bản ESDCMS của một user dùng chung cổng COM. Nhiều line trên một PC cần nhiều user Windows, hoặc mở rộng thêm (ví dụ tham số dòng lệnh chọn file config).</li>
</ul>
'''
    write("kien_truc/08_gioi_han.html", body=body)


def build():
    boi_canh(); phan_tang(); ket_noi(); poll(); conveyor(); cau_hinh(); giao_dien(); gioi_han()
    section_index("kien_truc/kien_truc.html",
                  "<p>Tám trang về ứng dụng WPF: bối cảnh và luồng, bản đồ file, xử lý cổng COM, vòng polling, luật dừng băng tải, cấu hình, giao diện, và các giới hạn hiện tại.</p>")
