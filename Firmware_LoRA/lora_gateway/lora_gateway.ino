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
 *   WiFi SSID/password and the Firebase settings come from secrets.h (copy secrets.example.h).
 *   The gateway joins that WiFi by itself at power-up and stays on it: ENABLE_CLOUD is the only
 *   switch (build with 0 for a gateway without WiFi). The PC app only reads the cloud state.
 *   The USB port is the PC protocol link: the cloud part never prints to it.
 *
 *   Status frames (USB only, never sent over LoRa). They start with 0xA5 instead of 0xAA, so a
 *   gateway built without the option and an older PC app both ignore them:
 *     [0]   [1]   [2]  [3 .. 3+N-1]  [3+N]  [3+N+1]
 *     0xA5  TYPE  LEN  DATA...       CRC8   0xBB          CRC8 (poly 0x31) over TYPE, LEN, DATA
 *     PC -> gateway  'S'  no DATA                           ask for the cloud status
 *     gateway -> PC  's'  DATA = state, WiFi RSSI (-dBm, 0..127), seconds since the last good
 *                         write (2 bytes), step, code (2 bytes), detail                  (8 bytes)
 *                         state: 1 connecting to WiFi, 2 on WiFi, connecting to the cloud,
 *                                3 cloud OK, 4 cloud error, 5 WiFi error   (0 is not used)
 *                         step:  what it is doing (state 1/2) or where it failed (state 4/5):
 *                                1 WiFi, 2 clock (NTP), 3 Firebase sign-in, 4 database write
 *                         code:  step 1 = WiFi disconnect reason (ESP-IDF wifi_err_reason_t) of the last try,
 *                                0 = none yet; steps 3/4 = HTTP status, or < 0 =
 *                                HTTPClient error (-1 no connection, -11 no answer)
 *                         detail: see CloudDetail (DNS / TCP / TLS / certificate, sign-in refusals)
 *                         Sent as the answer to 'S', and also by itself each time one of
 *                         state / step / code / detail changes.
 *                    'r'  DATA = one report per request the gateway sends to the cloud:
 *                         what (1 sign-in, 2 database write), 0 OK / 1 failed, code (2 bytes),
 *                         detail, bytes sent (2 bytes), time taken in 10 ms (2 bytes); a write adds
 *                         boxes written, events written, events still waiting, events dropped,
 *                         then 2 bytes per box written: ID (+0x40 = via a repeater), status A/W/I.
 *     Numbers of 2 bytes are 14 bits, low 7 bits first; a code is signed (8192..16383 = negative).
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
#include <time.h>
#include <sys/time.h>
#include "gts_root_ca.h"
#if __has_include("secrets.h")
#include "secrets.h"
#else
#error "ENABLE_CLOUD 1 needs secrets.h: copy secrets.example.h to secrets.h and fill in the Firebase and WiFi settings"
#endif
#if !defined(WIFI_SSID) || !defined(WIFI_PASSWORD)
#error "secrets.h needs WIFI_SSID and WIFI_PASSWORD (see secrets.example.h)"
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

// Reported to the PC ('s' frame). The app shows 1/2 yellow, 3 green, 4/5 gray. (0 is not used.)
enum CloudState : uint8_t {
  CS_CONNECTING = 1,  // joining the WiFi
  CS_WIFI = 2,        // on WiFi, connecting to the cloud (clock, sign-in, first write)
  CS_OK = 3,          // last write to Firebase OK
  CS_CLOUD_ERR = 4,   // on WiFi but Firebase not reached (no internet, sign-in or write refused)
  CS_WIFI_ERR = 5     // cannot join the WiFi (wrong password, SSID not found, 30 s without success)
};
#define CLOUD_TIMEOUT_MS 30000   // on WiFi but no clock from the internet after this long = cloud error

// What the gateway is doing (state 1/2) or where it failed (state 4/5): 's' frame, step byte.
enum CloudStep : uint8_t {
  CST_NONE = 0,
  CST_WIFI = 1,       // joining the WiFi (code = WiFi disconnect reason of the last try)
  CST_CLOCK = 2,      // setting the clock from the internet (NTP), needed for HTTPS
  CST_SIGNIN = 3,     // signing in to Firebase Auth (code = HTTP status or HTTPClient error)
  CST_WRITE = 4       // writing to the database (code = HTTP status or HTTPClient error)
};

