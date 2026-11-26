using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RfGatewaySim;

/// <summary>
/// Simulates the ESDCMS RF Gateway (Firmware_LoRA/lora_gateway v2) and up to 20 ESD boxes behind it (lora_node v2).
/// Serial 9600 8N1. Frame both ways: AA NetID ID LEN DATA CRC8 BB, CRC8 poly 0x31 init 0 over NetID, ID, LEN, DATA.
///   box -> PC : DATA = 'A' / 'W' / 'I' (sent when a box's inputs change, at power-up, and as a query answer)
///   PC -> box : DATA = '?' (status query); ID = one box (answers after 20 ms) or 0xFF = all (answer at ID x 60 ms)
/// Put it on one end of a com0com pair and ESDCMS's RF Gateway port on the other end.
/// Command line: RfGatewaySim.exe COM18 [log.txt]  opens that port at start (and also writes the log to the file).
/// </summary>
public partial class MainWindow : Window
{
	private const byte Stx = 0xAA;
	private const byte Etx = 0xBB;
	private const byte CmdQuery = (byte)'?';
	private const byte Broadcast = 0xFF;
	private const int BoxCount = 20;
	private const int ReplyDelayMs = 20;     // direct query: the box answers after 20 ms
	private const int SlotMs = 60;           // query to all / power-up: the box answers at ID x 60 ms
	private const int PcByteGapMs = 50;      // the gateway drops a half frame from the PC after 50 ms
	private const int PcFrameMax = 64;

	private readonly char[] _status = new char[BoxCount + 1];
	private readonly bool[] _online = new bool[BoxCount + 1];
	private readonly Border[] _cards = new Border[BoxCount + 1];
	private readonly TextBlock[] _cardText = new TextBlock[BoxCount + 1];

	private SerialPort _port;

	/// <summary>Optional 2nd command-line argument: every log line is also appended to this file.</summary>
	private string _logFile;
	private readonly object _rxLock = new object();
	private readonly object _txLock = new object();
	private readonly List<byte> _pc = new List<byte>();
	private DateTime _pcLastByte = DateTime.MinValue;

	public MainWindow()
	{
		InitializeComponent();
		for (int id = 1; id <= BoxCount; id++)
		{
			_status[id] = 'A';
			_online[id] = id <= 8;               // ESDCMS uses boxes 1..8 by default
			BuildCard(id);
		}
		RefreshPorts();
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

	// ------------------------------------------------------------------ boxes

	private static SolidColorBrush Hex(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

	private void BuildCard(int id)
	{
		Grid grid = new Grid { Margin = new Thickness(8, 6, 8, 8) };
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		DockPanel head = new DockPanel();
		CheckBox online = new CheckBox { Content = "Online", IsChecked = _online[id], VerticalAlignment = VerticalAlignment.Center };
		DockPanel.SetDock(online, Dock.Right);
		// Checked / Unchecked (not Click) so a keyboard toggle or UI Automation works too
		online.Checked += delegate { SetOnline(id, true); };
		online.Unchecked += delegate { SetOnline(id, false); };
		head.Children.Add(online);
		_cardText[id] = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
		head.Children.Add(_cardText[id]);
		grid.Children.Add(head);

		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
		buttons.Children.Add(StatusButton(id, 'A', "#45F248", "Active: both inputs OK"));
		buttons.Children.Add(StatusButton(id, 'W', "#FF4500", "Warning: ESD fault -> ESDCMS stops the conveyor"));
		buttons.Children.Add(StatusButton(id, 'I', "#FFEB3B", "Inactive: nobody at this station"));
		Grid.SetRow(buttons, 1);
		grid.Children.Add(buttons);

		_cards[id] = new Border { Child = grid, Margin = new Thickness(0, 0, 8, 8), BorderBrush = Hex("#90A4AE"), BorderThickness = new Thickness(1) };
		BoxGrid.Children.Add(_cards[id]);
		UpdateCard(id);
	}

	private Button StatusButton(int id, char status, string color, string tip)
	{
		Button b = new Button
		{
			Content = status.ToString(),
			Width = 38,
			Height = 26,
			Margin = new Thickness(0, 0, 6, 0),
			FontWeight = FontWeights.Bold,
			Background = Hex(color),
			Foreground = status == 'W' ? Brushes.White : Brushes.Black,
			ToolTip = tip
		};
		b.Click += delegate { ChangeInputs(id, status); };
		return b;
	}

	private void UpdateCard(int id)
	{
		string name = _status[id] == 'A' ? "Active" : (_status[id] == 'W' ? "Warning" : "Inactive");
		_cardText[id].Text = $"Box {id:D2} · {(_online[id] ? name : "offline")}";
		if (!_online[id])
		{
			_cards[id].Background = Hex("#CFD8DC");
			_cardText[id].Foreground = Hex("#546E7A");
			return;
		}
		_cards[id].Background = _status[id] == 'A' ? Hex("#C8F7C5") : (_status[id] == 'W' ? Hex("#FFCCBC") : Hex("#FFF9C4"));
		_cardText[id].Foreground = Brushes.Black;
	}

	private void ChangeInputs(int id, char status)
	{
		_status[id] = status;
		UpdateCard(id);
		if (_online[id])
		{
			SendStatus(id, "inputs changed");
		}
		else
		{
			Log($"Box {id:D2} = {status} (offline, nothing sent)");
		}
	}

	private async void SetOnline(int id, bool online)
	{
		_online[id] = online;
		UpdateCard(id);
		if (!online)
		{
			Log($"Box {id:D2} offline (silent)");
			return;
		}
		await Task.Delay(id * SlotMs);          // power-up: send the first status in the box's slot
		if (_online[id])
		{
			SendStatus(id, "power-up");
		}
	}

	private async void PowerUp_Click(object sender, RoutedEventArgs e)
	{
		Log("Power up: every online box sends its status in its slot");
		for (int id = 1; id <= BoxCount; id++)
		{
			int box = id;
			await Task.Delay(SlotMs);
			if (_online[box])
			{
				SendStatus(box, "power-up");
			}
		}
	}

	private void AllActive_Click(object sender, RoutedEventArgs e)
	{
		for (int id = 1; id <= BoxCount; id++)
		{
			if (_status[id] != 'A')
			{
				ChangeInputs(id, 'A');
			}
		}
	}

	// ------------------------------------------------------------------ frames

	private static byte Crc8(byte[] data, int start, int length)
	{
		byte crc = 0;
		for (int i = start; i < start + length; i++)
		{
			crc ^= data[i];
			for (int bit = 0; bit < 8; bit++)
			{
				crc = (byte)(((crc & 0x80) != 0) ? ((crc << 1) ^ 0x31) : (crc << 1));
			}
		}
		return crc;
	}

	private byte NetId()
	{
		string text = NetIdBox.Text.Trim();
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(2);
		}
		return byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte id) ? id : (byte)3;
	}

