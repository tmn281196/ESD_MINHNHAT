using System;
using System.IO.Ports;
using System.Windows.Media;

namespace ESDCMS.Device;

public class RFGateway
{
	private const int BaudRate = 9600;

	public SerialPort SerialPort;

	private ViewModel viewModel;

	private readonly object _portLock = new object();

	public event EventHandler SerialReceiverHandler;

	public RFGateway(ViewModel viewModel)
	{
		this.viewModel = viewModel;
		SerialPort = SerialPortSafe.Create(BaudRate);
		SerialReceiverHandler += viewModel.RFGateway_SerialReceiverHandler;
	}

	private void PortDataReceived(object sender, SerialDataReceivedEventArgs e)
	{
		SerialReceiverHandler?.Invoke(sender, null);
	}

	/// <summary>Increases every time the port is opened, so the status poller can query all boxes again.</summary>
	public int OpenGeneration { get; private set; }

	public bool IsOpen
	{
		get
		{
			SerialPort port = SerialPort;
			return port != null && port.IsOpen;
		}
	}

	/// <summary>Writes one frame to the gateway (forwarded over LoRa). Returns false if the port is closed or the write failed.</summary>
	public bool Send(byte[] frame)
	{
		lock (_portLock)
		{
			SerialPort port = SerialPort;
			if (port == null || !port.IsOpen)
			{
				return false;
			}
			try
			{
				port.Write(frame, 0, frame.Length);
				return true;
			}
			catch (Exception ex)
			{
				Console.WriteLine("RFGateway: send failed - " + ex.Message);
				return false;
			}
		}
	}

	public void OpenCommunication()
	{
		string portName = viewModel.Config.RFGatewayCOM;
		if (!PortNames.IsPort(portName))
		{
			Console.WriteLine("RFGateway: no COM port selected, skip open");
			return;
		}
		lock (_portLock)
		{
			if (SerialPort != null && SerialPort.IsOpen)
			{
				if (string.Equals(SerialPort.PortName, portName, StringComparison.OrdinalIgnoreCase))
				{
					Console.WriteLine("RFGateway: " + portName + " already open");
					return;
				}
				CloseLocked();
			}
			// Always open on a fresh object: a port object that failed to close stays unusable.
			SerialPort port = SerialPortSafe.Create(BaudRate, portName);
			SerialPort = port;
			try
			{
				Console.WriteLine("RFGateway: opening " + portName + "...");
				SerialPortSafe.Open(port);
				if (port.IsOpen)
				{
					SolidColorBrush ok = new SolidColorBrush(Color.FromRgb(117, 247, 17));
					ok.Freeze();
					viewModel.ConnectionStatusRFColor = ok;
					port.DataReceived += PortDataReceived;
					OpenGeneration++;
					Console.WriteLine($"RFGateway: opened {portName} ({port.BaudRate} baud)");
				}
			}
			catch (Exception ex)
			{
				SerialPortSafe.Close(port);
				SerialPort = SerialPortSafe.Create(BaudRate, portName);
				Console.WriteLine("RFGateway: open failed - " + SerialPortSafe.DescribeOpenError(portName, ex));
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
		viewModel.ConnectionStatusRFColor = Brushes.White;
		if (!clean)
		{
			Console.WriteLine("RFGateway: closing " + portName + " timed out (USB-serial driver not responding) - unplug/replug the USB cable");
		}
		else if (wasOpen)
		{
			Console.WriteLine("RFGateway: closed " + portName);
		}
	}
}
