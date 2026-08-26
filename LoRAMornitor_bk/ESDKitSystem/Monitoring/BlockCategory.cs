using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Text.Json.Serialization;
using System.Windows.Data;

namespace ESDKitSystem.Monitoring
{
    public class BlockCategory : BlockCanvas
    {
        public string Name { get; set; }
        public int Length { get; set; } = 1;
        public int PC { get; set; }
        public bool Active { get; set; } = false;
        public List<BlockDevice> Devices
        {
            get
            {
                return rootCanvas.DynamicDevicesList.Where(item => item.IsActive && item.Position >= this.Position && item.Position < (this.Position + this.Length)).ToList();
            }
        }

        public BlockCategory(RootCanvas rootCanvas, int index, int position, int pc, int length, int x, int y, Brush fillColor, string text) : base(rootCanvas, index, position, x, y, fillColor, text)
        {
            this.Position = position;
            this.Index = index;
            this.Length = length;
            this.PC = pc;
            this.Name = text;

            this.x = x;
            this.y = y;
            this.fillColor = fillColor;

            this.rootCanvas = rootCanvas;
            this.width = (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle) * Length - rootCanvas.SpaceStaticRectangle;
            this.height = 25;

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
                Foreground = Brushes.White,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = width,
                Height = height,

                TextAlignment = TextAlignment.Center
            };
            // Add the rectangle and text to the container
            CanvasContent.Children.Add(NewRectangle);
            CanvasContent.Children.Add(TextBlock);

            // Set the position of the container
            Canvas.SetLeft(CanvasContent, x);
            Canvas.SetTop(CanvasContent, y);

            // Attach mouse events to the container for dragging
            CanvasContent.MouseDown += Element_MouseDown;
            CanvasContent.MouseMove += Element_MouseMove;
            CanvasContent.MouseUp += Element_MouseUp;
        }

        public void IncreaseWidth()
        {
            try
            {
                if (ShiftingCategory(this))
                {
                    this.Length += 1;
                    this.Length = Math.Min(20, this.Length);
                    if (this.Length >= 20)
                    {
                        this.width = (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle) * 20 - rootCanvas.SpaceStaticRectangle;
                    }
                    else
                    {
                        this.width += rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle;
                    }

                    this.CanvasContent.Width = this.width;
                    this.NewRectangle.Width = this.width;
                    this.TextBlock.Width = this.width;

                    this.rootCanvas.ConfigData.CategoryLength[this.Index] = this.Length;
                }
            }
            catch
            {

            }

        }

        private bool ShiftingCategory(BlockCategory categoryStart)
        {
            bool result = true;
            if (!(categoryStart.Position + categoryStart.Length == 20))
            {
                var dynamicCategoryNext = rootCanvas.DynamicCategoriesList.FirstOrDefault(category => category.Position == (categoryStart.Position + categoryStart.Length));

                if (dynamicCategoryNext != null)
                {
                    if (categoryStart.Position + categoryStart.Length == 19)
                    {
                        result = false;
                    }
                    else
                    {
                        if (ShiftingCategory(dynamicCategoryNext))
                        {
                            rootCanvas.DynamicDevicesList.ForEach(item =>
                            {
                                if (item.Position >= dynamicCategoryNext.Position && item.Position < (dynamicCategoryNext.Position + dynamicCategoryNext.Length) && item.IsActive)
                                {
                                    item.Position += 1;
                                    item.Left = (rootCanvas.LeftActiveSlot + rootCanvas.OffsetXStaticRectangle) + item.Position * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                                    Canvas.SetLeft(item.CanvasContent, item.Left);
                                }
                            });

                            dynamicCategoryNext.Position += 1;
                            dynamicCategoryNext.Left = (rootCanvas.LeftActiveSlot + rootCanvas.OffsetXStaticRectangle) + dynamicCategoryNext.Position * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                            Canvas.SetLeft(dynamicCategoryNext.CanvasContent, dynamicCategoryNext.Left);
                        }
                        else
                        {
                            result = false;
                        }
                    }
                }
            }
            else
            {
                result = false;
            }
            return result;
        }

        public void DescreaseWidth()
        {
            try
            {
                this.Length -= 1;
                this.Length = Math.Max(1, this.Length);
                if (this.Length <= 1)
                {
                    this.width = rootCanvas.WidthStaticRectangle;
                }
                else
                {
                    this.width -= rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle;
                }
                this.CanvasContent.Width = this.width;
                this.NewRectangle.Width = this.width;
                this.TextBlock.Width = this.width;

                this.rootCanvas.ConfigData.CategoryLength[this.Index] = this.Length;

            }
            catch
            {

            }
        }
        private void Element_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
            {
                rootCanvas.SelectedDynamicCategory = this;

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

                        Left = (rootCanvas.LeftActiveSlot + rootCanvas.OffsetXStaticRectangle) + i * (rootCanvas.WidthStaticRectangle + rootCanvas.SpaceStaticRectangle);
                        Top = rootCanvas.TopActiveSlot + rootCanvas.OffsetYStaticRectangle - 25;

                        Canvas.SetLeft(_draggingElement, Left);
                        Canvas.SetTop(_draggingElement, Top);

                        IsActive = true;
                        Position = i;

                        this.rootCanvas.ConfigData.CategoryPosition[this.Index] = Position;
                    }

                }

            }




            _isDragging = false;
            _draggingElement.ReleaseMouseCapture(); // Release the mouse capture
            _draggingElement = null;
        }
    }
}

