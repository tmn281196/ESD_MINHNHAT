// Line1 / L1PC1 - control box firmware for ESDCMS (ESD Compliance Monitoring System)
//
// ---------- Frame from PC (ESDCMS.Device.ControlBox.GetFrame) ----------
//   0x44 0x45 LEN OPCODE STATE CS 0x56          (LEN = 3, total 7 bytes)
//     OPCODE : 1..4 = Motor 1..4, 5 = Start All (SWON)
//     STATE  : 0 = release (LOW), 1 = assert (HIGH)
//     CS     : XOR of all bytes before it (0x44 ^ 0x45 ^ LEN ^ OPCODE ^ STATE)
//   Bytes after LEN = LEN + 1  (payload, CS, 0x56)
//
// ---------- Response ----------
//   Echo the received frame back unchanged. The PC compares byte-for-byte
//   and retries up to 4 times (500 ms timeout each) if the echo is missing.
//
// Serial: 9600 8N1

#include "Pin.h"

#define PREFIX1     0x44
#define PREFIX2     0x45
#define SUFFIX      0x56
#define MAX_FRAME   20
#define FRAME_TIMEOUT_MS 100

enum RxState : uint8_t { WAIT_P1, WAIT_P2, WAIT_LEN, WAIT_BODY };

static RxState  rxState = WAIT_P1;
static uint8_t  frame[MAX_FRAME];
static uint8_t  frameLen  = 0;   // bytes stored so far
static uint8_t  frameNeed = 0;   // total bytes expected (3 + LEN + 1)
static uint32_t lastByteMs = 0;

void handleByte(uint8_t b);
bool checksumOk();
void processFrame();

void setup() {
  Serial.begin(9600);
  SetSolenoidPinMode();
}

void loop() {
  // Drop a half-received frame if the sender went silent
  if (rxState != WAIT_P1 && (millis() - lastByteMs) > FRAME_TIMEOUT_MS) {
    rxState = WAIT_P1;
  }
  while (Serial.available()) {
    handleByte((uint8_t)Serial.read());
    lastByteMs = millis();
  }
}

void handleByte(uint8_t b) {
  switch (rxState) {
    case WAIT_P1:
      if (b == PREFIX1) { frame[0] = b; frameLen = 1; rxState = WAIT_P2; }
      break;

    case WAIT_P2:
      if (b == PREFIX2) { frame[1] = b; frameLen = 2; rxState = WAIT_LEN; }
      else               { rxState = (b == PREFIX1) ? WAIT_P2 : WAIT_P1; }
      break;

    case WAIT_LEN:
      frameNeed = 3 + b + 1;              // header(3) + LEN payload-ish + suffix
      if (frameNeed > MAX_FRAME) { rxState = WAIT_P1; break; }
      frame[2] = b; frameLen = 3; rxState = WAIT_BODY;
      break;

    case WAIT_BODY:
      frame[frameLen++] = b;
      if (frameLen >= frameNeed) {
        processFrame();
        rxState = WAIT_P1;
      }
      break;
  }
}

bool checksumOk() {
  // CS is the byte right before the suffix; XOR over everything before CS
  uint8_t cs = 0;
  for (uint8_t i = 0; i < frameNeed - 2; i++) cs ^= frame[i];
  return cs == frame[frameNeed - 2] && frame[frameNeed - 1] == SUFFIX;
}

void processFrame() {
  if (!checksumOk()) return;             // bad frame: no echo -> PC retries

  if (frame[2] != 3) return;             // only OPCODE+STATE frames are valid

  if (SetChannel(frame[3], frame[4])) {
    Serial.write(frame, frameNeed);       // exact echo = ACK
    Serial.flush();
  }
}
