using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ESDCloudViewer;

/// <summary>
/// Firebase settings (built in code, same look as the ESDCMS dialogs): database URL, Web API key, email, password and
/// the line (the same values as ESDCMS, Cloud settings). Returns the edited copy, or null when cancelled.
/// </summary>
internal static class SettingsDialog
{
	public static ViewerSettings Show(Window owner, ViewerSettings current)
	{
		ViewerSettings result = null;
		FontFamily mono = Application.Current?.TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");

		TextBox url = Field(current.DbUrl, mono);
		TextBox key = Field(current.ApiKey, mono);
		TextBox email = Field(current.Email, mono);
		PasswordBox pass = new PasswordBox { Password = current.Password, Height = 26.0, Padding = new Thickness(4.0, 2.0, 4.0, 2.0), VerticalContentAlignment = VerticalAlignment.Center };
		TextBox line = Field(current.Line, mono);
		line.Width = 60.0;
		line.HorizontalAlignment = HorizontalAlignment.Left;
		line.TextAlignment = TextAlignment.Center;
		CheckBox remember = new CheckBox
		{
			Content = Lang.T("S.Set.Remember"),
			IsChecked = current.Remember,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		TextBlock message = new TextBlock { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0.0, 10.0, 0.0, 0.0), Visibility = Visibility.Collapsed };

		Button save = new Button { Content = Lang.T("S.Set.Save"), MinWidth = 100.0, Height = 28.0, IsDefault = true, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) };
		Button cancel = new Button { Content = Lang.T("S.Set.Cancel"), MinWidth = 90.0, Height = 28.0, IsCancel = true };
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
		buttons.Children.Add(save);
		buttons.Children.Add(cancel);

		StackPanel panel = new StackPanel { Margin = new Thickness(20.0), Width = 480.0 };
		AddRow(panel, "S.Set.Url", url);
		AddRow(panel, "S.Set.Key", key);
		AddRow(panel, "S.Set.Email", email);
		AddRow(panel, "S.Set.Password", pass);
		AddRow(panel, "S.Set.Line", line);
		panel.Children.Add(remember);
		panel.Children.Add(message);
		panel.Children.Add(buttons);

		Window win = new Window
		{
			Title = Lang.T("S.Set.Title"),
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

		save.Click += delegate
		{
			ViewerSettings s = current.Clone();
			s.DbUrl = url.Text.Trim().TrimEnd('/');
			s.ApiKey = key.Text.Trim();
			s.Email = email.Text.Trim();
			s.Password = pass.Password;
			s.Line = line.Text.Trim();
			s.Remember = remember.IsChecked == true;
			if (!s.IsComplete || !Regex.IsMatch(s.Line, "^\\d{2}$"))
			{
				message.Text = Lang.T("S.Set.Missing");
				message.Visibility = Visibility.Visible;
				return;
			}
			result = s;
			win.DialogResult = true;
		};
		win.Loaded += delegate
		{
			url.Focus();
		};
		win.ShowDialog();
		return result;
	}

	private static TextBox Field(string value, FontFamily mono)
	{
		return new TextBox
		{
			Text = value ?? string.Empty,
			Height = 26.0,
			FontFamily = mono,
			FontSize = 12.0,
			Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
			VerticalContentAlignment = VerticalAlignment.Center
		};
	}

	private static void AddRow(Panel panel, string labelKey, Control control)
	{
		panel.Children.Add(new TextBlock { Text = Lang.T(labelKey), FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, Margin = new Thickness(0.0, 10.0, 0.0, 3.0) });
		panel.Children.Add(control);
	}
}
