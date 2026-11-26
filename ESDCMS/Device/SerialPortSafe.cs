using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace ESDCMS.Device;

/// <summary>
/// Open/close helpers that keep a USB-serial port from getting stuck.
/// - Each open uses a fresh SerialPort object: after a failed Close (cable pulled) the old object
///   can report IsOpen=true forever and every later Open() fails until the app/PC restarts.
/// - The BaseStream finalizer is suppressed after open: when the USB device disappears it throws
///   (UnauthorizedAccessException / ObjectDisposedException) on the GC thread and kills the process.
/// - Close runs with a timeout so a hung driver can never freeze the UI or keep the process alive.
/// </summary>
internal static class SerialPortSafe
{
	public const int CloseTimeoutMs = 2000;

	public static SerialPort Create(int baudRate, string portName = null)
	{
		SerialPort port = new SerialPort
		{
			BaudRate = baudRate,
			ReadTimeout = 500,
			// Default is infinite: a dead USB-serial driver would block Write() forever and hold the TX lock.
			WriteTimeout = 500
		};
		if (!string.IsNullOrWhiteSpace(portName))
		{
			try
			{
				port.PortName = portName;
			}
			catch
			{
			}
		}
		return port;
	}

	public static void Open(SerialPort port)
	{
		port.Open();
		try
		{
			GC.SuppressFinalize(port.BaseStream);
		}
		catch
		{
		}
	}

	/// <summary>Closes and disposes the port. Returns false if the driver did not finish within the timeout.</summary>
	public static bool Close(SerialPort port, int timeoutMs = CloseTimeoutMs)
	{
		if (port == null)
		{
			return true;
		}
		Task closeTask = Task.Run(delegate
		{
			try
			{
				if (port.IsOpen)
				{
					try
					{
						port.DiscardInBuffer();
						port.DiscardOutBuffer();
					}
					catch
					{
					}
					port.Close();
				}
			}
			catch
			{
				// Close() failed (typically the device is gone): release the OS handle directly.
				try
				{
					port.BaseStream.Dispose();
				}
				catch
				{
				}
			}
			try
			{
				port.Dispose();
			}
			catch
			{
			}
		});
		try
		{
			return closeTask.Wait(timeoutMs);
		}
		catch
		{
			return true;
		}
	}

	public static string DescribeOpenError(string portName, Exception ex)
	{
		if (ex is UnauthorizedAccessException)
		{
			return portName + " is busy (access denied) - another program or another ESDCMS instance is using it, "
				+ "or the USB-serial driver is stuck: unplug/replug the USB cable, then press Refresh Connection";
		}
		if (ex is System.IO.IOException)
		{
			return portName + " not found / not responding (" + ex.Message.Trim() + ") - check USB cable, then press Refresh Connection";
		}
		return portName + " (" + ex.Message.Trim() + ")";
	}
}
