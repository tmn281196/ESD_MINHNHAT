#include <SPI.h>
#include <LoRa.h>

/*
 * RF gateway (ESP32 + SX1278 433 MHz) - v2: also forwards PC commands to the boxes.
 * (Was "lora_rx": it both receives box frames and transmits PC commands, so the name
 *  now describes its role, not one direction.)
 *
 *   LoRa  -> Serial : valid frames from the boxes are forwarded to the PC (as before).
 *   Serial -> LoRa  : valid frames from the PC (e.g. status query '?') are sent over LoRa.
 *
 * [0]    [1]         [2]    [3]    [4 .. 4+N-1]  [4+N]  [4+N+1]
 * 0xAA   NetworkID   ID     LEN    DATA...        CRC8   0xBB
 *
 * A box out of direct range is reached through a repeater node (lora_node with
 * ENABLE_REPEATER = 1): it re-sends every valid frame once, so the gateway need not do
 * anything special - a relayed frame carries an extra hop-marker byte at the end of DATA,
 * but the gateway reads only DATA as a block and re-checks CRC over the declared LEN, so
 * it forwards a relayed frame exactly like a direct one.
 *
 * ---------------------------------------------------------------------------------------
 * CLOUD option (set ENABLE_CLOUD = 1; default 0 = the gateway works exactly as before):
 *   The gateway also WRITES the box states to a Firebase Realtime Database over WiFi, so the
 *   line can be watched from outside the factory. Nothing on the line depends on it: the cloud
 *   part runs in its own task on the other CPU core and never makes the LoRa/USB loop wait.
 *   When WiFi or the internet is down it keeps the latest state of every box (and up to
 *   EVENT_BUFFER status changes) and catches up when the connection is back.
 *
 *   WiFi SSID/password come from the PC app (ESDCMS, RF Gateway > WiFi) over USB and are kept
 *   in flash (NVS), so they survive a power cut. Firebase settings come from secrets.h (copy
 *   secrets.example.h). The USB port is the PC protocol link: the cloud part never prints to it.
 *
 *   Config frames (USB only, never sent over LoRa). They start with 0xA5 instead of 0xAA, so a
 *   gateway built without the option and an older PC app both ignore them:
 *     [0]   [1]   [2]  [3 .. 3+N-1]  [3+N]  [3+N+1]
 *     0xA5  TYPE  LEN  DATA...       CRC8   0xBB          CRC8 (poly 0x31) over TYPE, LEN, DATA
 *     PC -> gateway  'W'  DATA = SSID, 0x00, password      set WiFi (saved only if it changed)
 *                    'W'  no DATA                           WiFi off (forget the SSID, radio off)
 *                    'S'  no DATA                           ask for the cloud status
 *     gateway -> PC  'w'  DATA = 0 saved / 1 unchanged / 2 bad format
 *                    's'  DATA = state, WiFi RSSI (-dBm, 0..127), seconds since the last good
 *                         write in two 7-bit bytes (low, high; 16383 = never)
 *                         state: 0 no WiFi set, 1 connecting to WiFi,
 *                                2 WiFi OK but cloud not reached yet / last write failed, 3 cloud OK
 *     (Every DATA byte of a reply is < 0x80, so a reply can never look like the start of an
 *      0xAA status frame to the PC.)
 *
 *   Database (one line = one NETWORK_ID, written as 2 digits, e.g. "03"):
 *     lines/03/gateway       lastSeen (server time), uptimeS, wifiRssi, fw, pcLastFrame, droppedEvents
 *     lines/03/boxes/09      s (A/W/I), t (status since), seen (last heard), rssi, snr, via
 *     lines/03/events/<t>_<n>  { box, s, t }   one per status change
 *   Times are Unix epoch in ms. A viewer can treat a box as silent when "seen" is old while
 *   the PC app is running (pcLastFrame recent): the PC asks every box every AskAgainSec (120 s).
 * ---------------------------------------------------------------------------------------
 */

