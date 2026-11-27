using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ESDCMS.Device;

/// <summary>Cloud status reported by the RF gateway (lora_gateway.ino built with ENABLE_CLOUD 1).</summary>
public sealed class GatewayCloudStatus
{
	/// <summary>
	/// 0 WiFi off, 1 connecting to WiFi, 2 on WiFi and connecting to the cloud, 3 cloud OK (last write OK),
	/// 4 cloud error (Firebase not reached), 5 WiFi error (cannot join the WiFi).
	/// </summary>
	public int State;

	/// <summary>WiFi signal in dBm (negative), 0 = unknown.</summary>
	public int WifiRssi;

	/// <summary>Seconds since the last good write to the database, -1 = never (16382 means "that long or more").</summary>
	public int SecondsSinceWrite;

	/// <summary>
	/// What the gateway is doing (state 1/2) or where it failed (state 4/5): 1 WiFi, 2 clock (NTP), 3 Firebase sign-in,
	/// 4 database write; 0 = none (or an older firmware that does not send it).
	/// </summary>
	public int Step;

	/// <summary>Step 1: WiFi disconnect reason of the last try (0 = none). Steps 3/4: HTTP status, or &lt; 0 = HTTPClient error.</summary>
	public int Code;

	/// <summary>More about a failure (lora_gateway.ino CloudDetail): 1 DNS, 2 TCP, 3 TLS, 4 certificate, 10.. sign-in refusals.</summary>
	public int Detail;
}

/// <summary>One box in a write report: the status the gateway sent to the database for it.</summary>
public struct GatewayCloudBox
{
	public int Id;

	public char Status;

	public bool ViaRepeater;
}

/// <summary>Report of one request the gateway sent to the cloud ('r' frame): what, the answer, and what was in it.</summary>
public sealed class GatewayCloudReport
{
	public const int SignIn = 1;

	public const int Write = 2;

	/// <summary><see cref="SignIn"/> or <see cref="Write"/>.</summary>
	public int What;

	public bool Ok;

	/// <summary>HTTP status, or &lt; 0 = HTTPClient error (-1 no connection, -11 no answer).</summary>
	public int Code;

	/// <summary>Same as <see cref="GatewayCloudStatus.Detail"/>.</summary>
	public int Detail;

	public int BytesSent;

	public int Milliseconds;

	/// <summary>Write only: boxes and status changes (events) in this write.</summary>
	public int Boxes;

	public int Events;

	/// <summary>Write only: events still waiting after this write, and events dropped since power-up (127 = that many or more).</summary>
	public int EventsWaiting;

	public int EventsDropped;

	public List<GatewayCloudBox> BoxList = new List<GatewayCloudBox>();
}

/// <summary>
/// Reads the cloud state of the RF gateway over its USB port (read only: the WiFi SSID/password are in the gateway
/// firmware, secrets.h, and ENABLE_CLOUD is the only on/off). These frames start with 0xA5 (box/query frames with 0xAA),
/// are never sent over LoRa, and a gateway built without the option ignores them (no answer).
///   0xA5  TYPE  LEN  DATA...  CRC8  0xBB       CRC8 (poly 0x31) over TYPE, LEN, DATA
///   PC -> gateway  'S'  (no data) = status request
///   gateway -> PC  's'  state, -RSSI (dBm), seconds since the last good write (2 bytes, 16383 = never), step,
///                       code (2 bytes), detail; sent as the answer to 'S' and also by itself on every change
///                  'r'  one report per request the gateway sends to the cloud (sign-in, database write)
/// Numbers of 2 bytes are 14 bits, low 7 bits first; a code is signed (8192..16383 = negative). Every DATA byte of a
/// reply is below 0x80, so a reply can never look like the start of a 0xAA status frame.
/// </summary>
public sealed class GatewayCloudLink
{
	public const byte Stx = 0xA5;

	private const byte Etx = 0xBB;

	/// <summary>Longest DATA of a reply (lora_gateway.ino CFG_REPLY_MAX).</summary>
	private const int MaxReplyData = 64;

	private readonly List<byte> _rx = new List<byte>();

	private readonly object _lock = new object();

	private TaskCompletionSource<GatewayCloudStatus> _statusWaiter;

	/// <summary>Every status frame from the gateway, asked for or sent by itself (raised on the serial thread).</summary>
	public event Action<GatewayCloudStatus> StatusReceived;

	/// <summary>Every request the gateway sent to the cloud (raised on the serial thread).</summary>
	public event Action<GatewayCloudReport> ReportReceived;

