#define DEBUG
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ESDCMS.Monitoring;

namespace ESDCMS.Device;

public class ViewModel : INotifyPropertyChanged
{
	private enum ParseState
	{
		WaitSTX,
		WaitNetworkID,
		WaitTxID,
		WaitLen,
		WaitData,
		WaitCRC,
		WaitETX
	}

	public RootCanvas RootCanvas;

	private const int DEVNUM = 20;

	public DeviceManagerPage DeviceManagerPage;

	private const int MaxLogLines = 1000;

	private const byte STX = 170;

	private const byte ETX = 187;

	private ParseState _parseState = ParseState.WaitSTX;

	private byte _networkID;

	private byte _txID;

	private byte _dataLen;

	private byte[] _dataBuf = new byte[255];

	private byte _dataIdx;

	private byte _crcRx;

	private readonly List<byte> _frameBytes = new List<byte>();

	private bool? _lastConveyorWarning = null;

	private readonly SemaphoreSlim _controlBoxGate = new SemaphoreSlim(1, 1);

	private volatile bool _controlsUnlocked = false;

	private readonly object _conveyorQueueLock = new object();

	private bool? _desiredConveyorWarning = null;

	private DispatcherTimer _conveyorTimer;

	private const int ConveyorPollMs = 200;

	public List<string> SerialPorts { get; set; } = new List<string>();

	public Config Config { get; set; }

	public string StringLog { get; set; } = string.Empty;

	public ObservableCollection<string> LogLines { get; set; } = new ObservableCollection<string>();

	public ObservableCollection<ESDBox> ESDBoxes { get; set; } = new ObservableCollection<ESDBox>();

	public SolidColorBrush ConnectionStatusControlBoxColor { get; set; } = Brushes.White;

	public SolidColorBrush ConnectionStatusRFColor { get; set; } = Brushes.White;

	public string ConveyorStatusText { get; set; } = "Conveyor —";

	public SolidColorBrush ConveyorStatusColor { get; set; } = Brushes.White;

	/// <summary>Soft warning bar above the console log: shown while a box in use is Offline.</summary>
	public bool HasOfflineAlert { get; set; }

	public string OfflineAlertText { get; set; } = string.Empty;

	/// <summary>
	/// Recomputes the offline alert on the UI thread: the boxes in use that are Offline, e.g.
	/// "Box 03, 07 offline - please check the power supply." (text in the current UI language).
	/// </summary>
	public void UpdateOfflineAlert()
	{
		List<string> ids = new List<string>();
		List<BlockDevice> tiles = RootCanvas?.DynamicDevicesList;
		for (int i = 0; i < ESDBoxes.Count; i++)
		{
			bool used = tiles != null && i < tiles.Count && tiles[i].IsUsed;
			if (used && ESDBoxes[i].Status == DeviceStatus.Offline)
			{
				ids.Add((i + 1).ToString("D2"));
			}
		}
		HasOfflineAlert = ids.Count > 0;
		OfflineAlertText = (ids.Count == 0) ? string.Empty : string.Format(Lang.T("S.Alert.Offline"), string.Join(", ", ids));
	}

	public event PropertyChangedEventHandler PropertyChanged;

	public void AppendLog(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		Action add = delegate
		{
			LogLines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
			while (LogLines.Count > 1000)
			{
				LogLines.RemoveAt(0);
			}
		};
		Dispatcher disp = Application.Current?.Dispatcher;
		if (disp == null || disp.CheckAccess())
		{
			add();
		}
		else
		{
			disp.BeginInvoke(add);
		}
	}

	private byte Crc8(byte[] data, int len)
	{
		byte crc = 0;
		for (int i = 0; i < len; i++)
		{
			crc ^= data[i];
			for (int j = 0; j < 8; j++)
			{
				crc = (((crc & 0x80) != 0) ? ((byte)((crc << 1) ^ 0x31)) : ((byte)(crc << 1)));
			}
		}
		return crc;
	}

