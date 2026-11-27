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
		// .NET Framework allows only 2 connections to one host by default, and Firebase answers over HTTP/1.1: every line
		// keeps 2 streams open (pcs, boxes), so without this the events reads (and the 2nd line) would wait for ever.
		ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 64);
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
