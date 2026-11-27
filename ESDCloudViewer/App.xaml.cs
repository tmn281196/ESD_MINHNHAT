using System;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Threading;

namespace ESDCloudViewer;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		// Firebase needs TLS 1.2 or newer (Windows 10/11 already allow it; older .NET defaults may not).
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		DispatcherUnhandledException += OnUnhandled;
		base.OnStartup(e);
	}

	/// <summary>An unexpected error: written to %USERPROFILE%\.esdcms\cloudviewer-crash.log, the window keeps running.</summary>
	private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(ViewerSettings.Folder);
			File.AppendAllText(Path.Combine(ViewerSettings.Folder, "cloudviewer-crash.log"),
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Exception + Environment.NewLine + Environment.NewLine);
		}
		catch (Exception)
		{
		}
		(Current?.MainWindow as MainWindow)?.Log("Error: " + e.Exception.GetBaseException().Message);
		e.Handled = true;
	}
}