	private void FeedByte(byte b)
	{
		switch (_parseState)
		{
		case ParseState.WaitSTX:
			if (b == 170)
			{
				_frameBytes.Clear();
				_frameBytes.Add(b);
				_parseState = ParseState.WaitNetworkID;
			}
			break;
		case ParseState.WaitNetworkID:
			_frameBytes.Add(b);
			_networkID = b;
			byte expectedNetworkID = Config?.NetworkID ?? Config.DefaultNetworkID;
			_parseState = ((b == expectedNetworkID) ? ParseState.WaitTxID : ParseState.WaitSTX);
			break;
		case ParseState.WaitTxID:
			_frameBytes.Add(b);
			_txID = b;
			_parseState = ParseState.WaitLen;
			break;
		case ParseState.WaitLen:
			_frameBytes.Add(b);
			_dataLen = b;
			_dataIdx = 0;
			_parseState = ((_dataLen > 0) ? ParseState.WaitData : ParseState.WaitCRC);
			break;
		case ParseState.WaitData:
			_frameBytes.Add(b);
			_dataBuf[_dataIdx++] = b;
			if (_dataIdx >= _dataLen)
			{
				_parseState = ParseState.WaitCRC;
			}
			break;
		case ParseState.WaitCRC:
			_frameBytes.Add(b);
			_crcRx = b;
			_parseState = ParseState.WaitETX;
			break;
		case ParseState.WaitETX:
		{
			_frameBytes.Add(b);
			_parseState = ParseState.WaitSTX;
			if (b != 187)
			{
				break;
			}
			string frameHex = string.Join(" ", _frameBytes.Select((byte x) => x.ToString("X2")));
			byte lineId = _networkID;
			byte boxId = _txID;
			char statusCode = (char)_dataBuf[0];
			byte[] crcBuf = new byte[_dataLen + 3];
			crcBuf[0] = _networkID;
			crcBuf[1] = _txID;
			crcBuf[2] = _dataLen;
			Array.Copy(_dataBuf, 0, crcBuf, 3, _dataLen);
			bool crcOk = Crc8(crcBuf, crcBuf.Length) == _crcRx;
			if (_txID < 1 || _txID > ESDBoxes.Count)
			{
				Console.WriteLine($"RX <- RFGateway: {frameHex}  | Line {lineId:D2} Box {boxId:D2} (invalid Box ID)");
				break;
			}
			if (statusCode == CmdQuery)
			{
				// A status query (from another PC on the same Net ID) - not a box status.
				break;
			}
			if (crcOk)
			{
				NotifyBoxAnswered(boxId);
			}
			ESDBox esdBox = ESDBoxes[_txID - 1];
			// BeginInvoke (not Invoke): the serial event thread must never wait for the UI thread,
			// otherwise SerialPort.Close() and the UI can deadlock and the COM port stays locked.
			Dispatcher dispatcher = Application.Current?.Dispatcher;
			if (dispatcher == null || dispatcher.HasShutdownStarted)
			{
				break;
			}
			dispatcher.BeginInvoke((Action)delegate
			{
				if (!crcOk)
				{
					esdBox.Status = DeviceStatus.Error;
					Console.WriteLine($"RX <- RFGateway: {frameHex}  | Line {lineId:D2} Box {boxId:D2} CRC FAIL -> Error");
				}
				else
				{
					DeviceStatus status = esdBox.Status;
					switch (statusCode)
					{
					case 'A':
						esdBox.Status = DeviceStatus.Active;
						break;
					case 'W':
						esdBox.Status = DeviceStatus.Warning;
						break;
					case 'I':
						esdBox.Status = DeviceStatus.Inactive;
						break;
					default:
						esdBox.Status = DeviceStatus.Unknown;
						break;
					}
					if (status != esdBox.Status)
					{
						Console.WriteLine($"RX <- RFGateway: {frameHex}  | Line {lineId:D2} Box {boxId:D2} {esdBox.Status}");
					}
				}
			});
			break;
		}
		}
	}

	private readonly object _rxLock = new object();

	private DateTime _lastRxErrorLog = DateTime.MinValue;

	public void RFGateway_SerialReceiverHandler(object sender, EventArgs e)
	{
		// Runs on a SerialPort thread-pool thread: an exception escaping here terminates the whole
		// process (e.g. USB cable pulled while data is arriving), so everything is caught.
		try
		{
			if (!(sender is SerialPort port) || !port.IsOpen)
			{
				return;
			}
			int count = port.BytesToRead;
			if (count <= 0)
			{
				return;
			}
			byte[] buf = new byte[count];
			int read = port.Read(buf, 0, count);
			lock (_rxLock)
			{
				for (int i = 0; i < read; i++)
				{
					FeedByte(buf[i]);
				}
			}
		}
		catch (Exception ex)
		{
			DateTime now = DateTime.Now;
			if (now - _lastRxErrorLog > TimeSpan.FromSeconds(5))
			{
				_lastRxErrorLog = now;
				Console.WriteLine("RFGateway: receive error - " + ex.Message);
			}
		}
	}

