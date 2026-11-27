using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ESDCMS.Device;
using ESDCMS.Monitoring;
using FontAwesome.Sharp;
using Microsoft.Win32;

namespace ESDCMS;

public partial class MainWindow : Window
{
	public DeviceManagerPage DeviceManagerPage;

	public MonitorPage MonitorPage;

	public ViewModel GlobalViewModel;

	private Dictionary<int, Button> channelButtons;

	private bool _logAutoScroll = true;

	// Auto refresh COM list on device plug/unplug (WM_DEVICECHANGE)
	private const int WM_DEVICECHANGE = 0x0219;

	private const int DBT_DEVNODES_CHANGED = 0x0007;

	private DispatcherTimer _comChangeTimer;

	private string[] _lastKnownPorts = new string[0];

	// What the user wants (toggle state), so a port that vanished with the USB cable is reopened when it comes back.
	private bool _rfWanted;

	private bool _cbWanted;

	private bool _portRecoveryBusy;

	public MainWindow()
	{
		InitializeComponent();
		ApplyLock(LockToggle.IsChecked == true);   // starts locked
		// Title bar: the name + the version from AssemblyInformationalVersion (Properties/AssemblyInfo.cs), e.g. "... 2.1".
		string version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (!string.IsNullOrEmpty(version))
		{
			Title = Title + " " + version;
		}
		base.Closed += WindowClosed;
		GlobalViewModel = new ViewModel();
		DeviceManagerPage = new DeviceManagerPage(GlobalViewModel);
		MonitorPage = new MonitorPage(GlobalViewModel);
		MonitorPageHolder.Content = MonitorPage;
		// UI language from config.json (the language button shows it and switches to the next one)
		string lang = Lang.Normalize(GlobalViewModel.Config.Language);
		GlobalViewModel.Config.Language = lang;
		Lang.Apply(lang);
		Lang.Changed += GlobalViewModel.UpdateOfflineAlert;
		base.DataContext = GlobalViewModel;
		channelButtons = new Dictionary<int, Button>
		{
			{ 0, ToggleableButton0 },
			{ 1, ToggleableButton1 },
			{ 2, ToggleableButton2 },
			{ ControlBox.SwOnChannel, StartAllButton }
		};
		DeviceManagerPage.ControlBox.Channels.CollectionChanged += Channels_CollectionChanged;
		for (int i = 0; i < DeviceManagerPage.ControlBox.Channels.Count; i++)
		{
			UpdateButtonColor(i, DeviceManagerPage.ControlBox.Channels[i]);
		}
		Console.SetOut(new UiLogWriter(GlobalViewModel.AppendLog, Console.Out));
		Console.WriteLine("App: started");
		_cfgSnapshot = SnapshotConfig();
		AutoStart.Apply(GlobalViewModel.Config.AutoStart);
		ApplyLogCollapsed(GlobalViewModel.Config.LogCollapsed);
		base.Loaded += delegate
		{
			InitComConnections();
		};
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		_lastKnownPorts = SerialPort.GetPortNames();
		_comChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		_comChangeTimer.Tick += ComChangeTimer_Tick;
		HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
		source?.AddHook(WndProc);
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_DEVICECHANGE && wParam.ToInt64() == DBT_DEVNODES_CHANGED)
		{
			// Windows fires this several times per plug/unplug: debounce, then check if the COM set really changed
			_comChangeTimer.Stop();
			_comChangeTimer.Start();
		}
		return IntPtr.Zero;
	}

	private async void ComChangeTimer_Tick(object sender, EventArgs e)
	{
		_comChangeTimer.Stop();
		string[] now = SerialPort.GetPortNames();
		if (now.OrderBy(x => x).SequenceEqual(_lastKnownPorts.OrderBy(x => x)))
		{
			return;
		}
		string[] added = now.Except(_lastKnownPorts).ToArray();
		string[] removed = _lastKnownPorts.Except(now).ToArray();
		_lastKnownPorts = now;
		Console.WriteLine("COM: device change" + (added.Length > 0 ? " +" + string.Join(",", added) : "") + (removed.Length > 0 ? " -" + string.Join(",", removed) : "") + " -> auto refresh");
		DeviceManagerPage.RefreshComPorts();
		await RecoverPortsAsync(added, removed);
	}

	private static bool ContainsPort(string[] ports, string name)
	{
		return !string.IsNullOrWhiteSpace(name) && ports.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// USB cable pulled: close our handle right away (keeping a handle on a removed USB-serial device is what
	/// leaves the COM port "access denied" until reboot). Cable plugged back: reopen if the user wanted it open.
	/// </summary>
	private async Task RecoverPortsAsync(string[] added, string[] removed)
	{
		if (_portRecoveryBusy)
		{
			return;
		}
		_portRecoveryBusy = true;
		try
		{
			Config cfg = GlobalViewModel.Config;
			if (ContainsPort(removed, cfg.RFGatewayCOM))
			{
				Console.WriteLine("RFGateway: " + cfg.RFGatewayCOM + " removed -> closing");
				await DeviceManagerPage.ApplyRFConnection(open: false);
			}
			if (ContainsPort(removed, cfg.ControlBoxCOM))
			{
				Console.WriteLine("ControlBox: " + cfg.ControlBoxCOM + " removed -> closing");
				await DeviceManagerPage.ApplyControlBoxConnection(open: false);
			}
			bool rfBack = _rfWanted && ContainsPort(added, cfg.RFGatewayCOM) && !DeviceManagerPage.RFGateway.SerialPort.IsOpen;
			bool cbBack = _cbWanted && ContainsPort(added, cfg.ControlBoxCOM) && !DeviceManagerPage.ControlBox.SerialPort.IsOpen;
			if (rfBack || cbBack)
			{
				// Give the USB-serial driver a moment to finish enumerating before opening.
				await Task.Delay(1500);
			}
			if (rfBack)
			{
				Console.WriteLine("RFGateway: " + cfg.RFGatewayCOM + " is back -> reconnecting");
				await DeviceManagerPage.ApplyRFConnection(open: true);
			}
			if (cbBack)
			{
				Console.WriteLine("ControlBox: " + cfg.ControlBoxCOM + " is back -> reconnecting");
				await DeviceManagerPage.ApplyControlBoxConnection(open: true);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("COM: auto recover failed - " + ex.Message);
		}
		finally
		{
			_portRecoveryBusy = false;
		}
	}

	private void LogScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (e.ExtentHeightChange == 0.0)
		{
			_logAutoScroll = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 1.0;
		}
		if (_logAutoScroll && e.ExtentHeightChange != 0.0)
		{
			LogScroll.ScrollToVerticalOffset(LogScroll.ExtentHeight);
		}
	}

	private void ClearLog_Click(object sender, RoutedEventArgs e)
	{
		GlobalViewModel.LogLines.Clear();
	}

	private void SaveLog_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SaveFileDialog dlg = new SaveFileDialog
			{
				Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
				FileName = $"ESD_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
				Title = "Save console log"
			};
			if (dlg.ShowDialog() == true)
			{
				File.WriteAllLines(dlg.FileName, GlobalViewModel.LogLines);
				Console.WriteLine($"App: log saved -> {dlg.FileName} ({GlobalViewModel.LogLines.Count} lines)");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("App: save log failed - " + ex.Message);
		}
	}

	private void LogCollapseToggle_Click(object sender, RoutedEventArgs e)
	{
		bool collapsed = LogCollapseToggle.IsChecked == true;
		ApplyLogCollapsed(collapsed);
		GlobalViewModel.Config.LogCollapsed = collapsed;
		SaveConfigIfChanged();
	}

	private void ApplyLogCollapsed(bool collapsed)
	{
		LogCollapseToggle.IsChecked = collapsed;
		LogScroll.Visibility = (collapsed ? Visibility.Collapsed : Visibility.Visible);
		LogRow.Height = (collapsed ? GridLength.Auto : new GridLength(1.0, GridUnitType.Star));
		LogCollapseIcon.Icon = (collapsed ? IconChar.ChevronUp : IconChar.ChevronDown);
	}

	private async void InitComConnections()
	{
		_rfWanted = PortNames.IsPort(GlobalViewModel.Config.RFGatewayCOM);
		_cbWanted = PortNames.IsPort(GlobalViewModel.Config.ControlBoxCOM);
		await DeviceManagerPage.ApplyRFConnection(open: true);
		await DeviceManagerPage.ApplyControlBoxConnection(open: true);
		await RetryStartupConnectionsAsync();
	}

	/// <summary>
	/// When the program starts together with Windows, the USB-serial drivers are often not ready yet and the
	/// first open fails. Keep retrying for about a minute instead of staying disconnected.
	/// </summary>
	private async Task RetryStartupConnectionsAsync()
	{
		const int attempts = 12;
		const int delayMs = 5000;
		for (int i = 1; i <= attempts; i++)
		{
			bool rfMissing = _rfWanted && IsPortConfigured(GlobalViewModel.Config.RFGatewayCOM) && !DeviceManagerPage.RFGateway.SerialPort.IsOpen;
			bool cbMissing = _cbWanted && IsPortConfigured(GlobalViewModel.Config.ControlBoxCOM) && !DeviceManagerPage.ControlBox.SerialPort.IsOpen;
			if (!rfMissing && !cbMissing)
			{
				return;
			}
			await Task.Delay(delayMs);
			if (_portRecoveryBusy)
			{
				continue;
			}
			_portRecoveryBusy = true;
			try
			{
				if (_rfWanted && IsPortConfigured(GlobalViewModel.Config.RFGatewayCOM) && !DeviceManagerPage.RFGateway.SerialPort.IsOpen)
				{
					Console.WriteLine($"RFGateway: startup retry {i}/{attempts}");
					await DeviceManagerPage.ApplyRFConnection(open: true);
				}
				if (_cbWanted && IsPortConfigured(GlobalViewModel.Config.ControlBoxCOM) && !DeviceManagerPage.ControlBox.SerialPort.IsOpen)
				{
					Console.WriteLine($"ControlBox: startup retry {i}/{attempts}");
					await DeviceManagerPage.ApplyControlBoxConnection(open: true);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("COM: startup retry failed - " + ex.Message);
			}
			finally
			{
				_portRecoveryBusy = false;
			}
		}
	}

	private static bool IsPortConfigured(string name)
	{
		return PortNames.IsPort(name);
	}

	private void WindowClosed(object sender, EventArgs e)
	{
		try
		{
			DeviceManagerPage.ViewModel.RootCanvas.SaveConfig();
			DeviceManagerPage.SaveConfig();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Config: save on exit failed - " + ex.Message);
		}
		_comChangeTimer?.Stop();
		GlobalViewModel.StopTimers();
		// Release both COM ports explicitly. Before, they were left open on exit; with some USB-serial
		// drivers that leaves the port locked (and the process lingering) until Windows is restarted.
		try
		{
			DeviceManagerPage.RFGateway.CloseCommunication();
		}
		catch
		{
		}
		try
		{
			DeviceManagerPage.ControlBox.CloseCommunication();
		}
		catch
		{
		}
		DeviceManagerPage.Dispose();
		App.ScheduleForcedExit();
	}

	private void MonitorPageHolder_Navigated(object sender, NavigationEventArgs e)
	{
	}

	/// <summary>Port boxes: set until the startup connection is done; while false a selection does not connect.</summary>

	private async void ButtonReconnect_Click(object sender, RoutedEventArgs e)
	{
		if (_portRecoveryBusy)
		{
			return;
		}
		_portRecoveryBusy = true;   // also keeps the port list refresh from reconnecting on its own
		try
		{
			DeviceManagerPage.RefreshComPorts();
			_rfWanted = PortNames.IsPort(GlobalViewModel.Config.RFGatewayCOM);
			_cbWanted = PortNames.IsPort(GlobalViewModel.Config.ControlBoxCOM);
			await DeviceManagerPage.ApplyRFConnection(open: false);
			await DeviceManagerPage.ApplyControlBoxConnection(open: false);
			await DeviceManagerPage.ApplyRFConnection(open: true);
			await DeviceManagerPage.ApplyControlBoxConnection(open: true);
			Console.WriteLine("App: reconnect all COM");
		}
		finally
		{
			_portRecoveryBusy = false;
		}
	}

	private async void SendControl_Click(object sender, RoutedEventArgs e)
	{
		ControlBox controlBox = DeviceManagerPage.ControlBox;
		if (controlBox.SerialPort.IsOpen)
		{
			try
			{
				if (sender is Button clickedButton)
				{
					int channelIndex = GetChannelIndex(clickedButton);
					if (channelIndex >= 0)
					{
						bool state = !controlBox.Channels[channelIndex];
						controlBox.Channels[channelIndex] = state;
						string name = ControlBox.ChannelName(channelIndex);
						Console.WriteLine("ControlBox: " + name + " -> " + (state ? "ON" : "OFF"));
						if (!(await Task.Run(() => controlBox.SendChannel(channelIndex, state))))
						{
							Console.WriteLine("ControlBox: " + name + " - no echo received, command may not have been applied");
						}
					}
				}
				return;
			}
			catch (Exception ex)
			{
				Console.WriteLine("ControlBox: motor command error - " + ex.Message);
				return;
			}
		}
		Console.WriteLine("ControlBox: not connected, command ignored");
	}

	private void Channels_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex >= 0)
		{
			int index = e.NewStartingIndex;
			ObservableCollection<bool> channels = DeviceManagerPage.ControlBox.Channels;
			bool state = index < channels.Count && channels[index];
			base.Dispatcher.Invoke(delegate
			{
				UpdateButtonColor(index, state);
			});
		}
	}

	private void UpdateButtonColor(int channelIndex, bool state)
	{
		if (channelButtons != null && channelButtons.TryGetValue(channelIndex, out var btn) && btn != null)
		{
			if (state)
			{
				btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45f248"));
				btn.Foreground = Brushes.Black;
			}
			else
			{
				btn.ClearValue(Control.BackgroundProperty);
				btn.ClearValue(Control.ForegroundProperty);
			}
		}
	}

	private int GetChannelIndex(Button button)
	{
		if (button != null && button.Tag != null && int.TryParse(button.Tag.ToString(), out var channelIndex))
		{
			return channelIndex;
		}
		return -1;
	}

	private Dictionary<string, string> _cfgSnapshot;

	private Dictionary<string, string> SnapshotConfig()
	{
		Config c = GlobalViewModel.Config;
		return new Dictionary<string, string>
		{
			{ "RF Gateway COM", c.RFGatewayCOM ?? "" },
			{ "Control Box COM", c.ControlBoxCOM ?? "" },
			{ "RF Gateway Net ID", $"0x{c.NetworkID:X2}" },
			{ "RF Gateway ask again", c.AskAgainSec > 0 ? c.EffectiveAskAgainSec + " s (all boxes)" : "off" },
			{ "RF Gateway status query", c.EffectivePollRetries + " rounds x " + (c.EffectivePollRoundWaitMs / 1000.0).ToString("0.#") + " s before Offline" },
			{ "Control Box TX gap", c.TxGapMs + " ms" },
			{ "Control Box RX timeout", c.TxTimeoutMs + " ms" },
			{ "Control Box retry", c.TxRetries + "x" },
			{ "Control Box retry gap", c.TxRetryDelayMs + " ms" },
			{ "Cloud", c.CloudEnabled ? ("on, " + (Uri.TryCreate(c.CloudDbUrl, UriKind.Absolute, out Uri u) ? u.Host : "?") + ", " + c.CloudEmail) : "off" },
			{ "Language", c.Language ?? "en" },
			{ "Start with Windows", c.AutoStart ? "on" : "off" },
			{ "Console log", c.LogCollapsed ? "hidden" : "shown" }
		};
	}

	/// <summary>Saves config.json only if something changed, and logs just the changed settings.</summary>
	private void SaveConfigIfChanged()
	{
		if (GlobalViewModel?.Config == null)
		{
			return;
		}
		Dictionary<string, string> now = SnapshotConfig();
		if (_cfgSnapshot == null)
		{
			_cfgSnapshot = now;
			return;
		}
		List<KeyValuePair<string, string>> changed = now.Where(kv => _cfgSnapshot[kv.Key] != kv.Value).ToList();
		if (changed.Count == 0)
		{
			return;
		}
		try
		{
			DeviceManagerPage.SaveConfig();
			foreach (KeyValuePair<string, string> kv in changed)
			{
				Console.WriteLine($"Config: {kv.Key} = {kv.Value} (saved)");
			}
			_cfgSnapshot = now;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Config: save failed - " + ex.Message);
		}
	}

	private void AutoStartBox_Click(object sender, RoutedEventArgs e)
	{
		bool enable = AutoStartBox.IsChecked == true;
		GlobalViewModel.Config.AutoStart = enable;
		AutoStart.Apply(enable);
		SaveConfigIfChanged();
	}

	/// <summary>
	/// A port picked in a Settings dialog connects that device at once ("Close" disconnects it), like picking it in the
	/// old port box did. The config already holds the new port.
	/// </summary>
	private async Task ApplyPortChoiceAsync(bool rf, bool use)
	{
		if (_portRecoveryBusy)
		{
			Console.WriteLine("COM: busy reconnecting, the new port is used at the next connect");
			return;
		}
		_portRecoveryBusy = true;
		try
		{
			if (rf)
			{
				_rfWanted = use;
				await DeviceManagerPage.ApplyRFConnection(use);
			}
			else
			{
				_cbWanted = use;
				await DeviceManagerPage.ApplyControlBoxConnection(use);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("COM: connect failed - " + ex.Message);
		}
		finally
		{
			_portRecoveryBusy = false;
		}
	}

	private static string NumberError(string labelKey)
	{
		return Lang.F("S.Set.BadNumber", Lang.T(labelKey));
	}

	/// <summary>RF Gateway settings: COM port, Net ID (hex), Ask again (s).</summary>
	private async void RfSettings_Click(object sender, RoutedEventArgs e)
	{
		Config c = GlobalViewModel.Config;
		SettingsForm form = new SettingsForm(this, Lang.T("S.RfGateway") + " · " + Lang.T("S.Settings"));
		ComboBox port = form.AddCombo("S.ComPort", "S.Tip.RfPort", GlobalViewModel.SerialPorts, c.RFGatewayCOM);
		TextBox net = form.AddText("S.NetId", "S.Tip.NetId", c.NetworkID.ToString("X2"), "hex", 60.0, mono: true);
		TextBox ask = form.AddText("S.AskAgain", "S.Tip.AskAgain", c.AskAgainSec.ToString(), "s");
		byte netId = 0;
		int askSec = 0;
		bool ok = form.Show(delegate
		{
			string hex = net.Text.Trim();
			if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				hex = hex.Substring(2);
			}
			if (!byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out netId))
			{
				return Lang.T("S.Set.BadNetId");
			}
			return (!int.TryParse(ask.Text.Trim(), out askSec) || askSec < 0) ? NumberError("S.AskAgain") : null;
		});
		if (!ok)
		{
			return;
		}
		string newPort = (port.SelectedItem as string) ?? c.RFGatewayCOM;
		bool portChanged = !string.Equals(newPort, c.RFGatewayCOM, StringComparison.OrdinalIgnoreCase);
		c.RFGatewayCOM = newPort;
		c.NetworkID = netId;
		c.AskAgainSec = askSec;
		SaveConfigIfChanged();
		if (portChanged)
		{
			await ApplyPortChoiceAsync(rf: true, PortNames.IsPort(newPort));
		}
	}

	/// <summary>Control Box settings: COM port, TX gap, RX timeout, Retry, Retry gap.</summary>
	private async void CbSettings_Click(object sender, RoutedEventArgs e)
	{
		Config c = GlobalViewModel.Config;
		SettingsForm form = new SettingsForm(this, Lang.T("S.ControlBox") + " · " + Lang.T("S.Settings"));
		ComboBox port = form.AddCombo("S.ComPort", "S.Tip.CbPort", GlobalViewModel.SerialPorts, c.ControlBoxCOM);
		TextBox gap = form.AddText("S.TxGap", "S.Tip.TxGap", c.TxGapMs.ToString(), "ms");
		TextBox timeout = form.AddText("S.RxTimeout", "S.Tip.RxTimeout", c.TxTimeoutMs.ToString(), "ms");
		TextBox retries = form.AddText("S.Retry", "S.Tip.Retry", c.TxRetries.ToString(), "x");
		TextBox retryGap = form.AddText("S.RetryGap", "S.Tip.RetryGap", c.TxRetryDelayMs.ToString(), "ms");
		int vGap = 0, vTimeout = 0, vRetries = 0, vRetryGap = 0;
		bool ok = form.Show(delegate
		{
			if (!int.TryParse(gap.Text.Trim(), out vGap) || vGap < 0)
			{
				return NumberError("S.TxGap");
			}
			if (!int.TryParse(timeout.Text.Trim(), out vTimeout) || vTimeout < 0)
			{
				return NumberError("S.RxTimeout");
			}
			if (!int.TryParse(retries.Text.Trim(), out vRetries) || vRetries < 1)
			{
				return NumberError("S.Retry");
			}
			return (!int.TryParse(retryGap.Text.Trim(), out vRetryGap) || vRetryGap < 0) ? NumberError("S.RetryGap") : null;
		});
		if (!ok)
		{
			return;
		}
		string newPort = (port.SelectedItem as string) ?? c.ControlBoxCOM;
		bool portChanged = !string.Equals(newPort, c.ControlBoxCOM, StringComparison.OrdinalIgnoreCase);
		c.ControlBoxCOM = newPort;
		c.TxGapMs = vGap;
		c.TxTimeoutMs = vTimeout;
		c.TxRetries = vRetries;
		c.TxRetryDelayMs = vRetryGap;
		SaveConfigIfChanged();
		if (portChanged)
		{
			await ApplyPortChoiceAsync(rf: false, PortNames.IsPort(newPort));
		}
	}

	/// <summary>
	/// Cloud settings: on/off and the Firebase project and account (database URL, Web API key, email, password).
	/// The line written is the Net ID of the RF Gateway settings.
	/// </summary>
	private void CloudSettings_Click(object sender, RoutedEventArgs e)
	{
		Config c = GlobalViewModel.Config;
		SettingsForm form = new SettingsForm(this, Lang.T("S.Cloud") + " · " + Lang.T("S.Settings"));
		CheckBox enabled = form.AddCheck("S.Cloud.Enable", c.CloudEnabled);
		TextBox url = form.AddText("S.Cloud.Url", null, c.CloudDbUrl, null, 320.0, mono: true);
		TextBox key = form.AddText("S.Cloud.Key", null, c.CloudApiKey, null, 320.0, mono: true);
		TextBox email = form.AddText("S.Cloud.Email", null, c.CloudEmail, null, 320.0, mono: true);
		PasswordBox pass = form.AddPassword("S.Cloud.Password", c.GetCloudPassword());
		form.AddInfo("S.Cloud.Line", "lines/" + c.NetworkID.ToString("X2") + "  (Net ID 0x" + c.NetworkID.ToString("X2") + ")");
		form.AddStatus(GlobalViewModel, "CloudIconBrush", "CloudStatusText");
		bool ok = form.Show(delegate
		{
			bool complete = url.Text.Trim().Length > 0 && key.Text.Trim().Length > 0 && email.Text.Trim().Length > 0 && pass.Password.Length > 0;
			return (enabled.IsChecked == true && !complete) ? Lang.T("S.Cloud.Missing") : null;
		});
		if (!ok)
		{
			return;
		}
		c.CloudEnabled = enabled.IsChecked == true;
		c.CloudDbUrl = url.Text.Trim().TrimEnd('/');
		c.CloudApiKey = key.Text.Trim();
		c.CloudEmail = email.Text.Trim();
		c.SetCloudPassword(pass.Password);
		SaveConfigIfChanged();
		try
		{
			DeviceManagerPage.SaveConfig();                     // also when only the password changed (not in the log)
			_cfgSnapshot = SnapshotConfig();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Config: save failed - " + ex.Message);
		}
		GlobalViewModel.Cloud?.SettingsChanged();
	}

	/// <summary>Tooltip of a Settings button: the current settings in one line.</summary>
	private void RfSettings_ToolTipOpening(object sender, ToolTipEventArgs e)
	{
		Config c = GlobalViewModel.Config;
		RfSettingsButton.ToolTip = (c.RFGatewayCOM ?? "-") + " · " + Lang.T("S.NetId") + " 0x" + c.NetworkID.ToString("X2")
			+ " · " + Lang.T("S.AskAgain") + " " + c.AskAgainSec + " s";
	}

	private void CbSettings_ToolTipOpening(object sender, ToolTipEventArgs e)
	{
		Config c = GlobalViewModel.Config;
		CbSettingsButton.ToolTip = (c.ControlBoxCOM ?? "-") + " · " + Lang.T("S.TxGap") + " " + c.TxGapMs + " ms · " + Lang.T("S.RxTimeout") + " "
			+ c.TxTimeoutMs + " ms · " + Lang.T("S.Retry") + " " + c.TxRetries + "x · " + Lang.T("S.RetryGap") + " " + c.TxRetryDelayMs + " ms";
	}

	/// <summary>
	/// The bottom bar is always shown. While locked: the Settings buttons, "Start with Windows" and the motor buttons
	/// do not react (their tooltips still show the settings and states). Refresh Connection and the language switch
	/// always work.
	/// </summary>
	private void ApplyLock(bool locked)
	{
		foreach (Control c in new Control[]
		{
			RfSettingsButton, CbSettingsButton, CloudSettingsButton, AutoStartBox,
			StartAllButton, ToggleableButton0, ToggleableButton1, ToggleableButton2
		})
		{
			c.IsHitTestVisible = !locked;
			c.Focusable = !locked;
		}
	}

	/// <summary>Language button: English → 한국어 → ไทย → English. Works while locked too.</summary>
	private void LangButton_Click(object sender, RoutedEventArgs e)
	{
		int i = Array.IndexOf(Lang.Codes, Lang.Current);
		Lang.Apply(Lang.Codes[(i + 1) % Lang.Codes.Length]);
		GlobalViewModel.Config.Language = Lang.Current;
		SaveConfigIfChanged();
	}

	private void LockToggle_Click(object sender, RoutedEventArgs e)
	{
		// The toggle has already flipped: unchecked = the user wants to unlock, which needs the password
		// (asked once per run of the program, see UnlockDialog.EnteredThisSession).
		if (LockToggle.IsChecked != true && !UnlockDialog.EnsureUnlocked(this))
		{
			LockToggle.IsChecked = true;
			return;
		}
		bool locked = LockToggle.IsChecked == true;
		GlobalViewModel?.SetControlsUnlocked(!locked);
		ApplyLock(locked);
		LockIcon.Icon = (locked ? IconChar.Lock : IconChar.LockOpen);
		if (GlobalViewModel?.RootCanvas != null)
		{
			GlobalViewModel.RootCanvas.Locked = locked;
		}
		Console.WriteLine(locked ? "App: controls locked" : "App: controls unlocked");
	}

}
