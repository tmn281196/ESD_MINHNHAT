using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Text.Json.Serialization;
using ESDKitSystem.Device; 

namespace ESDKitSystem.Monitoring
{
    public class BlockDevice : BlockCanvas
    {

        public DeviceStatus Status;

        public TextBlock DeviceNote;

        public BlockCategory Category
        {
            get
            {
                if(this.IsActive)
                {
                    var dynamicCategoriesList = rootCanvas.DynamicCategoriesList.Where(item => this.Position >= item.Position && this.Position < (item.Position + item.Length));

                    if (dynamicCategoriesList != null)
                    {
                        return dynamicCategoriesList.First();
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
        }
        public BlockDevice(RootCanvas parentCanvas, int index , int position, bool isActive, int x, int y, Brush fillColor, string text) : base(parentCanvas, index ,  position, x, y, fillColor, text)
        {
            this.Position = position;
            this.Index = index;
            this.IsActive = isActive;

            this.x = x;
            this.y = y;
            this.fillColor = fillColor;

            this.rootCanvas = parentCanvas;
            this.width = parentCanvas.WidthStaticRectangle;
            this.height = parentCanvas.HeightStaticRectangle;

            CanvasContent = new Canvas
            {
                Width = width,
                Height = height,
            };

            // Create the rectangle
            NewRectangle = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = fillColor
            };
            // Create the text
            TextBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = width,
                Height = height,                

                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, height+5, 0, 0)
            };

            DeviceNote = new TextBlock
            {
                Text = string.Empty,
                Foreground = Brushes.Black,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,   
                Width = width,
                Height = height,

                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, height + 20, 0, 0)
            };

            Image image = new Image
            {
                Width = width,
                Height = height,
                Source = new BitmapImage(new Uri("user.png", UriKind.RelativeOrAbsolute)), // Provide the image path
                Stretch = Stretch.UniformToFill, // Adjust the image stretch mode as needed
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Add the rectangle and text to the container
            CanvasContent.Children.Add(NewRectangle);
            CanvasContent.Children.Add(image);
            CanvasContent.Children.Add(TextBlock);
            CanvasContent.Children.Add(DeviceNote);

            // Set the position of the container
            Canvas.SetLeft(CanvasContent, x);
            Canvas.SetTop(CanvasContent, y);

            // Attach mouse events to the container for dragging
            CanvasContent.MouseDown += Element_MouseDown;
            CanvasContent.MouseMove += Element_MouseMove;
            CanvasContent.MouseUp += Element_MouseUp;
        }
        private void Element_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
            {
                _isDragging = true;
                _draggingElement = element;
                _startPoint = e.GetPosition(rootCanvas.Canvas);
                element.CaptureMouse(); // Capture the mouse to track movement
            }
        }
        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggingElement != null)
            {
                // Current mouse position relative to Canvas
                Point currentPosition = e.GetPosition(rootCanvas.Canvas);

                // Calculate new position
                double offsetX = currentPosition.X - _startPoint.X;
                double offsetY = currentPosition.Y - _startPoint.Y;

                // Update element position
                Left = Canvas.GetLeft(_draggingElement) + offsetX;
                Top = Canvas.GetTop(_draggingElement) + offsetY;

                Canvas.SetLeft(_draggingElement, Left);
                Canvas.SetTop(_draggingElement, Top);

                // Update the start point for the next movement
                _startPoint = currentPosition;
            }
        }

        private void Element_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingElement != null)
            {
                if (Top >= rootCanvas.TopActiveSlot && Top <= rootCanvas.TopActiveSlot + rootCanvas.HeightSlot)
                {
                    if (Left >= rootCanvas.LeftActiveSlot && Left <= rootCanvas.LeftActiveSlot + rootCanvas.WidthSlot)
                    {
                        int i = (int)(Left - (rootCanvas.LeftActiveSlot + rootCanvas.OffsetXStaticRectangle)) / (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                        //Console.WriteLine(i);
                        Left = (rootCanvas.LeftActiveSlot + rootCanvas.OffsetXStaticRectangle) + i * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                        Top = rootCanvas.TopActiveSlot + rootCanvas.OffsetYStaticRectangle;

                        Canvas.SetLeft(_draggingElement, Left);
                        Canvas.SetTop(_draggingElement, Top);

                        IsActive = true;
                        Position = i;
                        this.rootCanvas.ConfigData.DevicesIsActive[this.Index] = IsActive;
                        this.rootCanvas.ConfigData.DevicePosition[this.Index] = Position;
                    }
                }

                if (Top >= rootCanvas.TopInactiveSlot && Top <= rootCanvas.TopInactiveSlot + rootCanvas.HeightSlot)
                {
                    if (Left >= rootCanvas.LeftInactiveSlot && Left <= rootCanvas.LeftInactiveSlot + rootCanvas.WidthSlot)
                    {
                        int i = (int)(Left - (rootCanvas.LeftInactiveSlot + rootCanvas.OffsetXStaticRectangle)) / (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                        Left = (rootCanvas.LeftInactiveSlot + rootCanvas.OffsetXStaticRectangle) + i * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                        Top = rootCanvas.TopInactiveSlot + rootCanvas.OffsetYStaticRectangle;

                        Canvas.SetLeft(_draggingElement, Left);
                        Canvas.SetTop(_draggingElement, Top);

                        IsActive = false;
                        Position = i;
                        this.rootCanvas.ConfigData.DevicesIsActive[this.Index] = IsActive;
                        this.rootCanvas.ConfigData.DevicePosition[this.Index] = Position;
                    }
                }

                _isDragging = false;
                _draggingElement.ReleaseMouseCapture(); // Release the mouse capture
                _draggingElement = null;
            }
        }
    }
}
