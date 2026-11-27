using System;
using System.Windows;

namespace ESDCloudViewer;

/// <summary>
/// UI language (English / Korean / Thai), same as ESDCMS: each language is a ResourceDictionary of strings in
/// Lang/Strings.&lt;code&gt;.xaml with the same keys. XAML uses {DynamicResource S.Key}; code uses <see cref="T"/>.
/// The console log stays in English (it is a technical log).
/// </summary>
public static class Lang
{
	public static readonly string[] Codes = { "en", "ko", "th" };

	/// <summary>Key present in every strings dictionary; marks the one to replace.</summary>
	private const string MarkerKey = "S.LangCode";

	public static string Current { get; private set; } = "en";

	/// <summary>Raised after the language changed, for text built in code.</summary>
	public static event Action Changed;

	public static string Normalize(string code)
	{
		code = (code ?? string.Empty).Trim().ToLowerInvariant();
		return Array.IndexOf(Codes, code) >= 0 ? code : "en";
	}

	/// <summary>The language after the current one (English → 한국어 → ไทย → English).</summary>
	public static string Next()
	{
		return Codes[(Array.IndexOf(Codes, Current) + 1) % Codes.Length];
	}

	public static void Apply(string code)
	{
		code = Normalize(code);
		Application app = Application.Current;
		if (app == null)
		{
			return;
		}
		ResourceDictionary dict = new ResourceDictionary
		{
			Source = new Uri("pack://application:,,,/ESDCloudViewer;component/Lang/Strings." + code + ".xaml", UriKind.Absolute)
		};
		var merged = app.Resources.MergedDictionaries;
		for (int i = merged.Count - 1; i >= 0; i--)
		{
			if (merged[i].Contains(MarkerKey))
			{
				merged.RemoveAt(i);
			}
		}
		merged.Add(dict);
		Current = code;
		Changed?.Invoke();
	}

	/// <summary>The string for a key in the current language (the key itself if it is missing).</summary>
	public static string T(string key)
	{
		return Application.Current?.TryFindResource(key) as string ?? key;
	}

	public static string F(string key, params object[] args)
	{
		return string.Format(T(key), args);
	}
}
