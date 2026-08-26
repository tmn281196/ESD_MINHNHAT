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
    public class ControlBox
    {

        public SerialPort SerialPort;


        public event EventHandler SerialReceiverHandler;

        private ViewModel viewModel;

        public ControlBox(ViewModel viewModel)
        {
            this.viewModel = viewModel;
            SerialPort = new SerialPort()
            {
                BaudRate = 9600,
                ReadTimeout = 500,
            };

        }

        private void PortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialReceiverHandler?.Invoke(sender, null);
        }

        public void OpenCommunication()
        {
            string portName = viewModel.Config.ControlBoxCOM;
            if (portName != null && portName != "Unset")
            {

                SerialPort.PortName = ((string)portName);
                SerialPort.Open();


                if (SerialPort.IsOpen)
                {
                    viewModel.ConnectionStatusControlBoxColor = new SolidColorBrush(Color.FromRgb(117, 247, 17));
                    SerialPort.DataReceived += PortDataReceived;
                }

            }


        }
        public void CloseCommunication()
        {
            SerialPort.Close();


            if (!SerialPort.IsOpen)
            {
                viewModel.ConnectionStatusControlBoxColor = Brushes.White;
                SerialPort.DataReceived -= PortDataReceived;
            }

        }

        public static byte Prefix1 = 0x44;
        public static byte Prefix2 = 0x45;
        public static byte Suffix = 0x56;
        public List<int> Buffer = new List<int>();


        public static byte CalculateCheckSumXOR(byte[] byteData)
        {
            Byte chkSumByte = 0x00;
            for (int i = 0; i < byteData.Length; i++)
                chkSumByte ^= byteData[i];
            return chkSumByte;
        }


        public static byte CreateChecksum(byte[] bytes)
        {
            return CalculateCheckSumXOR(bytes);
        }

        public void ConsoleWriteLine(string content)
        {
            // Get the current datetime
            DateTime currentTime = DateTime.Now;

            // Format the datetime for the log
            string formattedTime = currentTime.ToString("yyyy-MM-dd HH:mm:ss");

            Console.WriteLine($"[{formattedTime}]: {content}");
        }



        public void SendBytes(byte[] buf)
        {
            try
            {

                SerialPort.DiscardInBuffer();

                SerialPort.Write(buf, 0, buf.Length);

            }
            catch (Exception)
            {
            }
        }

        public bool Send(byte[] data)
        {
            Buffer.Clear();

            if (SerialPort.IsOpen)
            {
                try
                {
                    SendBytes(data);
                    return true;
                }
                catch (System.IO.IOException)
                {
                    return false;
                }
                catch (TimeoutException)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }


        public static byte[] GetFrame(byte[] datas, bool IsNoSize = false)
        {
            if (datas == null) return null;

            List<byte> dataToSend = datas.ToList();
            if (!IsNoSize)
            {
                if (datas.Length > 1)
                {
                    dataToSend.Insert(0, (byte)(dataToSend.Count + 1));
                }
                else
                {
                    dataToSend.Add(0x00);
                }
            }
            dataToSend.Insert(0, Prefix2);
            dataToSend.Insert(0, Prefix1);
            var checksum = CalculateCheckSumXOR(dataToSend.ToArray());
            dataToSend.Add(checksum);
            dataToSend.Add(Suffix);

            foreach (var item in dataToSend)
            {
                Console.Write(item.ToString("X2") + " ");
            }
            Console.WriteLine(" ");
            return dataToSend.ToArray();
        }

        public static bool GetResponse(byte[] datas, byte[] compare)
        {
            if (datas == null) return false;

            List<byte> dataToSend = datas.ToList();
            dataToSend.Insert(0, Prefix2);
            dataToSend.Insert(0, Prefix1);
            var checksum = CalculateCheckSumXOR(dataToSend.ToArray());
            dataToSend.Add(checksum);

            dataToSend.Add(Suffix);

            var result = true;
            var byteData = dataToSend.ToArray();
            for (int i = 0; i < compare.Length; i++)
            {
                if (i < byteData.Length)
                {
                    result = byteData[i] == compare[i];
                }
            }

            return result;
        }

        public ObservableCollection<bool> Channels { get; set; } = new ObservableCollection<bool>()
        {
          false,false,false,false,false
        };


        public bool Send()
        {
            byte[] payload = new byte[5];

            payload[0] = (byte)0x53;

            UInt32 data = 0;
            for (int i = Channels.Count - 1; i >= 0; i--)
            {
                data = data << 1;
                if (Channels[i])
                {
                    data |= 1;
                }
            }
            var bytes = BitConverter.GetBytes(data);
            for (int i = payload.Length - 1; i >= 1; i--)
            {
                payload[i] = bytes[payload.Length - 1 - i];
            }

            return SendTX(payload, 500, new byte[] { 0x53, 0x00 });
        }


        public bool SendTX(byte[] buf, int TimeOut, byte[] checkResponse)
        {
            if (SerialPort == null || !SerialPort.IsOpen)
            {
                return false;
            }

            List<byte> frame = new List<byte>();
            try
            {
                if (SerialPort.IsOpen)
                {

                    var frameToSend = GetFrame(buf);
                    SerialPort.Write(frameToSend, 0, frameToSend.Length);
                    var start = DateTime.Now;
                    while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                    {
                        if (SerialPort.BytesToRead >= 5)
                        {
                            byte startByte = (byte)SerialPort.ReadByte();

                            if (startByte == Prefix1)
                            {
                                var secondByte = (byte)SerialPort.ReadByte();

                                if (secondByte == Prefix2)
                                {
                                    frame.Clear();
                                    frame.Add(startByte);
                                    frame.Add(secondByte);
                                    for (int i = 0; i < 4; i++)
                                    {
                                        frame.Add((byte)SerialPort.ReadByte());
                                    }

                                    var result = GetResponse(checkResponse, frame.ToArray());
                                    return result;
                                }
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
