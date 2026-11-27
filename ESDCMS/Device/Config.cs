using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ESDCMS.Device;

public class Config
{
	public string RFGatewayCOM { get; set; }

	public string ControlBoxCOM { get; set; }

	/// <summary>
	/// "Gateway joins WiFi" ticked in the WiFi dialog: the SSID/password are sent to the gateway (cloud option) and the
	/// app shows its WiFi/cloud state. Unticked: the gateway is told to turn its WiFi off (the SSID stays saved here).
	/// </summary>
	public bool WifiEnabled { get; set; }

	/// <summary>WiFi network for the RF gateway's cloud option (sent to the gateway over USB).</summary>
	public string WifiSsid { get; set; } = string.Empty;

	/// <summary>WiFi password, encrypted with Windows DPAPI for this Windows user (base64). Use Get/SetWifiPassword.</summary>
	public string WifiPasswordProtected { get; set; } = string.Empty;

	private static readonly byte[] WifiEntropy = Encoding.UTF8.GetBytes("ESDCMS-gateway-wifi");

	/// <summary>The WiFi password ("" if none, or if config.json comes from another Windows user / PC).</summary>
	public string GetWifiPassword()
	{
		if (string.IsNullOrEmpty(WifiPasswordProtected))
		{
			return string.Empty;
		}
		try
		{
			byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(WifiPasswordProtected), WifiEntropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(plain);
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}

	public void SetWifiPassword(string password)
	{
		WifiPasswordProtected = string.IsNullOrEmpty(password)
			? string.Empty
			: Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), WifiEntropy, DataProtectionScope.CurrentUser));
	}

	/// <summary>Start the program automatically when Windows starts (user logs in).</summary>
	public bool AutoStart { get; set; } = true;

	/// <summary>Console log panel hidden (collapsed); restored at the next start.</summary>
	public bool LogCollapsed { get; set; }

	/// <summary>UI language: "en", "ko" or "th" (anything else = English).</summary>
	public string Language { get; set; } = "en";

	/// <summary>How long (ms) to listen for a box's answer after one status query, before the next query is sent.</summary>
	public int PollReplyTimeoutMs { get; set; } = DefaultPollReplyTimeoutMs;

	public const int DefaultPollReplyTimeoutMs = 400;

	[JsonIgnore]
	public int EffectivePollReplyTimeoutMs => PollReplyTimeoutMs < 100 ? 100 : PollReplyTimeoutMs;

	/// <summary>
	/// One round of status queries lasts at least this long (ms) unless every box answered sooner: after asking
	/// the boxes the PC waits this long for the answers before it asks the silent ones again. Only in config.json.
	/// </summary>
	public int PollRoundWaitMs { get; set; } = DefaultPollRoundWaitMs;

	public const int DefaultPollRoundWaitMs = 5000;

	[JsonIgnore]
	public int EffectivePollRoundWaitMs => PollRoundWaitMs < 1000 ? 1000 : PollRoundWaitMs;

	/// <summary>How many rounds of queries a box gets before it is shown as Offline (first round included).</summary>
	public int PollRetries { get; set; } = DefaultPollRetries;

	public const int DefaultPollRetries = 3;

	[JsonIgnore]
	public int EffectivePollRetries => PollRetries < 1 ? 1 : PollRetries;

	/// <summary>Every this many seconds every box in use is asked for its status, one at a time. 0 = never.</summary>
	public int AskAgainSec { get; set; } = DefaultAskAgainSec;

	// Was 300 before v2.1; a value already saved in config.json is kept, so set it on the settings bar.
	public const int DefaultAskAgainSec = 120;

	[JsonIgnore]
	public int EffectiveAskAgainSec => AskAgainSec <= 0 ? 0 : (AskAgainSec < 5 ? 5 : AskAgainSec);

	/// <summary>Delay (ms) between two consecutive commands sent to the control box.</summary>
	public int TxGapMs { get; set; } = DefaultTxGapMs;

	public const int DefaultTxGapMs = 500;

	/// <summary>Network ID (line) accepted from the RF Gateway; LoRa frames with another ID are ignored.</summary>
	public byte NetworkID { get; set; } = DefaultNetworkID;

	public const byte DefaultNetworkID = 3;

	[JsonIgnore]
	public int EffectiveTxGapMs => TxGapMs < 0 ? 0 : TxGapMs;

	/// <summary>How long (ms) to wait for the control box echo before treating a command as failed.</summary>
	public int TxTimeoutMs { get; set; } = DefaultTxTimeoutMs;

	public const int DefaultTxTimeoutMs = 500;

	[JsonIgnore]
	public int EffectiveTxTimeoutMs => TxTimeoutMs < 50 ? 50 : TxTimeoutMs;

	/// <summary>How many times a command is sent before giving up (first attempt included).</summary>
	public int TxRetries { get; set; } = DefaultTxRetries;

	public const int DefaultTxRetries = 4;

	[JsonIgnore]
	public int EffectiveTxRetries => TxRetries < 1 ? 1 : TxRetries;

	/// <summary>Delay (ms) between two attempts of the same command.</summary>
	public int TxRetryDelayMs { get; set; } = DefaultTxRetryDelayMs;

	public const int DefaultTxRetryDelayMs = 100;

	[JsonIgnore]
	public int EffectiveTxRetryDelayMs => TxRetryDelayMs < 0 ? 0 : TxRetryDelayMs;
}