// More about a failure: detail byte of the 's' and 'r' frames.
enum CloudDetail : uint8_t {
  CD_NONE = 0,
  CD_DNS = 1,         // server name not found (no internet, or a wrong URL)
  CD_TCP = 2,         // server found but no connection
  CD_TLS = 3,         // secure connection (TLS) failed
  CD_CERT = 4,        // server certificate not trusted (gts_root_ca.h, or the clock)
  CD_LOGIN = 10,      // Firebase: wrong email or password
  CD_API_KEY = 11,    // Firebase: API key not valid or blocked
  CD_LOGIN_OFF = 12,  // Firebase: Email/Password sign-in not enabled
  CD_USER_OFF = 13,   // Firebase: the user is disabled
  CD_TOO_MANY = 14,   // Firebase: too many attempts, try later
  CD_BAD_EMAIL = 15,  // Firebase: the email is not valid
  CD_NO_TOKEN = 16    // Firebase: answer 200 without an ID token
};

#define REQ_SIGNIN     1         // 'r' frame, what byte
#define REQ_WRITE      2
#define CFG_REPLY_MAX  64        // DATA bytes of a reply to the PC
#define REPORT_QUEUE   4         // 'r' reports waiting for loop() to send them
#define TLS_CERT_VERIFY_FAILED (-0x2700)   // MBEDTLS_ERR_X509_CERT_VERIFY_FAILED

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
SemaphoreHandle_t cLock;

// What the gateway sent to the cloud, for the PC ('r' frames): filled by the cloud task, sent by loop().
struct CloudReport {
  uint8_t len;
  uint8_t d[CFG_REPLY_MAX];
};
CloudReport cReports[REPORT_QUEUE];  // under cLock
uint8_t cRepHead = 0;
volatile uint8_t cRepCount = 0;

// Written by the cloud task, read by loop() for the status frame. The status is one 32-bit word,
// state | step << 4 | detail << 8 | code (14 bits) << 16, so loop() never sees half an update.
volatile uint32_t cStatus = CS_CONNECTING | (CST_WIFI << 4);
volatile int8_t cWifiRssi = 0;
volatile uint32_t cLastOkMs = 0;
volatile bool cEverOk = false;
volatile uint8_t cWifiReason = 0;   // reason of the last WiFi disconnect (WiFi event)

// Used by the cloud task only.
String cIdToken;
uint32_t cTokenMs = 0;

static inline void cloudLock()   { xSemaphoreTake(cLock, portMAX_DELAY); }
static inline void cloudUnlock() { xSemaphoreGive(cLock); }

static inline void setCloudStatus(uint8_t state, uint8_t step, int code = 0, uint8_t detail = CD_NONE) {
  cStatus = (uint32_t)state | ((uint32_t)step << 4) | ((uint32_t)detail << 8) | ((uint32_t)(code & 0x3FFF) << 16);
}

static inline uint8_t cloudState() { return (uint8_t)(cStatus & 0x0F); }

// A number in two 7-bit bytes (low first); a negative code becomes 8192..16383.
static inline void put14(uint8_t *p, int v) {
  p[0] = (uint8_t)(v & 0x7F);
  p[1] = (uint8_t)((v >> 7) & 0x7F);
}

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
  uint8_t crcBuf[2 + CFG_REPLY_MAX];
  if (len > CFG_REPLY_MAX) return;
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

void sendCloudStatus(uint32_t st) {
  uint8_t rssiMag = cWifiRssi < 0 ? (uint8_t)(-cWifiRssi) : 0;
  if (rssiMag > 127) rssiMag = 127;
  uint32_t age = 16383;                                   // never
  if (cEverOk) {
    age = (millis() - cLastOkMs) / 1000;
    if (age > 16382) age = 16382;
  }
  uint8_t d[8];
  d[0] = (uint8_t)(st & 0x0F);                            // state
  d[1] = rssiMag;
  put14(d + 2, (int)age);
  d[4] = (uint8_t)((st >> 4) & 0x0F);                     // step
  put14(d + 5, (int)((st >> 16) & 0x3FFF));               // code
  d[7] = (uint8_t)((st >> 8) & 0x7F);                     // detail
  sendCfgFrame('s', d, sizeof d);
}

