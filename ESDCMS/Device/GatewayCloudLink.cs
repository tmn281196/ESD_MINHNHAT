using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ESDCMS.Device;

/// <summary>Cloud status reported by the RF gateway (lora_gateway.ino built with ENABLE_CLOUD 1).</summary>
public sealed class GatewayCloudStatus
{
	/// <summary>0 no WiFi set, 1 connecting to WiFi, 2 WiFi OK but cloud not reached yet / last write failed, 3 cloud OK.</summary>
	public int State;

	/// <summary>WiFi signal in dBm (negative), 0 = unknown.</summary>
	public int WifiRssi;

	/// <summary>Seconds since the last good write to the database, -1 = never (16382 means "that long or more").</summary>
	public int SecondsSinceWrite;
}

/// <summary>
/// Talks to the cloud option of the RF gateway over its USB port: sends the WiFi SSID/password and asks for the
/// cloud status. Config frames start with 0xA5 (box/query frames with 0xAA), are never sent over LoRa, and a
/// gateway built without the option ignores them (no answer).
///   0xA5  TYPE  LEN  DATA...  CRC8  0xBB       CRC8 (poly 0x31) over TYPE, LEN, DATA
///   PC -> gateway  'W'  SSID 0x00 password     set WiFi (the gateway stores it only if it changed)
///                  'W'  (no data)              WiFi off (the gateway forgets the SSID and turns its WiFi off)
///                  'S'  (no data)              status request
///   gateway -> PC  'w'  0 saved / 1 unchanged / 2 bad format
///                  's'  state, -RSSI (dBm), seconds since the last good write in two 7-bit bytes (16383 = never)
/// Every DATA byte of a reply is below 0x80, so a reply can never look like the start of a 0xAA status frame.
/// </summary>
public sealed class GatewayCloudLink
{
	public const byte Stx = 0xA5;

	private const byte Etx = 0xBB;

	public const int MaxSsidBytes = 32;

	public const int MaxPasswordBytes = 63;

	private readonly List<byte> _rx = new List<byte>();

	private readonly object _lock = new object();

	private TaskCompletionSource<int> _ackWaiter;

	private TaskCompletionSource<GatewayCloudStatus> _statusWaiter;

	/// <summary>Feeds one byte received from the gateway (serial thread). Bytes of 0xAA frames are skipped.</summary>
	public void Feed(byte b)
	{
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
			if ((_rx.Count == 2 && b != (byte)'w' && b != (byte)'s') || (_rx.Count == 3 && b > 8))
			{
				// Not a reply of the gateway (only 'w' / 's', a few bytes long): start over.
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
			if (f[1] == (byte)'w' && len >= 1)
			{
				_ackWaiter?.TrySetResult(f[3]);
				_ackWaiter = null;
			}
			else if (f[1] == (byte)'s' && len >= 4)
			{
				int age = f[5] | (f[6] << 7);
				_statusWaiter?.TrySetResult(new GatewayCloudStatus
				{
					State = f[3],
					WifiRssi = -f[4],
					SecondsSinceWrite = (age >= 16383) ? -1 : age
				});
				_statusWaiter = null;
			}
		}
	}

	/// <summary>
	/// Sends the WiFi settings. Returns 0 saved, 1 unchanged, 2 refused (bad length), or null when the gateway did not
	/// answer (port closed, or firmware without the cloud option).
	/// </summary>
	public async Task<int?> SendWifiAsync(RFGateway gateway, string ssid, string password, int timeoutMs = 1500)
	{
		byte[] s = Encoding.UTF8.GetBytes(ssid ?? string.Empty);
		byte[] p = Encoding.UTF8.GetBytes(password ?? string.Empty);
		if (s.Length < 1 || s.Length > MaxSsidBytes || p.Length > MaxPasswordBytes)
		{
			return 2;
		}
		byte[] data = new byte[s.Length + 1 + p.Length];
		Array.Copy(s, 0, data, 0, s.Length);
		Array.Copy(p, 0, data, s.Length + 1, p.Length);
		return await SendWifiFrameAsync(gateway, data, timeoutMs);
	}

	/// <summary>Turns the gateway's WiFi off (it forgets the SSID). Returns 0 done, 1 already off, or null when it did not answer.</summary>
	public Task<int?> SendWifiOffAsync(RFGateway gateway, int timeoutMs = 1500)
	{
		return SendWifiFrameAsync(gateway, new byte[0], timeoutMs);
	}

	private async Task<int?> SendWifiFrameAsync(RFGateway gateway, byte[] data, int timeoutMs)
	{
		TaskCompletionSource<int> waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_lock)
		{
			_ackWaiter = waiter;
		}
		bool answered = await SendAndWaitAsync(gateway, BuildFrame((byte)'W', data), waiter.Task, timeoutMs);
		lock (_lock)
		{
			if (_ackWaiter == waiter)
			{
				_ackWaiter = null;
			}
		}
		return answered ? waiter.Task.Result : (int?)null;
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
