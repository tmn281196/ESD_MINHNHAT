using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ESDKitSystem.Monitoring
{
    public class BlockCanvas
    {
        public Canvas CanvasContent;
        public bool IsActive { get; set; }
        public int Position { get; set; } = 0;
        public int Index { get; set; } = 0;

        protected int x , y, width, height;
        protected RootCanvas rootCanvas;
        public Rectangle NewRectangle;
        public TextBlock TextBlock;

        protected Brush fillColor;

        protected bool _isDragging = false;
        protected UIElement _draggingElement;
        protected Point _startPoint;

        public double Left = 0;
        public double Top = 0;

        public BlockCanvas(RootCanvas rootCanvas, int index , int position, int x,int y, Brush fillColor, string text)
        {          
        }

    }
}
