using ESDCMS.Monitoring;

namespace ESDCMS.Device;

public class AppConfig
{
	public Config Com { get; set; } = new Config();

	public ConfigMonitoring Monitoring { get; set; } = new ConfigMonitoring();
}
