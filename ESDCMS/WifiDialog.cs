using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ESDCMS.Device;

namespace ESDCMS;

/// <summary>
/// WiFi of the RF gateway's cloud option (lora_gateway.ino, ENABLE_CLOUD 1), opened from the cloud button of the
/// RF Gateway group (only while unlocked).
///   Ticked + Save: the SSID/password are saved in config.json (password encrypted for this Windows user) and sent to
///   the gateway over its USB port; it keeps them in flash, joins the WiFi and writes the box states to Firebase.
///   Unticked + Save: the gateway is told to turn its WiFi off (the SSID stays saved here for next time).
/// The same is sent again each time the RF port opens. The status line (and the cloud icon on the bar) shows what the
/// gateway reports: green = it is on WiFi.
/// </summary>
internal static class WifiDialog
{
	public static void Show(Window owner, ViewModel viewModel, Func<RFGateway> gateway, Action saveConfig)
	{
		Config cfg = viewModel.Config;
		CheckBox enableBox = new CheckBox
		{
			Content = Lang.T("S.Wifi.Enable"),
			IsChecked = cfg.WifiEnabled,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.Black,
			VerticalContentAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		TextBox ssidBox = new TextBox
		{
			Width = 300.0,
			Height = 28.0,
			FontSize = 14.0,
			VerticalContentAlignment = VerticalAlignment.Center,
			MaxLength = GatewayCloudLink.MaxSsidBytes,
			Text = cfg.WifiSsid ?? string.Empty
		};
		PasswordBox passBox = new PasswordBox
		{
			Width = 300.0,
			Height = 28.0,
			FontSize = 14.0,
			VerticalContentAlignment = VerticalAlignment.Center,
			MaxLength = GatewayCloudLink.MaxPasswordBytes,
			Password = cfg.GetWifiPassword()
		};
		void UpdateEnabled()
		{
			bool on = enableBox.IsChecked == true;
			ssidBox.IsEnabled = on;
			passBox.IsEnabled = on;
		}
		enableBox.Checked += delegate { UpdateEnabled(); };
		enableBox.Unchecked += delegate { UpdateEnabled(); };
		UpdateEnabled();

		TextBlock message = new TextBlock
		{
			Width = 300.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		// Gateway status: same colour and text as the cloud icon on the bar (refreshed every 5 s by the ViewModel).
		Ellipse dot = new Ellipse
		{
			Width = 10.0,
			Height = 10.0,
			Margin = new Thickness(0.0, 4.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Top,
			Stroke = Brushes.Gray,
			StrokeThickness = 0.5
		};
		dot.SetBinding(Shape.FillProperty, new Binding("CloudIconBrush") { Source = viewModel });
		TextBlock status = new TextBlock
		{
			Width = 282.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.DimGray
		};
		status.SetBinding(TextBlock.TextProperty, new Binding("CloudStatusText") { Source = viewModel });
		StackPanel statusRow = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		statusRow.Children.Add(dot);
		statusRow.Children.Add(status);

		Button saveButton = new Button { Content = Lang.T("S.Wifi.Save"), MinWidth = 110.0, Height = 28.0, Padding = new Thickness(10.0, 0.0, 10.0, 0.0), IsDefault = true, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) };
		Button closeButton = new Button { Content = Lang.T("S.Wifi.Close"), MinWidth = 90.0, Height = 28.0, IsCancel = true };
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		buttons.Children.Add(saveButton);
		buttons.Children.Add(closeButton);

		StackPanel panel = new StackPanel { Margin = new Thickness(20.0) };
		panel.Children.Add(new TextBlock
		{
			Text = Lang.T("S.Wifi.Intro"),
			Width = 300.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.Black,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		panel.Children.Add(enableBox);
		panel.Children.Add(Label(Lang.T("S.Wifi.Ssid")));
		panel.Children.Add(ssidBox);
		panel.Children.Add(Label(Lang.T("S.Wifi.Password"), 10.0));
		panel.Children.Add(passBox);
		panel.Children.Add(message);
		panel.Children.Add(statusRow);
		panel.Children.Add(buttons);

		Window win = new Window
		{
			Title = Lang.T("S.Wifi.Title"),
			SizeToContent = SizeToContent.WidthAndHeight,
			ResizeMode = ResizeMode.NoResize,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = owner,
			ShowInTaskbar = false,
			Background = Brushes.White,
			Content = panel
		};
		// A window created in code does not inherit the main window font: use the app font (App.xaml).
		if (Application.Current?.TryFindResource("AppFont") is FontFamily appFont)
		{
			win.FontFamily = appFont;
		}

		void ShowMessage(string text, bool error)
		{
			message.Text = text;
			message.Foreground = error ? Brushes.Red : Brushes.DarkGreen;
			message.Visibility = Visibility.Visible;
		}

		saveButton.Click += async delegate
		{
			bool on = enableBox.IsChecked == true;
			string ssid = ssidBox.Text.Trim();
			string pass = passBox.Password;
			if (on)
			{
				int ssidBytes = Encoding.UTF8.GetByteCount(ssid);
				int passBytes = Encoding.UTF8.GetByteCount(pass);
				if (ssidBytes < 1 || ssidBytes > GatewayCloudLink.MaxSsidBytes)
				{
					ShowMessage(Lang.T("S.Wifi.BadSsid"), error: true);
					ssidBox.Focus();
					return;
				}
				if (passBytes != 0 && (passBytes < 8 || passBytes > GatewayCloudLink.MaxPasswordBytes))
				{
					ShowMessage(Lang.T("S.Wifi.BadPassword"), error: true);
					passBox.Focus();
					return;
				}
			}
			bool changed = on != cfg.WifiEnabled || (on && (ssid != (cfg.WifiSsid ?? string.Empty) || pass != cfg.GetWifiPassword()));
			cfg.WifiEnabled = on;
			if (on)
			{
				cfg.WifiSsid = ssid;
				cfg.SetWifiPassword(pass);
			}
			try
			{
				saveConfig();
				if (changed)
				{
					Console.WriteLine("Config: RF Gateway WiFi = " + (on ? ssid : "off") + " (saved)");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Config: save failed - " + ex.Message);
			}

			RFGateway gw = gateway();
			if (gw == null || !gw.IsOpen)
			{
				ShowMessage(Lang.T(on ? "S.Wifi.SavedNotSent" : "S.Wifi.OffNotSent"), error: false);
				await viewModel.RefreshCloudStatusAsync();
				return;
			}
			saveButton.IsEnabled = false;
			try
			{
				int? result = on
					? await viewModel.GatewayCloud.SendWifiAsync(gw, ssid, pass)
					: await viewModel.GatewayCloud.SendWifiOffAsync(gw);
				ViewModel.LogWifiResult(result, off: !on);
				if (result == null)
				{
					ShowMessage(Lang.T("S.Wifi.NoAnswer"), error: true);
				}
				else if (result == 2)
				{
					ShowMessage(Lang.T("S.Wifi.Refused"), error: true);
				}
				else if (!on)
				{
					ShowMessage(Lang.T("S.Wifi.OffSent"), error: false);
				}
				else
				{
					ShowMessage(Lang.T((result == 0) ? "S.Wifi.Saved" : "S.Wifi.Unchanged"), error: false);
				}
				await viewModel.RefreshCloudStatusAsync();
			}
			finally
			{
				saveButton.IsEnabled = true;
			}
		};

		win.Loaded += async delegate
		{
			if (enableBox.IsChecked == true)
			{
				ssidBox.Focus();
			}
			else
			{
				enableBox.Focus();
			}
			await viewModel.RefreshCloudStatusAsync();
		};
		win.ShowDialog();
	}

	private static TextBlock Label(string text, double top = 0.0)
	{
		return new TextBlock
		{
			Text = text,
			Foreground = Brushes.Black,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, top, 0.0, 4.0)
		};
	}
}
