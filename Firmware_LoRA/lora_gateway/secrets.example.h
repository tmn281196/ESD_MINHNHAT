// Firebase settings for lora_gateway with ENABLE_CLOUD 1.
// Copy this file to secrets.h (same folder) and fill it in. secrets.h is in .gitignore: never commit it.
// WiFi SSID/password are NOT here: they are entered in the ESDCMS app (RF Gateway > WiFi) and sent
// to the gateway over USB, which keeps them in flash.
//
// Firebase console:
//   1. Build > Realtime Database: create the database, copy its URL below.
//   2. Build > Authentication > Sign-in method: enable Email/Password, add one user per gateway.
//   3. Realtime Database > Rules: paste firebase.rules.json, then in Data add
//        gateways/<UID of that user> = "03"   (the line = NETWORK_ID, 2 digits)
//      so this gateway can write only lines/03.
#pragma once

#define FIREBASE_DB_URL   "https://your-project-default-rtdb.asia-southeast1.firebasedatabase.app"  // no '/' at the end
#define FIREBASE_API_KEY  "your-web-api-key"            // Project settings > General > Web API key
#define FIREBASE_EMAIL    "gateway-line03@example.com"  // the gateway user from step 2
#define FIREBASE_PASSWORD "change-me"
