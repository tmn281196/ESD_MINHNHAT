using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ESDCMS.Device;

public enum CloudState
{
	/// <summary>Cloud upload turned off in the settings.</summary>
	Off,
	/// <summary>On, but the Firebase settings are not complete.</summary>
	NotSet,
	/// <summary>Signing in / first write not answered yet.</summary>
	Connecting,
	/// <summary>Last write OK.</summary>
	Ok,
	/// <summary>Last sign-in or write failed (see <see cref="CloudUploader.ErrorName"/>); trying again.</summary>
	Error
}

/// <summary>What the PC knows right now, read on the UI thread for each upload round.</summary>
public sealed class CloudSnapshot
{
	/// <summary>The line = Net ID as 2 hex digits ("03").</summary>
	public string Line;

	/// <summary>Boxes in use: ID -> status letter ('A' / 'W' / 'I' / 'O' Offline; '\0' = not known yet) and last heard (epoch ms, 0 = never).</summary>
	public List<(int Id, char Status, long SeenMs)> Boxes = new List<(int, char, long)>();

	public bool RfOpen;

	public bool ControlBoxOpen;

	/// <summary>"RUN" / "STOP"; null = not decided yet (no box in use known).</summary>
	public string Conveyor;
}

/// <summary>
/// Sends the line to the Firebase Realtime Database (the PC does it: the RF gateway only links LoRa and USB).
/// Every second it compares a snapshot of the boxes with what it has sent; a status change is written within about
/// a second, and everything (PC heartbeat, every box, "seen" times) at least every HeartbeatMs. Without internet the
/// status changes wait in memory (up to MaxEvents, the oldest are dropped) and go out when the connection is back.
///   lines/&lt;NN&gt;/pcs/&lt;PC&gt;   lastSeen (server time), app, startedAt, rf, controlBox, conveyor, boxesInUse, droppedEvents
///   lines/&lt;NN&gt;/boxes/&lt;ID&gt;  s (A/W/I/O), t (status since), seen (last heard), pc
///   lines/&lt;NN&gt;/events/&lt;t&gt;_&lt;PC&gt;_&lt;n&gt;  { box, s, t, pc }   one per status change
/// Times are Unix epoch ms from the PC clock (lastSeen from the server). Same Firebase account and rules as the
/// gateway option had: the account may write only lines/&lt;NN&gt; where gateways/&lt;UID&gt; = "NN".
/// </summary>
public sealed class CloudUploader
{
	private const int TickMs = 1000;

	private const int GatherMs = 1000;

	private const int HeartbeatMs = 60000;

	private const int MaxEvents = 500;

	private const int EventsPerWrite = 100;

	private static readonly TimeSpan TokenMaxAge = TimeSpan.FromMinutes(50);

	private readonly Func<Config> _config;

	private readonly Func<Task<CloudSnapshot>> _snapshot;

	private readonly string _pc = PcKey(Environment.MachineName);

	private readonly long _startedMs = NowMs();

	private readonly string _app = Assembly.GetExecutingAssembly().GetName().Version.ToString(2);

	private sealed class BoxState
	{
		public char Status;

		public long SinceMs;

		public long SeenMs;
	}

	private readonly Dictionary<int, BoxState> _boxes = new Dictionary<int, BoxState>();

	private readonly HashSet<int> _written = new HashSet<int>();

	private readonly List<JObject> _events = new List<JObject>();

	private readonly List<string> _eventKeys = new List<string>();

	private int _eventSeq;

	private int _dropped;

	private string _line;

	private string _token;

	private DateTime _tokenAtUtc;

	private string _tokenFor;

	private DateTime _lastWriteUtc = DateTime.MinValue;

	private DateTime _changeSinceUtc = DateTime.MinValue;

	private DateTime _nextTryUtc = DateTime.MinValue;

	private int _backoffMs;

	private volatile bool _writeNow;

	public CloudState State { get; private set; } = CloudState.Off;

	/// <summary>Code and name of the last error (FirebaseResult.ErrorName), for the tooltip; null when none.</summary>
	public string ErrorName { get; private set; }

	public DateTime LastOkUtc { get; private set; } = DateTime.MinValue;

	/// <summary>State / error / last write changed (any thread).</summary>
	public event Action Changed;

	/// <summary>A console line (English, any thread).</summary>
	public event Action<string> Log;

	public CloudUploader(Func<Config> config, Func<Task<CloudSnapshot>> snapshot)
	{
		_config = config;
		_snapshot = snapshot;
	}

	public void Start(CancellationToken ct)
	{
		_ = Task.Run(() => LoopAsync(ct));
	}

	/// <summary>The settings were saved: sign in again with them and write at once.</summary>
	public void SettingsChanged()
	{
		_token = null;
		_backoffMs = 0;
		_nextTryUtc = DateTime.MinValue;
		_writeNow = true;
	}