	public async Task RunControlBoxExclusiveAsync(Func<Task> action)
	{
		await _controlBoxGate.WaitAsync();
		try
		{
			await action();
		}
		finally
		{
			_lastConveyorWarning = null;
			_controlBoxGate.Release();
		}
	}

	public void SetControlsUnlocked(bool unlocked)
	{
		_controlsUnlocked = unlocked;
		if (!unlocked)
		{
			_lastConveyorWarning = null;
		}
	}

	private void StartConveyorTimer()
	{
		_conveyorTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(200.0)
		};
		_conveyorTimer.Tick += ConveyorTimer_Tick;
		_conveyorTimer.Start();
	}

	public void EnqueueControlBoxUpdate()
	{
		List<BlockDevice> devices = RootCanvas.DynamicDevicesList.Where((BlockDevice item) => item.IsUsed).ToList();
		if (devices.Count == 0)
		{
			return;
		}
		bool warning = devices.Any((BlockDevice item) => item.Status == DeviceStatus.Warning);
		ConveyorStatusText = (warning ? "Conveyor Stopped" : "Conveyor Moving");
		ConveyorStatusColor = (warning ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45F248")));
		lock (_conveyorQueueLock)
		{
			_desiredConveyorWarning = warning;
		}
	}

	private async void ConveyorTimer_Tick(object sender, EventArgs e)
	{
		if (_controlsUnlocked)
		{
			return;
		}
		bool desired;
		lock (_conveyorQueueLock)
		{
			if (!_desiredConveyorWarning.HasValue)
			{
				return;
			}
			desired = _desiredConveyorWarning.Value;
		}
		if (_lastConveyorWarning == desired || !_controlBoxGate.Wait(0))
		{
			return;
		}
		try
		{
			if (await SendControlBoxState(desired))
			{
				_lastConveyorWarning = desired;
			}
			else
			{
				Console.WriteLine($"Conveyor: send FAILED (warning={desired}) -> will retry");
			}
		}
		finally
		{
			_controlBoxGate.Release();
		}
	}

	private async Task<bool> AssertChannel(int channel, bool on)
	{
		DeviceManagerPage.ControlBox.Channels[channel] = on;
		int retries = Config?.EffectiveTxRetries ?? Config.DefaultTxRetries;
		int retryDelayMs = Config?.EffectiveTxRetryDelayMs ?? Config.DefaultTxRetryDelayMs;
		for (int i = 0; i < retries; i++)
		{
			if (await Task.Run(() => DeviceManagerPage.ControlBox.SendChannel(channel, on)))
			{
				return true;
			}
			if (i < retries - 1 && retryDelayMs > 0)
			{
				await Task.Delay(retryDelayMs);
			}
		}
		Console.WriteLine("Conveyor: " + ControlBox.ChannelName(channel) + " -> " + (on ? "STOP" : "RUN") + " not acknowledged");
		return false;
	}

	private async Task<bool> AssertMotorChannels(bool on)
	{
		bool all = true;
		int gapMs = Config?.EffectiveTxGapMs ?? Config.DefaultTxGapMs;
		for (int channel = 0; channel <= 3; channel++)
		{
			if (channel > 0 && gapMs > 0)
			{
				await Task.Delay(gapMs);
			}
			bool flag = all;
			all = flag & await AssertChannel(channel, on);
		}
		return all;
	}

	private async Task<bool> SendControlBoxState(bool warning)
	{
		Console.WriteLine(warning ? "Conveyor: STOPPED (warning detected)" : "Conveyor: MOVING (all clear)");
		bool ok = await AssertMotorChannels(warning);
		if (!ok)
		{
			Console.WriteLine("Conveyor: ch0..3 (" + (warning ? "STOP" : "MOVING") + ") - some channels not acknowledged");
		}
		return ok;
	}

	private static SolidColorBrush TextColorForStatus(DeviceStatus status)
	{
		return status switch
		{
			DeviceStatus.Active => Brushes.Black, 
			DeviceStatus.Inactive => Brushes.Black,
			DeviceStatus.Warning => Brushes.White,
			_ => Brushes.White, 
		};
	}

	public async void StatusChangeActionFunc(ESDBox esdBox)
	{
		await Application.Current.Dispatcher.BeginInvoke((Action)delegate
		{
			try
			{
				BlockDevice blockDevice = RootCanvas.DynamicDevicesList[esdBox.ID - 1];
				blockDevice.Status = esdBox.Status;
				if (blockDevice.IsUsed)
				{
					blockDevice.NewRectangle.Fill = esdBox.ColorState;
					blockDevice.TextBlock.Foreground = TextColorForStatus(esdBox.Status);
				}
				else
				{
					blockDevice.ApplyBaseColor();
					blockDevice.TextBlock.Foreground = Brushes.White;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
			UpdateOfflineAlert();
		});
		EnqueueControlBoxUpdate();
	}

	public void StopTimers()
	{
		_conveyorTimer?.Stop();
		_pollCts.Cancel();
	}

	public ViewModel()
	{
		for (int i = 0; i < 20; i++)
		{
			ESDBox esbBox = new ESDBox(i + 1);
			ESDBoxes.Add(esbBox);
			esbBox.StatusChangeAction = StatusChangeActionFunc;
		}
		StartConveyorTimer();
		// Run the poller on the UI dispatcher: box status and the monitor layout are UI objects.
		Application.Current?.Dispatcher.BeginInvoke((Action)delegate
		{
			_ = StatusPollLoopAsync(_pollCts.Token);
		});
	}

	// ---------------------------------------------------------------------------------------------
	// Status query (PC -> RF gateway -> LoRa -> boxes). Needs the v2 box and gateway firmware.
	//   PC -> box : AA NetID ID 01 '?' CRC BB   (ID = box ID; the firmware also accepts 0xFF = all)
	//   box -> PC : the normal status frame 'A' / 'W' / 'I'
	// Only when the RF port opens (program start, Refresh Connection) each used box is asked in
	// turn (up to PollRetries queries each), so the first status is known without waiting for an
	// input change. After that the boxes report changes themselves; a click on a box tile asks
	// that box again. A box that does not answer is shown as Offline (blue, like Unknown) until it is heard again,
	// and every AskAgainSec seconds the Offline boxes are asked again.
	// ---------------------------------------------------------------------------------------------

	private const byte CmdQuery = (byte)'?';

	/// <summary>The ESP32 gateway can reboot when its COM port is opened; wait before the first query.</summary>
	private const int StartupQueryDelayMs = 2500;

	private const int GapBetweenBoxesMs = 50;

	private readonly CancellationTokenSource _pollCts = new CancellationTokenSource();

	private readonly object _waiterLock = new object();

	private readonly TaskCompletionSource<bool>[] _answerWaiters = new TaskCompletionSource<bool>[DEVNUM + 1];

	private readonly long[] _lastAnswerTicks = new long[DEVNUM + 1];

	/// <summary>One query on the air at a time: the startup round and a click on a box tile take turns.</summary>
	private readonly SemaphoreSlim _radioLock = new SemaphoreSlim(1, 1);

	/// <summary>Box IDs with a click request in progress (UI thread only), so repeated clicks do not queue up.</summary>
	private readonly bool[] _clickBusy = new bool[DEVNUM + 1];

	/// <summary>Called from the serial thread for every valid status frame of a box.</summary>
	private void NotifyBoxAnswered(byte boxId)
	{
		if (boxId < 1 || boxId > DEVNUM)
		{
			return;
		}
		Interlocked.Exchange(ref _lastAnswerTicks[boxId], DateTime.UtcNow.Ticks);
		TaskCompletionSource<bool> waiter;
		lock (_waiterLock)
		{
			waiter = _answerWaiters[boxId];
			_answerWaiters[boxId] = null;
		}
		waiter?.TrySetResult(true);
	}

	private byte[] BuildQueryFrame(byte targetId)
	{
		byte netId = Config?.NetworkID ?? Config.DefaultNetworkID;
		byte[] crcBuf = new byte[4] { netId, targetId, 1, CmdQuery };
		return new byte[7] { STX, netId, targetId, 1, CmdQuery, Crc8(crcBuf, crcBuf.Length), ETX };
	}

	private async Task<bool> SendQueryAsync(RFGateway gateway, byte targetId)
	{
		byte[] frame = BuildQueryFrame(targetId);
		// The write can block up to the port WriteTimeout if the USB driver hangs: keep it off the UI thread.
		return await Task.Run(() => gateway.Send(frame));
	}

	/// <summary>Box IDs (1..20) placed on the monitor layout.</summary>
	private List<byte> UsedBoxIds()
	{
		List<byte> ids = new List<byte>();
		List<bool> active = RootCanvas?.ConfigData?.DevicesIsActive;
		if (RootCanvas?.DynamicDevicesList != null)
		{
			for (int i = 0; i < RootCanvas.DynamicDevicesList.Count && i < DEVNUM; i++)
			{
				if (RootCanvas.DynamicDevicesList[i].IsUsed)
				{
					ids.Add((byte)(i + 1));
				}
			}
		}
		else if (active != null)
		{
			for (int i = 0; i < active.Count && i < DEVNUM; i++)
			{
				if (active[i])
				{
					ids.Add((byte)(i + 1));
				}
			}
		}
		return ids;
	}

	private async Task StatusPollLoopAsync(CancellationToken ct)
	{
		int handledGeneration = -1;
		DateTime lastRound = DateTime.MaxValue;
		while (!ct.IsCancellationRequested)
		{
			try
			{
				RFGateway gateway = DeviceManagerPage?.RFGateway;
				if (gateway == null || !gateway.IsOpen)
				{
					await Task.Delay(500, ct);
					continue;
				}
				if (gateway.OpenGeneration != handledGeneration)
				{
					// The RF port was (re)opened: ask every box once for its first status.
					handledGeneration = gateway.OpenGeneration;
					await Task.Delay(StartupQueryDelayMs, ct);
					if (gateway.IsOpen)
					{
						await QueryAllBoxesAsync(gateway, ct);
					}
					lastRound = DateTime.Now;
					continue;
				}
				// Read every time so a change in the settings applies at once.
				int askAgainSec = Config?.EffectiveAskAgainSec ?? Config.DefaultAskAgainSec;
				if (askAgainSec <= 0 || DateTime.Now < lastRound.AddSeconds(askAgainSec))
				{
					await Task.Delay(500, ct);
					continue;
				}
				await AskOfflineBoxesAgainAsync(gateway, ct);
				lastRound = DateTime.Now;
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				Console.WriteLine("Status: poll error - " + ex.Message);
				try
				{
					await Task.Delay(1000, ct);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
	}

	/// <summary>Startup: asks each used box in turn for its first status and logs who answered.</summary>
	private async Task QueryAllBoxesAsync(RFGateway gateway, CancellationToken ct)
	{
		List<byte> used = UsedBoxIds();
		int timeoutMs = Config?.EffectivePollReplyTimeoutMs ?? Config.DefaultPollReplyTimeoutMs;
		int tries = Config?.EffectivePollRetries ?? Config.DefaultPollRetries;
		Console.WriteLine($"Status: asking each box for its status ({used.Count} in use)");
		List<byte> silent = new List<byte>();
		foreach (byte id in used)
		{
			ct.ThrowIfCancellationRequested();
			if (!gateway.IsOpen)
			{
				return;
			}
			if (!await AskBoxAsync(gateway, id, tries, timeoutMs, ct))
			{
				silent.Add(id);
				if (gateway.IsOpen)
				{
					ESDBoxes[id - 1].Status = DeviceStatus.Offline;
				}
			}
			await Task.Delay(GapBetweenBoxesMs, ct);
		}
		Console.WriteLine((silent.Count == 0)
			? "Status: all boxes in use answered"
			: $"Status: no answer after {tries} queries from box " + string.Join(", ", silent.Select((byte id) => id.ToString("D2"))) + " -> Offline");
	}

	/// <summary>Asks the used boxes that have not answered (Offline / Unknown) again.</summary>
	private async Task AskOfflineBoxesAgainAsync(RFGateway gateway, CancellationToken ct)
	{
		List<byte> ids = UsedBoxIds().Where((byte id) => ESDBoxes[id - 1].Status == DeviceStatus.Offline || ESDBoxes[id - 1].Status == DeviceStatus.Unknown).ToList();
		if (ids.Count == 0)
		{
			return;
		}
		int timeoutMs = Config?.EffectivePollReplyTimeoutMs ?? Config.DefaultPollReplyTimeoutMs;
		int tries = Config?.EffectivePollRetries ?? Config.DefaultPollRetries;
		Console.WriteLine("Status: asking again box " + string.Join(", ", ids.Select((byte id) => id.ToString("D2"))));
		List<byte> answered = new List<byte>();
		List<byte> silent = new List<byte>();
		foreach (byte id in ids)
		{
			ct.ThrowIfCancellationRequested();
			if (!gateway.IsOpen)
			{
				return;
			}
			if (await AskBoxAsync(gateway, id, tries, timeoutMs, ct))
			{
				answered.Add(id);
			}
			else
			{
				silent.Add(id);
				ESDBox box = ESDBoxes[id - 1];
				if (gateway.IsOpen && box.Status != DeviceStatus.Offline)
				{
					box.Status = DeviceStatus.Offline;
				}
			}
			await Task.Delay(GapBetweenBoxesMs, ct);
		}
		if (answered.Count > 0)
		{
			Console.WriteLine("Status: box " + string.Join(", ", answered.Select((byte id) => id.ToString("D2"))) + " answered - back online");
		}
		if (silent.Count > 0)
		{
			Console.WriteLine("Status: still no answer from box " + string.Join(", ", silent.Select((byte id) => id.ToString("D2"))) + " -> Offline");
		}
	}

	/// <summary>Sends up to <paramref name="tries"/> queries to one box; true as soon as it answers.</summary>
	private async Task<bool> AskBoxAsync(RFGateway gateway, byte id, int tries, int timeoutMs, CancellationToken ct)
	{
		await _radioLock.WaitAsync(ct);
		try
		{
			for (int attempt = 1; attempt <= tries; attempt++)
			{
				if (!gateway.IsOpen)
				{
					return false;
				}
				bool answered = false;
				TaskCompletionSource<bool> waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				lock (_waiterLock)
				{
					_answerWaiters[id] = waiter;
				}
				if (await SendQueryAsync(gateway, id))
				{
					Task done = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs, ct));
					answered = done == waiter.Task;
				}
				else
				{
					await Task.Delay(timeoutMs, ct);
				}
				lock (_waiterLock)
				{
					if (_answerWaiters[id] == waiter)
					{
						_answerWaiters[id] = null;
					}
				}
				ct.ThrowIfCancellationRequested();
				if (answered)
				{
					return true;
				}
			}
			return false;
		}
		finally
		{
			_radioLock.Release();
		}
	}

	/// <summary>Click on a box tile (index 0-based): ask that box for its status now.</summary>
	public void RequestBoxStatus(int index)
	{
		_ = RequestBoxStatusAsync(index);
	}

	private async Task RequestBoxStatusAsync(int index)
	{
		if (index < 0 || index >= DEVNUM)
		{
			return;
		}
		byte id = (byte)(index + 1);
		if (_clickBusy[id])
		{
			return;
		}
		if (!UsedBoxIds().Contains(id))
		{
			Console.WriteLine($"Status: Box {id:D2} is not in use (right-click to enable it while unlocked)");
			return;
		}
		RFGateway gateway = DeviceManagerPage?.RFGateway;
		if (gateway == null || !gateway.IsOpen)
		{
			Console.WriteLine($"Status: cannot ask Box {id:D2} - RF Gateway is not connected");
			return;
		}
		_clickBusy[id] = true;
		try
		{
			int timeoutMs = Config?.EffectivePollReplyTimeoutMs ?? Config.DefaultPollReplyTimeoutMs;
			int tries = Config?.EffectivePollRetries ?? Config.DefaultPollRetries;
			Console.WriteLine($"Status: asking Box {id:D2} for its status...");
			bool answered = await AskBoxAsync(gateway, id, tries, timeoutMs, _pollCts.Token);
			if (answered)
			{
				Console.WriteLine($"Status: Box {id:D2} answered");
				return;
			}
			ESDBox box = ESDBoxes[id - 1];
			if (gateway.IsOpen && box.Status != DeviceStatus.Offline)
			{
				box.Status = DeviceStatus.Offline;
			}
			Console.WriteLine($"Status: Box {id:D2} no answer after {tries} queries -> Offline");
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Status: asking Box {id:D2} failed - " + ex.Message);
		}
		finally
		{
			_clickBusy[id] = false;
		}
	}

	public void OpenNetwork()
	{
		StringLog = "Ready";
		ResetStates();
	}

	public async Task CloseNetWork()
	{
		StringLog = "Stopped";
		ResetStates();
	}

	public void ResetStates()
	{
		foreach (ESDBox esdBox in ESDBoxes)
		{
			esdBox.Status = DeviceStatus.Unknown;
		}
	}

}
