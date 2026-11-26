using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlBoxSim;

/// <summary>
/// Simulates the ESDCMS Control Box (Arduino Mega running Firmware_ControBox/Line1).
/// Serial 9600 8N1. Frame from the PC: 44 45 03 OP STATE XOR 56 (OP 1..4 = Motor 1..4, 5 = Start All;
/// STATE 1 = pin HIGH = relay ON = motor STOP, 0 = LOW = RUN; XOR of the 5 bytes before it).
/// A valid frame sets the channel and is echoed back unchanged as the ACK, like the firmware.
/// Put it on one end of a com0com pair and ESDCMS's Control Box port on the other end.
/// Command line: ControlBoxSim.exe COM17 [log.txt]  opens that port at start (and also writes the log to the file).
/// </summary>
public partial class MainWindow : Window
{
	private const byte Prefix1 = 0x44;
	private const byte Prefix2 = 0x45;
	private const byte Suffix = 0x56;
	private const int MaxFrame = 20;
	private const int FrameTimeoutMs = 100;   // the firmware drops a half frame after 100 ms of silence

	private static readonly string[] Names = { "Motor 1", "Motor 2", "Motor 3", "Motor 4", "Start All" };
	private static readonly string[] Labels = { "MI · pin 28", "SOLDER · pin 24", "PACKING · pin 22", "FCT · pin 22", "SWON · pin 30" };

	private readonly bool[] _high = new bool[5];
	private readonly Border[] _tiles = new Border[5];
	private readonly TextBlock[] _tileState = new TextBlock[5];

	private SerialPort _port;

	/// <summary>Optional 2nd command-line argument: every log line is also appended to this file.</summary>
	private string _logFile;
	private readonly object _rxLock = new object();
	private readonly object _txLock = new object();
	private readonly List<byte> _frame = new List<byte>();
	private int _need;
	private DateTime _lastByte = DateTime.MinValue;

	public MainWindow()
	{
		InitializeComponent();
		BuildTiles();
		RefreshPorts();
		UpdateConveyor();
		Closed += delegate { ClosePort(); };
		string[] args = Environment.GetCommandLineArgs();
		if (args.Length > 2)
		{
			_logFile = args[2];
		}
		if (args.Length > 1)
		{
			PortBox.Text = args[1];
			OpenPort(args[1]);
		}
	}

	// ------------------------------------------------------------------ UI

	private void BuildTiles()
	{
		for (int i = 0; i < 5; i++)
		{
			StackPanel sp = new StackPanel { Margin = new Thickness(8) };
			sp.Children.Add(new TextBlock { Text = Names[i], FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White });
			sp.Children.Add(new TextBlock { Text = Labels[i], FontSize = 11, Foreground = Brushes.White, Opacity = 0.85 });
			_tileState[i] = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
			sp.Children.Add(_tileState[i]);
			_tiles[i] = new Border { Child = sp, Margin = new Thickness(0, 0, 8, 0), MinHeight = 90 };
			ChannelGrid.Children.Add(_tiles[i]);
			UpdateTile(i);
		}
	}

	private static SolidColorBrush Hex(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

	private void UpdateTile(int i)
	{
		if (i == 4)
		{
			_tiles[i].Background = _high[i] ? Hex("#FFB300") : Hex("#9E9E9E");
			_tileState[i].Text = _high[i] ? "HIGH (pulse)" : "LOW";
			return;
		}
		_tiles[i].Background = _high[i] ? Hex("#E53935") : Hex("#43A047");
		_tileState[i].Text = _high[i] ? "STOP · HIGH" : "RUN · LOW";
	}

	private void UpdateConveyor()
	{
		bool stopped = _high.Take(4).Any(h => h);
		ConveyorBanner.Background = stopped ? Hex("#C62828") : Hex("#2E7D32");
		ConveyorText.Text = stopped ? "Conveyor STOPPED  (a motor relay is ON)" : "Conveyor RUNNING  (all motor relays OFF)";
	}

	private void Log(string text)
	{
		string line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
		LogList.Items.Add(line);
		if (_logFile != null)
		{
			try
			{
				File.AppendAllText(_logFile, line + Environment.NewLine);
			}
			catch
			{
			}
		}
		while (LogList.Items.Count > 500)
		{
			LogList.Items.RemoveAt(0);
		}
		LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
	}

	private void Ui(Action action) => Dispatcher.BeginInvoke(action);

	private static string HexBytes(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));

	private void RefreshPorts()
	{
		string current = PortBox.Text;
		PortBox.ItemsSource = SerialPort.GetPortNames().OrderBy(n => n).ToList();
		PortBox.Text = current;
	}

	private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPorts();

	private void ClearLog_Click(object sender, RoutedEventArgs e) => LogList.Items.Clear();

	private void Reset_Click(object sender, RoutedEventArgs e)
	{
		for (int i = 0; i < 5; i++)
		{
			_high[i] = false;
			UpdateTile(i);
		}
		UpdateConveyor();
		Log("Reset: all channels LOW");
	}

