using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ESDCMS.Device;

namespace ESDCMS;

/// <summary>
/// Cloud state of the RF gateway (lora_gateway.ino, ENABLE_CLOUD 1), opened from the cloud button of the RF Gateway
/// group, also while locked. Read only: the WiFi SSID/password and the Firebase account are in the gateway firmware
/// (secrets.h) and ENABLE_CLOUD is the only on/off. The status line has the same colour and text as the cloud icon.
/// </summary>
internal static class CloudDialog
{
	public static void Show(Window owner, ViewModel viewModel)
	{
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
			Width = 322.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.Black
		};
		status.SetBinding(TextBlock.TextProperty, new Binding("CloudStatusText") { Source = viewModel });
		StackPanel statusRow = new StackPanel { Orientation = Orientation.Horizontal };
		statusRow.Children.Add(dot);
		statusRow.Children.Add(status);

		Button closeButton = new Button
		{
			Content = Lang.T("S.Wifi.Close"),
			MinWidth = 90.0,
			Height = 28.0,
			IsCancel = true,
			IsDefault = true,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};

		StackPanel panel = new StackPanel { Margin = new Thickness(20.0) };
		panel.Children.Add(statusRow);
		panel.Children.Add(closeButton);

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
		win.Loaded += async delegate
		{
			await viewModel.RefreshCloudStatusAsync();
		};
		win.ShowDialog();
	}
}
