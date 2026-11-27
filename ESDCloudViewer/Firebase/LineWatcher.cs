using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ESDCloudViewer.Firebase;

/// <summary>What the viewer shows, copied out of the watcher (safe to read on the UI thread).</summary>
public sealed class LineSnapshot
{
	/// <summary>lines/&lt;line&gt;/pcs: one entry per PC (ESDCMS) writing this line; null until loaded or when none wrote yet.</summary>
	public JObject Pcs;

	/// <summary>lines/&lt;line&gt;/boxes, keyed "01".."20".</summary>
	public JObject Boxes;

	/// <summary>The latest lines/&lt;line&gt;/events, keyed "&lt;t&gt;_&lt;n&gt;".</summary>
	public JObject Events;

	public bool PcsLoaded;

	public bool BoxesLoaded;

	public bool EventsLoaded;

	/// <summary>A problem to show (English detail in the console log); null = fine.</summary>
	public string ErrorKey;

	public DateTime LastDataUtc;
}

/// <summary>
/// Watches one line live: a database stream for lines/&lt;line&gt;/pcs and one for .../boxes (Server-Sent Events: put /
/// patch with a path relative to the streamed location), and the latest 30 events every 10 s. Reconnects by itself;
/// the ID token lasts 60 min, so it signs in again every 50 min (and on "auth_revoked") and reopens the streams.
/// Box status changes and PC writes are reported to <see cref="Log"/> (English). ESDCMS writes the data (CloudUploader).
/// </summary>
public sealed class LineWatcher : IDisposable
{
	private const int EventsPollMs = 10000;

	private const int EventsShown = 30;

	private static readonly TimeSpan TokenRenewEvery = TimeSpan.FromMinutes(50);

	/// <summary>Firebase sends keep-alive every 30 s: nothing for this long = the connection is dead, reopen it.</summary>
	private static readonly TimeSpan StreamSilentMax = TimeSpan.FromSeconds(90);

	private readonly ViewerSettings _s;

	private readonly object _lock = new object();

	private readonly CancellationTokenSource _stop = new CancellationTokenSource();

	/// <summary>The open stream connections; cancelled to reconnect them (new token, dead connection).</summary>
	private CancellationTokenSource _conn;

	private string _token;

	private JToken _pcs;

	private JToken _boxes;

	private JObject _events;

	private bool _pcsLoaded;

	private bool _boxesLoaded;

	private bool _eventsLoaded;

	private string _errorKey;

	private DateTime _lastDataUtc;

	private DateTime _lastActivityUtc = DateTime.UtcNow;

	/// <summary>Something changed (any thread).</summary>
	public event Action Changed;

	/// <summary>A console line (any thread).</summary>
	public event Action<string> Log;

	private readonly string _line;

	public LineWatcher(ViewerSettings settings, string line, string token)
	{
		_s = settings.Clone();
		_line = line;
		_token = token;
		_conn = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
	}

	public string Line => _line;

	public void Start()
	{
		Log?.Invoke("Watch: lines/" + _line + " on " + _s.DbHost);
		_ = RunStreamAsync("pcs");
		_ = RunStreamAsync("boxes");
		_ = PollEventsAsync();
		_ = RenewTokenLoopAsync();
		_ = WatchdogAsync();
	}

	public void Dispose()
	{
		if (!_stop.IsCancellationRequested)
		{
			_stop.Cancel();
			Log?.Invoke("Watch: stopped");
		}
	}

	public LineSnapshot Snapshot()
	{
		lock (_lock)
		{
			return new LineSnapshot
			{
				Pcs = _pcs?.DeepClone() as JObject,
				Boxes = AsObject(_boxes)?.DeepClone() as JObject,
				Events = _events?.DeepClone() as JObject,
				PcsLoaded = _pcsLoaded,
				BoxesLoaded = _boxesLoaded,
				EventsLoaded = _eventsLoaded,
				ErrorKey = _errorKey,
				LastDataUtc = _lastDataUtc
			};
		}
	}

	private string Url(string child)
	{
		string token;
		lock (_lock)
		{
			token = _token;
		}
		return _s.DbUrl + "/lines/" + _line + "/" + child + ".json?auth=" + Uri.EscapeDataString(token);
	}

	private void SetError(string key)
	{
		lock (_lock)
		{
			_errorKey = key;
		}
		Changed?.Invoke();
	}