const int ss   = 5;
const int rst  = 14;
const int dio0 = 2;

#define STX        0xAA
#define ETX        0xBB
#define NETWORK_ID 0x03  // chỉnh cho đúng network

#define PC_FRAME_MAX     64
#define PC_BYTE_GAP_MS   50   // a frame from the PC arrives in one burst; drop half frames older than this

// ---- Cloud option (default OFF = gateway exactly as before). Set 1 and add secrets.h to use it. ----
#define ENABLE_CLOUD 0

#if ENABLE_CLOUD
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <time.h>
#include <sys/time.h>
#include "gts_root_ca.h"
#if __has_include("secrets.h")
#include "secrets.h"
#else
#error "ENABLE_CLOUD 1 needs secrets.h: copy secrets.example.h to secrets.h and fill in the Firebase settings"
#endif

#define CFG_STX               0xA5
#define CFG_FRAME_MAX         128    // A5 TYPE LEN DATA(max 123) CRC BB
#define HOP_MARKER            0x52   // last DATA byte of a frame relayed by a repeater node
#define MAX_BOX               20
#define UPLOAD_INTERVAL_S     60     // heartbeat + last-heard / RSSI updates
#define UPLOAD_GATHER_MS      1000   // after a status change wait this long, to send changes together
#define EVENT_BUFFER          200    // status changes kept while offline (the oldest is dropped when full)
#define MAX_EVENTS_PER_WRITE  40
#define WIFI_RETRY_MS         30000
#define TOKEN_MAX_AGE_MS      (50UL * 60UL * 1000UL)   // a Firebase ID token lasts 60 min
#define GATEWAY_FW            "2.1-cloud"
#define PC_BUF_LEN            CFG_FRAME_MAX
#else
#define PC_BUF_LEN            PC_FRAME_MAX
#endif

// ---- CRC8 ----
uint8_t crc8(uint8_t *data, uint8_t len) {
  uint8_t crc = 0x00;
  for (uint8_t i = 0; i < len; i++) {
    crc ^= data[i];
    for (uint8_t j = 0; j < 8; j++) {
      if (crc & 0x80) crc = (crc << 1) ^ 0x31;
      else            crc <<= 1;
    }
  }
  return crc;
}

// ---- Forward frame lên PC ----
void forwardFrame(uint8_t networkID, uint8_t txID, uint8_t *data, uint8_t dataLen) {
  uint8_t crcBuf[258];
  crcBuf[0] = networkID;
  crcBuf[1] = txID;
  crcBuf[2] = dataLen;
  memcpy(crcBuf + 3, data, dataLen);
  uint8_t crc = crc8(crcBuf, dataLen + 3);

  Serial.write(STX);
  Serial.write(networkID);
  Serial.write(txID);
  Serial.write(dataLen);
  Serial.write(data, dataLen);
  Serial.write(crc);
  Serial.write(ETX);
}

#if ENABLE_CLOUD
// =============================== Cloud option ===============================

enum CloudState : uint8_t { CS_NO_WIFI = 0, CS_CONNECTING = 1, CS_WIFI = 2, CS_OK = 3 };

struct CloudBox {
  char status;        // 'A' / 'W' / 'I', 0 = not heard yet
  uint32_t sinceMs;   // millis() when the status last changed
  uint32_t seenMs;    // millis() of the last frame from it
  int16_t rssi;
  float snr;
  bool viaRepeater;
  bool dirty;         // changed since the last good write
  uint32_t ver;       // bumped on every change: "dirty" is cleared only if nothing changed during a write
};

struct CloudEvent {
  uint32_t seq;
  uint32_t ms;
  uint8_t box;
  char status;
};

