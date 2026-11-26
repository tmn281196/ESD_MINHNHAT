using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ESDCMS.Device;

/// <summary>
/// Start the program when the current Windows user logs in (HKCU\...\Run, no admin rights needed).
/// The entry is rewritten at every launch so it follows the exe if the folder is moved.
/// </summary>
internal static class AutoStart
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ValueName = "ESDCMS";

	/// <summary>Run entry written by versions before the rename (ESDKitSystem.exe); removed on every Apply.</summary>
	private const string LegacyValueName = "ESDKitSystem";

	private static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

	public static void Apply(bool enable)
	{
		try
		{
			using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
			// The old entry would also start the old exe (or fail on a missing one) at every login.
			if (key.GetValue(LegacyValueName) != null)
			{
				key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
				Console.WriteLine("App: removed old start-with-Windows entry (" + LegacyValueName + ")");
			}
			string current = key.GetValue(ValueName) as string;
			if (enable)
			{
				string desired = "\"" + ExePath + "\"";
				if (!string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
				{
					key.SetValue(ValueName, desired, RegistryValueKind.String);
					Console.WriteLine("App: start with Windows ON (" + ExePath + ")");
				}
			}
			else if (current != null)
			{
				key.DeleteValue(ValueName, throwOnMissingValue: false);
				Console.WriteLine("App: start with Windows OFF");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("App: start with Windows - registry update failed (" + ex.Message + ")");
		}
	}
}
