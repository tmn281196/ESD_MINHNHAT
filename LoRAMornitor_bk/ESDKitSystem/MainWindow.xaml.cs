using System.Diagnostics;
using System.Text;
using System;
using System.Windows;

using System.Threading.Tasks;
using System.Net;
using System.Windows.Documents;
using System.Collections.Generic;
using System.Windows.Shapes;
using System.Diagnostics.Tracing;
using System.Windows.Controls;

using ESDKitSystem.Monitoring;
using ESDKitSystem.Device;

using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;


namespace ESDKitSystem
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string LastPageSelected = "";

        public DeviceManagerPage DeviceManagerPage;

        public MonitorPage MonitorPage;


        public ViewModel GlobalViewModel;


        public MainWindow()
        {
            InitializeComponent();
            this.Closed += WindowClosed;

            GlobalViewModel = new ViewModel();

            DeviceManagerPage = new DeviceManagerPage(GlobalViewModel);
            MonitorPage = new MonitorPage(GlobalViewModel);


            MonitorPageHolder.Content = MonitorPage;
            DeviceManagerPageHolder.Content = DeviceManagerPage;

            DeviceManagerPageHolder.Visibility = Visibility.Hidden;
            MonitorPageHolder.Visibility = Visibility.Visible;

            this.DataContext = GlobalViewModel;

        }



        private void WindowClosed(object sender, EventArgs e)
        {
            DeviceManagerPage.ViewModel.RootCanvas.SaveConfig();


            DeviceManagerPage.SaveConfig();
            DeviceManagerPage.Dispose();
        }

        private void DeviceManagerPageHolder_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            Console.WriteLine("DeviceManagerPageHolder_Navigated");
        }

        private void MonitorPageHolder_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            Console.WriteLine("MonitorPageHolder_Navigated");
        }


        private void btSelectPage_Click(object sender, RoutedEventArgs e)
        {
            var bt = (sender as ToggleButton);
            btDeviceManagerPage.IsChecked = bt == btDeviceManagerPage;
            btLinePage.IsChecked = bt == btLinePage;

            if (bt.Name == LastPageSelected)
            {
                bt.IsChecked = true;
                return;
            }
            DeviceManagerPageHolder.Visibility = Visibility.Hidden;
            MonitorPageHolder.Visibility = Visibility.Hidden;

            LastPageSelected = bt.Name;

            switch (bt.Name)
            {
                case "btDeviceManagerPage":
                    DeviceManagerPageHolder.Visibility = Visibility.Visible;
                    break;

                case "btLinePage":
                    MonitorPageHolder.Visibility = Visibility.Visible;
                    break;

                default:
                    break;
            }
        }

    }
}