// Shared between loop() (core 1) and the cloud task (core 0): always under cLock.
CloudBox cBoxes[MAX_BOX + 1];
CloudEvent cEvents[EVENT_BUFFER];
uint16_t cEvHead = 0, cEvCount = 0;
uint32_t cEvSeq = 0, cDropped = 0;
uint32_t cPcMs = 0;                 // millis() of the last valid frame from the PC (0 = none yet)
bool cUploadSoon = false;           // a status changed: write without waiting for the interval
uint32_t cSoonSinceMs = 0;
String cWifiSsid, cWifiPass;        // from the PC, kept in NVS
bool cWifiChanged = false;
SemaphoreHandle_t cLock;
Preferences cPrefs;

// Written by the cloud task, read by loop() for the status frame.
volatile uint8_t cState = CS_NO_WIFI;
volatile int8_t cWifiRssi = 0;
volatile uint32_t cLastOkMs = 0;
volatile bool cEverOk = false;

// Used by the cloud task only.
String cIdToken;
uint32_t cTokenMs = 0;

static inline void cloudLock()   { xSemaphoreTake(cLock, portMAX_DELAY); }
static inline void cloudUnlock() { xSemaphoreGive(cLock); }

// A valid frame heard on LoRa (loop, core 1). Only box status frames are kept.
void cloudOnLoRaFrame(uint8_t id, const uint8_t *data, uint8_t dataLen, int rssi, float snr) {
  if (id < 1 || id > MAX_BOX || dataLen < 1) return;
  char s = (char)data[0];
  if (s != 'A' && s != 'W' && s != 'I') return;          // e.g. a query relayed by a repeater
  bool relayed = dataLen >= 2 && data[dataLen - 1] == HOP_MARKER;
  uint32_t now = millis();
  cloudLock();
  CloudBox &b = cBoxes[id];
  if (b.status != s) {
    b.status = s;
    b.sinceMs = now;
    if (cEvCount == EVENT_BUFFER) {                       // full: drop the oldest change
      cEvHead = (cEvHead + 1) % EVENT_BUFFER;
      cEvCount--;
      cDropped++;
    }
    CloudEvent &e = cEvents[(cEvHead + cEvCount) % EVENT_BUFFER];
    e.seq = ++cEvSeq;
    e.ms = now;
    e.box = id;
    e.status = s;
    cEvCount++;
    if (!cUploadSoon) {
      cUploadSoon = true;
      cSoonSinceMs = now;
    }
  }
  b.seenMs = now;
  b.rssi = (int16_t)rssi;
  b.snr = snr;
  b.viaRepeater = relayed;
  b.dirty = true;
  b.ver++;
  cloudUnlock();
}

// A valid frame from the PC (loop, core 1): the PC app is running.
void cloudOnPcFrame() {
  uint32_t now = millis();
  cloudLock();
  cPcMs = now;
  cloudUnlock();
}

// Reply to the PC over USB (loop only, so it never mixes with forwardFrame()).
void sendCfgFrame(uint8_t type, const uint8_t *data, uint8_t len) {
  uint8_t crcBuf[2 + 8];
  if (len > 8) return;
  crcBuf[0] = type;
  crcBuf[1] = len;
  memcpy(crcBuf + 2, data, len);
  Serial.write(CFG_STX);
  Serial.write(type);
  Serial.write(len);
  Serial.write(data, len);
  Serial.write(crc8(crcBuf, len + 2));
  Serial.write(ETX);
}

void sendCloudStatus() {
  uint8_t rssiMag = cWifiRssi < 0 ? (uint8_t)(-cWifiRssi) : 0;
  if (rssiMag > 127) rssiMag = 127;
  uint32_t age = 16383;                                   // never
  if (cEverOk) {
    age = (millis() - cLastOkMs) / 1000;
    if (age > 16382) age = 16382;
  }
  uint8_t d[4] = { (uint8_t)cState, rssiMag, (uint8_t)(age & 0x7F), (uint8_t)((age >> 7) & 0x7F) };
  sendCfgFrame('s', d, 4);
}

