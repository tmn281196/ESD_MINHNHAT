using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ESDKitSystem.Device;


namespace ESDKitSystem.Monitoring
{
    /// <summary>
    /// Interaction logic for LineMonitor.xaml
    /// </summary>
    public partial class MonitorPage : Page
    {
        public RootCanvas RootCanvas;
        public ViewModel ViewModel { get; set; }

        public MonitorPage(ViewModel viewModel)
        {
            InitializeComponent();

            this.ViewModel = viewModel;
            RootCanvas = new RootCanvas(MainCanvas);
            ViewModel.RootCanvas = RootCanvas;
        }

        private void ButtonIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (RootCanvas.SelectedDynamicCategory != null)
                RootCanvas.SelectedDynamicCategory.IncreaseWidth();
        }
        private void ButtonDescrease_Click(object sender, RoutedEventArgs e)
        {
            if (RootCanvas.SelectedDynamicCategory != null)
                RootCanvas.SelectedDynamicCategory.DescreaseWidth();
        }
    }
}
