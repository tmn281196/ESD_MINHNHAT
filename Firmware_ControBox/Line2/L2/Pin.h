// Line2 / Conveyor2 - output pin mapping (Arduino Mega)
// Channel index (app)  Opcode  Pin      Meaning
//   0  Motor 1            1    MOTOR1   MI
//   1  Motor 2            2    MOTOR2   SOLDER
//   2  Motor 3            3    MOTOR3   PACKING
//   3  Motor 4            4    MOTOR4   FCT
//   4  Start All          5    SWON     START pulse (app: 500 ms HIGH then LOW)
//
// state 1 -> pin HIGH (relay ON  -> motor STOP via NC contact)
// state 0 -> pin LOW  (relay OFF -> motor RUN)

#define SWON   30

#define MOTOR1 28  // MI
#define MOTOR2 24  // SOLDER
#define MOTOR3 22  // PACKING
#define MOTOR4 22  // FCT

static const uint8_t CHANNEL_PINS[5] = { MOTOR1, MOTOR2, MOTOR3, MOTOR4, SWON };

void SetSolenoidPinMode() {
  for (uint8_t i = 0; i < 5; i++) {
    pinMode(CHANNEL_PINS[i], OUTPUT);
    digitalWrite(CHANNEL_PINS[i], LOW);
  }
}

// Per-channel command from ESDCMS: opcode 1..5, state 0/1
bool SetChannel(uint8_t opcode, uint8_t state) {
  if (opcode < 1 || opcode > 5 || state > 1) return false;
  digitalWrite(CHANNEL_PINS[opcode - 1], state ? HIGH : LOW);
  return true;
}

