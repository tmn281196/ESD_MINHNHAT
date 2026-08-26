#include <SPI.h>
#include <LoRa.h>

// Define pins according to the wiring diagram above
const int csPin = 10;
const int resetPin = 9;
const int irqPin = 2;

void setup() {
  Serial.begin(9600);

  // Set pins for the LoRa library
  LoRa.setPins(csPin, resetPin, irqPin);

  if (!LoRa.begin(433E6)) { // 433MHz frequency according to Ra-02 specifications
    Serial.println("Error: LoRa module not detected!");
    while (1);
  }

  Serial.println("LoRa Transmitter ready!");
}

void loop() {
  Serial.print("Sending packet... ");

  LoRa.beginPacket();
  LoRa.print("Pro Mini 1");
  LoRa.endPacket();

  Serial.println("Done!");
  delay(500); // Send every 2 seconds
}