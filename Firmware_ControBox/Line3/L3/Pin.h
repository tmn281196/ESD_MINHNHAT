// Line 3 Control Box - output pin mapping (Arduino Mega).
//
// !!! NOT VERIFIED: nobody recorded the Line 3 wiring yet (Moonsung-ESD 3_Doc\ControlBox_Wiring_EN.pdf, "No wiring
// map yet"). The box now runs a firmware the original developer flashed remotely, no copy kept. The pins below come
// from the OLD Line 3 firmware: Start = DIO 24, every Motor channel = DIO 22 (one relay stops the whole line).
// Before flashing: open the box, note which DIO pins go to the relay / remotes, fix the #defines, then set
// L3_WIRING_CHECKED to 1 (the compiler warns until then) and add the map to the wiring document.
//
// Channel (app)  Opcode  Pin  Wired to (old firmware, to check)
//   Motor 1        1     22   relay -> conveyor STOP (same pin for all motors)
//   Motor 2        2     22   same relay
//   Motor 3        3     22   same relay
//   Start All      5     24   START
// Opcode 4 (Motor 4) was removed, as on Line 1 / Line 2: a frame with it is not answered.
// With one pin for all motors, the last Motor command wins: ESDCMS always sets Motor 1..3 together, but a manual
// "Stop 2" RUN press while the others are STOP also lets the whole line run.
//
// state 1 -> pin HIGH (relay ON  -> motor STOP via NC contact)
// state 0 -> pin LOW  (relay OFF -> motor RUN)

#define L3_WIRING_CHECKED 0   // set to 1 once the pins below match the real wiring
#if !L3_WIRING_CHECKED
#warning "Line 3 pins come from the old firmware (Start 24, all motors 22): check the real wiring, then set L3_WIRING_CHECKED 1 in Pin.h"
#endif

#define SWON   24  // START (old firmware)
#define MOTOR1 22  // conveyor stop relay (old firmware: all motor channels)
#define MOTOR2 22
#define MOTOR3 22

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
