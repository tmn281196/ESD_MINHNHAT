// Line 2 Control Box - output pin mapping (Arduino Mega), matches the real wiring
// (see Moonsung-ESD 3_Doc\ControlBox_Wiring_EN.pdf).
// The same firmware runs on both Line 2 Control Boxes; each box only has some pins wired.
// Channel (app)  Opcode  Pin  PC1 Control Box (head/middle)    PC2 Control Box (end of line)
//   Motor 1        1     24   Remote 72-072 UP1 -> head        not connected
//   Motor 2        2     26   Remote 67-067 UP2 -> middle      not connected
//   Motor 3        3     23   not connected                    Remote 80-080 UP2 -> end
//   Start All      5     22   Relay -> SW ON                   Relay -> SW ON   (app: 500 ms HIGH then LOW)
// Opcode 4 (Motor 4) was removed: nothing is wired to it, and a frame with it is not answered.
//
// state 1 -> pin HIGH (relay ON  -> motor STOP via NC contact)
// state 0 -> pin LOW  (relay OFF -> motor RUN)

#define SWON   22  // Relay -> SW ON (PC1 and PC2)
#define MOTOR1 24  // head of line   (PC1)
#define MOTOR2 26  // middle of line (PC1)
#define MOTOR3 23  // end of line    (PC2)

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
