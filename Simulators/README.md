# Simulators — test ESDCMS without hardware

Hai app WPF (.NET Framework 4.8) giả lập phần cứng, nói đúng giao thức của firmware, để chạy ESDCMS trên một PC
không có Control Box / RF Gateway thật. Nối với ESDCMS bằng **cặp cổng COM ảo com0com** (null-modem: ghi đầu này,
đầu kia nhận).

| App | Giả lập | Giao thức |
|---|---|---|
| `ControlBoxSim` | Control Box (Arduino Mega, `Firmware_ControBox/Line1`) | nhận `44 45 03 OP STATE XOR 56`, đặt kênh, **echo nguyên khung** làm ACK |
| `RfGatewaySim` | RF Gateway (`Firmware_LoRA/lora_gateway` v2) + 20 hộp ESD (`lora_node` v2) | `AA NetID ID LEN DATA CRC8 BB`; hộp gửi `A`/`W`/`I`, trả lời query `?` |

Build: mở `Simulators.slnx` bằng Visual Studio, hoặc `dotnet build Simulators.slnx`.

## Nối dây với com0com

Mỗi thiết bị cần **một cặp** cổng ảo: ESDCMS mở một đầu, simulator mở đầu kia (cả hai 9600 8N1).
Tạo cặp bằng *com0com Setup* hoặc `setupc.exe` (chạy với quyền admin), ví dụ:

```
setupc install PortName=COM15 PortName=COM18
setupc install PortName=COM16 PortName=COM17
```

Trên PC hiện tại đã có sẵn hai cặp **COM15 ⇄ COM18** và **COM16 ⇄ COM17**, nên:

| | ESDCMS chọn | Simulator mở |
|---|---|---|
| RF Gateway | `COM15` | `RfGatewaySim.exe COM18` |
| Control Box | `COM16` | `ControlBoxSim.exe COM17` |

Cách nào cũng được, miễn mỗi bên mở **hai đầu khác nhau của cùng một cặp**. Config ESDCMS hiện tại trên máy này đang là RF Gateway = `COM16`, Control Box = `COM15`, nên khi đó mở `RfGatewaySim.exe COM17` và `ControlBoxSim.exe COM18` (đã chạy thử đầu–cuối với cách nối này: 8/8 bước đạt).

Truyền tên cổng làm tham số dòng lệnh thì simulator tự mở cổng đó lúc chạy; không truyền thì chọn trong combo rồi bấm **Open**. Tham số thứ hai (tùy chọn) là đường dẫn file: mọi dòng log được ghi thêm vào đó, ví dụ `RfGatewaySim.exe COM18 rf.log`.

## Thử nhanh

1. Mở hai simulator, rồi ESDCMS; trong ESDCMS chọn `COM15` cho RF Gateway, `COM16` cho Control Box.
2. Khoảng 2,5 s sau ESDCMS hỏi từng hộp đang dùng (`?`) → `RfGatewaySim` trả lời; hộp 1..8 lên màu theo trạng thái.
   Hộp nào bỏ tick **Online** thì không trả lời → ESDCMS cho Offline (xanh dương) và hiện dải cảnh báo vàng ngay dưới hàng ô hộp.
3. Bấm **W** ở một hộp đang dùng → ESDCMS dừng băng tải: `ControlBoxSim` nhận lần lượt Motor 1..3 = HIGH (STOP),
   dải dưới chuyển đỏ. Bấm **A** lại → Motor 1..3 = LOW (RUN).
4. Lúc ESDCMS mở cổng Control Box, `ControlBoxSim` thấy Motor 1..3 = LOW rồi xung Start All HIGH → LOW.
5. Bỏ tick **Reply with echo** (hoặc đặt *Echo delay* > 500 ms) → ESDCMS hết RX timeout, thử lại, log `not acknowledged`.
6. Bỏ tick **Boxes answer '?' queries** → giống hộp chạy firmware cũ: query không có trả lời.

Mỗi khung gửi/nhận đều hiện trong log của simulator dạng hex, kèm ý nghĩa.
