#include <SPI.h>
#include <LoRa.h>

/*
 * RF gateway (ESP32 + SX1278 433 MHz) - v2: also forwards PC commands to the boxes.
 * (Was "lora_rx": it both receives box frames and transmits PC commands, so the name
 *  now describes its role, not one direction.)
 *
 *   LoRa  -> Serial : valid frames from the boxes are forwarded to the PC (as before).
 *   Serial -> LoRa  : valid frames from the PC (e.g. status query '?') are sent over LoRa.
 *
 * [0]    [1]         [2]    [3]    [4 .. 4+N-1]  [4+N]  [4+N+1]
 * 0xAA   NetworkID   ID     LEN    DATA...        CRC8   0xBB
 *
 * A box out of direct range is reached through a repeater node (lora_node with
 * ENABLE_REPEATER = 1): it re-sends every valid frame once, so the gateway need not do
 * anything special - a relayed frame carries an extra hop-marker byte at the end of DATA,
 * but the gateway reads only DATA as a block and re-checks CRC over the declared LEN, so
 * it forwards a relayed frame exactly like a direct one.
 */

const int ss   = 5;
const int rst  = 14;
const int dio0 = 2;

#define STX        0xAA
#define ETX        0xBB
#define NETWORK_ID 0x03  // chỉnh cho đúng network

#define PC_FRAME_MAX     64
#define PC_BYTE_GAP_MS   50   // a frame from the PC arrives in one burst; drop half frames older than this

// ---- CRC8 ----
uint8_t crc8(uint8_t *data, uint8_t len) {
  uint8_t crc = 0x00;
  for (uint8_t i = 0; i < len; i++) {
    crc ^= data[i];
    for (uint8_t j = 0; j < 8; j++) {
      if (crc & 0x80) crc = (crc << 1) ^ 0x31;
      else            crc <<= 1;
    }
  }
  return crc;
}

// ---- Forward frame lên PC ----
void forwardFrame(uint8_t networkID, uint8_t txID, uint8_t *data, uint8_t dataLen) {
  uint8_t crcBuf[258];
  crcBuf[0] = networkID;
  crcBuf[1] = txID;
  crcBuf[2] = dataLen;
  memcpy(crcBuf + 3, data, dataLen);
  uint8_t crc = crc8(crcBuf, dataLen + 3);

  Serial.write(STX);
  Serial.write(networkID);
  Serial.write(txID);
  Serial.write(dataLen);
  Serial.write(data, dataLen);
  Serial.write(crc);
  Serial.write(ETX);
}

// ---- Nhận frame từ PC, phát ra LoRa ----
uint8_t pcBuf[PC_FRAME_MAX];
uint8_t pcLen = 0;
unsigned long pcLastByte = 0;

void sendPcFrameOverLoRa(uint8_t *f, uint8_t len) {
  if (f[len - 1] != ETX)                    return;
  if (f[1] != NETWORK_ID)                   return;
  if (crc8(f + 1, f[3] + 3) != f[len - 2])  return;

  LoRa.beginPacket();
  LoRa.write(f, len);
  LoRa.endPacket();   // blocking until sent; parsePacket() in loop() puts the radio back into receive
}

void pollSerialFromPC() {
  while (Serial.available()) {
    uint8_t b = (uint8_t)Serial.read();
    unsigned long now = millis();
    if (pcLen > 0 && now - pcLastByte > PC_BYTE_GAP_MS) {
      pcLen = 0;                            // stale half frame
    }
    pcLastByte = now;

    if (pcLen == 0 && b != STX) continue;   // wait for start of frame
    pcBuf[pcLen++] = b;

    if (pcLen >= 4) {
      uint16_t total = (uint16_t)pcBuf[3] + 6;
      if (total > PC_FRAME_MAX) {
        pcLen = 0;                          // invalid length
        continue;
      }
      if (pcLen == total) {
        sendPcFrameOverLoRa(pcBuf, pcLen);
        pcLen = 0;
      }
    }
  }
}

void setup() {
  Serial.begin(9600);
  while (!Serial);

  LoRa.setPins(ss, rst, dio0);
  if (!LoRa.begin(433E6)) {
    while (1);
  }
}

// ---- Loop ----
void loop() {
  pollSerialFromPC();

  int packetSize = LoRa.parsePacket();
  if (packetSize == 0) return;

  uint8_t buf[255];
  uint8_t len = 0;
  while (LoRa.available() && len < sizeof(buf)) {
    buf[len++] = LoRa.read();
  }

  // Tối thiểu: STX + NetworkID + TxID + LEN + CRC + ETX = 6 bytes
  if (len < 6)                    return;
  if (buf[0] != STX)              return;
  if (buf[len - 1] != ETX)        return;

  uint8_t networkID = buf[1];
  uint8_t txID      = buf[2];
  uint8_t dataLen   = buf[3];

  // Tổng: 6 bytes cố định + dataLen
  if (len != dataLen + 6)         return;

  // Lọc đúng network
  if (networkID != NETWORK_ID)    return;

  // CRC tính trên: NetworkID + TxID + LEN + DATA
  uint8_t crcRx   = buf[len - 2];
  uint8_t crcCalc = crc8(buf + 1, dataLen + 3);
  if (crcRx != crcCalc)           return;

  forwardFrame(networkID, txID, buf + 4, dataLen);
}
