using RemoteCapture.Lib.WindowsRuntimeHelpers;
using System.Configuration;
using System.Data;
using System.Windows;
using Windows.System;

namespace RemoteCapture
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            _controller = CoreMessagingHelper.CreateDispatcherQueueControllerForCurrentThread();
        }

        private DispatcherQueueController _controller;
    }

}