	// ------------------------------------------------------------------ port

	private void Open_Click(object sender, RoutedEventArgs e)
	{
		if (_port != null)
		{
			ClosePort();
		}
		else
		{
			OpenPort(PortBox.Text.Trim());
		}
	}

	private void OpenPort(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			Log("Pick a COM port first");
			return;
		}
		try
		{
			SerialPort p = new SerialPort(name, 9600, Parity.None, 8, StopBits.One) { ReadTimeout = 500, WriteTimeout = 500 };
			p.DataReceived += Port_DataReceived;
			p.Open();
			_port = p;
			OpenBtn.Content = "Close";
			PortBox.IsEnabled = false;
			PortState.Text = name + " open";
			PortState.Foreground = Hex("#2E7D32");
			Log("Opened " + name + " (9600 8N1) - waiting for ESDCMS");
		}
		catch (Exception ex)
		{
			Log("Open " + name + " failed: " + ex.Message);
		}
	}

	private void ClosePort()
	{
		SerialPort p = _port;
		_port = null;
		if (p == null)
		{
			return;
		}
		try
		{
			p.DataReceived -= Port_DataReceived;
			p.Close();
			p.Dispose();
		}
		catch
		{
		}
		OpenBtn.Content = "Open";
		PortBox.IsEnabled = true;
		PortState.Text = "closed";
		PortState.Foreground = Hex("#757575");
		Log("Closed");
	}

	private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
	{
		try
		{
			SerialPort p = (SerialPort)sender;
			int count = p.BytesToRead;
			if (count <= 0)
			{
				return;
			}
			byte[] buf = new byte[count];
			int read = p.Read(buf, 0, count);
			lock (_rxLock)
			{
				for (int i = 0; i < read; i++)
				{
					Feed(buf[i]);
				}
			}
		}
		catch (Exception ex)
		{
			Ui(() => Log("RX error: " + ex.Message));
		}
	}

	// ------------------------------------------------------------------ protocol (same steps as L1.ino)

	private void Feed(byte b)
	{
		DateTime now = DateTime.Now;
		if (_frame.Count > 0 && (now - _lastByte).TotalMilliseconds > FrameTimeoutMs)
		{
			_frame.Clear();
		}
		_lastByte = now;
		switch (_frame.Count)
		{
		case 0:
			if (b == Prefix1)
			{
				_frame.Add(b);
			}
			return;
		case 1:
			_frame.Clear();
			if (b == Prefix2)
			{
				_frame.Add(Prefix1);
				_frame.Add(b);
			}
			else if (b == Prefix1)
			{
				_frame.Add(b);
			}
			return;
		case 2:
			_need = 3 + b + 1;
			if (_need > MaxFrame)
			{
				_frame.Clear();
				return;
			}
			_frame.Add(b);
			return;
		}
		_frame.Add(b);
		if (_frame.Count >= _need)
		{
			byte[] f = _frame.ToArray();
			_frame.Clear();
			Process(f);
		}
	}

	private void Process(byte[] f)
	{
		string hex = HexBytes(f);
		byte cs = 0;
		for (int i = 0; i < f.Length - 2; i++)
		{
			cs ^= f[i];
		}
		if (cs != f[f.Length - 2] || f[f.Length - 1] != Suffix)
		{
			Ui(() => Log("RX " + hex + "   bad checksum / suffix -> no echo"));
			return;
		}
		if (f[2] != 3)
		{
			Ui(() => Log("RX " + hex + "   LEN is not 3 -> ignored"));
			return;
		}
		int op = f[3];
		int state = f[4];
		if (op < 1 || op > 5 || state > 1)
		{
			Ui(() => Log("RX " + hex + "   bad opcode / state -> no echo"));
			return;
		}
		Ui(delegate
		{
			_high[op - 1] = state == 1;
			UpdateTile(op - 1);
			UpdateConveyor();
			string meaning = (op == 5) ? (state == 1 ? "HIGH" : "LOW") : (state == 1 ? "HIGH = STOP" : "LOW = RUN");
			Log($"RX {hex}   {Names[op - 1]} {meaning}");
			if (EchoBox.IsChecked == true)
			{
				SendEcho(f);
			}
			else
			{
				Log("   echo is off -> ESDCMS retries, then logs 'not acknowledged'");
			}
		});
	}

	private async void SendEcho(byte[] f)
	{
		int delay = (int.TryParse(DelayBox.Text, out int d) && d > 0) ? d : 0;
		if (delay > 0)
		{
			await Task.Delay(delay);
		}
		SerialPort p = _port;
		if (p == null || !p.IsOpen)
		{
			return;
		}
		try
		{
			lock (_txLock)
			{
				p.Write(f, 0, f.Length);
			}
			Log("TX " + HexBytes(f) + "   echo (ACK)" + (delay > 0 ? $" after {delay} ms" : ""));
		}
		catch (Exception ex)
		{
			Log("TX failed: " + ex.Message);
		}
	}
}