// A complete 0xA5 frame from the PC.
void handleCfgFrame(uint8_t *f, uint8_t len) {
  if (f[len - 1] != ETX) return;
  uint8_t type = f[1];
  uint8_t dlen = f[2];
  if (crc8(f + 1, dlen + 2) != f[len - 2]) return;
  const uint8_t *d = f + 3;

  if (type == 'S') {
    sendCloudStatus();
    return;
  }
  if (type != 'W') return;

  if (dlen == 0) {                                        // WiFi off (unticked in the PC app)
    cloudLock();
    bool changed = cWifiSsid.length() > 0 || cWifiPass.length() > 0;
    if (changed) {
      cWifiSsid = String();
      cWifiPass = String();
      cWifiChanged = true;
    }
    cloudUnlock();
    if (changed) {
      cPrefs.remove("ssid");
      cPrefs.remove("pass");
    }
    uint8_t off = changed ? 0 : 1;
    sendCfgFrame('w', &off, 1);
    return;
  }

  // DATA = SSID 0x00 password
  int sep = -1;
  for (int i = 0; i < dlen; i++) {
    if (d[i] == 0) { sep = i; break; }
  }
  int passLen = dlen - sep - 1;
  uint8_t result = 2;                                     // bad format
  if (sep >= 1 && sep <= 32 && passLen >= 0 && passLen <= 63) {
    char ssid[33];
    char pass[64];
    memcpy(ssid, d, sep);
    ssid[sep] = 0;
    memcpy(pass, d + sep + 1, passLen);
    pass[passLen] = 0;
    cloudLock();
    bool changed = cWifiSsid != ssid || cWifiPass != pass;
    if (changed) {
      cWifiSsid = ssid;
      cWifiPass = pass;
      cWifiChanged = true;
    }
    cloudUnlock();
    if (changed) {                                        // flash is written only on a real change
      cPrefs.putString("ssid", ssid);
      cPrefs.putString("pass", pass);
    }
    result = changed ? 0 : 1;
  }
  sendCfgFrame('w', &result, 1);
}

String u64str(uint64_t v) {
  char b[21];
  snprintf(b, sizeof b, "%llu", (unsigned long long)v);
  return String(b);
}

uint64_t epochMsNow() {
  struct timeval tv;
  gettimeofday(&tv, nullptr);
  return (uint64_t)tv.tv_sec * 1000ULL + (uint64_t)(tv.tv_usec / 1000);
}

bool timeValid() {
  return time(nullptr) > 1700000000;                      // NTP has set the clock
}

String jsonEscape(const char *s) {
  String o;
  for (; *s; s++) {
    char c = *s;
    if (c == '"' || c == '\\') {
      o += '\\';
      o += c;
    } else if ((uint8_t)c < 0x20) {
      char b[7];
      snprintf(b, sizeof b, "\\u%04x", (unsigned)c);
      o += b;
    } else {
      o += c;
    }
  }
  return o;
}

// Value of "key":"..." in a flat JSON answer.
String jsonField(const String &body, const char *key) {
  String k = String("\"") + key + "\"";
  int i = body.indexOf(k);
  if (i < 0) return String();
  i = body.indexOf(':', i + k.length());
  if (i < 0) return String();
  i = body.indexOf('"', i);
  if (i < 0) return String();
  int j = body.indexOf('"', i + 1);
  if (j < 0) return String();
  return body.substring(i + 1, j);
}

// Firebase Auth (email/password) -> ID token for the database (?auth=...).
bool cloudSignIn() {
  WiFiClientSecure client;
  client.setCACert(GTS_ROOT_CA);
  HTTPClient http;
  http.setConnectTimeout(10000);
  http.setTimeout(15000);
  String url = String("https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=") + FIREBASE_API_KEY;
  if (!http.begin(client, url)) return false;
  http.addHeader("Content-Type", "application/json");
  String body = String("{\"email\":\"") + jsonEscape(FIREBASE_EMAIL) + "\",\"password\":\"" +
                jsonEscape(FIREBASE_PASSWORD) + "\",\"returnSecureToken\":true}";
  int code = http.POST(body);
  String resp = code > 0 ? http.getString() : String();
  http.end();
  if (code != 200) return false;
  String token = jsonField(resp, "idToken");
  if (token.length() == 0) return false;
  cIdToken = token;
  cTokenMs = millis();
  return true;
}