// A report for the PC (cloud task): loop() sends it as an 'r' frame.
void cloudQueueReport(const uint8_t *d, uint8_t len) {
  if (len > CFG_REPLY_MAX) len = CFG_REPLY_MAX;
  cloudLock();
  if (cRepCount == REPORT_QUEUE) {                        // the PC side is not keeping up: drop the oldest
    cRepHead = (cRepHead + 1) % REPORT_QUEUE;
    cRepCount--;
  }
  CloudReport &r = cReports[(cRepHead + cRepCount) % REPORT_QUEUE];
  r.len = len;
  memcpy(r.d, d, len);
  cRepCount++;
  cloudUnlock();
}

// Start of an 'r' report: what, OK/failed, code, detail, bytes sent, time taken (10 ms). Returns its length.
uint8_t reportHeader(uint8_t *d, uint8_t what, bool ok, int code, uint8_t detail, size_t bytes, uint32_t ms) {
  d[0] = what;
  d[1] = ok ? 0 : 1;
  put14(d + 2, code);
  d[4] = detail;
  put14(d + 5, bytes > 16383 ? 16383 : (int)bytes);
  uint32_t t = ms / 10;
  put14(d + 7, t > 16383 ? 16383 : (int)t);
  return 9;
}

// Why an HTTPS request got no HTTP answer at all (code < 0).
uint8_t connectFailDetail(WiFiClientSecure &client, int code) {
  if (code != HTTPC_ERROR_CONNECTION_REFUSED) return CD_NONE;   // connected, then lost or no answer: the code says it
  char buf[8];
  int e = client.lastError(buf, sizeof buf);
  if (e == 0) return CD_DNS;                              // the name lookup failed: no TCP / TLS was tried
  if (e == -1) return CD_TCP;
  return e == TLS_CERT_VERIFY_FAILED ? CD_CERT : CD_TLS;
}

// Why Firebase Auth refused the sign-in (error message in its JSON answer).
uint8_t signInDetail(const String &resp) {
  if (resp.indexOf("INVALID_LOGIN_CREDENTIALS") >= 0 || resp.indexOf("INVALID_PASSWORD") >= 0 ||
      resp.indexOf("EMAIL_NOT_FOUND") >= 0 || resp.indexOf("MISSING_PASSWORD") >= 0) return CD_LOGIN;
  if (resp.indexOf("API key") >= 0 || resp.indexOf("API_KEY") >= 0) return CD_API_KEY;
  if (resp.indexOf("PASSWORD_LOGIN_DISABLED") >= 0 || resp.indexOf("OPERATION_NOT_ALLOWED") >= 0 ||
      resp.indexOf("CONFIGURATION_NOT_FOUND") >= 0) return CD_LOGIN_OFF;
  if (resp.indexOf("USER_DISABLED") >= 0) return CD_USER_OFF;
  if (resp.indexOf("TOO_MANY_ATTEMPTS") >= 0) return CD_TOO_MANY;
  if (resp.indexOf("INVALID_EMAIL") >= 0) return CD_BAD_EMAIL;
  return CD_NONE;
}

