using System.Collections.Generic;

namespace ESDCMS.Device;

/// <summary>
/// Names of the error codes in the gateway's cloud status / report frames, shown as "code NAME" (no advice: the meaning
/// of each code is in the docs, LoRa Status Protocol > Error codes).
///   WiFi: ESP-IDF wifi_err_reason_t (generated from esp_wifi_types_generic.h of the ESP32 core 3.3.12)
///   HTTP: status phrase; HTTPClient: its negative HTTPC_ERROR_* codes; detail: lora_gateway.ino CloudDetail.
/// </summary>
internal static class GatewayErrorNames
{
	private static readonly Dictionary<int, string> WifiReasons = new Dictionary<int, string>
	{
		{ 1, "UNSPECIFIED" },
		{ 2, "AUTH_EXPIRE" },
		{ 3, "AUTH_LEAVE" },
		{ 4, "DISASSOC_DUE_TO_INACTIVITY" },
		{ 5, "ASSOC_TOOMANY" },
		{ 6, "CLASS2_FRAME_FROM_NONAUTH_STA" },
		{ 7, "CLASS3_FRAME_FROM_NONASSOC_STA" },
		{ 8, "ASSOC_LEAVE" },
		{ 9, "ASSOC_NOT_AUTHED" },
		{ 10, "DISASSOC_PWRCAP_BAD" },
		{ 11, "DISASSOC_SUPCHAN_BAD" },
		{ 12, "BSS_TRANSITION_DISASSOC" },
		{ 13, "IE_INVALID" },
		{ 14, "MIC_FAILURE" },
		{ 15, "4WAY_HANDSHAKE_TIMEOUT" },
		{ 16, "GROUP_KEY_UPDATE_TIMEOUT" },
		{ 17, "IE_IN_4WAY_DIFFERS" },
		{ 18, "GROUP_CIPHER_INVALID" },
		{ 19, "PAIRWISE_CIPHER_INVALID" },
		{ 20, "AKMP_INVALID" },
		{ 21, "UNSUPP_RSN_IE_VERSION" },
		{ 22, "INVALID_RSN_IE_CAP" },
		{ 23, "802_1X_AUTH_FAILED" },
		{ 24, "CIPHER_SUITE_REJECTED" },
		{ 25, "TDLS_PEER_UNREACHABLE" },
		{ 26, "TDLS_UNSPECIFIED" },
		{ 27, "SSP_REQUESTED_DISASSOC" },
		{ 28, "NO_SSP_ROAMING_AGREEMENT" },
		{ 29, "BAD_CIPHER_OR_AKM" },
		{ 30, "NOT_AUTHORIZED_THIS_LOCATION" },
		{ 31, "SERVICE_CHANGE_PERCLUDES_TS" },
		{ 32, "UNSPECIFIED_QOS" },
		{ 33, "NOT_ENOUGH_BANDWIDTH" },
		{ 34, "MISSING_ACKS" },
		{ 35, "EXCEEDED_TXOP" },
		{ 36, "STA_LEAVING" },
		{ 37, "END_BA" },
		{ 38, "UNKNOWN_BA" },
		{ 39, "TIMEOUT" },
		{ 46, "PEER_INITIATED" },
		{ 47, "AP_INITIATED" },
		{ 48, "INVALID_FT_ACTION_FRAME_COUNT" },
		{ 49, "INVALID_PMKID" },
		{ 50, "INVALID_MDE" },
		{ 51, "INVALID_FTE" },
		{ 67, "TRANSMISSION_LINK_ESTABLISH_FAILED" },
		{ 68, "ALTERATIVE_CHANNEL_OCCUPIED" },
		{ 200, "BEACON_TIMEOUT" },
		{ 201, "NO_AP_FOUND" },
		{ 202, "AUTH_FAIL" },
		{ 203, "ASSOC_FAIL" },
		{ 204, "HANDSHAKE_TIMEOUT" },
		{ 205, "CONNECTION_FAIL" },
		{ 206, "AP_TSF_RESET" },
		{ 207, "ROAMING" },
		{ 208, "ASSOC_COMEBACK_TIME_TOO_LONG" },
		{ 209, "SA_QUERY_TIMEOUT" },
		{ 210, "NO_AP_FOUND_W_COMPATIBLE_SECURITY" },
		{ 211, "NO_AP_FOUND_IN_AUTHMODE_THRESHOLD" },
		{ 212, "NO_AP_FOUND_IN_RSSI_THRESHOLD" },
	};

