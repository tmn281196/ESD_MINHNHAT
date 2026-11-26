using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ESDCMS;

public partial class App : Application
{
	/// <summary>Name shown to the user (window title, Task Manager description). The exe is ESDCMS.exe.</summary>
	public const string AppDisplayName = "ESD Compliance Monitoring System";

	private static Mutex _singleInstanceMutex;

	/// <summary>%USERPROFILE%\.esdcms\crash.log, next to config.json.</summary>
	public static string CrashLogPath => Path.Combine(Device.ConfigStore.DataDir, "crash.log");

	protected override void OnStartup(StartupEventArgs e)
	{
		// Log every crash to crash.log so the cause can be found later instead of restarting the PC.
		AppDomain.CurrentDomain.UnhandledException += (s, args) => WriteCrashLog("UnhandledException", args.ExceptionObject as Exception);
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		TaskScheduler.UnobservedTaskException += (s, args) =>
		{
			WriteCrashLog("UnobservedTaskException", args.Exception);
			args.SetObserved();
		};

		if (!AcquireSingleInstance())
		{
			MessageBox.Show(
				AppDisplayName + " is already running from this folder.\n\n"
				+ "If no window is visible, open Task Manager, end \"ESDCMS.exe\", then start the program again "
				+ "(no need to restart the computer).",
				AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
			Environment.Exit(0);
			return;
		}

		base.OnStartup(e);
		NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
		List<string> macAddrList = nics.Select((NetworkInterface x) => x.GetPhysicalAddress().ToString()).ToList();
		string[] whiteListMacAddress = new string[6] { "00D861E3E550", "D4F32D1F99D9", "D8BBC1B21D13", "D8BBC1B21D13", "9C6B006D4718", "D843AEC0F3BD" };
		bool exists = macAddrList.Any((string x) => whiteListMacAddress.Contains(x));
	}

	/// <summary>
	/// A second copy started from the same folder cannot open the COM ports (the first one holds them),
	/// which looks exactly like "the program stopped working". Copies in different folders are still allowed.
	/// </summary>
	private static bool AcquireSingleInstance()
	{
		try
		{
			string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').ToUpperInvariant();
			string hash;
			using (SHA1 sha = SHA1.Create())
			{
				hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(dir))).Replace("-", "").Substring(0, 16);
			}
			_singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\ESDCMS_" + hash, out bool createdNew);
			return createdNew;
		}
		catch
		{
			return true;
		}
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		WriteCrashLog("DispatcherUnhandledException", e.Exception);
		try
		{
			Console.WriteLine("App: unexpected error - " + e.Exception.Message + " (details in crash.log)");
		}
		catch
		{
		}
		// Keep the monitoring app running instead of dying with the COM ports still open.
		e.Handled = true;
	}

	public static void WriteCrashLog(string source, Exception ex)
	{
		try
		{
			Directory.CreateDirectory(Device.ConfigStore.DataDir);
			File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}: {ex}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	/// <summary>
	/// Called when the main window closes. If a stuck USB-serial driver keeps the process from exiting,
	/// kill it after a few seconds so no hidden ESDCMS.exe keeps holding the COM ports.
	/// </summary>
	public static void ScheduleForcedExit(int delayMs = 5000)
	{
		Thread watchdog = new Thread((ThreadStart)delegate
		{
			Thread.Sleep(delayMs);
			WriteCrashLog("ForcedExit", new TimeoutException($"Process still alive {delayMs} ms after the main window closed - terminating."));
			try
			{
				Process.GetCurrentProcess().Kill();
			}
			catch
			{
				Environment.Exit(1);
			}
		})
		{
			IsBackground = true,
			Name = "ExitWatchdog"
		};
		watchdog.Start();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		try
		{
			_singleInstanceMutex?.ReleaseMutex();
		}
		catch
		{
		}
		base.OnExit(e);
	}
}
