using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO.Ports;
using System.IO;
using Newtonsoft.Json;


namespace ESDKitSystem.Device
{
    public partial class DeviceManagerPage : Page
    {
        private const int baurate = 9600;
        private static readonly string filePath = "COM.json";

        public ViewModel ViewModel;

        public ControlBox ControlBox;
        public RFGateway RFGateway;


        public void SaveConfig()
        {

            string json = JsonConvert.SerializeObject(ViewModel.Config, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public Config LoadConfig()
        {
            Config config = new Config();
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                config = JsonConvert.DeserializeObject<Config>(json);
            }
            return config;
        }
        private void RefreshComPorts()
        {
            ViewModel.SerialPorts = SerialPort.GetPortNames()
                .Concat(new[]
                {
            ViewModel.Config.RFGatewayCOM,
            ViewModel.Config.ControlBoxCOM,
            "Unused"
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }

        public DeviceManagerPage(ViewModel viewModel)
        {
            InitializeComponent();
            this.ViewModel = viewModel;
            ViewModel.Config = LoadConfig();
            ViewModel.DeviceManagerPage = this;
            this.DataContext = ViewModel;

            RefreshComPorts();
            ControlBox = new ControlBox(ViewModel);
            RFGateway = new RFGateway(ViewModel);



            OpenRFConnection();

            // Initialize button states
            buttonStates[ToggleableButton0] = false;
            buttonStates[ToggleableButton1] = false;
            buttonStates[ToggleableButton2] = false;
            buttonStates[ToggleableButton3] = false;
            buttonStates[ToggleableButton4] = false;

            _ = OpenControlBoxConnection();



        }



        public async void Dispose()
        {
            await ViewModel.CloseNetWork();
        }
        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
        }
        private async Task OpenControlBoxConnection()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    ControlBox.OpenCommunication();
                });


                // SW ON DOWN EDGE
                ControlBox.Channels[4] = true;
                ControlBox.Send();

                await Task.Delay(1000);

                ControlBox.Channels[4] = false;
                ControlBox.Send();
            }
            catch
            {

            }
        }
        private void CloseControlBoxConnection()
        {
            Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        ControlBox.CloseCommunication();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    });
                }
            });
        }
        private async void ReConnectControlBoxConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            CloseControlBoxConnection();
            await Task.Delay(1000);
            await OpenControlBoxConnection();
        }
        private void CloseControlBoxConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            CloseControlBoxConnection();
        }


        private void OpenRFConnection()
        {
            Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        RFGateway.OpenCommunication();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    });
                }
            });
        }

        private void CloseRFConnection()
        {
            Task.Run(async () =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        RFGateway.CloseCommunication();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    });
                }
            });
        }

        private async void ReConnectRFConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRFConnection();
            await Task.Delay(1000);
            OpenRFConnection();
        }

        private async void CloseRFConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRFConnection();
        }

        private Dictionary<Button, bool> buttonStates = new Dictionary<Button, bool>();

        private void SendControl_Click(object sender, RoutedEventArgs e)
        {
            if (ControlBox.SerialPort.IsOpen)
            {
                try
                {
                    Button clickedButton = sender as Button;

                    if (clickedButton == null) return;
                    // Cast sender to Button

                    // Toggle the state for the clicked button
                    buttonStates[clickedButton] = !buttonStates[clickedButton];

                    // Change the button's background color based on its state
                    if (buttonStates[clickedButton])
                    {
                        clickedButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45f248")); // Green
                    }
                    else
                    {
                        clickedButton.Background = new SolidColorBrush(Colors.Gray); // Red
                    }

                    // Example usage with ConveyorIO (adjust channels as needed)
                    int channelIndex = GetChannelIndex(clickedButton);
                    if (channelIndex >= 0)
                    {
                        ControlBox.Channels[channelIndex] = buttonStates[clickedButton];
                        Console.WriteLine(channelIndex);
                        ControlBox.Send();
                    }
                }
                catch
                {

                }
            }
        }

        // Helper method to determine the channel index based on the button
        private int GetChannelIndex(Button button)
        {
            if (button == ToggleableButton0) return 0;
            if (button == ToggleableButton1) return 1;
            if (button == ToggleableButton2) return 2;
            if (button == ToggleableButton3) return 3;
            if (button == ToggleableButton4) return 4;

            return -1; // Invalid button
        }


    }
}
