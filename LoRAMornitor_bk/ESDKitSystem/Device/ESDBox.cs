
using ESDKitSystem.Monitoring;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;


namespace ESDKitSystem.Device
{
    public enum DeviceStatus
    {
        Unknown,  // Default value
        Active,
        Inactive,
        Warning,
        Error

    }
    public class ESDBox : INotifyPropertyChanged
    {
        public int StateChangeCounter = 0;
        public int ErrorCounter = 0;

        public event PropertyChangedEventHandler PropertyChanged;


        public int ID = -1;
        public Action<ESDBox> StatusChangeAction { get; set; }     

        public Brush ColorState { get; set; } = (Brush)new BrushConverter().ConvertFromString("#037ef3");

        private static readonly Brush BrushUnknown = (Brush)new BrushConverter().ConvertFromString("#037ef3");
        private static readonly Brush BrushActive = (Brush)new BrushConverter().ConvertFromString("#45f248");

        private static readonly Brush BrushWarning = Brushes.OrangeRed;
        private static readonly Brush BrushInactive = Brushes.Yellow;
        private static readonly Brush BrushError = Brushes.LightGray;

        static ESDBox()
        {
            if (BrushUnknown.CanFreeze) BrushUnknown.Freeze();
            if (BrushActive.CanFreeze) BrushActive.Freeze();
        }

        public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
        private void OnStatusChanged()
        {
            StateChangeCounter++;

            var brush = BrushUnknown;

            switch (Status)
            {
                case DeviceStatus.Unknown:
                    brush = BrushUnknown;
                    break;
                case DeviceStatus.Active:
                    brush = BrushActive;
                    break;
                case DeviceStatus.Warning:
                    brush = BrushWarning;
                    break;
                case DeviceStatus.Inactive:
                    brush = BrushInactive;
                    break;
                case DeviceStatus.Error:
                    ErrorCounter++;
                    return;
                default:
                    break;
            }

            ColorState = brush;

            try
            {
                StatusChangeAction.Invoke(this);

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }


        public ESDBox(int id)
        {
            ID = id;
        }




        public async Task ReceiveState(DeviceStatus deviceStatus)
        {
           
        }
      
    }
}