	/// <summary>Feeds one byte received from the gateway (serial thread). Bytes of 0xAA frames are skipped.</summary>
	public void Feed(byte b)
	{
		GatewayCloudStatus status = null;
		GatewayCloudReport report = null;
		lock (_lock)
		{
			if (_rx.Count == 0)
			{
				if (b == Stx)
				{
					_rx.Add(b);
				}
				return;
			}
			_rx.Add(b);
			if ((_rx.Count == 2 && b != (byte)'s' && b != (byte)'r') || (_rx.Count == 3 && b > MaxReplyData))
			{
				// Not a frame of the gateway (only 's' / 'r', up to MaxReplyData bytes): start over.
				_rx.Clear();
				if (b == Stx)
				{
					_rx.Add(b);
				}
				return;
			}
			if (_rx.Count < 3 || _rx.Count < _rx[2] + 5)
			{
				return;
			}
			byte[] f = _rx.ToArray();
			_rx.Clear();
			int len = f[2];
			if (f[f.Length - 1] != Etx || Crc8(f, 1, len + 2) != f[f.Length - 2])
			{
				return;
			}
			if (f[1] == (byte)'s' && len >= 4)
			{
				int age = U14(f, 5);
				status = new GatewayCloudStatus
				{
					State = f[3],
					WifiRssi = -f[4],
					SecondsSinceWrite = (age >= 16383) ? -1 : age
				};
				if (len >= 8)
				{
					status.Step = f[7];
					status.Code = S14(f, 8);
					status.Detail = f[10];
				}
				_statusWaiter?.TrySetResult(status);
				_statusWaiter = null;
			}
			else if (f[1] == (byte)'r' && len >= 9)
			{
				report = ParseReport(f, len);
			}
		}
		if (status != null)
		{
			StatusReceived?.Invoke(status);
		}
		if (report != null)
		{
			ReportReceived?.Invoke(report);
		}
	}

	/// <summary>DATA of an 'r' frame: header (9 bytes), then for a write 4 counts and 2 bytes per box.</summary>
	private static GatewayCloudReport ParseReport(byte[] f, int len)
	{
		GatewayCloudReport r = new GatewayCloudReport
		{
			What = f[3],
			Ok = f[4] == 0,
			Code = S14(f, 5),
			Detail = f[7],
			BytesSent = U14(f, 8),
			Milliseconds = U14(f, 10) * 10
		};
		if (r.What == GatewayCloudReport.Write && len >= 13)
		{
			r.Boxes = f[12];
			r.Events = f[13];
			r.EventsWaiting = f[14];
			r.EventsDropped = f[15];
			for (int i = 16; i + 1 < 3 + len; i += 2)
			{
				r.BoxList.Add(new GatewayCloudBox
				{
					Id = f[i] & 0x3F,
					ViaRepeater = (f[i] & 0x40) != 0,
					Status = (char)f[i + 1]
				});
			}
		}
		return r;
	}

	/// <summary>Number in two 7-bit bytes, low first.</summary>
	private static int U14(byte[] f, int i)
	{
		return f[i] | (f[i + 1] << 7);
	}

	/// <summary>Signed number in two 7-bit bytes (8192..16383 = negative).</summary>
	private static int S14(byte[] f, int i)
	{
		int v = U14(f, i);
		return (v >= 8192) ? (v - 16384) : v;
	}

	/// <summary>Asks the gateway for its cloud status; null when it did not answer.</summary>
	public async Task<GatewayCloudStatus> RequestStatusAsync(RFGateway gateway, int timeoutMs = 1000)
	{
		TaskCompletionSource<GatewayCloudStatus> waiter = new TaskCompletionSource<GatewayCloudStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_lock)
		{
			_statusWaiter = waiter;
		}
		bool answered = await SendAndWaitAsync(gateway, BuildFrame((byte)'S', new byte[0]), waiter.Task, timeoutMs);
		lock (_lock)
		{
			if (_statusWaiter == waiter)
			{
				_statusWaiter = null;
			}
		}
		return answered ? waiter.Task.Result : null;
	}

	private static async Task<bool> SendAndWaitAsync(RFGateway gateway, byte[] frame, Task reply, int timeoutMs)
	{
		if (gateway == null || !gateway.IsOpen)
		{
			return false;
		}
		// The write can block up to the port WriteTimeout if the USB driver hangs: keep it off the UI thread.
		if (!await Task.Run(() => gateway.Send(frame)))
		{
			return false;
		}
		return await Task.WhenAny(reply, Task.Delay(timeoutMs)) == reply;
	}

	private static byte[] BuildFrame(byte type, byte[] data)
	{
		byte[] f = new byte[data.Length + 5];
		f[0] = Stx;
		f[1] = type;
		f[2] = (byte)data.Length;
		Array.Copy(data, 0, f, 3, data.Length);
		f[f.Length - 2] = Crc8(f, 1, data.Length + 2);
		f[f.Length - 1] = Etx;
		return f;
	}

	/// <summary>Same CRC8 as the status frames (poly 0x31, init 0).</summary>
	private static byte Crc8(byte[] data, int offset, int count)
	{
		byte crc = 0;
		for (int i = offset; i < offset + count; i++)
		{
			crc ^= data[i];
			for (int j = 0; j < 8; j++)
			{
				crc = (((crc & 0x80) != 0) ? ((byte)((crc << 1) ^ 0x31)) : ((byte)(crc << 1)));
			}
		}
		return crc;
	}
}
