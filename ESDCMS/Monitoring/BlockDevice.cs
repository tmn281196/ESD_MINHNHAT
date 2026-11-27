using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ESDCMS.Device;

namespace ESDCMS.Monitoring;

public class BlockDevice : BlockCanvas
{
	public DeviceStatus Status;

	public static readonly Brush NotInUseBrush = new SolidColorBrush(Color.FromRgb(158, 158, 158));

	public Brush InUseColor;

	public BlockDevice(RootCanvas parentCanvas, int index, int position, bool isUsed, int x, int y, Brush fillColor, string text)
		: base(parentCanvas, index, position, x, y, fillColor, text)
	{
		base.Position = position;
		base.Index = index;
		base.IsUsed = isUsed;
		base.x = x;
		base.y = y;
		base.fillColor = fillColor;
		InUseColor = fillColor;
		rootCanvas = parentCanvas;
		width = parentCanvas.WidthStaticRectangle;
		height = parentCanvas.HeightStaticRectangle;
		CanvasContent = new Canvas
		{
			Width = width,
			Height = height
		};
		NewRectangle = new Rectangle
		{
			Width = width,
			Height = height,
			Fill = (isUsed ? fillColor : NotInUseBrush)
		};
		TextBlock = new TextBlock
		{
			Text = text,
			Foreground = Brushes.White,
			FontSize = 34.0,
			FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Width = width,
			Height = height,
			TextAlignment = TextAlignment.Center,
			Padding = new Thickness(0.0, (double)(height - 42) / 2.0, 0.0, 0.0)
		};
		CanvasContent.Children.Add(NewRectangle);
		CanvasContent.Children.Add(TextBlock);
		Canvas.SetLeft(CanvasContent, x);
		Canvas.SetTop(CanvasContent, y);
		CanvasContent.MouseDown += Element_MouseDown;
		CanvasContent.MouseUp += Element_MouseUp;
		CanvasContent.MouseRightButtonUp += Element_RightClick;
		CanvasContent.Cursor = Cursors.Hand;
		// Follows the UI language (Lang/Strings.*.xaml)
		CanvasContent.SetResourceReference(FrameworkElement.ToolTipProperty, "S.Tip.Tile");
	}

	/// <summary>A press that moves less than this (px) before the release is a click.</summary>
	private const double ClickSlop = 4.0;

	private bool _leftPressed;

	private Point _pressPoint;

	public void ApplyBaseColor()
	{
		if (base.IsUsed)
		{
			NewRectangle.Fill = InUseColor;
			return;
		}
		NewRectangle.Fill = NotInUseBrush;
		TextBlock.Foreground = Brushes.White;
	}

	private void Element_RightClick(object sender, MouseButtonEventArgs e)
	{
		if (!rootCanvas.Locked)
		{
			base.IsUsed = !base.IsUsed;
			rootCanvas.ConfigData.DevicesIsActive[base.Index] = base.IsUsed;
			ApplyBaseColor();
			rootCanvas.UsedChanged?.Invoke();
			e.Handled = true;
		}
	}

	// The tiles do not move (no drag): each box stays on the slot from config.json (ConfigMonitoring.DevicePosition).
	private void Element_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			_leftPressed = true;
			_pressPoint = e.GetPosition(rootCanvas.Canvas);
		}
	}

	private void Element_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left && _leftPressed)
		{
			_leftPressed = false;
			Vector moved = e.GetPosition(rootCanvas.Canvas) - _pressPoint;
			if (Math.Abs(moved.X) < ClickSlop && Math.Abs(moved.Y) < ClickSlop)
			{
				rootCanvas.BoxClicked?.Invoke(base.Index);
			}
		}
	}

	public void SnapToCurrentPosition()
	{
		Left = rootCanvas.LeftSlot + rootCanvas.OffsetXStaticRectangle + base.Position * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
		Top = rootCanvas.TopSlot + rootCanvas.OffsetYStaticRectangle;
		Canvas.SetLeft(CanvasContent, Left);
		Canvas.SetTop(CanvasContent, Top);
	}
}