// A complete 0xA5 frame from the PC.
void handleCfgFrame(uint8_t *f, uint8_t len) {
  if (f[len - 1] != ETX) return;
  uint8_t type = f[1];
  uint8_t dlen = f[2];
  if (crc8(f + 1, dlen + 2) != f[len - 2]) return;
  if (type == 'S') sendCloudStatus(cStatus);              // anything else (e.g. 'W' of an older app) is ignored
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

// Firebase Auth (email/password) -> ID token for the database (?auth=...). Reported to the PC ('r').
bool cloudSignIn(int &code, uint8_t &detail) {
  uint32_t t0 = millis();
  WiFiClientSecure client;
  client.setCACert(GTS_ROOT_CA);
  HTTPClient http;
  http.setConnectTimeout(10000);
  http.setTimeout(15000);
  String url = String("https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=") + FIREBASE_API_KEY;
  String body = String("{\"email\":\"") + jsonEscape(FIREBASE_EMAIL) + "\",\"password\":\"" +
                jsonEscape(FIREBASE_PASSWORD) + "\",\"returnSecureToken\":true}";
  String resp;
  code = HTTPC_ERROR_CONNECTION_REFUSED;
  detail = CD_NONE;
  if (http.begin(client, url)) {
    http.addHeader("Content-Type", "application/json");
    code = http.POST(body);
    if (code > 0) resp = http.getString();
    http.end();
  }
  bool ok = false;
  if (code == 200) {
    String token = jsonField(resp, "idToken");
    if (token.length() > 0) {
      cIdToken = token;
      cTokenMs = millis();
      ok = true;
    } else {
      detail = CD_NO_TOKEN;
    }
  } else if (code > 0) {
    detail = signInDetail(resp);
  } else {
    detail = connectFailDetail(client, code);
  }
  uint8_t d[CFG_REPLY_MAX];
  cloudQueueReport(d, reportHeader(d, REQ_SIGNIN, ok, code, detail, body.length(), millis() - t0));
  return ok;
}

// One PATCH to lines/<NN>.json with everything that changed (multi-path update). True if written.
// Reported to the PC ('r'): what was sent (boxes, events, bytes) and the answer.
bool cloudWrite(int &code, uint8_t &detail) {
  static CloudBox snap[MAX_BOX + 1];
  static CloudEvent evs[MAX_EVENTS_PER_WRITE];
  uint32_t t0 = millis();
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
  uint8_t nBoxes = 0;
  for (int id = 1; id <= MAX_BOX; id++) {
    const CloudBox &x = snap[id];
    if (!x.dirty || !x.status) continue;
    nBoxes++;
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
  code = HTTPC_ERROR_CONNECTION_REFUSED;
  if (http.begin(client, url)) {
    http.addHeader("Content-Type", "application/json");
    code = http.PATCH(b);
    http.end();
  }
  detail = code < 0 ? connectFailDetail(client, code) : CD_NONE;
  if (code == 401 || code == 403) cIdToken = String();   // token expired or refused: sign in again

  uint16_t waiting;
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
  waiting = cEvCount;
  cloudUnlock();

  // Report: header, boxes, events written, events still waiting, events dropped, then ID + status per box.
  uint8_t d[CFG_REPLY_MAX];
  uint8_t n = reportHeader(d, REQ_WRITE, code == 200, code, detail, b.length(), millis() - t0);
  d[n++] = nBoxes;
  d[n++] = nEv > 127 ? 127 : (uint8_t)nEv;
  d[n++] = waiting > 127 ? 127 : (uint8_t)waiting;
  d[n++] = dropped > 127 ? 127 : (uint8_t)dropped;
  for (int id = 1; id <= MAX_BOX && n + 2 <= CFG_REPLY_MAX; id++) {
    const CloudBox &x = snap[id];
    if (!x.dirty || !x.status) continue;
    d[n++] = (uint8_t)(id | (x.viaRepeater ? 0x40 : 0));
    d[n++] = (uint8_t)x.status;
  }
  cloudQueueReport(d, n);
  return code == 200;
}

// WiFi event (WiFi task): keep why the last connection attempt ended.
void onWifiDisconnected(arduino_event_id_t, arduino_event_info_t info) {
  uint8_t r = info.wifi_sta_disconnected.reason;
  // 8 ASSOC_LEAVE / 36 STA_LEAVING = the gateway itself left (WiFi.disconnect before each new try): keep the real
  // reason of the failed try instead.
  if (r != WIFI_REASON_ASSOC_LEAVE && r != WIFI_REASON_STA_LEAVING) cWifiReason = r;
}

// Cloud task (core 0): WiFi, clock, sign-in, writes. Never blocks loop().
void cloudTask(void *) {
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.onEvent(onWifiDisconnected, ARDUINO_EVENT_WIFI_STA_DISCONNECTED);
  bool wifiUp = false, wifiFailed = false;
  bool clockChecked = false, clockNoDns = false;
  uint32_t wifiUpSince = 0;
  uint32_t lastWifiTry = 0, lastWrite = 0, lastTry = 0, backoffMs = 0;
  bool wroteOnce = false, sntpStarted = false, writeNow = false;

  for (;;) {
    vTaskDelay(pdMS_TO_TICKS(200));

    cloudLock();
    bool soon = cUploadSoon;
    uint32_t soonSince = cSoonSinceMs;
    cloudUnlock();

    wl_status_t ws = WiFi.status();
    if (ws != WL_CONNECTED) {
      if (wifiUp) {                                       // just lost the WiFi: start over as "connecting"
        wifiUp = false;
        lastWifiTry = 0;
      }
      if (ws == WL_CONNECT_FAILED || ws == WL_NO_SSID_AVAIL) wifiFailed = true;   // wrong password / SSID not found
      if (lastWifiTry == 0 || millis() - lastWifiTry >= WIFI_RETRY_MS) {
        if (lastWifiTry != 0) wifiFailed = true;          // the last try did not get in
        lastWifiTry = millis();
        WiFi.disconnect();
        WiFi.begin(WIFI_SSID, WIFI_PASSWORD);             // from secrets.h
      }
      // The code is why the last try ended (0 = not known yet), also while still connecting.
      setCloudStatus(wifiFailed ? CS_WIFI_ERR : CS_CONNECTING, CST_WIFI, cWifiReason);
      continue;
    }
    if (!wifiUp) {                                        // just joined the WiFi: now connecting to the cloud
      wifiUp = true;
      wifiFailed = false;
      wifiUpSince = millis();
      cWifiReason = 0;
      clockChecked = false;
      writeNow = true;                                    // write at once, not at the next interval
      setCloudStatus(CS_WIFI, CST_CLOCK);
    }
    cWifiRssi = (int8_t)WiFi.RSSI();
    if (!sntpStarted) {
      configTime(0, 0, "pool.ntp.org", "time.google.com");
      sntpStarted = true;
    }
    if (!timeValid()) {                                   // HTTPS needs the clock for the certificate dates
      if (millis() - wifiUpSince >= CLOUD_TIMEOUT_MS) {   // WiFi but no time from the internet
        if (!clockChecked) {                              // once: is there internet at all (name lookup)?
          IPAddress ip;
          clockNoDns = !WiFi.hostByName("pool.ntp.org", ip);
          clockChecked = true;
        }
        setCloudStatus(CS_CLOUD_ERR, CST_CLOCK, 0, clockNoDns ? CD_DNS : CD_NONE);
      }
      continue;
    }

    uint32_t now = millis();
    bool due = !wroteOnce || writeNow || now - lastWrite >= UPLOAD_INTERVAL_S * 1000UL ||
               (soon && now - soonSince >= UPLOAD_GATHER_MS);
    if (!due) continue;
    if (backoffMs && now - lastTry < backoffMs) continue;
    lastTry = now;
    writeNow = false;

    // Each step is shown (yellow) only until the first answer since joining the WiFi; after that an OK or
    // an error stays shown during the next tries, and every try is reported to the PC ('r') anyway.
    bool firstTry = cloudState() == CS_WIFI;
    int code = 0;
    uint8_t detail = CD_NONE;
    uint8_t step = CST_SIGNIN;
    bool ok = cIdToken.length() > 0 && now - cTokenMs < TOKEN_MAX_AGE_MS;
    if (!ok) {
      if (firstTry) setCloudStatus(CS_WIFI, CST_SIGNIN);
      ok = cloudSignIn(code, detail);
    }
    if (ok) {
      step = CST_WRITE;
      if (firstTry) setCloudStatus(CS_WIFI, CST_WRITE);
      ok = cloudWrite(code, detail);
    }
    if (ok) {
      wroteOnce = true;
      lastWrite = millis();
      backoffMs = 0;
      cLastOkMs = lastWrite;
      cEverOk = true;
      setCloudStatus(CS_OK, CST_WRITE);
    } else {
      backoffMs = backoffMs == 0 ? 5000 : (backoffMs >= 150000 ? 300000 : backoffMs * 2);
      setCloudStatus(CS_CLOUD_ERR, step, code, detail);
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
  xTaskCreatePinnedToCore(cloudTask, "cloud", 16384, nullptr, 1, nullptr, 0);
#endif
}

// ---- Loop ----
void loop() {
  pollSerialFromPC();

#if ENABLE_CLOUD
  // Tell the PC each time the cloud status changes, and what was sent to the cloud (from loop() only,
  // so these frames never split a box frame).
  static uint32_t sentStatus = 0xFFFFFFFF;
  uint32_t st = cStatus;
  if (st != sentStatus) {
    sentStatus = st;
    sendCloudStatus(st);
  }
  if (cRepCount) {
    CloudReport rep;
    cloudLock();
    rep = cReports[cRepHead];
    cRepHead = (cRepHead + 1) % REPORT_QUEUE;
    cRepCount--;
    cloudUnlock();
    sendCfgFrame('r', rep.d, rep.len);
  }
#endif

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
