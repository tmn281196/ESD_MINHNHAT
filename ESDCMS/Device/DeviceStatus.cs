namespace ESDCMS.Device;

public enum DeviceStatus
{
	Unknown,
	Active,
	Inactive,
	Warning,
	Error,
	/// <summary>The box did not answer the status query (no power, out of range, old firmware).</summary>
	Offline
}
