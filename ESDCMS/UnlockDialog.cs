using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ESDCMS;

/// <summary>
/// Password prompt shown before the controls are unlocked (settings bar, manual motor buttons, layout editing,
/// and the automatic conveyor control is paused while unlocked). Locking again needs no password; once entered, the password is remembered until the program is closed.
/// Only a salted SHA-256 hash of the password is kept in the exe, so it cannot be read from a decompile.
/// This keeps operators from changing things by accident; it is not strong security (the exe can be patched).
/// </summary>
internal static class UnlockDialog
{
	private const string Salt = "ESDCMS-unlock:";

	/// <summary>SHA-256 of Salt + password, upper-case hex. To change the password, replace this value.</summary>
	private const string PasswordHash = "EFAC191C66AE0C1D337ADAD61300D105F9E91642A0FE7C803D2D7572EDBBF282";

	/// <summary>
	/// Set once the password was entered correctly. Kept only in memory for this run of the program: locking again
	/// and unlocking does not ask again; after the program is closed and started again it must be entered again.
	/// </summary>
	public static bool EnteredThisSession { get; private set; }

	/// <summary>True when the controls may be unlocked: the password was already entered in this run, or is entered now.</summary>
	public static bool EnsureUnlocked(Window owner)
	{
		return EnteredThisSession || Ask(owner);
	}

	public static bool Verify(string password)
	{
		using SHA256 sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Salt + (password ?? string.Empty)));
		return string.Equals(BitConverter.ToString(hash).Replace("-", ""), PasswordHash, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Asks for the password. True only when it was entered correctly; a wrong one can be retried in the dialog.</summary>
	public static bool Ask(Window owner)
	{
		bool unlocked = false;
		PasswordBox box = new PasswordBox
		{
			Width = 260.0,
			Height = 28.0,
			FontSize = 14.0,
			VerticalContentAlignment = VerticalAlignment.Center
		};
		TextBlock error = new TextBlock
		{
			Text = Lang.T("S.Unlock.Wrong"),
			Foreground = Brushes.Red,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			Visibility = Visibility.Hidden
		};
		Button okButton = new Button { Content = Lang.T("S.Unlock.Ok"), Width = 90.0, Height = 28.0, IsDefault = true, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) };
		Button cancelButton = new Button { Content = Lang.T("S.Cancel"), Width = 90.0, Height = 28.0, IsCancel = true };
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		buttons.Children.Add(okButton);
		buttons.Children.Add(cancelButton);
		StackPanel panel = new StackPanel { Margin = new Thickness(20.0) };
		panel.Children.Add(new TextBlock
		{
			Text = Lang.T("S.Unlock.Prompt"),
			Foreground = Brushes.Black,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		panel.Children.Add(box);
		panel.Children.Add(error);
		panel.Children.Add(buttons);
		Window win = new Window
		{
			Title = Lang.T("S.Unlock.Title"),
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
		okButton.Click += delegate
		{
			if (Verify(box.Password))
			{
				unlocked = true;
				EnteredThisSession = true;
				win.DialogResult = true;
				return;
			}
			Console.WriteLine("App: wrong unlock password");
			error.Visibility = Visibility.Visible;
			box.Clear();
			box.Focus();
		};
		win.Loaded += delegate
		{
			box.Focus();
		};
		win.ShowDialog();
		return unlocked;
	}
}
