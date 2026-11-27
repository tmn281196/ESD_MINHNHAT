using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ESDCloudViewer.Firebase;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace ESDCloudViewer;

/// <summary>
/// Main window: one block per watched line (the PCs writing it and its conveyor map, drawn like the ESDCMS monitor),
/// the latest status changes of all lines, the console log (English) and the Firebase group with Settings / Check
/// settings / Watch / Stop. Rebuilt every second from the watchers' snapshots. With saved settings it signs in and
/// starts watching by itself; the first answer of each stream is the current state, so the map is right at once.
/// </summary>
public partial class MainWindow : Window
{
	private const int LogMax = 1000;

	private const int EventsShown = 30;

	/// <summary>ESDCMS writes at least every 60 s (CloudUploader): later than this = the PC is not writing.</summary>
	private const double PcLateS = 150;

	// Conveyor map, same sizes as the ESDCMS monitor (Monitoring/RootCanvas.cs): 20 slots of 90 px, 5 px apart.
	private const int Slots = 20;

	private const double Tile = 90.0;

	private const double TileGap = 5.0;

	private const double Edge = 5.0;

	private static readonly SolidColorBrush ActiveBrush = Frozen(0x45, 0xF2, 0x48);

	private static readonly SolidColorBrush OfflineBrush = Frozen(0x03, 0x7E, 0xF3);

	private static readonly SolidColorBrush NotInUseBrush = Frozen(0x9E, 0x9E, 0x9E);

	private static readonly SolidColorBrush ConveyorBrush = Frozen(0xAD, 0xD8, 0xE6);

	private static readonly SolidColorBrush CloudGray = Frozen(0x9A, 0xA5, 0xB1);

	private static readonly SolidColorBrush CloudAmber = Frozen(0xFF, 0xC1, 0x07);

	private static readonly SolidColorBrush CloudGreen = Frozen(117, 247, 17);

	private static readonly SolidColorBrush CardBack = Frozen(0xF4, 0xF6, 0xF9);

	private static readonly SolidColorBrush CardLine = Frozen(0xD5, 0xDC, 0xE4);

	private static readonly SolidColorBrush Muted = Frozen(0x5B, 0x6B, 0x7C);

	private static readonly SolidColorBrush Ink = Frozen(0x1F, 0x2A, 0x36);

	private static readonly SolidColorBrush TitleInk = Frozen(0x32, 0x3F, 0x4E);

	private static readonly SolidColorBrush OkInk = Frozen(0x2E, 0x7D, 0x32);

	private static readonly SolidColorBrush WarnInk = Frozen(0xB2, 0x6A, 0x00);

	private static readonly SolidColorBrush BadInk = Frozen(0xC6, 0x28, 0x28);

	private ViewerSettings _settings;

