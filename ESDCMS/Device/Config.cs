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

	/// <summary>How long (ms) to wait for a box to answer one status query.</summary>
	public int PollReplyTimeoutMs { get; set; } = DefaultPollReplyTimeoutMs;

	public const int DefaultPollReplyTimeoutMs = 400;

	[JsonIgnore]
	public int EffectivePollReplyTimeoutMs => PollReplyTimeoutMs < 100 ? 100 : PollReplyTimeoutMs;

	/// <summary>How many queries a box gets in one round before it is shown as Offline (first query included).</summary>
	public int PollRetries { get; set; } = DefaultPollRetries;

	public const int DefaultPollRetries = 3;

	[JsonIgnore]
	public int EffectivePollRetries => PollRetries < 1 ? 1 : PollRetries;

	/// <summary>Every this many seconds the boxes that did not answer (Offline) are asked again. 0 = never.</summary>
	public int AskAgainSec { get; set; } = DefaultAskAgainSec;

	public const int DefaultAskAgainSec = 300;

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