// One PATCH to lines/<NN>.json with everything that changed (multi-path update). True if written.
bool cloudWrite() {
  static CloudBox snap[MAX_BOX + 1];
  static CloudEvent evs[MAX_EVENTS_PER_WRITE];
  uint16_t nEv;
  uint32_t pcMs, dropped;

  cloudLock();
  memcpy(snap, cBoxes, sizeof(cBoxes));
  nEv = cEvCount < MAX_EVENTS_PER_WRITE ? cEvCount : MAX_EVENTS_PER_WRITE;
  for (uint16_t i = 0; i < nEv; i++) evs[i] = cEvents[(cEvHead + i) % EVENT_BUFFER];
  pcMs = cPcMs;
  dropped = cDropped;
  cUploadSoon = false;
  cloudUnlock();

  uint32_t nowMs = millis();
  uint64_t nowEpoch = epochMsNow();
  auto epochOf = [&](uint32_t ms) -> uint64_t { return nowEpoch - (uint64_t)(uint32_t)(nowMs - ms); };

  String b;
  b.reserve(4096);
  b = "{\"gateway/lastSeen\":{\".sv\":\"timestamp\"}";
  b += ",\"gateway/uptimeS\":";
  b += String(nowMs / 1000);
  b += ",\"gateway/wifiRssi\":";
  b += String((int)cWifiRssi);
  b += ",\"gateway/fw\":\"" GATEWAY_FW "\"";
  b += ",\"gateway/droppedEvents\":";
  b += String(dropped);
  if (pcMs) {
    b += ",\"gateway/pcLastFrame\":";
    b += u64str(epochOf(pcMs));
  }
  for (int id = 1; id <= MAX_BOX; id++) {
    const CloudBox &x = snap[id];
    if (!x.dirty || !x.status) continue;
    char p[16];
    snprintf(p, sizeof p, ",\"boxes/%02d/", id);
    b += p; b += "s\":\""; b += x.status; b += "\"";
    b += p; b += "t\":"; b += u64str(epochOf(x.sinceMs));
    b += p; b += "seen\":"; b += u64str(epochOf(x.seenMs));
    b += p; b += "rssi\":"; b += String((int)x.rssi);
    b += p; b += "snr\":"; b += String(x.snr, 1);
    b += p; b += "via\":\""; b += x.viaRepeater ? "repeater" : "direct"; b += "\"";
  }
  for (uint16_t i = 0; i < nEv; i++) {
    char k[96];
    uint64_t t = epochOf(evs[i].ms);
    snprintf(k, sizeof k, ",\"events/%013llu_%06lu\":{\"box\":%u,\"s\":\"%c\",\"t\":%llu}",
             (unsigned long long)t, (unsigned long)(evs[i].seq % 1000000UL),
             (unsigned)evs[i].box, evs[i].status, (unsigned long long)t);
    b += k;
  }
  b += "}";

  char path[24];
  snprintf(path, sizeof path, "/lines/%02u.json?auth=", (unsigned)NETWORK_ID);
  String url = String(FIREBASE_DB_URL) + path + cIdToken;

  WiFiClientSecure client;
  client.setCACert(GTS_ROOT_CA);
  HTTPClient http;
  http.setConnectTimeout(10000);
  http.setTimeout(15000);
  int code = -1;
  if (http.begin(client, url)) {
    http.addHeader("Content-Type", "application/json");
    code = http.PATCH(b);
    http.end();
  }
  if (code == 401 || code == 403) cIdToken = String();   // token expired or refused: sign in again

  cloudLock();
  if (code == 200) {
    for (int id = 1; id <= MAX_BOX; id++) {
      if (snap[id].dirty && cBoxes[id].ver == snap[id].ver) cBoxes[id].dirty = false;
    }
    if (nEv > 0) {
      uint32_t lastSeq = evs[nEv - 1].seq;
      while (cEvCount > 0 && (int32_t)(cEvents[cEvHead].seq - lastSeq) <= 0) {
        cEvHead = (cEvHead + 1) % EVENT_BUFFER;
        cEvCount--;
      }
    }
    if (cEvCount > 0 && !cUploadSoon) {                   // more changes waiting: write again at once
      cUploadSoon = true;
      cSoonSinceMs = 0;
    }
  } else if (!cUploadSoon) {                              // retry (after the back-off)
    cUploadSoon = true;
    cSoonSinceMs = 0;
  }
  cloudUnlock();
  return code == 200;
}

