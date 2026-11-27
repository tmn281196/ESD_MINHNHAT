using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace ESDCMS.Device;

public class ControlBox
{
	public SerialPort SerialPort;

	private ViewModel viewModel;

	public static byte Prefix1 = 68;

	public static byte Prefix2 = 69;

	public static byte Suffix = 86;

	public List<int> Buffer = new List<int>();

	public const byte OpMotor1 = 1;

	public const byte OpMotor2 = 2;

	public const byte OpMotor3 = 3;

	/// <summary>Start All (SWON). Opcode 4 (Motor 4) was removed: nothing is wired to it on either line.</summary>
	public const byte OpSwOn = 5;

	/// <summary>Motor channels 0..MotorCount-1 (Motor 1..3); the channel after them is Start All.</summary>
	public const int MotorCount = 3;

	/// <summary>Opcode of each channel index: Motor 1..3, then Start All.</summary>
	public static readonly byte[] ChannelOpcodes = new byte[MotorCount + 1] { OpMotor1, OpMotor2, OpMotor3, OpSwOn };

	public const int SwOnChannel = MotorCount;

	private readonly object _txLock = new object();

	private DateTime _lastPortClosedLog = DateTime.MinValue;

	private static readonly TimeSpan PortClosedLogInterval = TimeSpan.FromSeconds(5);

	private void LogPortNotOpen()
	{
		DateTime now = DateTime.Now;
		if (now - _lastPortClosedLog < PortClosedLogInterval)
		{
			return;
		}
		_lastPortClosedLog = now;
		string port = string.IsNullOrEmpty(SerialPort?.PortName) ? "(no port)" : SerialPort.PortName;
		Console.WriteLine("TX -> ControlBox: " + port + " is not open - command skipped (check USB cable / COM port)");
	}

	/// <summary>State of each channel (index as <see cref="ChannelOpcodes"/>): Motor 1..3, Start All.</summary>
	public ObservableCollection<bool> Channels { get; set; } = new ObservableCollection<bool> { false, false, false, false };

	public event EventHandler SerialReceiverHandler;

	public ControlBox(ViewModel viewModel)
	{
		this.viewModel = viewModel;
		SerialPort = SerialPortSafe.Create(BaudRate);
	}

	private const int BaudRate = 9600;

	private readonly object _portLock = new object();

	private void PortDataReceived(object sender, SerialDataReceivedEventArgs e)
	{
		SerialReceiverHandler?.Invoke(sender, null);
	}

