#include <SPI.h>
#include <LoRa.h>

// Define connection pins (matched with your wiring diagram)
const int ss = 5;
const int rst = 14;
const int dio0 = 2;

void setup() {
  Serial.begin(9600);
  while (!Serial); // Wait for Serial Monitor to open

  Serial.println("--- LoRa Receiver (ESP32) ---");

  // Set pins for the LoRa library
  LoRa.setPins(ss, rst, dio0);

  // Initialize LoRa at 433MHz
  if (!LoRa.begin(433E6)) {
    Serial.println("LoRa initialization failed! Check your wiring.");
    while (1);
  }

  // Additional settings (must match the transmitter side)
  // LoRa.setSpreadingFactor(7);
  // LoRa.setSignalBandwidth(125E3);

  Serial.println("Waiting for data...");
}

void loop() {
  // Check if a packet has been received
  int packetSize = LoRa.parsePacket();

  if (packetSize) {
    // Receive packet
    Serial.print("Received packet: '");

    // Read packet content
    while (LoRa.available()) {
      String LoRaData = LoRa.readString();
      Serial.print(LoRaData);
    }

    // Print signal strength (RSSI) to estimate range
    Serial.print("' with RSSI: ");
    Serial.println(LoRa.packetRssi());
  }
}