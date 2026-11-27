using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ESDCloudViewer.Firebase;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ESDCloudViewer;

/// <summary>
/// Main window: the live view of one line (the PCs writing it, boxes, latest status changes), rebuilt every second from the
/// watcher's snapshot, the console log (English) and the Firebase group with Settings / Check settings / Watch / Stop.
/// With saved settings it signs in and starts watching by itself.
/// </summary>
public partial class MainWindow : Window
{
	private const int LogMax = 1000;

	/// <summary>ESDCMS writes at least every 60 s (CloudUploader): later than this = the PC is not writing.</summary>
	private const double PcLateS = 150;

	private static readonly SolidColorBrush ActiveBrush = Frozen(0x45, 0xF2, 0x48);

	private static readonly SolidColorBrush OfflineBrush = Frozen(0x03, 0x7E, 0xF3);

	private static readonly SolidColorBrush UnknownBrush = Frozen(0x9E, 0x9E, 0x9E);

	private static readonly SolidColorBrush CloudGray = Frozen(0x9A, 0xA5, 0xB1);

	private static readonly SolidColorBrush CloudAmber = Frozen(0xFF, 0xC1, 0x07);

	private static readonly SolidColorBrush CloudGreen = Frozen(117, 247, 17);

	private static readonly SolidColorBrush CardBack = Frozen(0xF4, 0xF6, 0xF9);

	private static readonly SolidColorBrush CardLine = Frozen(0xD5, 0xDC, 0xE4);

	private static readonly SolidColorBrush Muted = Frozen(0x5B, 0x6B, 0x7C);

	private static readonly SolidColorBrush Ink = Frozen(0x1F, 0x2A, 0x36);

	private static readonly SolidColorBrush OkInk = Frozen(0x2E, 0x7D, 0x32);

	private static readonly SolidColorBrush WarnInk = Frozen(0xB2, 0x6A, 0x00);

	private static readonly SolidColorBrush BadInk = Frozen(0xC6, 0x28, 0x28);

	private ViewerSettings _settings;

	private LineWatcher _watcher;

	/// <summary>Key of the state text while signing in or checking; null = idle.</summary>
	private string _busyKey;

	private bool _signInFailed;

	private readonly ObservableCollection<string> _log = new ObservableCollection<string>();

	private readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

	public MainWindow()
	{
		InitializeComponent();
		_settings = ViewerSettings.Load();
		Lang.Apply(_settings.Language);
		LogList.ItemsSource = _log;
		Lang.Changed += Render;
		_tick.Tick += delegate
		{
			Render();
		};
		_tick.Start();
		Loaded += MainWindow_Loaded;
		Closed += delegate
		{
			_tick.Stop();
			_watcher?.Dispose();
		};
	}

