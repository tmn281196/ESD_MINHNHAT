using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ESDCMS.Device;

/// <summary>
/// Text for the cloud option of the RF gateway, built from its status and report frames (GatewayCloudLink): the
/// tooltip of the cloud icon (current language, <see cref="Lang.T"/>) and the console log (English, <see cref="Lang.En"/>).
/// </summary>
internal static class GatewayCloudText
{
	/// <summary>Status line: state, then what the gateway is doing or where it failed and why.</summary>
	public static string Status(GatewayCloudStatus s, Func<string, string> t)
	{
		string text = s.State switch
		{
			2 => string.Format(t("S.Wifi.State2"), s.WifiRssi),
			3 => string.Format(t("S.Wifi.State3"), s.WifiRssi),
			4 => string.Format(t("S.Wifi.State4"), s.WifiRssi),
			5 => t("S.Wifi.State5"),
			_ => t("S.Wifi.State1")                 // 1 connecting to WiFi (0 is not sent any more)
		};
		string step = StepName(s.Step, t);
		string reason = Reason(s.Step, s.Code, s.Detail);
		switch (s.State)
		{
		case 1:
			if (s.Step == 1 && s.Code != 0)                 // why the last try ended, while trying again
			{
				text += " " + string.Format(t("S.Cloud.LastTry"), reason);
			}
			break;
		case 2:
			if (step != null)
			{
				text += " " + string.Format(t("S.Cloud.Now"), step);
			}
			break;
		case 4:
			if (step != null)
			{
				text += " " + string.Format(t("S.Cloud.FailedAt"), step, reason ?? "?");
			}
			break;
		case 5:
			text += " " + string.Format(t("S.Cloud.Why"), reason ?? "?");
			break;
		}
		if ((s.State == 2 || s.State == 3 || s.State == 4) && s.SecondsSinceWrite >= 0)
		{
			text += " " + string.Format(t("S.Wifi.LastWrite"), FormatAge(s.SecondsSinceWrite));   // XAML trims a leading space
		}
		return text;
	}

	/// <summary>Console line for one request the gateway sent to the cloud (English).</summary>
	public static string Report(GatewayCloudReport r)
	{
		string took = (r.Milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
		if (r.What == GatewayCloudReport.SignIn)
		{
			return r.Ok
				? "Gateway -> Firebase: sign-in OK (" + took + ")"
				: "Gateway -> Firebase: sign-in FAILED (" + took + "): " + (Reason(3, r.Code, r.Detail) ?? "?");
		}
		if (r.What != GatewayCloudReport.Write)
		{
			return "Gateway -> Firebase: request " + r.What + (r.Ok ? " OK" : " FAILED");
		}
		string content = "gateway info, " + Plural(r.Boxes, "box", "boxes");
		if (r.BoxList.Count > 0)
		{
			IEnumerable<string> boxes = r.BoxList.Select((GatewayCloudBox b) => b.Id.ToString("00") + " " + b.Status + (b.ViaRepeater ? " via repeater" : ""));
			content += " [" + string.Join(", ", boxes) + (r.BoxList.Count < r.Boxes ? ", ..." : "") + "]";
		}
		content += ", " + Plural(r.Events, "status change", "status changes") + ", " + r.BytesSent + " bytes";
		string line = r.Ok
			? "Gateway -> Firebase: wrote " + content + " (" + took + ")"
			: "Gateway -> Firebase: write FAILED (" + took + "): " + (Reason(4, r.Code, r.Detail) ?? "?") + "; tried " + content;
		if (r.EventsWaiting > 0)
		{
			line += "; " + (r.EventsWaiting >= 127 ? "127+" : r.EventsWaiting.ToString()) + " status changes still waiting";
		}
		if (r.EventsDropped > 0)
		{
			line += "; " + (r.EventsDropped >= 127 ? "127+" : r.EventsDropped.ToString()) + " dropped (buffer full while offline)";
		}
		return line;
	}

	/// <summary>Console line for a WiFi info frame (English): MAC of the gateway, access point of the last try.</summary>
	public static string Info(GatewayWifiInfo i)
	{
		string line = "Gateway WiFi: MAC " + i.Mac;
		if (i.Bssid == null)
		{
			return line + ", no access point yet";
		}
		line += ", access point " + i.Bssid;
		if (i.Channel > 0)
		{
			line += " channel " + i.Channel;
		}
		if (i.Rssi < 0)
		{
			line += " " + i.Rssi + " dBm";
		}
		string auth = GatewayErrorNames.AuthMode(i.AuthMode);
		if (auth != null)
		{
			line += " " + auth;
		}
		return line + ", " + (i.GotIp ? "got IP" : i.Associated ? "associated, no IP" : "not associated");
	}

	/// <summary>Name of a step (state 2 "now: ...", state 4 "failed at: ..."); null for none.</summary>
	private static string StepName(int step, Func<string, string> t)
	{
		return (step >= 1 && step <= 4) ? t("S.Cloud.Step" + step) : null;
	}

	/// <summary>
	/// Code and name of what made a step fail (or the last WiFi try end): "201 NO_AP_FOUND", "0 CONNECT_TIMEOUT",
	/// "NTP_TIMEOUT", "HTTP 400 Bad Request, INVALID_LOGIN_CREDENTIALS", "-1 CONNECTION_REFUSED, DNS_FAILED".
	/// Names only, the same in every language; what each code means is in the docs (LoRa Status Protocol, error codes).
	/// </summary>
	public static string Reason(int step, int code, int detail)
	{
		switch (step)
		{
		case 1:
			return GatewayErrorNames.Wifi(code);
		case 2:
			return (detail == 1) ? "DNS_FAILED" : "NTP_TIMEOUT";
		case 3:
		case 4:
			if (code == 0)
			{
				return null;
			}
			string name = GatewayErrorNames.Detail(detail);
			return GatewayErrorNames.Http(code) + ((name != null) ? (", " + name) : "");
		default:
			return null;
		}
	}

	private static string Plural(int n, string one, string many)
	{
		return n + " " + ((n == 1) ? one : many);
	}

	private static string FormatAge(int seconds)
	{
		if (seconds >= 16382)
		{
			return "> 4 h";
		}
		if (seconds < 120)
		{
			return seconds + " s";
		}
		return (seconds < 7200) ? (seconds / 60 + " min") : (seconds / 3600 + " h");
	}
}
