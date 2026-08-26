using ESDKitSystem.Monitoring;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ESDKitSystem.Device
{
    public class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public List<string> SerialPorts { get; set; } = new List<string>();


        public RootCanvas RootCanvas;
        private const int DEVNUM = 20;
        public Config Config { get; set; }

        public DeviceManagerPage DeviceManagerPage;

        public string StringLog { get; set; } = string.Empty;
        public ObservableCollection<ESDBox> ESDBoxes { get; set; } = new ObservableCollection<ESDBox>();
        public SolidColorBrush ConnectionStatusControlBoxColor { get; set; } = Brushes.White;
        public SolidColorBrush ConnectionStatusRFColor { get; set; } = Brushes.White;

        /// <summary>
        /// /////////////////////////////////////////////////////////////////////////////////////////
        /// </summary>
        // ---- Constants ----
        private const byte STX = 0xAA;
        private const byte ETX = 0xBB;
        private const byte NETWORK_ID = 0x01;

        // ---- Frame parser state ----
        private enum ParseState { WaitSTX, WaitNetworkID, WaitTxID, WaitLen, WaitData, WaitCRC, WaitETX }
        private ParseState _parseState = ParseState.WaitSTX;
        private byte _networkID;
        private byte _txID;
        private byte _dataLen;
        private byte[] _dataBuf = new byte[255];
        private byte _dataIdx;
        private byte _crcRx;

        // ---- CRC8 ----
        private byte Crc8(byte[] data, int len)
        {
            byte crc = 0x00;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                    crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x31) : (byte)(crc << 1);
            }
            return crc;
        }

        // ---- Feed từng byte vào state machine ----
        private void FeedByte(byte b)
        {
            switch (_parseState)
            {
                case ParseState.WaitSTX:
                    if (b == STX) _parseState = ParseState.WaitNetworkID;
                    break;

                case ParseState.WaitNetworkID:
                    _networkID = b;
                    _parseState = b == NETWORK_ID ? ParseState.WaitTxID : ParseState.WaitSTX;
                    break;

                case ParseState.WaitTxID:
                    _txID = b;
                    _parseState = ParseState.WaitLen;
                    break;

                case ParseState.WaitLen:
                    _dataLen = b;
                    _dataIdx = 0;
                    _parseState = _dataLen > 0 ? ParseState.WaitData : ParseState.WaitCRC;
                    break;

                case ParseState.WaitData:
                    _dataBuf[_dataIdx++] = b;
                    if (_dataIdx >= _dataLen) _parseState = ParseState.WaitCRC;
                    break;

                case ParseState.WaitCRC:
                    _crcRx = b;
                    _parseState = ParseState.WaitETX;
                    break;

                case ParseState.WaitETX:
                    _parseState = ParseState.WaitSTX;  // reset dù đúng hay sai
                    if (b != ETX) break;

                    // Verify CRC: NetworkID + TxID + LEN + DATA
                    byte[] crcBuf = new byte[_dataLen + 3];
                    crcBuf[0] = _networkID;
                    crcBuf[1] = _txID;
                    crcBuf[2] = _dataLen;
                    Array.Copy(_dataBuf, 0, crcBuf, 3, _dataLen);

                    if (_txID < 1 || _txID > ESDBoxes.Count)
                    {
                        Debug.WriteLine($"Invalid TxID: {_txID} (expected 1..{ESDBoxes.Count})");
                        break;
                    }
                    ESDBox esdBox = ESDBoxes[_txID - 1];

                    Application.Current.Dispatcher.Invoke(() =>
                    {

                        if (Crc8(crcBuf, crcBuf.Length) != _crcRx)
                        {
                            // Frame ko hợp lệ 
                            esdBox.Status = DeviceStatus.Error;
                        }
                        else
                        {
                            // Frame hợp lệ
                            switch ((char)_dataBuf[0])
                            {
                                case 'A':
                                    esdBox.Status = DeviceStatus.Active;
                                    break;
                                case 'W':
                                    esdBox.Status = DeviceStatus.Warning;
                                    break;
                                case 'I':
                                    esdBox.Status = DeviceStatus.Inactive;
                                    break;
                                default:
                                    esdBox.Status = DeviceStatus.Unknown;
                                    break;
                            }
                        }
                    });
                    break;
            }
        }


        // ---- Handler ----
        public void RFGateway_SerialReceiverHandler(object sender, EventArgs e)
        {
            var port = sender as SerialPort;
            if (port == null) return;

            int bytesToRead = port.BytesToRead;
            for (int i = 0; i < bytesToRead; i++)
            {
                byte b = (byte)port.ReadByte();
                Debug.WriteLine($"RX: 0x{b:X2}  state={_parseState}");  // xem log


                FeedByte((byte)b);
            }
        }

        /// /////////////////////////////////////////////////////////////////////////////////////////

        public async Task UpdateControlBox()
        {
            await Task.Run(() =>
            {
                var allDevices = RootCanvas.DynamicCategoriesList.Where(item => item.Devices.Count > 0).SelectMany(category => category.Devices).ToList();
                if (allDevices.Count == 0) return;

                bool warning = allDevices.Any(item => item.Status == DeviceStatus.Warning);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    for (int i = 0; i <= 3; i++)
                    {
                        DeviceManagerPage.ControlBox.Channels[i] = warning;
                    }
                });

                DeviceManagerPage.ControlBox.Send();
                Console.WriteLine(warning ? "Conveyor Stopped" : "Conveyor Moving");
            });
        }

        public async void StatusChangeActionFunc(ESDBox esdBox)
        {
            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RootCanvas.DynamicDevicesList[esdBox.ID - 1].NewRectangle.Fill = esdBox.ColorState;
                    RootCanvas.DynamicDevicesList[esdBox.ID - 1].Status = esdBox.Status;
                    RootCanvas.DynamicDevicesList[esdBox.ID - 1].DeviceNote.Text = $"{esdBox.ErrorCounter}/{esdBox.StateChangeCounter}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }));

            await UpdateControlBox();
        }



        public ViewModel()
        {
            for (int i = 0; i < DEVNUM; i++)
            {
                ESDBox esbBox = new ESDBox(i + 1);
                ESDBoxes.Add(esbBox);

                esbBox.StatusChangeAction = StatusChangeActionFunc;


            }
        }
        public void OpenNetwork()
        {
            StringLog = "Ready";

            ResetStates();

        }
        public async Task CloseNetWork()
        {
            StringLog = "Stopped";

            ResetStates();
        }


        public void ResetStates()
        {
            foreach (var esdBox in ESDBoxes)
            {
                esdBox.Status = DeviceStatus.Unknown;
            }
        }




    }
}
