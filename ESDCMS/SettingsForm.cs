using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ESDCMS;

/// <summary>
/// Small settings dialog built in code (same look as the other ESDCMS dialogs): one row per setting (label, field,
/// unit), an optional status line, OK / Cancel. The bottom bar's Settings buttons use it (RF Gateway, Control Box,
/// Cloud), so the bar itself only shows the connection state and the motor buttons.
/// </summary>
internal sealed class SettingsForm
{
	private readonly Window _win;

	private readonly Grid _grid = new Grid();

	private readonly StackPanel _panel = new StackPanel { Margin = new Thickness(20.0) };

	private readonly TextBlock _error = new TextBlock
	{
		Foreground = Brushes.Red,
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
		MaxWidth = 440.0,
		Visibility = Visibility.Collapsed
	};

	private readonly FontFamily _mono;

	public SettingsForm(Window owner, string title)
	{
		_mono = Application.Current?.TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");
		_grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		_grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		_grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		_panel.Children.Add(_grid);
		_win = new Window
		{
			Title = title,
			SizeToContent = SizeToContent.WidthAndHeight,
			ResizeMode = ResizeMode.NoResize,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = owner,
			ShowInTaskbar = false,
			Background = Brushes.White,
			Content = _panel
		};
		// A window created in code does not inherit the main window font: use the app font (App.xaml).
		if (Application.Current?.TryFindResource("AppFont") is FontFamily appFont)
		{
			_win.FontFamily = appFont;
		}
	}

	private void AddRow(string labelKey, FrameworkElement field, string unit, string tipKey)
	{
		int row = _grid.RowDefinitions.Count;
		_grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		TextBlock label = new TextBlock
		{
			Text = Lang.T(labelKey),
			FontWeight = FontWeights.SemiBold,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 5.0, 12.0, 5.0)
		};
		Grid.SetRow(label, row);
		_grid.Children.Add(label);
		field.Margin = new Thickness(0.0, 5.0, 0.0, 5.0);
		field.VerticalAlignment = VerticalAlignment.Center;
		if (tipKey != null)
		{
			field.ToolTip = Lang.T(tipKey);
			label.ToolTip = Lang.T(tipKey);
		}
		FrameworkElement cell = field;
		if (unit != null)
		{
			// The unit right after its box (not in a column of its own, far from a narrow box).
			StackPanel withUnit = new StackPanel { Orientation = Orientation.Horizontal };
			field.Margin = new Thickness(0.0);
			withUnit.Children.Add(field);
			withUnit.Children.Add(new TextBlock { Text = unit, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6.0, 0.0, 0.0, 0.0) });
			withUnit.Margin = new Thickness(0.0, 5.0, 0.0, 5.0);
			cell = withUnit;
		}
		Grid.SetRow(cell, row);
		Grid.SetColumn(cell, 1);
		_grid.Children.Add(cell);
	}

	public ComboBox AddCombo(string labelKey, string tipKey, IEnumerable<string> items, string selected)
	{
		ComboBox box = new ComboBox { Width = 120.0, Height = 26.0, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
		foreach (string item in items)
		{
			box.Items.Add(item);
		}
		if (selected != null && !box.Items.Contains(selected))
		{
			box.Items.Insert(0, selected);
		}
		box.SelectedItem = selected;
		AddRow(labelKey, box, null, tipKey);
		return box;
	}

	public TextBox AddText(string labelKey, string tipKey, string value, string unit = null, double width = 80.0, bool mono = false)
	{
		TextBox box = new TextBox
		{
			Text = value ?? string.Empty,
			Width = width,
			Height = 26.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalContentAlignment = VerticalAlignment.Center,
			Padding = new Thickness(4.0, 0.0, 4.0, 0.0)
		};
		if (mono)
		{
			box.FontFamily = _mono;
			box.FontSize = 12.0;
		}
		AddRow(labelKey, box, unit, tipKey);
		return box;
	}

	public PasswordBox AddPassword(string labelKey, string value, double width = 320.0)
	{
		PasswordBox box = new PasswordBox
		{
			Password = value ?? string.Empty,
			Width = width,
			Height = 26.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalContentAlignment = VerticalAlignment.Center,
			Padding = new Thickness(4.0, 0.0, 4.0, 0.0)
		};
		AddRow(labelKey, box, null, null);
		return box;
	}

	/// <summary>A multi-line box (e.g. to paste the #define lines), over the full width.</summary>
	public TextBox AddMultiline(string labelKey, double height = 80.0)
	{
		TextBox box = new TextBox
		{
			AcceptsReturn = true,
			Width = 320.0,
			Height = height,
			FontFamily = _mono,
			FontSize = 12.0,
			TextWrapping = TextWrapping.NoWrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		AddRow(labelKey, box, null, null);
		return box;
	}

	public CheckBox AddCheck(string textKey, bool value)
	{
		CheckBox box = new CheckBox { Content = Lang.T(textKey), IsChecked = value, FontWeight = FontWeights.SemiBold, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 0.0, 8.0) };
		_panel.Children.Insert(_panel.Children.IndexOf(_grid), box);
		return box;
	}

	/// <summary>A plain value under a label (read only).</summary>
	public void AddInfo(string labelKey, string value)
	{
		AddRow(labelKey, new TextBlock { Text = value, Foreground = Brushes.Black }, null, null);
	}

	/// <summary>Status line under the fields: a dot and a text bound to the view model (e.g. the cloud state).</summary>
	public void AddStatus(object source, string brushPath, string textPath)
	{
		Ellipse dot = new Ellipse { Width = 10.0, Height = 10.0, Margin = new Thickness(0.0, 4.0, 8.0, 0.0), VerticalAlignment = VerticalAlignment.Top, Stroke = Brushes.Gray, StrokeThickness = 0.5 };
		dot.SetBinding(Shape.FillProperty, new Binding(brushPath) { Source = source });
		TextBlock text = new TextBlock { MaxWidth = 420.0, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
		text.SetBinding(TextBlock.TextProperty, new Binding(textPath) { Source = source });
		StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 12.0, 0.0, 0.0) };
		row.Children.Add(dot);
		row.Children.Add(text);
		_panel.Children.Add(row);
	}

	/// <summary>Shows the dialog. OK runs <paramref name="validate"/>: an error text keeps it open, null closes it. True = OK.</summary>
	public bool Show(Func<string> validate)
	{
		_panel.Children.Add(_error);
		Button ok = new Button { Content = Lang.T("S.Set.Ok"), MinWidth = 90.0, Height = 28.0, IsDefault = true, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) };
		Button cancel = new Button { Content = Lang.T("S.Set.Cancel"), MinWidth = 90.0, Height = 28.0, IsCancel = true };
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);
		_panel.Children.Add(buttons);
		ok.Click += delegate
		{
			string error = validate?.Invoke();
			if (error != null)
			{
				_error.Text = error;
				_error.Visibility = Visibility.Visible;
				return;
			}
			_win.DialogResult = true;
		};
		return _win.ShowDialog() == true;
	}
}
