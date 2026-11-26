using System;
using System.Collections.Generic;
using System.IO;
using ESDCMS.Monitoring;
using Newtonsoft.Json;

namespace ESDCMS.Device;

public static class ConfigStore
{
	/// <summary>
	/// Per-user data folder %USERPROFILE%\.esdcms (config.json, crash.log). Not the exe folder: it can be
	/// read-only (Program Files) or replaced by an update. Not the current directory: started from a
	/// shortcut / auto-start it can differ and the COM settings would silently be missing.
	/// </summary>
	public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".esdcms");

	private static readonly string FilePath = Path.Combine(DataDir, "config.json");

	/// <summary>Where versions before ESDCMS kept the file (next to the exe); read once to migrate.</summary>
	private static readonly string LegacyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

	private static bool _loaded;

	private const int DeviceCount = 20;

	public static AppConfig Current { get; private set; } = new AppConfig();

	public static AppConfig Load()
	{
		if (_loaded)
		{
			return Current;
		}
		// First start after the move: take over the old config.json next to the exe (COM ports, layout).
		bool migrate = !File.Exists(FilePath) && File.Exists(LegacyFilePath);
		string source = migrate ? LegacyFilePath : FilePath;
		if (File.Exists(source))
		{
			try
			{
				string json = File.ReadAllText(source);
				Current = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
			}
			catch (Exception ex)
			{
				migrate = false;
				// A config.json damaged by a power cut must not stop the program from starting.
				string bad = source + ".bad-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
				try
				{
					File.Copy(source, bad, overwrite: true);
				}
				catch
				{
				}
				Console.WriteLine("Config: config.json unreadable (" + ex.Message + "), using defaults; copy kept as " + Path.GetFileName(bad));
				Current = new AppConfig();
			}
		}
		else
		{
			Current = new AppConfig();
		}
		if (Current.Com == null)
		{
			Current.Com = new Config();
		}
		if (Current.Monitoring == null)
		{
			Current.Monitoring = new ConfigMonitoring();
		}
		EnsureMonitoringDefaults(Current.Monitoring);
		_loaded = true;
		if (migrate)
		{
			try
			{
				Save();
				Console.WriteLine("Config: config.json moved to " + FilePath + " (old copy next to the exe left as is)");
			}
			catch (Exception ex)
			{
				Console.WriteLine("Config: could not write " + FilePath + " - " + ex.Message);
			}
		}
		return Current;
	}

	public static void Save()
	{
		Directory.CreateDirectory(DataDir);
		string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
		// Write to a temp file then swap, so a crash/power cut mid-write cannot leave a half-written config.json.
		string tmp = FilePath + ".tmp";
		File.WriteAllText(tmp, json);
		if (File.Exists(FilePath))
		{
			File.Replace(tmp, FilePath, null);
		}
		else
		{
			File.Move(tmp, FilePath);
		}
	}

	private static void EnsureMonitoringDefaults(ConfigMonitoring m)
	{
		if (m.DevicePosition == null)
		{
			m.DevicePosition = new List<int>
			{
				0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
				10, 11, 12, 13, 14, 15, 16, 17, 18, 19
			};
		}
		if (m.DevicesIsActive == null)
		{
			m.DevicesIsActive = new List<bool>
			{
				true, true, true, true, true, true, true, true, false, false,
				false, false, false, false, false, false, false, false, false, false
			};
		}
		while (m.DevicePosition.Count < 20)
		{
			m.DevicePosition.Add(m.DevicePosition.Count);
		}
		while (m.DevicesIsActive.Count < 20)
		{
			m.DevicesIsActive.Add(item: false);
		}
		while (m.DevicePosition.Count > 20)
		{
			m.DevicePosition.RemoveAt(m.DevicePosition.Count - 1);
		}
		while (m.DevicesIsActive.Count > 20)
		{
			m.DevicesIsActive.RemoveAt(m.DevicesIsActive.Count - 1);
		}
		NormalizeUniquePositions(m.DevicePosition, 20);
	}

	private static void NormalizeUniquePositions(List<int> positions, int slotCount)
	{
		bool[] used = new bool[slotCount];
		for (int i = 0; i < positions.Count; i++)
		{
			int p = positions[i];
			if (p < 0 || p >= slotCount || used[p])
			{
				int free = Array.IndexOf(used, value: false);
				p = (positions[i] = ((free >= 0) ? free : 0));
			}
			if (p >= 0 && p < slotCount)
			{
				used[p] = true;
			}
		}
	}
}