	public void OpenCommunication()
	{
		string portName = viewModel.Config.ControlBoxCOM;
		if (!PortNames.IsPort(portName))
		{
			Console.WriteLine("ControlBox: no COM port selected, skip open");
			return;
		}
		lock (_portLock)
		{
			if (SerialPort != null && SerialPort.IsOpen)
			{
				if (string.Equals(SerialPort.PortName, portName, StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine("ControlBox: " + portName + " already open");
					return;
				}
				CloseLocked();
			}
			// Always open on a fresh object: a port object that failed to close stays unusable.
			SerialPort port = SerialPortSafe.Create(BaudRate, portName);
			SerialPort = port;
			try
			{
				Console.WriteLine("ControlBox: opening " + portName + "...");
				SerialPortSafe.Open(port);
				if (port.IsOpen)
				{
					SolidColorBrush ok = new SolidColorBrush(Color.FromRgb(117, 247, 17));
					ok.Freeze();
					viewModel.ConnectionStatusControlBoxColor = ok;
					port.DataReceived += PortDataReceived;
					Console.WriteLine($"ControlBox: opened {portName} ({port.BaudRate} baud)");
				}
			}
			catch (Exception ex)
			{
				SerialPortSafe.Close(port);
				SerialPort = SerialPortSafe.Create(BaudRate, portName);
				Console.WriteLine("ControlBox: open failed - " + SerialPortSafe.DescribeOpenError(portName, ex));
			}
		}
	}

	public void CloseCommunication()
	{
		lock (_portLock)
		{
			CloseLocked();
		}
	}

	private void CloseLocked()
	{
		SerialPort port = SerialPort;
		if (port == null)
		{
			return;
		}
		string portName = port.PortName;
		bool wasOpen = port.IsOpen;
		port.DataReceived -= PortDataReceived;
		bool clean = SerialPortSafe.Close(port);
		SerialPort = SerialPortSafe.Create(BaudRate, portName);
		viewModel.ConnectionStatusControlBoxColor = Brushes.White;
		if (!clean)
		{
			Console.WriteLine("ControlBox: closing " + portName + " timed out (USB-serial driver not responding) - unplug/replug the USB cable");
		}
		else if (wasOpen)
		{
			Console.WriteLine("ControlBox: closed " + portName);
		}
	}

	public static byte CalculateCheckSumXOR(byte[] byteData)
	{
		byte chkSumByte = 0;
		for (int i = 0; i < byteData.Length; i++)
		{
			chkSumByte ^= byteData[i];
		}
		return chkSumByte;
	}

	public static byte CreateChecksum(byte[] bytes)
	{
		return CalculateCheckSumXOR(bytes);
	}

	public void ConsoleWriteLine(string content)
	{
		string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		Console.WriteLine("[" + formattedTime + "]: " + content);
	}

	public void SendBytes(byte[] buf)
	{
		try
		{
			SerialPort port = SerialPort;
			port.DiscardInBuffer();
			port.Write(buf, 0, buf.Length);
		}
		catch (Exception)
		{
		}
	}

	public bool Send(byte[] data)
	{
		Buffer.Clear();
		if (SerialPort.IsOpen)
		{
			try
			{
				SendBytes(data);
				return true;
			}
			catch (IOException)
			{
				return false;
			}
			catch (TimeoutException)
			{
				return false;
			}
		}
		return false;
	}

	public static byte[] GetFrame(byte[] datas, bool IsNoSize = false)
	{
		if (datas == null)
		{
			return null;
		}
		List<byte> dataToSend = datas.ToList();
		if (!IsNoSize)
		{
			if (datas.Length > 1)
			{
				dataToSend.Insert(0, (byte)(dataToSend.Count + 1));
			}
			else
			{
				dataToSend.Add(0);
			}
		}
		dataToSend.Insert(0, Prefix2);
		dataToSend.Insert(0, Prefix1);
		byte checksum = CalculateCheckSumXOR(dataToSend.ToArray());
		dataToSend.Add(checksum);
		dataToSend.Add(Suffix);
		Console.WriteLine("TX -> ControlBox: " + string.Join(" ", dataToSend.Select((byte b) => b.ToString("X2"))));
		return dataToSend.ToArray();
	}

	public static bool GetResponse(byte[] datas, byte[] compare)
	{
		if (datas == null)
		{
			return false;
		}
		List<byte> dataToSend = datas.ToList();
		dataToSend.Insert(0, Prefix2);
		dataToSend.Insert(0, Prefix1);
		byte checksum = CalculateCheckSumXOR(dataToSend.ToArray());
		dataToSend.Add(checksum);
		dataToSend.Add(Suffix);
		byte[] byteData = dataToSend.ToArray();
		if (byteData.Length != compare.Length)
		{
			return false;
		}
		for (int i = 0; i < compare.Length; i++)
		{
			if (byteData[i] != compare[i])
			{
				return false;
			}
		}
		return true;
	}

	public static string ChannelName(int channelIndex)
	{
		return (channelIndex == SwOnChannel) ? "Start All" : $"Motor {channelIndex + 1}";
	}

	public bool SendChannel(int channelIndex, bool on, int timeOut = -1)
	{
		if (timeOut < 0)
		{
			timeOut = viewModel?.Config?.EffectiveTxTimeoutMs ?? Config.DefaultTxTimeoutMs;
		}
		if (channelIndex < 0 || channelIndex >= ChannelOpcodes.Length)
		{
			return false;
		}
		byte[] payload = new byte[2]
		{
			ChannelOpcodes[channelIndex],
			(byte)(on ? 1u : 0u)
		};
		return SendTX(payload, timeOut);
	}

	public bool SendChannel(int channelIndex)
	{
		if (channelIndex < 0 || channelIndex >= Channels.Count)
		{
			return false;
		}
		return SendChannel(channelIndex, Channels[channelIndex]);
	}

	private static bool WaitForBytes(SerialPort port, int count, DateTime start, int TimeOut)
	{
		while (port.BytesToRead < count)
		{
			if (DateTime.Now.Subtract(start).TotalMilliseconds >= (double)TimeOut)
			{
				return false;
			}
			Thread.Sleep(1);
		}
		return true;
	}

	public bool SendTX(byte[] buf, int TimeOut)
	{
		// Work on one port object for the whole exchange: the field may be swapped by a reconnect.
		SerialPort port = SerialPort;
		if (port == null || !port.IsOpen)
		{
			LogPortNotOpen();
			return false;
		}
		lock (_txLock)
		{
			try
			{
				if (!port.IsOpen)
				{
					LogPortNotOpen();
					return false;
				}
				byte[] frameToSend = GetFrame(buf);
				port.DiscardInBuffer();
				port.Write(frameToSend, 0, frameToSend.Length);
				DateTime start = DateTime.Now;
				byte startByte;
				do
				{
					if (!WaitForBytes(port, 1, start, TimeOut))
					{
						Console.WriteLine($"RX <- ControlBox: no response (timeout {TimeOut}ms)");
						return false;
					}
					startByte = (byte)port.ReadByte();
				}
				while (startByte != Prefix1);
				if (!WaitForBytes(port, 2, start, TimeOut))
				{
					Console.WriteLine($"RX <- ControlBox: incomplete header (timeout {TimeOut}ms)");
					return false;
				}
				byte secondByte = (byte)port.ReadByte();
				byte len = (byte)port.ReadByte();
				if (secondByte != Prefix2)
				{
					Console.WriteLine($"RX <- ControlBox: bad Prefix2 (0x{secondByte:X2})");
					return false;
				}
				if (!WaitForBytes(port, len + 1, start, TimeOut))
				{
					Console.WriteLine($"RX <- ControlBox: incomplete frame (timeout {TimeOut}ms)");
					return false;
				}
				List<byte> frame = new List<byte> { startByte, secondByte, len };
				for (int i = 0; i < len + 1; i++)
				{
					frame.Add((byte)port.ReadByte());
				}
				byte[] arr = frame.ToArray();
				bool result = arr.Length == frameToSend.Length && arr.SequenceEqual(frameToSend);
				Console.WriteLine("RX <- ControlBox: " + string.Join(" ", arr.Select((byte b) => b.ToString("X2"))) + "  (" + (result ? "OK (echo)" : "mismatch") + ")");
				return result;
			}
			catch (Exception ex)
			{
				Console.WriteLine("TX -> ControlBox: send error - " + ex.Message);
				return false;
			}
		}
	}
}
