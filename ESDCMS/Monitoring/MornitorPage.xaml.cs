using System.Windows.Controls;
using ESDCMS.Device;

namespace ESDCMS.Monitoring;

public partial class MonitorPage : Page
{
	/// <summary>Gap (px) between the bottom of the box row and the offline alert.</summary>
	private const int AlertGap = 16;

	public RootCanvas RootCanvas;

	public ViewModel ViewModel { get; set; }

	public MonitorPage(ViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;
		RootCanvas = new RootCanvas(MainCanvas);
		ViewModel.RootCanvas = RootCanvas;
		RootCanvas.BoxClicked = ViewModel.RequestBoxStatus;
		RootCanvas.UsedChanged = ViewModel.BoxUseChanged;
		// Offline alert: just below the box row, as wide as the conveyor strip (bound to HasOfflineAlert / OfflineAlertText).
		OfflineAlertBar.DataContext = viewModel;
		Canvas.SetLeft(OfflineAlertBar, RootCanvas.LeftSlot);
		Canvas.SetTop(OfflineAlertBar, RootCanvas.TopSlot + RootCanvas.HeightSlot + AlertGap);
		OfflineAlertBar.Width = RootCanvas.WidthSlot;
	}
}
