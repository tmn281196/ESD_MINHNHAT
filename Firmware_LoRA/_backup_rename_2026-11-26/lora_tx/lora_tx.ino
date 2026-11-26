#include <SPI.h>
#include <LoRa.h>

/*
 * ESD box node (Arduino + SX1278 433 MHz) - v2: answers status queries from the PC.
 *
 * Frame (both directions):
 *   [0]   [1]         [2]  [3]  [4 .. 4+N-1]  [4+N]  [4+N+1]
 *   0xAA  NetworkID   ID   LEN  DATA...       CRC8   0xBB
 *   CRC8 (poly 0x31, init 0x00) over NetworkID, ID, LEN, DATA.
 *
 * Box -> PC : ID = TX_ID of this box, DATA = 'A' / 'W' / 'I'.
 *             Sent at power-up, when the inputs change, and as the answer to a query.
 * PC  -> Box: DATA = '?' (status query). ID = TX_ID of one box, or 0xFF = all boxes.
 *             For 0xFF every box answers in its own time slot (TX_ID * SLOT_MS)
 *             so the answers do not collide.
 */

const int csPin = 10;
const int resetPin = 9;
const int irqPin = 2;

#define STX 0xAA
#define ETX 0xBB
#define NETWORK_ID 0x03
#define TX_ID 0x04            // ID of this box (1..20) - set for each box before flashing

#define CMD_QUERY      '?'
#define BROADCAST_ID   0xFF
#define REPLY_DELAY_MS 20     // direct query: give the gateway time to switch back to receive
#define SLOT_MS        60     // query to all boxes / power-up: answer at TX_ID * SLOT_MS

// On a status change the frame is sent once, then repeated CHANGE_RETRIES times (no ACK from the
// PC, so repeat to be sure). The gap doubles each time: 100, 200, 400, 800, 1600 ms.
// The small random part keeps two boxes that changed at the same moment from colliding on every repeat.
#define CHANGE_RETRIES     5
#define REPEAT_FIRST_GAP_MS 100
#define REPEAT_JITTER_MS   50

#define PIN_IN1 3
#define PIN_IN2 4
#define PIN_YELLOW 6
#define PIN_RED 7
#define PIN_GREEN 8
#define DEBOUNCE_MS 200

volatile bool txDone = false;

void onTxDoneISR() {
  txDone = true;
}

// Inputs last reported to the PC
bool lastIN1 = LOW;
bool lastIN2 = LOW;
// Inputs being debounced (must stay the same for DEBOUNCE_MS)
bool candIN1 = LOW;
bool candIN2 = LOW;
unsigned long candSince = 0;

uint8_t currentStatus = 'I';

// Pending answer to a status query
bool replyPending = false;
unsigned long replyAt = 0;

// Repeats already sent / still to send after a status change
uint8_t repeatsDone = 0;
uint8_t repeatsLeft = 0;
unsigned long nextRepeatAt = 0;

// Gap before the next repeat: REPEAT_FIRST_GAP_MS x 2^repeatsDone (+ jitter).
void scheduleRepeat() {
  unsigned long gap = (unsigned long)REPEAT_FIRST_GAP_MS << repeatsDone;
  nextRepeatAt = millis() + gap + (unsigned long)random(0, REPEAT_JITTER_MS + 1);
}

void softResetLoRa() {
  LoRa.end();
  delay(5);
  if (!LoRa.begin(433E6)) {
    Serial.println("[LoRa] soft reset FAIL");
  }
  LoRa.setTxPower(10);
}

uint8_t getStatus(bool in1, bool in2) {
  if (in1 && in2) return 'A';
  if (in1 && !in2) return 'W';
  return 'I';
}

void setLamps(uint8_t status) {
  digitalWrite(PIN_RED, status == 'W');
  digitalWrite(PIN_YELLOW, status == 'I');
  digitalWrite(PIN_GREEN, status == 'A');
}

// ---- CRC8 ----
uint8_t crc8(uint8_t *data, uint8_t len) {
  uint8_t crc = 0x00;

  for (uint8_t i = 0; i < len; i++) {
    crc ^= data[i];

    for (uint8_t j = 0; j < 8; j++) {
      if (crc & 0x80)
        crc = (crc << 1) ^ 0x31;
      else
        crc <<= 1;
    }
  }

  return crc;
}

void sendFrame(uint8_t data) {

  uint8_t crcBuf[4] = { NETWORK_ID, TX_ID, 0x01, data };
  uint8_t crc = crc8(crcBuf, 4);

  uint8_t frame[] = {
    STX, NETWORK_ID, TX_ID, 0x01, data, crc, ETX
  };

  // ---- Log frame 1 lần ----
  Serial.print("SEND => STATUS=");
  Serial.print((char)data);
  Serial.print(" CRC=0x");
  Serial.print(crc, HEX);
  Serial.print(" FRAME: ");
  for (uint8_t i = 0; i < sizeof(frame); i++) {
    if (frame[i] < 0x10) Serial.print('0');
    Serial.print(frame[i], HEX);
    Serial.print(' ');
  }
  Serial.println();

  const uint8_t  MAX_RETRY      = 5;
  const unsigned TX_TIMEOUT_MS  = 500;

  // The DIO0 interrupt is attached only while sending. Left attached, it would also fire on
  // "RxDone" (DIO0 mapping after a LoRa reset) and clear the flag before parsePacket() sees the query.
  LoRa.onTxDone(onTxDoneISR);

  for (uint8_t attempt = 1; attempt <= MAX_RETRY; attempt++) {

    if (LoRa.beginPacket() == 0) {
      Serial.print("  -> LoRa busy, attempt ");
      Serial.println(attempt);
      softResetLoRa();
      continue;                       // thử lại
    }

    for (uint8_t i = 0; i < sizeof(frame); i++) {
      LoRa.write(frame[i]);
    }

    txDone = false;
    LoRa.endPacket(true);

    // chờ TX_DONE
    unsigned long t0 = millis();
    while (!txDone) {
      if (millis() - t0 > TX_TIMEOUT_MS) {
        Serial.print("  -> TX TIMEOUT, attempt ");
        Serial.println(attempt);
        softResetLoRa();
        break;                        // ra khỏi while, sang vòng for kế
      }
    }

    if (txDone) {
      Serial.print("  -> TX OK (attempt ");
      Serial.print(attempt);
      Serial.println(")");
      LoRa.onTxDone(NULL);
      return;                         // gửi xong, thoát hàm
    }
    // không TX OK thì vòng for sẽ thử attempt kế tiếp
  }

  LoRa.onTxDone(NULL);
  Serial.println("  -> TX FAILED sau MAX_RETRY, bỏ qua");
}

