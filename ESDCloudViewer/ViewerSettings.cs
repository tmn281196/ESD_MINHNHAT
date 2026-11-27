using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ESDCloudViewer;

/// <summary>
/// Firebase settings of the viewer, kept in %USERPROFILE%\.esdcms\cloudviewer.json (next to the ESDCMS config).
/// The same values as the gateway's secrets.h. The password is encrypted with Windows DPAPI for the current user, so the
/// file is useless on another PC or for another Windows user (the password must then be entered again).
/// </summary>
public sealed class ViewerSettings
{
	public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".esdcms");

	private static readonly string FilePath = Path.Combine(Folder, "cloudviewer.json");

	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ESDCloudViewer:firebase");

	/// <summary>FIREBASE_DB_URL, without '/' at the end.</summary>
	public string DbUrl { get; set; } = string.Empty;

	/// <summary>FIREBASE_API_KEY (the Web API key of the Firebase project).</summary>
	public string ApiKey { get; set; } = string.Empty;

	/// <summary>FIREBASE_EMAIL.</summary>
	public string Email { get; set; } = string.Empty;

	/// <summary>FIREBASE_PASSWORD, DPAPI-encrypted (base64); empty when not remembered.</summary>
	public string PasswordProtected { get; set; } = string.Empty;

	/// <summary>The line to show (NETWORK_ID of the gateway, 2 digits).</summary>
	public string Line { get; set; } = "03";

	/// <summary>Remember the account (password included) on this PC; otherwise only the language is kept.</summary>
	public bool Remember { get; set; } = true;

	public string Language { get; set; } = "en";

	/// <summary>The password in clear, in memory only.</summary>
	[JsonIgnore]
	public string Password { get; set; } = string.Empty;

	[JsonIgnore]
	public bool IsComplete => DbUrl.Length > 0 && ApiKey.Length > 0 && Email.Length > 0 && Password.Length > 0 && Line.Length == 2;

	/// <summary>Host of the database URL, for the bottom bar.</summary>
	[JsonIgnore]
	public string DbHost
	{
		get
		{
			return Uri.TryCreate(DbUrl, UriKind.Absolute, out Uri uri) ? uri.Host : DbUrl;
		}
	}

	public ViewerSettings Clone()
	{
		return (ViewerSettings)MemberwiseClone();
	}

	public static ViewerSettings Load()
	{
		ViewerSettings s = null;
		try
		{
			if (File.Exists(FilePath))
			{
				s = JsonConvert.DeserializeObject<ViewerSettings>(File.ReadAllText(FilePath));
			}
		}
		catch (Exception)
		{
		}
		s ??= new ViewerSettings();
		s.DbUrl ??= string.Empty;
		s.ApiKey ??= string.Empty;
		s.Email ??= string.Empty;
		s.Line ??= "03";
		s.Password = Unprotect(s.PasswordProtected);
		return s;
	}

	public void Save()
	{
		ViewerSettings stored = Clone();
		if (Remember)
		{
			stored.PasswordProtected = Protect(Password);
		}
		else
		{
			stored.DbUrl = stored.ApiKey = stored.Email = stored.PasswordProtected = string.Empty;
		}
		Directory.CreateDirectory(Folder);
		File.WriteAllText(FilePath, JsonConvert.SerializeObject(stored, Formatting.Indented));
	}

	private static string Protect(string password)
	{
		if (string.IsNullOrEmpty(password))
		{
			return string.Empty;
		}
		byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
		return Convert.ToBase64String(data);
	}

	private static string Unprotect(string stored)
	{
		if (string.IsNullOrEmpty(stored))
		{
			return string.Empty;
		}
		try
		{
			return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
		}
		catch (Exception)
		{
			return string.Empty;     // another PC or Windows user: enter the password again
		}
	}
}