	private async Task RunStreamAsync(string child)
	{
		while (!_stop.IsCancellationRequested)
		{
			CancellationToken conn;
			lock (_lock)
			{
				conn = _conn.Token;
			}
			try
			{
				await FirebaseRest.StreamAsync(Url(child), (ev, data) => OnStreamEvent(child, ev, data), conn).ConfigureAwait(false);
				Log?.Invoke("Stream " + child + ": closed by Firebase, reconnecting");
			}
			catch (OperationCanceledException)
			{
				if (_stop.IsCancellationRequested)
				{
					return;
				}
				continue;                                        // reconnect (new token or dead connection)
			}
			catch (FirebaseHttpException ex) when (ex.Status == 401 || ex.Status == 403)
			{
				Log?.Invoke("Stream " + child + ": not allowed to read lines/" + _line + " (HTTP " + ex.Status + "): rules, or the sign-in expired");
				SetError("S.Err.Read");
				await RenewTokenAsync().ConfigureAwait(false);
				await DelayAsync(15000).ConfigureAwait(false);
				continue;
			}
			catch (FirebaseHttpException ex)
			{
				Log?.Invoke("Stream " + child + ": HTTP " + ex.Status + ", retrying");
				SetError(ex.Status == 404 ? "S.Err.Read" : "S.Err.Lost");
			}
			catch (Exception ex)
			{
				Log?.Invoke("Stream " + child + ": " + ex.GetBaseException().Message + ", retrying");
				SetError("S.Err.Lost");
			}
			await DelayAsync(5000).ConfigureAwait(false);
		}
	}

	private void OnStreamEvent(string child, string ev, string data)
	{
		lock (_lock)
		{
			_lastActivityUtc = DateTime.UtcNow;
		}
		switch (ev)
		{
		case "put":
		case "patch":
			Apply(child, ev == "patch", JObject.Parse(data));
			break;
		case "auth_revoked":
			Log?.Invoke("Stream " + child + ": sign-in expired, signing in again");
			_ = RenewTokenAsync();
			break;
		case "cancel":
			Log?.Invoke("Stream " + child + ": Firebase stopped the stream (rules no longer allow reading)");
			SetError("S.Err.Read");
			break;
		}
	}