// ---- Nhận lệnh hỏi trạng thái từ PC (qua gateway) ----
// parsePacket() also puts the radio back into receive mode after a transmit.
void pollLoRa() {
  int packetSize = LoRa.parsePacket();
  if (packetSize == 0) return;

  uint8_t buf[32];
  uint8_t len = 0;
  while (LoRa.available()) {
    int b = LoRa.read();
    if (len < sizeof(buf)) buf[len++] = (uint8_t)b;
  }
  if (packetSize > (int)sizeof(buf)) return;

  // STX + NetworkID + ID + LEN + CRC + ETX = 6 bytes + DATA
  if (len < 6)                                  return;
  if (buf[0] != STX || buf[len - 1] != ETX)     return;
  uint8_t dataLen = buf[3];
  if (len != dataLen + 6)                       return;
  if (buf[1] != NETWORK_ID)                     return;
  if (crc8(buf + 1, dataLen + 3) != buf[len - 2]) return;
  if (dataLen < 1 || buf[4] != CMD_QUERY)       return;   // e.g. status frame of another box

  uint8_t target = buf[2];
  if (target == TX_ID) {
    replyAt = millis() + REPLY_DELAY_MS;
  } else if (target == BROADCAST_ID) {
    replyAt = millis() + (unsigned long)TX_ID * SLOT_MS;
  } else {
    return;                                     // query for another box
  }
  replyPending = true;

  Serial.print("QUERY <= ");
  Serial.println(target == BROADCAST_ID ? "all boxes" : "this box");
}

// ---- Đọc IN1/IN2, chống dội không dùng delay() để vẫn nghe được LoRa ----
void pollInputs() {
  bool in1 = digitalRead(PIN_IN1);
  bool in2 = digitalRead(PIN_IN2);
  unsigned long now = millis();

  if (in1 != candIN1 || in2 != candIN2) {
    candIN1 = in1;
    candIN2 = in2;
    candSince = now;
    return;
  }
  if (now - candSince < DEBOUNCE_MS) return;
  if (candIN1 == lastIN1 && candIN2 == lastIN2) return;

  lastIN1 = candIN1;
  lastIN2 = candIN2;
  currentStatus = getStatus(lastIN1, lastIN2);

  Serial.print("IN1=");
  Serial.print(lastIN1);

  Serial.print(" IN2=");
  Serial.print(lastIN2);

  Serial.print(" => STATUS=");
  Serial.println((char)currentStatus);

  setLamps(currentStatus);
  sendFrame(currentStatus);

  // Repeat the new status; a newer change restarts the count (and the 100 ms gap) with the newer status.
  repeatsDone = 0;
  repeatsLeft = CHANGE_RETRIES;
  scheduleRepeat();
}

void setup() {

  Serial.begin(9600);

  // TX_ID in the seed: two boxes must never get the same random repeat times.
  randomSeed(((unsigned long)analogRead(A0) << 8) ^ TX_ID ^ micros());

  LoRa.setPins(csPin, resetPin, irqPin);

  if (!LoRa.begin(433E6)) {
    Serial.println("LoRa init failed!");
    while (1)
      ;
  }
  Serial.print("LoRa TX Ready, box ID ");
  Serial.println(TX_ID);

  pinMode(PIN_IN1, INPUT);
  pinMode(PIN_IN2, INPUT);

  pinMode(PIN_RED, OUTPUT);
  pinMode(PIN_YELLOW, OUTPUT);
  pinMode(PIN_GREEN, OUTPUT);

  lastIN1 = candIN1 = digitalRead(PIN_IN1);
  lastIN2 = candIN2 = digitalRead(PIN_IN2);
  candSince = millis();
  currentStatus = getStatus(lastIN1, lastIN2);
  setLamps(currentStatus);

  // After a power cut all boxes start together: send the first status in this box's slot.
  delay((unsigned long)TX_ID * SLOT_MS);
  sendFrame(currentStatus);
}

void loop() {
  pollLoRa();
  pollInputs();

  if (replyPending && (long)(millis() - replyAt) >= 0) {
    replyPending = false;
    Serial.print("REPLY => ");
    sendFrame(currentStatus);
  }

  if (repeatsLeft > 0 && (long)(millis() - nextRepeatAt) >= 0) {
    repeatsLeft--;
    repeatsDone++;
    Serial.print("RETRY (");
    Serial.print(repeatsDone);
    Serial.print("/");
    Serial.print(CHANGE_RETRIES);
    Serial.print(") => ");
    sendFrame(currentStatus);
    if (repeatsLeft > 0) scheduleRepeat();
  }
}
