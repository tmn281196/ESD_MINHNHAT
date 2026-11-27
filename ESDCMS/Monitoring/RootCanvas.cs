using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ESDCMS.Device;

namespace ESDCMS.Monitoring;

public class RootCanvas
{
	public int WidthSlot = 1650;

	public int HeightSlot = 100;

	public int OffsetYStaticRectangle = 5;

	public int OffsetXStaticRectangle = 5;

	public int WidthStaticRectangle = 90;

	public int HeightStaticRectangle = 90;

	public int SpaceStaticRectangle = 5;

	public int BlockItemCount = 20;

	public Rectangle Slot;

	public int TopSlot = 140;

	public int LeftSlot = 5;

	public Canvas Canvas;

	public bool Locked = true;

	/// <summary>Raised with the device index (0-based) when a box tile is clicked (not dragged).</summary>
	public System.Action<int> BoxClicked;

	/// <summary>Raised when a box tile is switched in / out of use (right-click while unlocked); the index of the box.</summary>
	public System.Action<int> UsedChanged;

	public List<BlockDevice> DynamicDevicesList = new List<BlockDevice>();

	public ConfigMonitoring ConfigData { get; set; }

	public void SaveConfig()
	{
		ConfigStore.Save();
	}

	public ConfigMonitoring LoadConfig()
	{
		return ConfigStore.Load().Monitoring;
	}

	public RootCanvas(Canvas canvas)
	{
		ConfigData = LoadConfig();
		Canvas = canvas;
		WidthSlot = OffsetXStaticRectangle + BlockItemCount * (WidthStaticRectangle + SpaceStaticRectangle);
		Slot = new Rectangle
		{
			Width = WidthSlot,
			Height = HeightSlot,
			Fill = Brushes.LightBlue
		};
		Canvas.SetLeft(Slot, LeftSlot);
		Canvas.SetTop(Slot, TopSlot);
		Canvas.Children.Add(Slot);
		for (int i = 0; i < BlockItemCount; i++)
		{
			AddStaticRectangleItem(LeftSlot + OffsetXStaticRectangle + i * (WidthStaticRectangle + SpaceStaticRectangle), TopSlot + OffsetYStaticRectangle);
		}
		for (int j = 0; j < ConfigData.DevicePosition.Count; j++)
		{
			AddDynamicDevicesItem(j);
		}
	}

	private void AddStaticRectangleItem(double left, double top)
	{
		Brush stroke = Brushes.Red;
		Brush backgroundColor = Brushes.White;
		Rectangle newRectangle = new Rectangle
		{
			Width = WidthStaticRectangle,
			Height = HeightStaticRectangle,
			Fill = backgroundColor,
			Stroke = stroke,
			StrokeThickness = 1.0,
			StrokeDashArray = new DoubleCollection { 2.0, 2.0 }
		};
		Canvas.SetLeft(newRectangle, left);
		Canvas.SetTop(newRectangle, top);
		Canvas.Children.Add(newRectangle);
	}

	private void AddDynamicDevicesItem(int index)
	{
		bool isUsed = ConfigData.DevicesIsActive[index];
		int position = ConfigData.DevicePosition[index];
		int left = LeftSlot + OffsetXStaticRectangle + position * (WidthStaticRectangle + SpaceStaticRectangle);
		int top = TopSlot + OffsetYStaticRectangle;
		BlockDevice dynamicRectangle = new BlockDevice(this, index, position, isUsed, left, top, (Brush)new BrushConverter().ConvertFromString("#037ef3"), (index + 1).ToString("D2") ?? "");
		DynamicDevicesList.Add(dynamicRectangle);
		Canvas.Children.Add(dynamicRectangle.CanvasContent);
	}
}
