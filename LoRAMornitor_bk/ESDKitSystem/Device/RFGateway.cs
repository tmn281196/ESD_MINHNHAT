using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;


namespace ESDKitSystem.Device
{
    public class RFGateway
    {
        public SerialPort SerialPort;


        public event EventHandler SerialReceiverHandler;

        private ViewModel viewModel;

        public RFGateway(ViewModel viewModel)
        {
            this.viewModel = viewModel;

            SerialPort = new SerialPort()
            {
                BaudRate = 9600,
                ReadTimeout = 500,
            };

            SerialReceiverHandler += viewModel.RFGateway_SerialReceiverHandler;

        }


        private void PortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialReceiverHandler?.Invoke(sender, null);
        }

        public void OpenCommunication()
        {
            string portName = viewModel.Config.RFGatewayCOM;
            if (portName != null && portName != "Unset")
            {

                SerialPort.PortName = portName;
                SerialPort.Open();


                if (SerialPort.IsOpen)
                {

                    viewModel.ConnectionStatusRFColor = new SolidColorBrush(Color.FromRgb(117, 247, 17));

                    SerialPort.DataReceived += PortDataReceived;
                }

            }


        }
        public void CloseCommunication()
        {
            SerialPort.Close();


            if (!SerialPort.IsOpen)
            {
                viewModel.ConnectionStatusRFColor  = Brushes.White;
                SerialPort.DataReceived -= PortDataReceived;
            }

        }
  
    }
}
