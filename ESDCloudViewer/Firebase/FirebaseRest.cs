using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ESDCloudViewer.Firebase;

/// <summary>Answer of one REST call. <see cref="Net"/> = no answer at all (no internet, wrong host...).</summary>
public sealed class HttpResult
{
	public bool Net;

	public int Status;

	/// <summary>Parsed JSON answer; null when it is not JSON or is JSON null.</summary>
	public JToken Json;

	public string Text = string.Empty;

	/// <summary>Firebase error message of the answer ("error" string or "error.message"), else the raw text.</summary>
	public string ErrorMessage
	{
		get
		{
			JToken e = (Json as JObject)?["error"];
			if (e is JObject o)
			{
				return (string)o["message"] ?? Text;
			}
			return (e != null && e.Type == JTokenType.String) ? (string)e : Text;
		}
	}
}

public sealed class SignInResult
{
	public string Token;

	public string Uid;

	/// <summary>English reason when the sign-in failed (null = OK).</summary>
	public string Error;
}

/// <summary>A stream answered with an HTTP error (401 = the rules do not allow reading).</summary>
public sealed class FirebaseHttpException : Exception
{
	public int Status { get; }

	public FirebaseHttpException(int status, string text)
		: base("HTTP " + status + " " + text)
	{
		Status = status;
	}
}

/// <summary>
/// The Firebase REST calls the gateway makes (lora_gateway.ino): Auth sign-in with email/password, then the Realtime
/// Database with ?auth=&lt;ID token&gt;. Plus the database stream (Server-Sent Events) for the live view. No Firebase SDK.
/// </summary>
public static class FirebaseRest
{
	private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

	/// <summary>Streams stay open for hours: no overall timeout (LineWatcher has its own watchdog).</summary>
	private static readonly HttpClient StreamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

	public static readonly HttpMethod Patch = new HttpMethod("PATCH");

	public static async Task<HttpResult> CallAsync(HttpMethod method, string url, JToken body = null)
	{
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(method, url);
			if (body != null)
			{
				req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
			}
			using HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false);
			string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			JToken json = null;
			try
			{
				json = JToken.Parse(text);
				if (json.Type == JTokenType.Null)
				{
					json = null;
				}
			}
			catch (JsonException)
			{
			}
			return new HttpResult { Status = (int)resp.StatusCode, Json = json, Text = text };
		}
		catch (Exception ex)
		{
			return new HttpResult { Net = true, Text = ex.GetBaseException().Message };
		}
	}

	/// <summary>Same call as cloudSignIn() in lora_gateway.ino.</summary>
	public static async Task<SignInResult> SignInAsync(ViewerSettings s)
	{
		JObject body = new JObject
		{
			["email"] = s.Email,
			["password"] = s.Password,
			["returnSecureToken"] = true
		};
		HttpResult r = await CallAsync(HttpMethod.Post,
			"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + Uri.EscapeDataString(s.ApiKey), body).ConfigureAwait(false);
		if (r.Net)
		{
			return new SignInResult { Error = "cannot reach Firebase Auth: check the internet of this PC (" + r.Text + ")" };
		}
		string token = (string)r.Json?["idToken"];
		if (r.Status != 200 || string.IsNullOrEmpty(token))
		{
			return new SignInResult { Error = AuthError(r.ErrorMessage) };
		}
		return new SignInResult { Token = token, Uid = (string)r.Json["localId"] };
	}

	/// <summary>Why Firebase Auth refused the sign-in, and where to fix it in the Firebase console.</summary>
	public static string AuthError(string message)
	{
		string m = message ?? string.Empty;
		if (Regex.IsMatch(m, "API key not valid|API_KEY_INVALID|API_KEY", RegexOptions.IgnoreCase))
		{
			return "wrong FIREBASE_API_KEY (Firebase does not know this key)";
		}
		if (Regex.IsMatch(m, "INVALID_LOGIN_CREDENTIALS|EMAIL_NOT_FOUND|INVALID_PASSWORD|MISSING_PASSWORD"))
		{
			return "wrong email or password: compare with the user in Security > Authentication > Users (must be identical)";
		}
		if (m.Contains("INVALID_EMAIL"))
		{
			return "the email is not valid";
		}
		if (Regex.IsMatch(m, "OPERATION_NOT_ALLOWED|PASSWORD_LOGIN_DISABLED"))
		{
			return "Email/Password sign-in is not enabled: Security > Authentication > Sign-in method > Email/Password > Enable";
		}
		if (m.Contains("USER_DISABLED"))
		{
			return "this user is disabled in Security > Authentication > Users";
		}
		if (m.Contains("TOO_MANY_ATTEMPTS"))
		{
			return "too many wrong tries, Firebase blocks them for a while: wait a few minutes";
		}
		if (m.Contains("CONFIGURATION_NOT_FOUND"))
		{
			return "Authentication is not set up in the project: Security > Authentication > Get started";
		}
		return "Firebase says: " + m;
	}

	/// <summary>
	/// Opens a database stream (GET with Accept: text/event-stream) and calls <paramref name="onEvent"/> (event, data)
	/// for each event: put / patch / keep-alive / cancel / auth_revoked. Returns when the server closes the stream; throws
	/// <see cref="FirebaseHttpException"/> for an HTTP error, OperationCanceledException when <paramref name="ct"/> fires.
	/// </summary>
	public static async Task StreamAsync(string url, Action<string, string> onEvent, CancellationToken ct)
	{
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
		req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		using HttpResponseMessage resp = await StreamHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
		if (!resp.IsSuccessStatusCode)
		{
			string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			throw new FirebaseHttpException((int)resp.StatusCode, text);
		}
		using Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
		// ReadLineAsync cannot be cancelled: closing the response ends it.
		using CancellationTokenRegistration reg = ct.Register(delegate
		{
			try
			{
				resp.Dispose();
			}
			catch (Exception)
			{
			}
		});
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		string ev = null;
		StringBuilder data = new StringBuilder();
		while (true)
		{
			string line;
			try
			{
				line = await reader.ReadLineAsync().ConfigureAwait(false);
			}
			catch (Exception) when (ct.IsCancellationRequested)
			{
				throw new OperationCanceledException(ct);
			}
			ct.ThrowIfCancellationRequested();
			if (line == null)
			{
				return;
			}
			if (line.Length == 0)
			{
				if (ev != null)
				{
					onEvent(ev, data.ToString());
				}
				ev = null;
				data.Clear();
			}
			else if (line.StartsWith("event:", StringComparison.Ordinal))
			{
				ev = line.Substring(6).Trim();
			}
			else if (line.StartsWith("data:", StringComparison.Ordinal))
			{
				if (data.Length > 0)
				{
					data.Append('\n');
				}
				data.Append(line.Substring(5).TrimStart());
			}
		}
	}
}
