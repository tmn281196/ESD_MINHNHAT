// Firebase + WiFi settings for lora_gateway with ENABLE_CLOUD 1.
// Copy this file to secrets.h (same folder) and fill it in. secrets.h is in .gitignore: never commit it,
// and never put real values in this example file (it is in git).
//
// Firebase console (console.firebase.google.com):
//   1. Databases & Storage > Realtime Database: create the database, copy its URL below.
//   2. Security > Authentication > Sign-in method: enable Email/Password; Users: add one user per gateway.
//   3. Settings > General > Your apps: add a Web app (</>), copy apiKey from its config below.
//   4. Realtime Database > Rules: paste firebase.rules.json, Publish; then in Data add
//        gateways/<UID of that user> = 03     (the line = NETWORK_ID, 2 digits; in the console pick the type
//                                              "string" and type 03 WITHOUT quotes, or quotes become part of it)
//      so this gateway can write only lines/03.
// The gateway joins WIFI_SSID by itself at power-up; the PC app only turns its WiFi on/off.
#pragma once

#define FIREBASE_DB_URL   "https://your-project-default-rtdb.asia-southeast1.firebasedatabase.app"  // no '/' at the end
#define FIREBASE_API_KEY  "your-web-api-key"
#define FIREBASE_EMAIL    "gateway-line03@example.com"
#define FIREBASE_PASSWORD "change-me"

#define WIFI_SSID         "your-wifi-name"      // 2.4 GHz, WPA2 password (no login web page)
#define WIFI_PASSWORD     "your-wifi-password"