	private readonly List<LineWatcher> _watchers = new List<LineWatcher>();

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
		ApplyLogCollapsed(_settings.LogCollapsed);
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
			StopWatchers();
		};
	}

	private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		Log("ESD Cloud Viewer " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " - settings: " + System.IO.Path.Combine(ViewerSettings.Folder, "cloudviewer.json"));
		Render();
		if (_settings.IsComplete)
		{
			await WatchAsync();
		}
		else
		{
			// Nothing saved yet (or the password was not remembered): ask for the settings at once; OK starts watching.
			Log("Settings: not complete yet");
			_ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
			{
				Settings_Click(this, null);
			}));
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
		Log("Settings: saved, lines " + string.Join(", ", _settings.LineList) + " on " + _settings.DbHost
			+ (_settings.Remember ? " (remembered on this PC, password encrypted)" : " (not remembered)"));
		Render();
		if (_settings.IsComplete)
		{
			await WatchAsync();                         // start (or go on) watching with the new settings
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
		Log("Check: Firebase settings on " + _settings.DbHost + ", lines " + string.Join(", ", _settings.LineList));
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
		Log(allOk ? "[ OK ] All right." : "[FAIL] Something to fix: see the [FAIL] / [WARN] lines above.");
		SetBusy(null);
		if (signIn?.Token != null)
		{
			_signInFailed = false;
			StartWatchers(signIn.Token);
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
		StopWatchers();
		Render();
	}

	/// <summary>Hides / shows the log window like ESDCMS (logging continues); remembered in cloudviewer.json.</summary>
	private void LogCollapseToggle_Click(object sender, RoutedEventArgs e)
	{
		_settings.LogCollapsed = LogCollapseToggle.IsChecked == true;
		ApplyLogCollapsed(_settings.LogCollapsed);
		TrySave();
	}

	private void ApplyLogCollapsed(bool collapsed)
	{
		LogCollapseToggle.IsChecked = collapsed;
		LogScroll.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
		LogRow.Height = collapsed ? GridLength.Auto : new GridLength(200.0);
		LogCollapseIcon.Icon = collapsed ? FontAwesome.Sharp.IconChar.ChevronUp : FontAwesome.Sharp.IconChar.ChevronDown;
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
		StopWatchers();
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
		StartWatchers(r.Token);
	}

	/// <summary>One watcher per line (its own streams; each signs in again by itself when the token gets old).</summary>
	private void StartWatchers(string token)
	{
		StopWatchers();
		foreach (string line in _settings.LineList)
		{
			LineWatcher w = new LineWatcher(_settings, line, token);
			w.Log += (string l) => Log("Line " + line + " · " + l);
			w.Changed += delegate
			{
				Dispatcher.BeginInvoke((Action)Render);
			};
			_watchers.Add(w);
			w.Start();
		}
		Render();
	}

	private void StopWatchers()
	{
		foreach (LineWatcher w in _watchers)
		{
			w.Dispose();
		}
		_watchers.Clear();
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
		List<string> lines = _settings.LineList;
		LineText.Text = (lines.Count > 0) ? string.Join(", ", lines) : "—";
		HostText.Text = _settings.DbUrl.Length > 0 ? _settings.DbHost : "—";
		HostText.ToolTip = _settings.DbUrl;
		bool busy = _busyKey != null;
		SettingsButton.IsEnabled = CheckButton.IsEnabled = WatchButton.IsEnabled = !busy;
		StopButton.IsEnabled = _watchers.Count > 0;

		List<(LineWatcher w, LineSnapshot snap)> snaps = _watchers.Select((LineWatcher w) => (w, w.Snapshot())).ToList();
		SolidColorBrush stateBrush;
		string stateText;
		(LineWatcher w, LineSnapshot snap) failing = snaps.FirstOrDefault(((LineWatcher w, LineSnapshot snap) x) => x.snap.ErrorKey != null);
		if (busy)
		{
			stateBrush = CloudAmber;
			stateText = Lang.T(_busyKey);
		}
		else if (snaps.Count == 0)
		{
			stateBrush = CloudGray;
			stateText = Lang.T(_signInFailed ? "S.Err.SignIn" : "S.Live.Off");
		}
		else if (failing.w != null)
		{
			stateBrush = CloudGray;
			stateText = Lang.F(failing.snap.ErrorKey, failing.w.Line);
		}
		else if (snaps.All(((LineWatcher w, LineSnapshot snap) x) => x.snap.LastDataUtc == default))
		{
			stateBrush = CloudAmber;
			stateText = Lang.T("S.Live.Connecting");
		}
		else
		{
			stateBrush = CloudGreen;
			stateText = Lang.F("S.Live.On", string.Join(", ", snaps.Select(((LineWatcher w, LineSnapshot snap) x) => x.w.Line)),
				AgoUtc(snaps.Max(((LineWatcher w, LineSnapshot snap) x) => x.snap.LastDataUtc)));
		}
		LiveIcon.Foreground = GroupIcon.Foreground = stateBrush;
		LiveText.Text = stateText;

		NotWatchingText.Visibility = (snaps.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
		LivePanel.Visibility = (snaps.Count == 0) ? Visibility.Collapsed : Visibility.Visible;
		if (snaps.Count == 0)
		{
			return;
		}
		LinesPanel.Children.Clear();
		foreach ((LineWatcher w, LineSnapshot snap) in snaps)
		{
			LinesPanel.Children.Add(LineBlock(w.Line, snap));
		}
		RenderEvents(snaps);
	}

	/// <summary>One line: title, its PCs on one row, its conveyor map.</summary>
	private UIElement LineBlock(string line, LineSnapshot snap)
	{
		StackPanel block = new StackPanel { Margin = new Thickness(0.0, 14.0, 0.0, 0.0) };
		WrapPanel head = new WrapPanel { Margin = new Thickness(0.0, 0.0, 0.0, 6.0) };
		head.Children.Add(new TextBlock { Text = Lang.F("S.Line.Title", line), FontSize = 20.0, FontWeight = FontWeights.Bold, Foreground = TitleInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 14.0, 0.0) });
		if (snap.ErrorKey != null)
		{
			head.Children.Add(new TextBlock { Text = Lang.F(snap.ErrorKey, line), Foreground = BadInk, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
		}
		else if (!snap.PcsLoaded)
		{
			head.Children.Add(new TextBlock { Text = Lang.T("S.Loading"), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
		}
		else if (snap.Pcs == null || !snap.Pcs.HasValues)
		{
			head.Children.Add(new TextBlock { Text = Lang.T("S.Line.NoPc"), Foreground = WarnInk, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
		}
		else
		{
			foreach (JProperty p in snap.Pcs.Properties().OrderBy((JProperty x) => x.Name, StringComparer.Ordinal))
			{
				if (p.Value is JObject pc)
				{
					head.Children.Add(PcChip(p.Name, pc));
				}
			}
		}
		block.Children.Add(head);
		block.Children.Add(ConveyorMap(snap));
		return block;
	}

	/// <summary>A PC writing the line: name, last write, RF Gateway, Control Box, conveyor.</summary>
	private Border PcChip(string name, JObject pc)
	{
		long lastSeen = (long?)pc["lastSeen"] ?? 0;
		bool late = lastSeen <= 0 || SecondsSince(lastSeen) > PcLateS;
		bool rf = (bool?)pc["rf"] == true;
		bool cb = (bool?)pc["controlBox"] == true;
		string conveyor = (string)pc["conveyor"];
		TextBlock t = new TextBlock { FontSize = 13.0, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
		t.Inlines.Add(new Run(name) { FontWeight = FontWeights.Bold });
		t.Inlines.Add(new Run("  " + ((lastSeen > 0) ? Lang.F("S.Pc.LastWrite", Ago(lastSeen)) : Lang.T("S.Never"))) { Foreground = late ? BadInk : OkInk, FontWeight = FontWeights.SemiBold });
		t.Inlines.Add(new Run("  ·  " + Lang.T("S.Pc.Rf") + " "));
		t.Inlines.Add(new Run(Lang.T(rf ? "S.Connected" : "S.NotConnected")) { Foreground = rf ? OkInk : BadInk, FontWeight = FontWeights.SemiBold });
		t.Inlines.Add(new Run("  ·  " + Lang.T("S.Pc.Cb") + " "));
		t.Inlines.Add(new Run(Lang.T(cb ? "S.Connected" : "S.NotConnected")) { Foreground = cb ? OkInk : BadInk, FontWeight = FontWeights.SemiBold });
		t.Inlines.Add(new Run("  ·  " + Lang.T("S.Pc.Conveyor") + " "));
		t.Inlines.Add(new Run((conveyor == "RUN") ? Lang.T("S.Conveyor.Run") : (conveyor == "STOP") ? Lang.T("S.Conveyor.Stop") : "—")
		{
			Foreground = (conveyor == "RUN") ? OkInk : (conveyor == "STOP") ? BadInk : Ink,
			FontWeight = FontWeights.SemiBold
		});
		long started = (long?)pc["startedAt"] ?? 0;
		return new Border
		{
			Background = CardBack,
			BorderBrush = CardLine,
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			Child = t,
			ToolTip = Lang.T("S.Pc.App") + " " + ((string)pc["app"] ?? "—") + Environment.NewLine
				+ Lang.T("S.Pc.Since") + " " + ((started > 0) ? Clock(started) : "—") + Environment.NewLine
				+ Lang.T("S.Pc.Boxes") + " " + (((int?)pc["boxesInUse"])?.ToString() ?? "—")
				+ ((((long?)pc["droppedEvents"] ?? 0) > 0) ? (Environment.NewLine + Lang.T("S.Pc.Dropped") + " " + (long?)pc["droppedEvents"]) : "")
		};
	}

	/// <summary>
	/// The conveyor map of a line, drawn like the ESDCMS monitor: a light-blue conveyor with 20 dashed slots and every box
	/// on its slot (pos), coloured by its status, in a Viewbox so it fits the width of the window.
	/// </summary>
	private UIElement ConveyorMap(LineSnapshot snap)
	{
		double width = Edge + Slots * (Tile + TileGap);
		Canvas c = new Canvas { Width = width + 2 * Edge, Height = Tile + 4 * Edge };
		Rectangle conveyor = new Rectangle { Width = width, Height = Tile + 2 * Edge, Fill = ConveyorBrush };
		Canvas.SetLeft(conveyor, Edge);
		Canvas.SetTop(conveyor, Edge);
		c.Children.Add(conveyor);
		for (int i = 0; i < Slots; i++)
		{
			Rectangle slot = new Rectangle
			{
				Width = Tile,
				Height = Tile,
				Fill = Brushes.White,
				Stroke = Brushes.Red,
				StrokeThickness = 1.0,
				StrokeDashArray = new DoubleCollection { 2.0, 2.0 }
			};
			Canvas.SetLeft(slot, SlotLeft(i));
			Canvas.SetTop(slot, 2 * Edge);
			c.Children.Add(slot);
		}
		if (snap.BoxesLoaded)
		{
			// A line with box data: a slot with no box is a box that is not in use (an older ESDCMS only wrote the boxes in
			// use, without pos: box N then sits on slot N-1), grey like ESDCMS draws it. A line with no data stays white.
			HashSet<int> filled = new HashSet<int>();
			HashSet<string> ids = new HashSet<string>();
			foreach (JProperty p in snap.Boxes?.Properties() ?? Enumerable.Empty<JProperty>())
			{
				if (p.Value is JObject b && int.TryParse(p.Name, out int id))
				{
					filled.Add((int?)b["pos"] ?? (id - 1));
					ids.Add(p.Name);
				}
			}
			for (int i = 0; i < Slots && ids.Count > 0; i++)
			{
				if (filled.Contains(i))
				{
					continue;
				}
				string name = (i + 1).ToString("00");
				Grid unused = new Grid { Width = Tile, Height = Tile, Background = Look("N").back };
				if (!ids.Contains(name))
				{
					unused.ToolTip = Lang.T("S.Ev.Box") + " " + name + " · " + Look("N").name;
					unused.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 34.0, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
				}
				Canvas.SetLeft(unused, SlotLeft(i));
				Canvas.SetTop(unused, 2 * Edge);
				c.Children.Add(unused);
			}
		}
		if (snap.BoxesLoaded && snap.Boxes != null)
		{
			foreach (JProperty p in snap.Boxes.Properties().OrderBy((JProperty x) => x.Name, StringComparer.Ordinal))
			{
				if (!(p.Value is JObject b) || !int.TryParse(p.Name, out int id))
				{
					continue;
				}
				int pos = (int?)b["pos"] ?? (id - 1);
				if (pos < 0 || pos >= Slots)
				{
					continue;
				}
				string s = (string)b["s"];
				(Brush back, Brush fore, string name) = Look(s);
				long seen = (long?)b["seen"] ?? 0;
				long since = (long?)b["t"] ?? 0;
				Grid tile = new Grid
				{
					Width = Tile,
					Height = Tile,
					Background = back,
					ToolTip = Lang.T("S.Ev.Box") + " " + p.Name + " · " + name + Environment.NewLine
						+ Lang.F("S.Box.Since", Clock(since)) + Environment.NewLine
						+ Lang.F("S.Box.Heard", (seen > 0) ? Ago(seen) : Lang.T("S.Never"))
						+ ((b["pc"] != null) ? (Environment.NewLine + "PC " + (string)b["pc"]) : "")
				};
				tile.Children.Add(new TextBlock { Text = p.Name, Foreground = fore, FontSize = 34.0, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
				Canvas.SetLeft(tile, SlotLeft(pos));
				Canvas.SetTop(tile, 2 * Edge);
				c.Children.Add(tile);
			}
		}
		return new Viewbox { Child = c, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Left, MaxHeight = 150.0 };
	}

	private static double SlotLeft(int pos)
	{
		return 2 * Edge + pos * (Tile + TileGap);
	}

	/// <summary>The latest status changes of all lines, newest first.</summary>
	private void RenderEvents(List<(LineWatcher w, LineSnapshot snap)> snaps)
	{
		EventPanel.Children.Clear();
		List<(string line, string key, JObject e)> all = new List<(string, string, JObject)>();
		bool loaded = false;
		foreach ((LineWatcher w, LineSnapshot snap) in snaps)
		{
			loaded |= snap.EventsLoaded;
			if (snap.Events == null)
			{
				continue;
			}
			foreach (JProperty p in snap.Events.Properties())
			{
				if (p.Value is JObject e)
				{
					all.Add((w.Line, p.Name, e));
				}
			}
		}
		if (all.Count == 0)
		{
			EventPanel.Children.Add(EmptyText(Lang.T(loaded ? "S.Empty.NoEvents" : "S.Loading")));
			return;
		}
		EventPanel.Children.Add(EventRow(Header(Lang.T("S.Ev.Time")), Header(Lang.T("S.Ev.Line")), Header(Lang.T("S.Ev.Box")), Header(Lang.T("S.Ev.Status"))));
		foreach ((string line, string key, JObject e) in all.OrderByDescending(((string line, string key, JObject e) x) => x.key, StringComparer.Ordinal).Take(EventsShown))
		{
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
			EventPanel.Children.Add(EventRow(new TextBlock { Text = Clock((long?)e["t"] ?? 0), Foreground = Ink },
				new TextBlock { Text = line, Foreground = Ink, FontWeight = FontWeights.SemiBold },
				new TextBlock { Text = box.ToString("00"), Foreground = Ink, FontWeight = FontWeights.SemiBold }, chip));
		}
	}

	private static TextBlock Header(string text)
	{
		return new TextBlock { Text = text, FontWeight = FontWeights.Bold, Foreground = Muted };
	}

	private static Border EventRow(UIElement time, UIElement line, UIElement box, UIElement status)
	{
		Grid g = new Grid();
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80.0) });
		g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		Grid.SetColumn(line, 1);
		Grid.SetColumn(box, 2);
		Grid.SetColumn(status, 3);
		g.Children.Add(time);
		g.Children.Add(line);
		g.Children.Add(box);
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
			"U" => (OfflineBrush, Brushes.White, Lang.T("S.Unknown")),       // not heard yet since ESDCMS started (blue, as on the monitor)
			"N" => (NotInUseBrush, Brushes.White, Lang.T("S.NotInUse")),
			_ => (NotInUseBrush, Brushes.White, s ?? "?")
		};
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

	private static SolidColorBrush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}
}