	private static readonly Dictionary<int, string> HttpPhrases = new Dictionary<int, string>
	{
		{ 200, "OK" },
		{ 400, "Bad Request" },
		{ 401, "Unauthorized" },
		{ 403, "Forbidden" },
		{ 404, "Not Found" },
		{ 405, "Method Not Allowed" },
		{ 408, "Request Timeout" },
		{ 412, "Precondition Failed" },
		{ 413, "Payload Too Large" },
		{ 423, "Locked" },
		{ 429, "Too Many Requests" },
		{ 500, "Internal Server Error" },
		{ 502, "Bad Gateway" },
		{ 503, "Service Unavailable" },
		{ 504, "Gateway Timeout" },
	};

	private static readonly Dictionary<int, string> HttpClientErrors = new Dictionary<int, string>
	{
		{ -1, "CONNECTION_REFUSED" },
		{ -2, "SEND_HEADER_FAILED" },
		{ -3, "SEND_PAYLOAD_FAILED" },
		{ -4, "NOT_CONNECTED" },
		{ -5, "CONNECTION_LOST" },
		{ -6, "NO_STREAM" },
		{ -7, "NO_HTTP_SERVER" },
		{ -8, "TOO_LESS_RAM" },
		{ -9, "ENCODING" },
		{ -10, "STREAM_WRITE" },
		{ -11, "READ_TIMEOUT" },
	};

	private static readonly Dictionary<int, string> Details = new Dictionary<int, string>
	{
		{ 1, "DNS_FAILED" },
		{ 2, "TCP_CONNECT_FAILED" },
		{ 3, "TLS_FAILED" },
		{ 4, "CERT_VERIFY_FAILED" },
		{ 10, "INVALID_LOGIN_CREDENTIALS" },
		{ 11, "API_KEY_INVALID" },
		{ 12, "PASSWORD_LOGIN_DISABLED" },
		{ 13, "USER_DISABLED" },
		{ 14, "TOO_MANY_ATTEMPTS_TRY_LATER" },
		{ 15, "INVALID_EMAIL" },
		{ 16, "NO_ID_TOKEN" },
	};

	/// <summary>ESP-IDF wifi_auth_mode_t (esp_wifi_types_generic.h, ESP32 core 3.3.12).</summary>
	private static readonly string[] AuthModes =
	{
		"OPEN", "WEP", "WPA_PSK", "WPA2_PSK", "WPA_WPA2_PSK", "WPA2_ENTERPRISE", "WPA3_PSK", "WPA2_WPA3_PSK", "WAPI_PSK",
		"OWE", "WPA3_ENT_192", "WPA3_EXT_PSK", "WPA3_EXT_PSK_MIXED_MODE", "DPP", "WPA3_ENTERPRISE", "WPA2_WPA3_ENTERPRISE",
		"WPA_ENTERPRISE"
	};

	/// <summary>"WPA2_PSK"; null when not known.</summary>
	public static string AuthMode(int mode)
	{
		if (mode < 0)
		{
			return null;
		}
		return (mode < AuthModes.Length) ? AuthModes[mode] : ("AUTH_" + mode);
	}

	/// <summary>"201 NO_AP_FOUND"; 0 = no reason from the WiFi driver (the 30 s try ran out).</summary>
	public static string Wifi(int code)
	{
		if (code == 0)
		{
			return "0 CONNECT_TIMEOUT";
		}
		return code + " " + (WifiReasons.TryGetValue(code, out string n) ? n : "UNKNOWN");
	}

	/// <summary>"HTTP 401 Unauthorized", or "-1 CONNECTION_REFUSED" for an HTTPClient error.</summary>
	public static string Http(int code)
	{
		if (code < 0)
		{
			return code + " " + (HttpClientErrors.TryGetValue(code, out string e) ? e : "HTTPC_ERROR");
		}
		return "HTTP " + code + (HttpPhrases.TryGetValue(code, out string p) ? (" " + p) : "");
	}

	/// <summary>Name of a detail code (lora_gateway.ino CloudDetail); null for 0 or unknown.</summary>
	public static string Detail(int detail)
	{
		return Details.TryGetValue(detail, out string n) ? n : null;
	}
}
