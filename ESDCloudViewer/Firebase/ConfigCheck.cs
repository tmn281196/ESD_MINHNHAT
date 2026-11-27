using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ESDCloudViewer.Firebase;

public enum CheckKind
{
	Ok,
	Warn,
	Fail
}

/// <summary>
/// Checks the Firebase settings exactly as ESDCMS uses them (its Cloud settings): format, sign-in, the gateways/&lt;UID&gt;
/// entry the rules need, a test write to lines/&lt;line&gt; (removed again) and that another line is refused.
/// Each result goes to <c>report</c> (English, for the console log).
/// </summary>
public static class ConfigCheck
{
	/// <summary>Runs every check. Returns the sign-in (to start watching) or null when it failed; <c>allOk</c> = nothing to fix.</summary>
	public static async Task<(SignInResult signIn, bool allOk)> RunAsync(ViewerSettings s, Action<CheckKind, string, string> report)
	{
		bool ok = true;
		void Fail(string title, string detail)
		{
			report(CheckKind.Fail, title, detail);
			ok = false;
		}

		// 1. Format
		if (!Regex.IsMatch(s.DbUrl, @"^https://[a-z0-9-]+(\.[a-z0-9-]+)*\.(firebasedatabase\.app|firebaseio\.com)$"))
		{
			Fail("FIREBASE_DB_URL has a wrong format", "must be https://<name>.<region>.firebasedatabase.app (or ...firebaseio.com), no '/' at the end, no spaces");
		}
		if (!Regex.IsMatch(s.ApiKey, @"^AIza[0-9A-Za-z_\-]{35}$"))
		{
			Fail("FIREBASE_API_KEY has a wrong format", "a key is 39 characters starting with AIza: check for extra or missing characters or spaces");
		}
		if (!Regex.IsMatch(s.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
		{
			Fail("FIREBASE_EMAIL has a wrong format", "must look like name@domain.com, no spaces");
		}
		if (s.Password.Length < 6)
		{
			Fail("FIREBASE_PASSWORD is too short", "Firebase needs at least 6 characters");
		}
		if (s.Password.IndexOf('"') >= 0 || s.Password.IndexOf('\\') >= 0)
		{
			Fail("FIREBASE_PASSWORD has \" or \\", "these break the #define line in secrets.h: use another password");
		}
		if (!Regex.IsMatch(s.Line, @"^\d{2}$"))
		{
			Fail("The line must be 2 digits", "e.g. NETWORK_ID 0x03 -> line 03");
		}
		if (!ok)
		{
			return (null, false);
		}
		report(CheckKind.Ok, "The 4 values have the right format", null);

		// 2. Sign in (same call as ESDCMS CloudUploader)
		SignInResult signIn = await FirebaseRest.SignInAsync(s);
		if (signIn.Error != null)
		{
			Fail("Sign-in failed", signIn.Error);
			return (null, false);
		}
		report(CheckKind.Ok, "Signed in (API key, email and password are right)", "UID of this user: " + signIn.Uid);
		string auth = "?auth=" + Uri.EscapeDataString(signIn.Token);
		string line = s.Line;

		// 3. gateways/<uid> must be the text "03"
		HttpResult g = await FirebaseRest.CallAsync(System.Net.Http.HttpMethod.Get, s.DbUrl + "/gateways/" + Uri.EscapeDataString(signIn.Uid) + ".json" + auth);
		if (g.Net)
		{
			Fail("Cannot reach the Realtime Database", "check FIREBASE_DB_URL (" + g.Text + ")");
			return (signIn, false);
		}
		if (g.Status == 404 || Regex.IsMatch(g.ErrorMessage ?? string.Empty, "Firebase error|ensure that you have the URL", RegexOptions.IgnoreCase))
		{
			Fail("FIREBASE_DB_URL is not this project's database", "copy the link again from Realtime Database > Data (the line with the link icon)");
			return (signIn, false);
		}
		if (g.Status == 401 || g.Status == 403)
		{
			Fail("Cannot read gateways/<UID>: the rules are not published", "Realtime Database > Rules: paste firebase.rules.json, then Publish");
		}
		else if (g.Json == null)
		{
			Fail("gateways/" + signIn.Uid + " is missing",
				"Realtime Database > Data: add gateways > " + signIn.Uid + " = " + line + " (type string, typed WITHOUT quotes)");
		}
		else if (g.Json.Type == JTokenType.Integer || g.Json.Type == JTokenType.Float)
		{
			Fail("gateways/<UID> is the NUMBER " + g.Json + ", it must be the TEXT " + line,
				"Data tab > click the value > type string > " + line + " (WITHOUT quotes) > Save");
		}
		else
		{
			string v = (string)g.Json;
			if (v == line)
			{
				report(CheckKind.Ok, "gateways/<UID> = " + line + " (right, as text)", null);
			}
			else if (Regex.Replace(v, "[\"'\\s]", "") == line)
			{
				Fail("gateways/<UID> has extra quotes or spaces: stored [" + v + "], must be [" + line + "]",
					"Data tab > click the value > clear it, type " + line + " WITHOUT quotes (type string) > Save");
			}
			else
			{
				report(CheckKind.Warn, "gateways/<UID> = [" + v + "], not the line checked (" + line + ")",
					"this account can write only lines/" + v + ". If the Net ID is " + line + ", set the value to " + line + " (type string, WITHOUT quotes)");
				ok = false;
			}
		}

		// 4. Write to lines/<line> like ESDCMS (multi-path PATCH), then remove the test value
		JObject testValue = new JObject { ["pcs/viewer-check/lastSeen"] = new JObject { [".sv"] = "timestamp" } };
		HttpResult w = await FirebaseRest.CallAsync(FirebaseRest.Patch, s.DbUrl + "/lines/" + line + ".json" + auth, testValue);
		if (w.Status == 200)
		{
			report(CheckKind.Ok, "Can write to lines/" + line, "the same way as ESDCMS; the test value (pcs/viewer-check) was removed again");
			await FirebaseRest.CallAsync(System.Net.Http.HttpMethod.Delete, s.DbUrl + "/lines/" + line + "/pcs/viewer-check.json" + auth);
		}
		else
		{
			Fail("Cannot write to lines/" + line + " (HTTP " + w.Status + ")",
				"usually the rules are not published or gateways/<UID> is not " + line + ". Firebase says: " + (w.Net ? w.Text : w.ErrorMessage));
		}

		// 5. Another line must be refused (the rules keep each account to its own line)
		string other = line == "99" ? "98" : "99";
		HttpResult n = await FirebaseRest.CallAsync(FirebaseRest.Patch, s.DbUrl + "/lines/" + other + ".json" + auth,
			new JObject { ["webTest"] = new JObject { [".sv"] = "timestamp" } });
		if (n.Status == 401 || n.Status == 403)
		{
			report(CheckKind.Ok, "The rules block other lines (lines/" + other + " refused)", null);
		}
		else if (n.Status == 200)
		{
			report(CheckKind.Warn, "Could also write to lines/" + other + ": the rules are too open", "paste firebase.rules.json into the Rules tab, then Publish");
			await FirebaseRest.CallAsync(System.Net.Http.HttpMethod.Delete, s.DbUrl + "/lines/" + other + "/webTest.json" + auth);
			ok = false;
		}
		else
		{
			report(CheckKind.Warn, "Unknown result of the test write to lines/" + other + " (HTTP " + n.Status + ")", n.Text);
		}
		return (signIn, ok);
	}
}