	private void Apply(string child, bool patch, JObject msg)
	{
		string path = (string)msg["path"] ?? "/";
		JToken data = msg["data"];
		List<string> logs = new List<string>();
		lock (_lock)
		{
			JToken root = (child == "pcs") ? _pcs : _boxes;
			bool firstLoad = (child == "pcs") ? !_pcsLoaded : !_boxesLoaded;
			Dictionary<string, string> before = Statuses(root);
			Dictionary<string, long> writesBefore = LastWrites(root);
			if (patch && data is JObject changes)
			{
				foreach (JProperty p in changes.Properties())
				{
					root = Put(root, path.TrimEnd('/') + "/" + p.Name, p.Value);
				}
			}
			else
			{
				root = Put(root, path, data);
			}
			if (child == "pcs")
			{
				_pcs = root;
				_pcsLoaded = true;
				Dictionary<string, long> writes = LastWrites(root);
				if (firstLoad)
				{
					logs.Add(writes.Count == 0 ? "PCs: none wrote to the cloud yet"
						: "PCs: " + string.Join(", ", writes.OrderBy(kv => kv.Key).Select(kv => kv.Key + " last write " + Ago(kv.Value))));
				}
				else
				{
					foreach (KeyValuePair<string, long> kv in writes.OrderBy(kv => kv.Key))
					{
						writesBefore.TryGetValue(kv.Key, out long old);
						if (kv.Value != old && kv.Value != 0)
						{
							logs.Add("PC " + kv.Key + ": wrote to the cloud");
						}
					}
				}
			}
			else
			{
				_boxes = root;
				_boxesLoaded = true;
				Dictionary<string, string> after = Statuses(root);
				if (firstLoad)
				{
					logs.Add("Boxes: " + after.Count + " loaded" + (after.Count > 0 ? " [" + string.Join(", ", after.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)) + "]" : ""));
				}
				else
				{
					foreach (KeyValuePair<string, string> kv in after.OrderBy(kv => kv.Key))
					{
						before.TryGetValue(kv.Key, out string old);
						if (old != kv.Value)
						{
							logs.Add("Box " + kv.Key + ": " + StatusName(old) + " -> " + StatusName(kv.Value));
						}
					}
				}
			}
			_lastDataUtc = DateTime.UtcNow;
			_errorKey = null;
		}
		foreach (string l in logs)
		{
			Log?.Invoke(l);
		}
		Changed?.Invoke();
	}

	/// <summary>lastSeen (epoch ms) per PC.</summary>
	private static Dictionary<string, long> LastWrites(JToken pcs)
	{
		Dictionary<string, long> d = new Dictionary<string, long>();
		if (pcs is JObject o)
		{
			foreach (JProperty p in o.Properties())
			{
				d[p.Name] = (long?)(p.Value as JObject)?["lastSeen"] ?? 0;
			}
		}
		return d;
	}

	/// <summary>Status letter (A/W/I/O) per box ID.</summary>
	private static Dictionary<string, string> Statuses(JToken boxes)
	{
		Dictionary<string, string> d = new Dictionary<string, string>();
		JObject o = AsObject(boxes);
		if (o != null)
		{
			foreach (JProperty p in o.Properties())
			{
				d[p.Name] = (string)(p.Value as JObject)?["s"] ?? "?";
			}
		}
		return d;
	}

	private static string StatusName(string s)
	{
		return s switch
		{
			"A" => "Active",
			"W" => "Warning",
			"I" => "Inactive",
			"O" => "Offline",
			"U" => "Unknown",
			"N" => "Not in use",
			null => "(new)",
			_ => s
		};
	}

	/// <summary>The database may answer an object with number-like keys as an array: back to "01".."20".</summary>
	private static JObject AsObject(JToken t)
	{
		if (t is JObject o)
		{
			return o;
		}
		if (t is JArray a)
		{
			JObject r = new JObject();
			for (int i = 0; i < a.Count; i++)
			{
				if (a[i] != null && a[i].Type != JTokenType.Null)
				{
					r[i.ToString("00")] = a[i];
				}
			}
			return r;
		}
		return null;
	}

	/// <summary>Writes data at a path into a tree; JSON null deletes. Returns the (new) root.</summary>
	private static JToken Put(JToken root, string path, JToken data)
	{
		bool remove = data == null || data.Type == JTokenType.Null;
		string[] segs = path.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
		if (segs.Length == 0)
		{
			return remove ? null : data.DeepClone();
		}
		JObject top = AsObject(root) ?? new JObject();
		JObject o = top;
		for (int i = 0; i < segs.Length - 1; i++)
		{
			if (!(o[segs[i]] is JObject c))
			{
				c = new JObject();
				o[segs[i]] = c;
			}
			o = c;
		}
		if (remove)
		{
			o.Remove(segs[segs.Length - 1]);
		}
		else
		{
			o[segs[segs.Length - 1]] = data.DeepClone();
		}
		return top;
	}

	private async Task PollEventsAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			string url = _s.DbUrl + "/lines/" + _line + "/events.json?orderBy=%22%24key%22&limitToLast=" + EventsShown;
			string token;
			lock (_lock)
			{
				token = _token;
			}
			HttpResult r = await FirebaseRest.CallAsync(System.Net.Http.HttpMethod.Get, url + "&auth=" + Uri.EscapeDataString(token)).ConfigureAwait(false);
			if (r.Status == 200)
			{
				lock (_lock)
				{
					_events = (r.Json as JObject) ?? new JObject();
					_eventsLoaded = true;
				}
				Changed?.Invoke();
			}
			await DelayAsync(EventsPollMs).ConfigureAwait(false);
		}
	}

	private async Task RenewTokenLoopAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			await DelayAsync((int)TokenRenewEvery.TotalMilliseconds).ConfigureAwait(false);
			if (!_stop.IsCancellationRequested)
			{
				await RenewTokenAsync().ConfigureAwait(false);
			}
		}
	}

	private int _renewing;

	/// <summary>Signs in again and reopens the streams with the new token.</summary>
	private async Task RenewTokenAsync()
	{
		if (Interlocked.Exchange(ref _renewing, 1) == 1)
		{
			return;
		}
		try
		{
			SignInResult r = await FirebaseRest.SignInAsync(_s).ConfigureAwait(false);
			if (r.Error != null)
			{
				Log?.Invoke("Sign-in again failed: " + r.Error);
				SetError("S.Err.SignIn");
				return;
			}
			Reconnect(r.Token);
		}
		finally
		{
			Interlocked.Exchange(ref _renewing, 0);
		}
	}

	private void Reconnect(string newToken)
	{
		CancellationTokenSource old;
		lock (_lock)
		{
			if (newToken != null)
			{
				_token = newToken;
			}
			old = _conn;
			_conn = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
			_lastActivityUtc = DateTime.UtcNow;
		}
		old.Cancel();
	}

	/// <summary>A stream that got nothing (not even keep-alive) for StreamSilentMax is dead: reopen both.</summary>
	private async Task WatchdogAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			await DelayAsync(15000).ConfigureAwait(false);
			bool dead;
			lock (_lock)
			{
				dead = DateTime.UtcNow - _lastActivityUtc > StreamSilentMax;
			}
			if (dead && !_stop.IsCancellationRequested)
			{
				Log?.Invoke("Streams: nothing from Firebase for " + (int)StreamSilentMax.TotalSeconds + " s, reconnecting");
				SetError("S.Err.Lost");
				Reconnect(null);
			}
		}
	}

	private async Task DelayAsync(int ms)
	{
		try
		{
			await Task.Delay(ms, _stop.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static string Ago(long epochMs)
	{
		if (epochMs <= 0)
		{
			return "never";
		}
		double s = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - epochMs) / 1000.0;
		return s < 120 ? ((int)s + " s ago") : s < 7200 ? ((int)(s / 60) + " min ago") : ((int)(s / 3600) + " h ago");
	}
}
