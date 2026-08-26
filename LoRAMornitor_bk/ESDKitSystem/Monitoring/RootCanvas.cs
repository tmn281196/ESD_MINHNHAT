using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;
using ESDKitSystem.Device;
using System.Windows.Data;
using System.Windows;

namespace ESDKitSystem.Monitoring
{
    public class RootCanvas
    {
        public int WidthSlot = 1650;
        public int HeightSlot = 160;
        public int OffsetYStaticRectangle = 50;
        public int OffsetXStaticRectangle = 10;
        public int WidthStaticRectangle = 70;
        public int HeightStaticRectangle = 70;
        public int SpaceStaticRectangle = 15;

        public Rectangle ActiveSlot;
        public int TopActiveSlot = 50;
        public int LeftActiveSlot = 10;

        public Rectangle InactiveSlot;
        public int TopInactiveSlot = 300;
        public int LeftInactiveSlot = 10;

        public Canvas Canvas;

        public BlockCategory SelectedDynamicCategory;
        public List<BlockDevice> DynamicDevicesList = new List<BlockDevice>();
        public List<BlockCategory> DynamicCategoriesList = new List<BlockCategory>();

        //private List<String> categoriesName = new List<string>() { "MI", "Solder",  "FCT" };
        private List<String> categoriesName = new List<string>() { "MI", "Solder", "Packing", "FCT" };

        //private List<string> categoriesName = new List<string>() { "FCT" };
        public ConfigMonitoring ConfigData { get; set; }
        private static readonly string filePath = "config.json";

        public void SaveConfig()
        {
            string json = JsonConvert.SerializeObject(ConfigData, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public ConfigMonitoring LoadConfig()
        {
            ConfigMonitoring config = new ConfigMonitoring();

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);

                config = JsonConvert.DeserializeObject<ConfigMonitoring>(json);
            }
            else
            {
                config.CategoryLength = new List<int>() { 8, 1, 1 ,1};
                config.CategoriePC = new List<int>() { 1, 1, 1 ,1};
                config.CategoryPosition = new List<int>() { 0, 8, 9, 10 };
                config.DevicePosition = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 9, 10, 11, 12, 13, 14, 15, 16 };
                config.DevicesIsActive = new List<bool>() { true, true, true, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false, false, false };
            }
            return config;
        }

        public RootCanvas(Canvas canvas)
        {
            ConfigData = LoadConfig();

            Canvas = canvas;

            WidthSlot = LeftActiveSlot + 21 * (WidthStaticRectangle + SpaceStaticRectangle);

            ActiveSlot = new Rectangle
            {
                Width = WidthSlot,
                Height = HeightSlot,
                Fill = Brushes.LightBlue
            };

            // Set the position of the new rectangle
            Canvas.SetLeft(ActiveSlot, LeftActiveSlot);
            Canvas.SetTop(ActiveSlot, TopActiveSlot);

            Canvas.Children.Add(ActiveSlot);

            for (int i = 0; i < 21; i++) // Initialize 'i' to 0
            {
                AddStaticRectangleItem(LeftActiveSlot + OffsetXStaticRectangle + i * (WidthStaticRectangle + SpaceStaticRectangle), TopActiveSlot + OffsetYStaticRectangle);
            }

            InactiveSlot = new Rectangle
            {
                Width = WidthSlot,
                Height = HeightSlot,
                Fill = Brushes.LightPink
            };

            // Set the position of the new rectangle
            Canvas.SetLeft(InactiveSlot, LeftInactiveSlot);
            Canvas.SetTop(InactiveSlot, TopInactiveSlot);
            Canvas.Children.Add(InactiveSlot);

            for (int i = 0; i < 21; i++) // Initialize 'i' to 0
            {
                AddStaticRectangleItem(LeftInactiveSlot + OffsetXStaticRectangle + i * (WidthStaticRectangle + SpaceStaticRectangle), TopInactiveSlot + OffsetYStaticRectangle);
            }
            for (int i = 0; i < 20; i++)
            {
                AddDynamicDevicesItem(i);
            }
            for (int i = 0; i < categoriesName.Count; i++)
            {
                AddDynamicCategoryItem(i);
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
                Fill = backgroundColor,   // Background color (e.g., Brushes.Blue)
                Stroke = stroke,       // Border (stroke) color
                StrokeThickness = 1,          // Border (stroke) thickness
                StrokeDashArray = new DoubleCollection { 2, 2 } // Dashed stroke pattern
            };
            // Set the position of the container
            Canvas.SetLeft(newRectangle, left);
            Canvas.SetTop(newRectangle, top);
            // Add the container to the MainCanvas
            Canvas.Children.Add(newRectangle);
        }
        private void AddDynamicDevicesItem(int index)
        {
            bool isActive = ConfigData.DevicesIsActive[index];
            int position = ConfigData.DevicePosition[index];
            int left = LeftActiveSlot + OffsetXStaticRectangle + ConfigData.DevicePosition[index] * (WidthStaticRectangle + SpaceStaticRectangle);
            int top = TopInactiveSlot + OffsetYStaticRectangle;
            if (isActive)
            {
                top = TopActiveSlot + OffsetYStaticRectangle;
            }
            BlockDevice dynamicRectangle = new BlockDevice(this, index, position, isActive, left, top, (Brush)new BrushConverter().ConvertFromString("#037ef3"), $"E{(index + 1).ToString("D2")}");
            DynamicDevicesList.Add(dynamicRectangle);
            Canvas.Children.Add(dynamicRectangle.CanvasContent);
        }
        private void AddDynamicCategoryItem(int index)
        {
            int position = ConfigData.CategoryPosition[index];
            int pc = ConfigData.CategoriePC[index];
            int length = ConfigData.CategoryLength[index];
            int left = LeftActiveSlot + OffsetXStaticRectangle + ConfigData.CategoryPosition[index] * (WidthStaticRectangle + SpaceStaticRectangle);
            int top = TopActiveSlot + OffsetYStaticRectangle - 25;
            BlockCategory dynamicRectangle = new BlockCategory(this, index, position, pc, length, left, top, Brushes.BlueViolet, categoriesName[index]);
            DynamicCategoriesList.Add(dynamicRectangle);
            Canvas.Children.Add(dynamicRectangle.CanvasContent);
        }
    }
}