// Cloud task (core 0): WiFi, clock, sign-in, writes. Never blocks loop().
void cloudTask(void *) {
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  String ssid, pass;
  uint32_t lastWifiTry = 0, lastWrite = 0, lastTry = 0, backoffMs = 0;
  bool wroteOnce = false, sntpStarted = false;

  for (;;) {
    vTaskDelay(pdMS_TO_TICKS(200));

    cloudLock();
    bool changed = cWifiChanged;
    if (changed) {
      ssid = cWifiSsid;
      pass = cWifiPass;
      cWifiChanged = false;
    }
    bool soon = cUploadSoon;
    uint32_t soonSince = cSoonSinceMs;
    cloudUnlock();

    if (changed) {
      WiFi.disconnect(ssid.length() == 0);                // no SSID = WiFi off: radio off too
      lastWifiTry = 0;
    }
    if (ssid.length() == 0) {
      cState = CS_NO_WIFI;
      cWifiRssi = 0;
      continue;
    }
    if (WiFi.status() != WL_CONNECTED) {
      cState = CS_CONNECTING;
      if (lastWifiTry == 0 || millis() - lastWifiTry >= WIFI_RETRY_MS) {
        lastWifiTry = millis();
        WiFi.mode(WIFI_STA);                              // back on after a "WiFi off"
        WiFi.disconnect();
        WiFi.begin(ssid.c_str(), pass.c_str());
      }
      continue;
    }
    cWifiRssi = (int8_t)WiFi.RSSI();
    if (cState < CS_WIFI) cState = CS_WIFI;
    if (!sntpStarted) {
      configTime(0, 0, "pool.ntp.org", "time.google.com");
      sntpStarted = true;
    }
    if (!timeValid()) continue;                           // HTTPS needs the clock for the certificate dates

    uint32_t now = millis();
    bool due = !wroteOnce || now - lastWrite >= UPLOAD_INTERVAL_S * 1000UL ||
               (soon && now - soonSince >= UPLOAD_GATHER_MS);
    if (!due) continue;
    if (backoffMs && now - lastTry < backoffMs) continue;
    lastTry = now;

    bool ok = (cIdToken.length() > 0 && now - cTokenMs < TOKEN_MAX_AGE_MS) || cloudSignIn();
    if (ok) ok = cloudWrite();
    if (ok) {
      wroteOnce = true;
      lastWrite = millis();
      backoffMs = 0;
      cLastOkMs = lastWrite;
      cEverOk = true;
      cState = CS_OK;
    } else {
      backoffMs = backoffMs == 0 ? 5000 : (backoffMs >= 150000 ? 300000 : backoffMs * 2);
      cState = CS_WIFI;
    }
  }
}
// ============================= end Cloud option =============================
#endif

// ---- Nhận frame từ PC, phát ra LoRa ----
uint8_t pcBuf[PC_BUF_LEN];
uint8_t pcLen = 0;
unsigned long pcLastByte = 0;