	private async Task LoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TickMs, ct).ConfigureAwait(false);
				await TickAsync().ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				Log?.Invoke("Cloud: error - " + ex.Message);
			}
		}
	}

	private async Task TickAsync()
	{
		Config cfg = _config();
		if (cfg == null || !cfg.CloudEnabled)
		{
			SetState(CloudState.Off, null, "Cloud: off");
			_token = null;
			return;
		}
		if (!cfg.CloudSettingsComplete)
		{
			SetState(CloudState.NotSet, null, "Cloud: on, but the Firebase settings are not complete");
			return;
		}
		CloudSnapshot snap = await _snapshot().ConfigureAwait(false);
		if (snap == null)
		{
			return;
		}
		Merge(snap);
		DateTime now = DateTime.UtcNow;
		bool due = _writeNow || State == CloudState.Off || State == CloudState.NotSet
			|| (now - _lastWriteUtc).TotalMilliseconds >= HeartbeatMs
			|| (_changeSinceUtc != DateTime.MinValue && (now - _changeSinceUtc).TotalMilliseconds >= GatherMs);
		if (!due || now < _nextTryUtc)
		{
			return;
		}
		_writeNow = false;
		if (State == CloudState.Off || State == CloudState.NotSet)
		{
			SetState(CloudState.Connecting, null, "Cloud: on, line " + snap.Line + " as " + _pc + " -> " + HostOf(cfg.CloudDbUrl));
		}

		string password = cfg.GetCloudPassword();
		string tokenFor = cfg.CloudDbUrl + "|" + cfg.CloudApiKey + "|" + cfg.CloudEmail + "|" + password.GetHashCode();
		if (_token == null || _tokenFor != tokenFor || now - _tokenAtUtc > TokenMaxAge)
		{
			DateTime t0 = DateTime.UtcNow;
			FirebaseResult r = await FirebaseRest.SignInAsync(cfg.CloudApiKey.Trim(), cfg.CloudEmail.Trim(), password).ConfigureAwait(false);
			string token = (string)r.Json?["idToken"];
			if (!r.Ok || string.IsNullOrEmpty(token))
			{
				Log?.Invoke("Cloud -> Firebase: sign-in FAILED (" + Took(t0) + "): " + r.ErrorName);
				Failed(r.ErrorName);
				return;
			}
			_token = token;
			_tokenAtUtc = DateTime.UtcNow;
			_tokenFor = tokenFor;
			Log?.Invoke("Cloud -> Firebase: sign-in OK (" + Took(t0) + ")");
		}
		await WriteAsync(cfg, snap).ConfigureAwait(false);
	}

	/// <summary>Takes the snapshot in: a new status of a box becomes a status change (event) to write.</summary>
	private void Merge(CloudSnapshot snap)
	{
		if (snap.Line != _line)
		{
			// Another line (Net ID changed): start over, nothing of the old line is sent to the new one.
			_line = snap.Line;
			_boxes.Clear();
			_written.Clear();
			_events.Clear();
			_eventKeys.Clear();
			_writeNow = true;
		}
		long now = NowMs();
		foreach ((int id, char status, long seen) in snap.Boxes)
		{
			if (!_boxes.TryGetValue(id, out BoxState b))
			{
				b = new BoxState();
				_boxes[id] = b;
			}
			b.SeenMs = seen;
			if (status != '\0' && status != b.Status)
			{
				b.Status = status;
				b.SinceMs = now;
				AddEvent(id, status, now);
			}
		}
		foreach (int id in _boxes.Keys.Except(snap.Boxes.Select(x => x.Id)).ToList())
		{
			_boxes.Remove(id);                                  // not in use any more: removed from the cloud at the next write
		}
	}

	private void AddEvent(int box, char status, long t)
	{
		if (_events.Count >= MaxEvents)
		{
			_events.RemoveAt(0);
			_eventKeys.RemoveAt(0);
			_dropped++;
		}
		_eventSeq = (_eventSeq + 1) % 1000000;
		_eventKeys.Add(t.ToString("0000000000000") + "_" + _pc + "_" + _eventSeq.ToString("000000"));
		_events.Add(new JObject { ["box"] = box, ["s"] = status.ToString(), ["t"] = t, ["pc"] = _pc });
		if (_changeSinceUtc == DateTime.MinValue)
		{
			_changeSinceUtc = DateTime.UtcNow;
		}
	}

	/// <summary>One multi-path PATCH to lines/&lt;NN&gt;.json: PC heartbeat, every box in use, waiting status changes.</summary>
	private async Task WriteAsync(Config cfg, CloudSnapshot snap)
	{
		string pc = "pcs/" + _pc + "/";
		JObject body = new JObject
		{
			[pc + "lastSeen"] = new JObject { [".sv"] = "timestamp" },
			[pc + "app"] = _app,
			[pc + "startedAt"] = _startedMs,
			[pc + "rf"] = snap.RfOpen,
			[pc + "controlBox"] = snap.ControlBoxOpen,
			[pc + "conveyor"] = snap.Conveyor,
			[pc + "boxesInUse"] = snap.Boxes.Count,
			[pc + "droppedEvents"] = _dropped
		};
		List<int> sent = new List<int>();
		foreach (KeyValuePair<int, BoxState> kv in _boxes.OrderBy(kv => kv.Key))
		{
			if (kv.Value.Status == '\0')
			{
				continue;
			}
			string p = "boxes/" + kv.Key.ToString("00") + "/";
			body[p + "s"] = kv.Value.Status.ToString();
			body[p + "t"] = kv.Value.SinceMs;
			body[p + "seen"] = (kv.Value.SeenMs > 0) ? (JToken)kv.Value.SeenMs : JValue.CreateNull();
			body[p + "pc"] = _pc;
			sent.Add(kv.Key);
		}
		List<int> removed = _written.Except(_boxes.Keys).ToList();
		foreach (int id in removed)
		{
			body["boxes/" + id.ToString("00")] = JValue.CreateNull();
		}
		int nEvents = Math.Min(_events.Count, EventsPerWrite);
		for (int i = 0; i < nEvents; i++)
		{
			body["events/" + _eventKeys[i]] = _events[i];
		}
		string text = body.ToString(Newtonsoft.Json.Formatting.None);

		DateTime t0 = DateTime.UtcNow;
		string url = cfg.CloudDbUrl.Trim().TrimEnd('/') + "/lines/" + snap.Line + ".json?auth=" + Uri.EscapeDataString(_token);
		FirebaseResult r = await FirebaseRest.CallAsync(FirebaseRest.Patch, url, body).ConfigureAwait(false);
		string content = "pc info, " + Plural(sent.Count, "box", "boxes")
			+ ((sent.Count > 0) ? (" [" + string.Join(", ", sent.Select(id => id.ToString("00") + " " + _boxes[id].Status)) + "]") : "")
			+ ", " + Plural(nEvents, "status change", "status changes") + ", " + Encoding.UTF8.GetByteCount(text) + " bytes";
		if (!r.Ok)
		{
			if (r.Status == 401 || r.Status == 403)
			{
				_token = null;                                   // expired or refused: sign in again next time
			}
			Log?.Invoke("Cloud -> Firebase: write FAILED (" + Took(t0) + "): " + r.ErrorName + "; tried " + content);
			Failed(r.ErrorName);
			return;
		}
		_events.RemoveRange(0, nEvents);
		_eventKeys.RemoveRange(0, nEvents);
		_changeSinceUtc = (_events.Count > 0) ? DateTime.UtcNow : DateTime.MinValue;
		_writeNow = _events.Count > 0;                          // more waiting: write again at once
		foreach (int id in removed)
		{
			_written.Remove(id);
		}
		foreach (int id in sent)
		{
			_written.Add(id);
		}
		_lastWriteUtc = DateTime.UtcNow;
		_backoffMs = 0;
		_nextTryUtc = DateTime.MinValue;
		LastOkUtc = _lastWriteUtc;
		Log?.Invoke("Cloud -> Firebase: wrote " + content + " (" + Took(t0) + ")"
			+ ((_events.Count > 0) ? ("; " + _events.Count + " status changes still waiting") : ""));
		SetState(CloudState.Ok, null, null);
	}

	private void Failed(string errorName)
	{
		_backoffMs = (_backoffMs == 0) ? 5000 : Math.Min(_backoffMs * 2, 300000);
		_nextTryUtc = DateTime.UtcNow.AddMilliseconds(_backoffMs);
		SetState(CloudState.Error, errorName, null);
	}

	private void SetState(CloudState state, string errorName, string logLine)
	{
		bool changed = state != State || errorName != ErrorName;
		State = state;
		ErrorName = errorName;
		if (changed && logLine != null)
		{
			Log?.Invoke(logLine);
		}
		Changed?.Invoke();
	}

	/// <summary>A Firebase key from the PC name (no . $ # [ ] /).</summary>
	private static string PcKey(string name)
	{
		StringBuilder sb = new StringBuilder();
		foreach (char c in name ?? "PC")
		{
			sb.Append(".$#[]/".IndexOf(c) >= 0 || char.IsControl(c) ? '-' : c);
		}
		return (sb.Length > 0) ? sb.ToString() : "PC";
	}

	private static string HostOf(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? u.Host : url;
	}

	public static long NowMs()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	}

	private static string Took(DateTime t0)
	{
		return (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
	}

	private static string Plural(int n, string one, string many)
	{
		return n + " " + ((n == 1) ? one : many);
	}
}
