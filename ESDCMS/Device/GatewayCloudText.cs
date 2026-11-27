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
			0 => t("S.Wifi.State0"),
			1 => t("S.Wifi.State1"),
			2 => string.Format(t("S.Wifi.State2"), s.WifiRssi),
			3 => string.Format(t("S.Wifi.State3"), s.WifiRssi),
			4 => string.Format(t("S.Wifi.State4"), s.WifiRssi),
			_ => t("S.Wifi.State5")
		};
		string step = StepName(s.Step, t);
		string reason = Reason(s.Step, s.Code, s.Detail, t);
		switch (s.State)
		{
		case 1:
			if (reason != null)
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
			text += " " + string.Format(t("S.Cloud.Why"), reason ?? t("S.Cloud.WifiTimeout"));
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
				: "Gateway -> Firebase: sign-in FAILED (" + took + "): " + (Reason(3, r.Code, r.Detail, Lang.En) ?? "?");
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
			: "Gateway -> Firebase: write FAILED (" + took + "): " + (Reason(4, r.Code, r.Detail, Lang.En) ?? "?") + "; tried " + content;
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

	/// <summary>Name of a step (state 2 "now: ...", state 4 "failed at: ..."); null for none.</summary>
	private static string StepName(int step, Func<string, string> t)
	{
		return (step >= 1 && step <= 4) ? t("S.Cloud.Step" + step) : null;
	}

	/// <summary>Why a step failed (or the last WiFi try ended), from its code and detail; null when nothing is known.</summary>
	public static string Reason(int step, int code, int detail, Func<string, string> t)
	{
		switch (step)
		{
		case 1:
			switch (code)
			{
			case 0:
				return null;
			case 201:
			case 210:
			case 211:
				return t("S.Cloud.WifiNoAp") + " (" + code + ")";
			case 15:
			case 202:
			case 204:
				return t("S.Cloud.WifiPassword") + " (" + code + ")";
			case 200:
				return t("S.Cloud.WifiLost") + " (" + code + ")";
			default:
				return string.Format(t("S.Cloud.WifiReason"), code);
			}
		case 2:
			return t((detail == 1) ? "S.Cloud.NoInternet" : "S.Cloud.NoTime");
		case 3:
		case 4:
			if (code < 0)
			{
				switch (detail)
				{
				case 1:
					return t("S.Cloud.Dns");
				case 2:
					return t("S.Cloud.Tcp");
				case 3:
					return t("S.Cloud.Tls");
				case 4:
					return t("S.Cloud.Cert");
				}
				return (code == -11) ? t("S.Cloud.NoAnswer") : string.Format(t("S.Cloud.NetError"), code);
			}
			if (code == 0)
			{
				return null;
			}
			string why = detail switch
			{
				10 => t("S.Cloud.Login"),
				11 => t("S.Cloud.ApiKey"),
				12 => t("S.Cloud.LoginOff"),
				13 => t("S.Cloud.UserOff"),
				14 => t("S.Cloud.TooMany"),
				15 => t("S.Cloud.BadEmail"),
				16 => t("S.Cloud.NoToken"),
				_ => null
			};
			if (why == null && step == 4)
			{
				why = code switch
				{
					401 => t("S.Cloud.Denied"),
					403 => t("S.Cloud.Denied"),
					404 => t("S.Cloud.NoDb"),
					_ => null
				};
			}
			return (why == null) ? ("HTTP " + code) : (why + " (HTTP " + code + ")");
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