void sendPcFrameOverLoRa(uint8_t *f, uint8_t len) {
  if (f[len - 1] != ETX)                    return;
  if (f[1] != NETWORK_ID)                   return;
  if (crc8(f + 1, f[3] + 3) != f[len - 2])  return;

#if ENABLE_CLOUD
  cloudOnPcFrame();
#endif
  LoRa.beginPacket();
  LoRa.write(f, len);
  LoRa.endPacket();   // blocking until sent; parsePacket() in loop() puts the radio back into receive
}

void pollSerialFromPC() {
  while (Serial.available()) {
    uint8_t b = (uint8_t)Serial.read();
    unsigned long now = millis();
    if (pcLen > 0 && now - pcLastByte > PC_BYTE_GAP_MS) {
      pcLen = 0;                            // stale half frame
    }
    pcLastByte = now;

#if ENABLE_CLOUD
    if (pcLen == 0 && b != STX && b != CFG_STX) continue;   // wait for start of frame
    pcBuf[pcLen++] = b;

    if (pcBuf[0] == CFG_STX) {              // config frame from the PC (never sent over LoRa)
      if (pcLen >= 3) {
        uint16_t total = (uint16_t)pcBuf[2] + 5;
        if (total > CFG_FRAME_MAX) {
          pcLen = 0;                        // invalid length
          continue;
        }
        if (pcLen == total) {
          handleCfgFrame(pcBuf, pcLen);
          pcLen = 0;
        }
      }
      continue;
    }
#else
    if (pcLen == 0 && b != STX) continue;   // wait for start of frame
    pcBuf[pcLen++] = b;
#endif

    if (pcLen >= 4) {
      uint16_t total = (uint16_t)pcBuf[3] + 6;
      if (total > PC_FRAME_MAX) {
        pcLen = 0;                          // invalid length
        continue;
      }
      if (pcLen == total) {
        sendPcFrameOverLoRa(pcBuf, pcLen);
        pcLen = 0;
      }
    }
  }
}

void setup() {
  Serial.begin(9600);
  while (!Serial);

  LoRa.setPins(ss, rst, dio0);
  if (!LoRa.begin(433E6)) {
    while (1);
  }

#if ENABLE_CLOUD
  cLock = xSemaphoreCreateMutex();
  cPrefs.begin("cloud", false);
  if (cPrefs.isKey("ssid")) cWifiSsid = cPrefs.getString("ssid");   // isKey first: no "not found" log
  if (cPrefs.isKey("pass")) cWifiPass = cPrefs.getString("pass");
  cWifiChanged = cWifiSsid.length() > 0;
  xTaskCreatePinnedToCore(cloudTask, "cloud", 16384, nullptr, 1, nullptr, 0);
#endif
}

// ---- Loop ----
void loop() {
  pollSerialFromPC();

  int packetSize = LoRa.parsePacket();
  if (packetSize == 0) return;

  uint8_t buf[255];
  uint8_t len = 0;
  while (LoRa.available() && len < sizeof(buf)) {
    buf[len++] = LoRa.read();
  }

  // Tối thiểu: STX + NetworkID + TxID + LEN + CRC + ETX = 6 bytes
  if (len < 6)                    return;
  if (buf[0] != STX)              return;
  if (buf[len - 1] != ETX)        return;

  uint8_t networkID = buf[1];
  uint8_t txID      = buf[2];
  uint8_t dataLen   = buf[3];

  // Tổng: 6 bytes cố định + dataLen
  if (len != dataLen + 6)         return;

  // Lọc đúng network
  if (networkID != NETWORK_ID)    return;

  // CRC tính trên: NetworkID + TxID + LEN + DATA
  uint8_t crcRx   = buf[len - 2];
  uint8_t crcCalc = crc8(buf + 1, dataLen + 3);
  if (crcRx != crcCalc)           return;

  forwardFrame(networkID, txID, buf + 4, dataLen);
#if ENABLE_CLOUD
  cloudOnLoRaFrame(txID, buf + 4, dataLen, LoRa.packetRssi(), LoRa.packetSnr());
#endif
}
