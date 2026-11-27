// Line 1 Control Box - output pin mapping (Arduino Mega), matches the real wiring
// (see Moonsung-ESD 3_Doc\ControlBox_Wiring_EN.pdf).
// Channel (app)  Opcode  Pin  Wired to
//   Motor 1        1     28   Remote 71-071 UP4 -> head-of-line motor
//   Motor 2        2     22   Remote 98-098 UP1 -> end-of-line motor
//   Motor 3        3     24   Remote 03-003 UP2 -> middle-of-line motor
//   Start All      5     30   Relay 24 VDC -> START (app: 500 ms HIGH then LOW)
// Opcode 4 (Motor 4) was removed: nothing is wired to it, and a frame with it is not answered.
//
// state 1 -> pin HIGH (relay ON  -> motor STOP via NC contact)
// state 0 -> pin LOW  (relay OFF -> motor RUN)

#define SWON 30    // Relay 24 VDC start
#define MOTOR1 28  // head of line   (Remote 71-071 UP4)
#define MOTOR2 22  // end of line    (Remote 98-098 UP1)
#define MOTOR3 24  // middle of line (Remote 03-003 UP2)

#define NO_PIN 0xFF

// Output pin of each opcode (index = opcode 0..5); NO_PIN = opcode not used.
static const uint8_t OPCODE_PINS[6] = { NO_PIN, MOTOR1, MOTOR2, MOTOR3, NO_PIN, SWON };

void SetSolenoidPinMode() {
  for (uint8_t op = 1; op <= 5; op++) {
    if (OPCODE_PINS[op] == NO_PIN) continue;
    pinMode(OPCODE_PINS[op], OUTPUT);
    digitalWrite(OPCODE_PINS[op], LOW);
  }
}

// Per-channel command from ESDCMS: opcode 1..3 or 5, state 0/1. False (no echo) for anything else.
bool SetChannel(uint8_t opcode, uint8_t state) {
  if (opcode > 5 || OPCODE_PINS[opcode] == NO_PIN || state > 1) return false;
  digitalWrite(OPCODE_PINS[opcode], state ? HIGH : LOW);
  return true;
}
