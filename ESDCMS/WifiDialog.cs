using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ESDCMS.Device;

namespace ESDCMS;

/// <summary>
/// Cloud option of the RF gateway (lora_gateway.ino, ENABLE_CLOUD 1), opened from the cloud button of the RF Gateway
/// group (only while unlocked). The WiFi name and password are in the gateway firmware (secrets.h); here the WiFi is
/// only turned on (ticked) or off, saved in config.json and sent to the gateway over its USB port (again each time
/// the RF port opens). The status line has the same colour and text as the cloud icon on the bar.
/// </summary>
internal static class WifiDialog
{
	public static void Show(Window owner, ViewModel viewModel, Func<RFGateway> gateway, Action saveConfig)
	{
		Config cfg = viewModel.Config;
		CheckBox enableBox = new CheckBox
		{
			Content = Lang.T("S.Wifi.Enable"),
			IsChecked = cfg.WifiEnabled ?? true,          // never set: the gateway's own default, WiFi on
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.Black,
			VerticalContentAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		TextBlock message = new TextBlock
		{
			Width = 300.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
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
			bool changed = cfg.WifiEnabled != on;
			cfg.WifiEnabled = on;
			try
			{
				saveConfig();
				if (changed)
				{
					Console.WriteLine("Config: RF Gateway WiFi = " + (on ? "on" : "off") + " (saved)");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Config: save failed - " + ex.Message);
			}

			RFGateway gw = gateway();
			if (gw == null || !gw.IsOpen)
			{
				ShowMessage(Lang.T("S.Wifi.SavedNotSent"), error: false);
				await viewModel.RefreshCloudStatusAsync();
				return;
			}
			saveButton.IsEnabled = false;
			try
			{
				int? result = await viewModel.GatewayCloud.SendWifiStateAsync(gw, on);
				ViewModel.LogWifiResult(result, on);
				if (result == null)
				{
					ShowMessage(Lang.T("S.Wifi.NoAnswer"), error: true);
				}
				else if (result == 1)
				{
					ShowMessage(Lang.T("S.Wifi.Unchanged"), error: false);
				}
				else
				{
					ShowMessage(Lang.T(on ? "S.Wifi.OnSent" : "S.Wifi.OffSent"), error: false);
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
			enableBox.Focus();
			await viewModel.RefreshCloudStatusAsync();
		};
		win.ShowDialog();
	}
}
