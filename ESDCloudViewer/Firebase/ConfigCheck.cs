using System;
using System.Collections.Generic;
using System.Net.Http;
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
/// Checks the Firebase settings: format, sign-in, that every line to watch can be read, and a test write (removed again)
/// the way ESDCMS writes. Each result goes to <c>report</c> (English, for the console log).
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
			Fail("Database URL has a wrong format", "must be https://<name>.<region>.firebasedatabase.app (or ...firebaseio.com), no '/' at the end, no spaces");
		}
		if (!Regex.IsMatch(s.ApiKey, @"^AIza[0-9A-Za-z_\-]{35}$"))
		{
			Fail("Web API key has a wrong format", "a key is 39 characters starting with AIza: check for extra or missing characters or spaces");
		}
		if (!Regex.IsMatch(s.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
		{
			Fail("Email has a wrong format", "must look like name@domain.com, no spaces");
		}
		if (s.Password.Length < 6)
		{
			Fail("Password is too short", "Firebase needs at least 6 characters");
		}
		List<string> lines = s.LineList;
		if (lines.Count == 0)
		{
			Fail("No line to watch", "lines are Net IDs as 2 hex digits, e.g. 03, 02");
		}
		if (!ok)
		{
			return (null, false);
		}
		report(CheckKind.Ok, "The settings have the right format", null);

		// 2. Sign in (same call as ESDCMS CloudUploader)
		SignInResult signIn = await FirebaseRest.SignInAsync(s);
		if (signIn.Error != null)
		{
			Fail("Sign-in failed", signIn.Error);
			return (null, false);
		}
		report(CheckKind.Ok, "Signed in (API key, email and password are right)", null);
		string auth = "?auth=" + Uri.EscapeDataString(signIn.Token);

		// 3. Every line to watch can be read
		foreach (string line in lines)
		{
			HttpResult r = await FirebaseRest.CallAsync(HttpMethod.Get, s.DbUrl + "/lines/" + line + "/pcs.json" + auth + "&shallow=true");
			if (r.Net)
			{
				Fail("Cannot reach the Realtime Database", "check the database URL (" + r.Text + ")");
				return (signIn, false);
			}
			if (r.Status == 404 || Regex.IsMatch(r.ErrorMessage ?? string.Empty, "Firebase error|ensure that you have the URL", RegexOptions.IgnoreCase))
			{
				Fail("The database URL is not this project's database", "copy the link again from Realtime Database > Data (the line with the link icon)");
				return (signIn, false);
			}
			if (r.Status == 200)
			{
				int pcs = (r.Json as JObject)?.Count ?? 0;
				if (pcs > 0)
				{
					report(CheckKind.Ok, "Can read lines/" + line, pcs + " PC(s) write this line");
				}
				else
				{
					report(CheckKind.Warn, "Can read lines/" + line + ", but no PC wrote it yet", "turn the cloud on in ESDCMS of that line (Cloud group, gear button)");
				}
			}
			else
			{
				Fail("Cannot read lines/" + line + " (HTTP " + r.Status + ")", "the rules must let a signed-in user read lines. Firebase says: " + (r.Net ? r.Text : r.ErrorMessage));
			}
		}

		// 4. This account can write a line the way ESDCMS does (a test value, removed again)
		string first = lines[0];
		JObject testValue = new JObject { ["pcs/viewer-check/lastSeen"] = new JObject { [".sv"] = "timestamp" } };
		HttpResult w = await FirebaseRest.CallAsync(FirebaseRest.Patch, s.DbUrl + "/lines/" + first + ".json" + auth, testValue);
		if (w.Status == 200)
		{
			report(CheckKind.Ok, "Can write to lines/" + first, "the same way as ESDCMS; the test value (pcs/viewer-check) was removed again");
			await FirebaseRest.CallAsync(HttpMethod.Delete, s.DbUrl + "/lines/" + first + "/pcs/viewer-check.json" + auth);
		}
		else
		{
			Fail("Cannot write to lines/" + first + " (HTTP " + w.Status + ")", "usually the rules are not published. Firebase says: " + (w.Net ? w.Text : w.ErrorMessage));
		}
		return (signIn, ok);
	}
}
