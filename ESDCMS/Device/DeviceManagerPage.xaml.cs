using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ESDCMS.Device;

public partial class DeviceManagerPage : Page
{
	private const int baurate = 9600;

	public ViewModel ViewModel;

	public ControlBox ControlBox;

	public RFGateway RFGateway;

	public void SaveConfig()
	{
		ConfigStore.Save();
	}

	public Config LoadConfig()
	{
		return ConfigStore.Load().Com;
	}

	public void RefreshComPorts()
	{
		string[] available = SerialPort.GetPortNames();
		// Ports present now + the saved ones (so a box still shows its port while the device is unplugged) + "Close"
		ViewModel.SerialPorts = available
			.Concat(new[] { ViewModel.Config.RFGatewayCOM, ViewModel.Config.ControlBoxCOM }.Where(PortNames.IsPort))
			.Concat(new[] { PortNames.Close })
			.Distinct()
			.ToList();
		Console.WriteLine(string.Format("COM: refresh -> {0} port(s) [{1}]", available.Length, string.Join(", ", available)));
	}

	public DeviceManagerPage(ViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;
		ViewModel.Config = LoadConfig();
		ViewModel.Config.RFGatewayCOM = PortNames.Normalize(ViewModel.Config.RFGatewayCOM);
		ViewModel.Config.ControlBoxCOM = PortNames.Normalize(ViewModel.Config.ControlBoxCOM);
		ViewModel.DeviceManagerPage = this;
		base.DataContext = ViewModel;
		RefreshComPorts();
		ControlBox = new ControlBox(ViewModel);
		RFGateway = new RFGateway(ViewModel);
	}

	public async void Dispose()
	{
		await ViewModel.CloseNetWork();
	}

	private async Task OpenControlBoxCore()
	{
		try
		{
			await Task.Run(delegate
			{
				ControlBox.OpenCommunication();
			});
			if (ControlBox.SerialPort == null || !ControlBox.SerialPort.IsOpen)
			{
				Console.WriteLine("ControlBox: port not open -> skipping SWON pulse");
				return;
			}
			int gapMs = ViewModel.Config?.EffectiveTxGapMs ?? Config.DefaultTxGapMs;
			for (int channel = 0; channel < ControlBox.MotorCount; channel++)
			{
				int ch = channel;
				ControlBox.Channels[ch] = false;
				await Task.Run(() => ControlBox.SendChannel(ch, on: false));
				await Task.Delay(gapMs);
			}
			int swOn = ControlBox.SwOnChannel;
			ControlBox.Channels[swOn] = true;
			await Task.Run(() => ControlBox.SendChannel(swOn, on: true));
			await Task.Delay(500);
			ControlBox.Channels[swOn] = false;
			await Task.Run(() => ControlBox.SendChannel(swOn, on: false));
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine("ControlBox: open error - " + ex2.Message);
		}
	}

	private void CloseControlBoxCore()
	{
		try
		{
			ControlBox.CloseCommunication();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			base.Dispatcher.Invoke(delegate
			{
				Console.WriteLine("ControlBox: close error - " + ex3.Message);
			});
		}
	}

	public Task OpenControlBoxConnection()
	{
		return ViewModel.RunControlBoxExclusiveAsync(OpenControlBoxCore);
	}

	public void CloseControlBoxConnection()
	{
		Task ignored = ViewModel.RunControlBoxExclusiveAsync(delegate
		{
			CloseControlBoxCore();
			return Task.CompletedTask;
		});
	}

	public async void ReconnectControlBox()
	{
		await ViewModel.RunControlBoxExclusiveAsync(async delegate
		{
			CloseControlBoxCore();
			await Task.Delay(1000);
			await OpenControlBoxCore();
		});
	}

	public void OpenRFConnection()
	{
		Task.Run(async delegate
		{
			try
			{
				RFGateway.OpenCommunication();
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				base.Dispatcher.Invoke(delegate
				{
					Console.WriteLine("RFGateway: open error - " + ex3.Message);
				});
			}
		});
	}

	public void CloseRFConnection()
	{
		Task.Run(async delegate
		{
			try
			{
				RFGateway.CloseCommunication();
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				base.Dispatcher.Invoke(delegate
				{
					Console.WriteLine("RFGateway: close error - " + ex3.Message);
				});
			}
		});
	}

	public async void ReconnectRF()
	{
		CloseRFConnection();
		await Task.Delay(1000);
		OpenRFConnection();
	}

	public async Task<bool> ApplyRFConnection(bool open)
	{
		// SerialPort.Open() can block for a long time (e.g. Bluetooth virtual COM ports),
		// so it must never run on the UI thread.
		await Task.Run(delegate
		{
			if (open)
			{
				RFGateway.OpenCommunication();
			}
			else
			{
				RFGateway.CloseCommunication();
			}
		});
		return RFGateway.SerialPort != null && RFGateway.SerialPort.IsOpen;
	}

	public async Task<bool> ApplyControlBoxConnection(bool open)
	{
		await ViewModel.RunControlBoxExclusiveAsync(async delegate
		{
			if (open)
			{
				await OpenControlBoxCore();
			}
			else
			{
				CloseControlBoxCore();
			}
		});
		return ControlBox.SerialPort != null && ControlBox.SerialPort.IsOpen;
	}

}
