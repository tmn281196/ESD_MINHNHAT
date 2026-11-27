using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ESDCMS.Device;

/// <summary>Answer of one Firebase REST call; <see cref="Failed"/> = no HTTP answer at all.</summary>
public sealed class FirebaseResult
{
	public int Status;

	public string Reason = string.Empty;

	public JToken Json;

	public string Text = string.Empty;

	/// <summary>No answer: name of the network error (NameResolutionFailure, ConnectFailure, Timeout...).</summary>
	public string Failed;

	public bool Ok => Failed == null && Status == 200;

	/// <summary>
	/// Code and name of the error, no advice: "HTTP 401 Unauthorized, Permission denied",
	/// "HTTP 400 Bad Request, INVALID_LOGIN_CREDENTIALS", "NameResolutionFailure". The meaning is in the docs.
	/// </summary>
	public string ErrorName
	{
		get
		{
			if (Failed != null)
			{
				return Failed;
			}
			string name = "HTTP " + Status + (string.IsNullOrEmpty(Reason) ? "" : (" " + Reason));
			string detail = FirebaseError();
			return (detail == null) ? name : (name + ", " + detail);
		}
	}

	/// <summary>Firebase's own error name: "INVALID_LOGIN_CREDENTIALS", "API_KEY_INVALID", "Permission denied"...</summary>
	private string FirebaseError()
	{
		JToken e = (Json as JObject)?["error"];
		if (e == null)
		{
			return null;
		}
		if (e.Type == JTokenType.String)
		{
			return (string)e;                                   // database: {"error": "Permission denied"}
		}
		string message = (string)e["message"] ?? string.Empty;  // auth: {"error": {"message": "INVALID_LOGIN_CREDENTIALS", ...}}
		Match m = Regex.Match(message, "^[A-Z][A-Z0-9_]+(?=$| :)");   // "INVALID_LOGIN_CREDENTIALS", not "API" of "API key not valid"
		if (m.Success)
		{
			return m.Value;
		}
		foreach (JToken d in (e["details"] as JArray) ?? new JArray())
		{
			string reason = (string)d["reason"];
			if (!string.IsNullOrEmpty(reason))
			{
				return reason;                                  // e.g. API_KEY_INVALID
			}
		}
		return (string)e["status"] ?? (message.Length > 0 ? message : null);
	}
}

/// <summary>
/// The Firebase REST calls of the cloud upload: Auth sign-in with email/password (ID token), and the Realtime Database
/// with ?auth=&lt;ID token&gt;. No Firebase SDK.
/// </summary>
public static class FirebaseRest
{
	private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

	public static readonly HttpMethod Patch = new HttpMethod("PATCH");

	static FirebaseRest()
	{
		// Firebase needs TLS 1.2 or newer.
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	public static async Task<FirebaseResult> CallAsync(HttpMethod method, string url, JToken body = null)
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
			}
			catch (JsonException)
			{
			}
			return new FirebaseResult { Status = (int)resp.StatusCode, Reason = resp.ReasonPhrase ?? string.Empty, Json = json, Text = text };
		}
		catch (TaskCanceledException)
		{
			return new FirebaseResult { Failed = "Timeout" };
		}
		catch (Exception ex)
		{
			Exception inner = ex.GetBaseException();
			string name = (inner is WebException we) ? we.Status.ToString() : inner.GetType().Name;
			return new FirebaseResult { Failed = name };
		}
	}

	/// <summary>Email/password sign-in; the ID token is in Json["idToken"] when Ok.</summary>
	public static Task<FirebaseResult> SignInAsync(string apiKey, string email, string password)
	{
		JObject body = new JObject
		{
			["email"] = email,
			["password"] = password,
			["returnSecureToken"] = true
		};
		return CallAsync(HttpMethod.Post, "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + Uri.EscapeDataString(apiKey), body);
	}
}