	private void SendStatus(int id, string why)
	{
		SerialPort p = _port;
		if (p == null || !p.IsOpen)
		{
			Log($"Box {id:D2} = {_status[id]} ({why}) - port closed, not sent");
			return;
		}
		byte net = NetId();
		byte[] f = { Stx, net, (byte)id, 1, (byte)_status[id], 0, Etx };
		f[5] = Crc8(f, 1, 4);
		try
		{
			lock (_txLock)
			{
				p.Write(f, 0, f.Length);
			}
			Log($"TX {HexBytes(f)}   box {id:D2} = '{_status[id]}' ({why})");
		}
		catch (Exception ex)
		{
			Log("TX failed: " + ex.Message);
		}
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
					FeedFromPc(buf[i]);
				}
			}
		}
		catch (Exception ex)
		{
			Ui(() => Log("RX error: " + ex.Message));
		}
	}

	/// <summary>Same steps as lora_gateway.ino pollSerialFromPC(): collect one frame from the PC.</summary>
	private void FeedFromPc(byte b)
	{
		DateTime now = DateTime.Now;
		if (_pc.Count > 0 && (now - _pcLastByte).TotalMilliseconds > PcByteGapMs)
		{
			_pc.Clear();
		}
		_pcLastByte = now;
		if (_pc.Count == 0 && b != Stx)
		{
			return;
		}
		_pc.Add(b);
		if (_pc.Count < 4)
		{
			return;
		}
		int total = _pc[3] + 6;
		if (total > PcFrameMax)
		{
			_pc.Clear();
			return;
		}
		if (_pc.Count == total)
		{
			byte[] f = _pc.ToArray();
			_pc.Clear();
			Ui(() => HandlePcFrame(f));
		}
	}

	private void HandlePcFrame(byte[] f)
	{
		string hex = HexBytes(f);
		int len = f[3];
		if (f[f.Length - 1] != Etx)
		{
			Log($"RX {hex}   no ETX -> dropped");
			return;
		}
		if (f[1] != NetId())
		{
			Log($"RX {hex}   Net ID 0x{f[1]:X2} is not ours (0x{NetId():X2}) -> dropped");
			return;
		}
		if (Crc8(f, 1, len + 3) != f[f.Length - 2])
		{
			Log($"RX {hex}   CRC wrong -> dropped");
			return;
		}
		if (len < 1 || f[4] != CmdQuery)
		{
			Log($"RX {hex}   not a query -> sent over LoRa, no box reacts");
			return;
		}
		byte target = f[2];
		Log($"RX {hex}   query '?' for " + (target == Broadcast ? "all boxes" : $"box {target:D2}"));
		if (AnswerBox.IsChecked != true)
		{
			Log("   boxes do not answer queries (old firmware) -> no reply");
			return;
		}
		if (target == Broadcast)
		{
			for (int id = 1; id <= BoxCount; id++)
			{
				ReplyLater(id, id * SlotMs);
			}
		}
		else if (target >= 1 && target <= BoxCount)
		{
			ReplyLater(target, ReplyDelayMs);
		}
	}

	private async void ReplyLater(int id, int delayMs)
	{
		await Task.Delay(delayMs);
		if (_online[id])
		{
			SendStatus(id, "query answer");
		}
		else
		{
			Log($"   box {id:D2} is offline -> no answer");
		}
	}

	// ------------------------------------------------------------------ port / UI

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
			Log("Opened " + name + " (9600 8N1) - ESDCMS will query the boxes about 2.5 s after it opens its port");
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
}
