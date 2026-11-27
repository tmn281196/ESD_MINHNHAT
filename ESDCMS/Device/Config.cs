using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ESDCMS.Device;

public class Config
{
	public string RFGatewayCOM { get; set; }

	public string ControlBoxCOM { get; set; }

	/// <summary>Start the program automatically when Windows starts (user logs in).</summary>
	public bool AutoStart { get; set; } = true;

	/// <summary>Console log panel hidden (collapsed); restored at the next start.</summary>
	public bool LogCollapsed { get; set; }

	/// <summary>UI language: "en", "ko" or "th" (anything else = English).</summary>
	public string Language { get; set; } = "en";

	/// <summary>Send the line to the cloud (Firebase Realtime Database, see Device/CloudUploader.cs).</summary>
	public bool CloudEnabled { get; set; }

	/// <summary>Firebase Realtime Database URL, e.g. https://name-default-rtdb.region.firebasedatabase.app (no '/' at the end).</summary>
	public string CloudDbUrl { get; set; } = string.Empty;

	/// <summary>Web API key of the Firebase project.</summary>
	public string CloudApiKey { get; set; } = string.Empty;

	/// <summary>Firebase Authentication account (Email/Password) allowed to write this line (gateways/&lt;UID&gt; in the rules).</summary>
	public string CloudEmail { get; set; } = string.Empty;

	/// <summary>Its password, encrypted with Windows DPAPI for the current user (another user or PC cannot read it).</summary>
	public string CloudPasswordProtected { get; set; } = string.Empty;

	private static readonly byte[] CloudEntropy = Encoding.UTF8.GetBytes("ESDCMS:cloud");

	public string GetCloudPassword()
	{
		if (string.IsNullOrEmpty(CloudPasswordProtected))
		{
			return string.Empty;
		}
		try
		{
			byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(CloudPasswordProtected), CloudEntropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(data);
		}
		catch (Exception)
		{
			return string.Empty;     // another PC or Windows user: enter the password again
		}
	}

	public void SetCloudPassword(string password)
	{
		CloudPasswordProtected = string.IsNullOrEmpty(password)
			? string.Empty
			: Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), CloudEntropy, DataProtectionScope.CurrentUser));
	}

	[JsonIgnore]
	public bool CloudSettingsComplete => !string.IsNullOrWhiteSpace(CloudDbUrl) && !string.IsNullOrWhiteSpace(CloudApiKey)
		&& !string.IsNullOrWhiteSpace(CloudEmail) && !string.IsNullOrEmpty(CloudPasswordProtected);

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
