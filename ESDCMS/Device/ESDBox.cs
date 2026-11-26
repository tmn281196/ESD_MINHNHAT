using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ESDCMS.Device;

public class ESDBox : INotifyPropertyChanged
{
	public int StateChangeCounter = 0;

	public int ErrorCounter = 0;

	public int ID = -1;

	private static readonly Brush BrushUnknown;

	private static readonly Brush BrushActive;

	private static readonly Brush BrushWarning;

	private static readonly Brush BrushInactive;

	public Action<ESDBox> StatusChangeAction { get; set; }

	public Brush ColorState { get; set; } = (Brush)new BrushConverter().ConvertFromString("#037ef3");

	public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

	public event PropertyChangedEventHandler PropertyChanged;

	static ESDBox()
	{
		BrushUnknown = (Brush)new BrushConverter().ConvertFromString("#037ef3");
		BrushActive = (Brush)new BrushConverter().ConvertFromString("#45f248");
		BrushWarning = Brushes.OrangeRed;
		BrushInactive = Brushes.Yellow;
		if (BrushUnknown.CanFreeze)
		{
			BrushUnknown.Freeze();
		}
		if (BrushActive.CanFreeze)
		{
			BrushActive.Freeze();
		}
	}

	private void OnStatusChanged()
	{
		StateChangeCounter++;
		Brush brush = BrushUnknown;
		switch (Status)
		{
		case DeviceStatus.Unknown:
			brush = BrushUnknown;
			break;
		case DeviceStatus.Active:
			brush = BrushActive;
			break;
		case DeviceStatus.Warning:
			brush = BrushWarning;
			break;
		case DeviceStatus.Inactive:
			brush = BrushInactive;
			break;
		case DeviceStatus.Offline:
			// Same blue as Unknown (the start-up colour): gray is reserved for "not in use" tiles.
			brush = BrushUnknown;
			break;
		case DeviceStatus.Error:
			ErrorCounter++;
			return;
		}
		ColorState = brush;
		try
		{
			StatusChangeAction(this);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message);
		}
	}

	public ESDBox(int id)
	{
		ID = id;
	}

	public async Task ReceiveState(DeviceStatus deviceStatus)
	{
	}

}
