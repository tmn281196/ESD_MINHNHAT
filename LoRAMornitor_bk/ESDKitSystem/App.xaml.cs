using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;

namespace ESDKitSystem
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            List<String> macAddrList = nics.Select(x => x.GetPhysicalAddress().ToString()).ToList();

            string[] whiteListMacAddress = { "00D861E3E550", "D4F32D1F99D9", "D8BBC1B21D13", "D8BBC1B21D13" ,"9C6B006D4718", "D843AEC0F3BD" };

            bool exists = macAddrList.Any(x => whiteListMacAddress.Contains(x));

            //if (!exists)
            //{
            //    MessageBox.Show("This Program Cannot be Copied!!!", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
            //    App.Current.Shutdown();
            //}
        }
    }
}