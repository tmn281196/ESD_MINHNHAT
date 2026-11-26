namespace ESDCMS.Device;

/// <summary>
/// Values of the RF Gateway / Control Box COM port boxes. A real port name connects that device;
/// "Close" (and the older "Unused" / "Unset" still found in some config.json files) means: keep it closed.
/// </summary>
public static class PortNames
{
	public const string Close = "Close";

	public static bool IsPort(string name)
	{
		return !string.IsNullOrWhiteSpace(name) && name != Close && name != "Unused" && name != "Unset";
	}

	/// <summary>Maps the older "Unused" to "Close" so the box shows the current option.</summary>
	public static string Normalize(string name)
	{
		return (name == "Unused") ? Close : name;
	}
}