	private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		Log("ESD Cloud Viewer " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " - settings: " + Path.Combine(ViewerSettings.Folder, "cloudviewer.json"));
		Render();
		if (_settings.IsComplete)
		{
			await WatchAsync();
		}
		else
		{
			Log("Settings: not complete yet - press Settings");
		}
	}

	/// <summary>Adds a line to the console log (any thread).</summary>
	public void Log(string line)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.BeginInvoke((Action)delegate
			{
				Log(line);
			});
			return;
		}
		bool atEnd = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 2.0;
		_log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
		while (_log.Count > LogMax)
		{
			_log.RemoveAt(0);
		}
		if (atEnd)
		{
			Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)LogScroll.ScrollToEnd);
		}
	}

	// ------------------------------------------------------------------------------------------ buttons

	private void LangButton_Click(object sender, RoutedEventArgs e)
	{
		_settings.Language = Lang.Next();
		Lang.Apply(_settings.Language);
		TrySave();
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		ViewerSettings edited = SettingsDialog.Show(this, _settings);
		if (edited == null)
		{
			return;
		}
		_settings = edited;
		TrySave();
		Log("Settings: saved, line " + _settings.Line + " on " + _settings.DbHost + (_settings.Remember ? " (remembered on this PC, password encrypted)" : " (not remembered)"));
		Render();
		if (_watcher != null)
		{
			await WatchAsync();                         // watching: go on with the new settings
		}
	}

	private async void Check_Click(object sender, RoutedEventArgs e)
	{
		if (!_settings.IsComplete)
		{
			Log("Check: the settings are not complete - press Settings");
			Settings_Click(sender, e);
			return;
		}
		SetBusy("S.Live.Checking");
		Log("Check: Firebase settings for lines/" + _settings.Line + " on " + _settings.DbHost);
		(SignInResult signIn, bool allOk) = await ConfigCheck.RunAsync(_settings, delegate (CheckKind kind, string title, string detail)
		{
			string tag = kind switch
			{
				CheckKind.Ok => "[ OK ] ",
				CheckKind.Warn => "[WARN] ",
				_ => "[FAIL] "
			};
			Log(tag + title + ((detail != null) ? (" - " + detail) : ""));
		});
		Log(allOk ? "[ OK ] All right: with these settings ESDCMS can write this line to the cloud."
		          : "[FAIL] Something to fix: see the [FAIL] / [WARN] lines above.");
		SetBusy(null);
		if (signIn?.Token != null)
		{
			_signInFailed = false;
			StartWatcher(signIn.Token);
		}
	}

	private async void Watch_Click(object sender, RoutedEventArgs e)
	{
		if (!_settings.IsComplete)
		{
			Log("Watch: the settings are not complete - press Settings");
			Settings_Click(sender, e);
			return;
		}
		await WatchAsync();
	}

	private void Stop_Click(object sender, RoutedEventArgs e)
	{
		StopWatcher();
		Render();
	}

	private void ClearLog_Click(object sender, RoutedEventArgs e)
	{
		_log.Clear();
	}

	private void SaveLog_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dlg = new SaveFileDialog
		{
			FileName = "cloudviewer-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
			Filter = "Text (*.txt)|*.txt"
		};
		if (dlg.ShowDialog(this) == true)
		{
			File.WriteAllLines(dlg.FileName, _log);
		}
	}

	// ------------------------------------------------------------------------------------------ watching

	private async System.Threading.Tasks.Task WatchAsync()
	{
		StopWatcher();
		SetBusy("S.Live.SigningIn");
		SignInResult r = await FirebaseRest.SignInAsync(_settings);
		SetBusy(null);
		if (r.Error != null)
		{
			_signInFailed = true;
			Log("Sign-in failed: " + r.Error);
			Render();
			return;
		}
		_signInFailed = false;
		Log("Sign-in: OK (" + _settings.Email + ")");
		StartWatcher(r.Token);
	}

	private void StartWatcher(string token)
	{
		StopWatcher();
		_watcher = new LineWatcher(_settings, token);
		_watcher.Log += Log;
		_watcher.Changed += delegate
		{
			Dispatcher.BeginInvoke((Action)Render);
		};
		_watcher.Start();
		Render();
	}

	private void StopWatcher()
	{
		_watcher?.Dispose();
		_watcher = null;
	}

	private void SetBusy(string key)
	{
		_busyKey = key;
		Render();
	}

	private void TrySave()
	{
		try
		{
			_settings.Save();
		}
		catch (Exception ex)
		{
			Log("Settings: save failed - " + ex.Message);
		}
	}

	// ------------------------------------------------------------------------------------------ view

	private void Render()
	{
		LineText.Text = _settings.Line.Length > 0 ? _settings.Line : "—";
		HostText.Text = _settings.DbUrl.Length > 0 ? _settings.DbHost : "—";
		HostText.ToolTip = _settings.DbUrl;
		bool busy = _busyKey != null;
		SettingsButton.IsEnabled = CheckButton.IsEnabled = WatchButton.IsEnabled = !busy;
		StopButton.IsEnabled = _watcher != null;

		LineSnapshot snap = _watcher?.Snapshot();
		SolidColorBrush stateBrush;
		string stateText;
		if (busy)
		{
			stateBrush = CloudAmber;
			stateText = Lang.T(_busyKey);
		}
		else if (snap == null)
		{
			stateBrush = CloudGray;
			stateText = Lang.T(_signInFailed ? "S.Err.SignIn" : "S.Live.Off");
		}
		else if (snap.ErrorKey != null)
		{
			stateBrush = CloudGray;
			stateText = Lang.F(snap.ErrorKey, _watcher.Line);
		}
		else if (snap.LastDataUtc == default)
		{
			stateBrush = CloudAmber;
			stateText = Lang.T("S.Live.Connecting");
		}
		else
		{
			stateBrush = CloudGreen;
			stateText = Lang.F("S.Live.On", _watcher.Line, AgoUtc(snap.LastDataUtc));
		}
		LiveIcon.Foreground = GroupIcon.Foreground = stateBrush;
		LiveText.Text = stateText;

		NotWatchingText.Visibility = (snap == null) ? Visibility.Visible : Visibility.Collapsed;
		LivePanel.Visibility = (snap == null) ? Visibility.Collapsed : Visibility.Visible;
		if (snap == null)
		{
			return;
		}
		RenderPcs(snap);
		RenderBoxes(snap);
		RenderEvents(snap);
	}

	/// <summary>One row of cards per PC (ESDCMS) writing the line.</summary>
	private void RenderPcs(LineSnapshot snap)
	{
		PcPanel.Children.Clear();
		if (!snap.PcsLoaded || snap.Pcs == null || !snap.Pcs.HasValues)
		{
			WrapPanel empty = new WrapPanel();
			empty.Children.Add(Card(Lang.T("S.Sec.Gateway"), Lang.T(snap.PcsLoaded ? "S.Pc.NoData" : "S.Loading"), snap.PcsLoaded ? WarnInk : Ink));
			PcPanel.Children.Add(empty);
			return;
		}
		foreach (JProperty p in snap.Pcs.Properties().OrderBy((JProperty x) => x.Name, StringComparer.Ordinal))
		{
			if (!(p.Value is JObject pc))
			{
				continue;
			}
			WrapPanel row = new WrapPanel();
			long lastSeen = (long?)pc["lastSeen"] ?? 0;
			double lateS = (lastSeen > 0) ? SecondsSince(lastSeen) : double.MaxValue;
			row.Children.Add(Card(p.Name, (lastSeen > 0) ? Lang.F("S.Pc.LastWrite", Ago(lastSeen)) : Lang.T("S.Never"), (lateS <= PcLateS) ? OkInk : BadInk));
			bool rf = (bool?)pc["rf"] == true;
			bool cb = (bool?)pc["controlBox"] == true;
			row.Children.Add(Card(Lang.T("S.Pc.Rf"), Lang.T(rf ? "S.Connected" : "S.NotConnected"), rf ? OkInk : BadInk));
			row.Children.Add(Card(Lang.T("S.Pc.Cb"), Lang.T(cb ? "S.Connected" : "S.NotConnected"), cb ? OkInk : BadInk));
			string conveyor = (string)pc["conveyor"];
			row.Children.Add(Card(Lang.T("S.Pc.Conveyor"), (conveyor == "RUN") ? Lang.T("S.Conveyor.Run") : (conveyor == "STOP") ? Lang.T("S.Conveyor.Stop") : "—",
				(conveyor == "RUN") ? OkInk : (conveyor == "STOP") ? BadInk : Ink));
			row.Children.Add(Card(Lang.T("S.Pc.Boxes"), ((int?)pc["boxesInUse"])?.ToString() ?? "—", Ink));
			long started = (long?)pc["startedAt"] ?? 0;
			row.Children.Add(Card(Lang.T("S.Pc.Since"), (started > 0) ? Clock(started) : "—", Ink));
			row.Children.Add(Card(Lang.T("S.Pc.App"), (string)pc["app"] ?? "—", Ink));
			long dropped = (long?)pc["droppedEvents"] ?? 0;
			if (dropped > 0)
			{
				row.Children.Add(Card(Lang.T("S.Pc.Dropped"), dropped.ToString(), WarnInk));
			}
			PcPanel.Children.Add(row);
		}
	}

	private void RenderBoxes(LineSnapshot snap)
	{
		BoxPanel.Children.Clear();
		if (!snap.BoxesLoaded || snap.Boxes == null || !snap.Boxes.HasValues)
		{
			BoxPanel.Children.Add(EmptyText(Lang.T(snap.BoxesLoaded ? "S.Empty.NoBoxes" : "S.Loading")));
			return;
		}
		foreach (JProperty p in snap.Boxes.Properties().OrderBy((JProperty x) => x.Name, StringComparer.Ordinal))
		{
			if (!(p.Value is JObject b))
			{
				continue;
			}
			string s = (string)b["s"];
			long seen = (long?)b["seen"] ?? 0;
			long since = (long?)b["t"] ?? 0;
			(Brush back, Brush fore, string name) = Look(s);

			StackPanel content = new StackPanel();
			DockPanel top = new DockPanel { LastChildFill = true };
			if ((string)b["via"] == "repeater")
			{
				Border tag = new Border
				{
					BorderBrush = fore,
					BorderThickness = new Thickness(1.0),
					Padding = new Thickness(4.0, 0.0, 4.0, 0.0),
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
					Child = new TextBlock { Text = Lang.T("S.Box.Repeater"), FontSize = 10.0, FontWeight = FontWeights.Bold, Foreground = fore }
				};
				DockPanel.SetDock(tag, Dock.Right);
				top.Children.Add(tag);
			}
			top.Children.Add(new TextBlock { Text = p.Name, FontSize = 28.0, FontWeight = FontWeights.Bold, Foreground = fore });
			content.Children.Add(top);
			string status = name;
			content.Children.Add(new TextBlock { Text = status, FontSize = 16.0, FontWeight = FontWeights.Bold, Foreground = fore, TextWrapping = TextWrapping.Wrap });
			content.Children.Add(Small(Lang.F("S.Box.Since", Clock(since)), fore));
			content.Children.Add(Small(Lang.F("S.Box.Heard", (seen > 0) ? Ago(seen) : Lang.T("S.Never")), fore));
			if (b["rssi"] != null)
			{
				content.Children.Add(Small((int?)b["rssi"] + " dBm · SNR " + (double?)b["snr"], fore));
			}
			BoxPanel.Children.Add(new Border
			{
				Width = 180.0,
				MinHeight = 124.0,
				Background = back,
				Padding = new Thickness(10.0, 6.0, 10.0, 8.0),
				Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
				Child = content
			});
		}
	}

	private void RenderEvents(LineSnapshot snap)
	{
		EventPanel.Children.Clear();
		if (!snap.EventsLoaded || snap.Events == null || !snap.Events.HasValues)
		{
			EventPanel.Children.Add(EmptyText(Lang.T(snap.EventsLoaded ? "S.Empty.NoEvents" : "S.Loading")));
			return;
		}
		EventPanel.Children.Add(EventRow(new TextBlock { Text = Lang.T("S.Ev.Time"), FontWeight = FontWeights.Bold, Foreground = Muted },
			Lang.T("S.Ev.Box"), new TextBlock { Text = Lang.T("S.Ev.Status"), FontWeight = FontWeights.Bold, Foreground = Muted }, header: true));
		foreach (JProperty p in snap.Events.Properties().OrderByDescending((JProperty x) => x.Name, StringComparer.Ordinal))
		{
			if (!(p.Value is JObject e))
			{
				continue;
			}
			(Brush back, Brush fore, string name) = Look((string)e["s"]);
			Border chip = new Border
			{
				Background = back,
				MinWidth = 96.0,
				Padding = new Thickness(8.0, 1.0, 8.0, 1.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = new TextBlock { Text = name, Foreground = fore, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
			};
			int box = (int?)e["box"] ?? 0;
			EventPanel.Children.Add(EventRow(new TextBlock { Text = Clock((long?)e["t"] ?? 0), Foreground = Ink }, box.ToString("00"), chip, header: false));
		}
	}

	private static Border EventRow(UIElement time, string box, UIElement status, bool header)
	{
		Grid g = new Grid();
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		TextBlock boxText = new TextBlock { Text = box, Foreground = header ? Muted : Ink, FontWeight = header ? FontWeights.Bold : FontWeights.SemiBold };
		Grid.SetColumn(boxText, 1);
		Grid.SetColumn(status, 2);
		g.Children.Add(time);
		g.Children.Add(boxText);
		g.Children.Add(status);
		return new Border
		{
			BorderBrush = CardLine,
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Padding = new Thickness(6.0, 5.0, 6.0, 5.0),
			Child = g
		};
	}

	/// <summary>Colour, text colour and name of a box status, as on the ESDCMS monitor.</summary>
	private static (Brush back, Brush fore, string name) Look(string s)
	{
		return s switch
		{
			"A" => (ActiveBrush, Brushes.Black, Lang.T("S.Active")),
			"W" => (Brushes.OrangeRed, Brushes.White, Lang.T("S.Warning")),
			"I" => (Brushes.Yellow, Brushes.Black, Lang.T("S.Inactive")),
			"O" => (OfflineBrush, Brushes.White, Lang.T("S.Offline")),       // the box did not answer the PC's status query
			_ => (UnknownBrush, Brushes.White, s ?? "?")
		};
	}

	private static Border Card(string label, string value, Brush valueBrush)
	{
		StackPanel p = new StackPanel();
		p.Children.Add(new TextBlock { Text = label, FontSize = 12.0, Foreground = Muted });
		p.Children.Add(new TextBlock { Text = value, FontSize = 18.0, FontWeight = FontWeights.SemiBold, Foreground = valueBrush, TextWrapping = TextWrapping.Wrap });
		return new Border
		{
			Background = CardBack,
			BorderBrush = CardLine,
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			MinWidth = 200.0,
			Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
			Child = p
		};
	}

	private static TextBlock Small(string text, Brush fore)
	{
		return new TextBlock { Text = text, FontSize = 12.0, Foreground = fore, TextWrapping = TextWrapping.Wrap };
	}

	private static TextBlock EmptyText(string text)
	{
		return new TextBlock { Text = text, FontSize = 14.0, Foreground = Muted, Margin = new Thickness(0.0, 0.0, 0.0, 6.0) };
	}

	// ------------------------------------------------------------------------------------------ time

	private static double SecondsSince(long epochMs)
	{
		return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - epochMs) / 1000.0;
	}

	private static string Ago(long epochMs)
	{
		return AgoSeconds(Math.Max(0.0, SecondsSince(epochMs)));
	}

	private static string AgoUtc(DateTime utc)
	{
		return AgoSeconds(Math.Max(0.0, (DateTime.UtcNow - utc).TotalSeconds));
	}

	private static string AgoSeconds(double s)
	{
		if (s < 90.0)
		{
			return Lang.F("S.Ago.Sec", (int)s);
		}
		if (s < 5400.0)
		{
			return Lang.F("S.Ago.Min", (int)Math.Round(s / 60.0));
		}
		if (s < 172800.0)
		{
			return Lang.F("S.Ago.Hour", (int)Math.Round(s / 3600.0));
		}
		return Lang.F("S.Ago.Day", (int)Math.Round(s / 86400.0));
	}

	/// <summary>Local time of an epoch in ms: only the time today, else day/month and time.</summary>
	private static string Clock(long epochMs)
	{
		if (epochMs <= 0)
		{
			return "—";
		}
		DateTime t = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;
		return (t.Date == DateTime.Today) ? t.ToString("HH:mm:ss") : t.ToString("dd/MM HH:mm");
	}

	private static string Uptime(long? seconds)
	{
		if (!seconds.HasValue)
		{
			return "—";
		}
		long s = seconds.Value;
		long d = s / 86400;
		long h = s % 86400 / 3600;
		long m = s % 3600 / 60;
		return (d > 0) ? Lang.F("S.Uptime.D", d, h, m) : Lang.F("S.Uptime.H", h, m);
	}

	private static SolidColorBrush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}
}
