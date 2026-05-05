using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
// going to need System.IO.Ports for the serial port stuff
using System.IO.Ports;

// Going to need the use of Regular Expressions to parse the serial data.
using System.Text.RegularExpressions;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PicoPowerMonitor
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private SerialPort _picoPort;
        // Reconnect timer to handle unexpected disconnections
        private DispatcherTimer _reconnectTimer;

        // Sets COM port
        private string _targetPort;

        // Updated Regex for "V: 0.000, I: 0.000, P: 0.000"
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.]+),\s*I:\s*([\d.]+),\s*P:\s*([\d.]+)");
        public MainWindow()
        {

            this.InitializeComponent();
            // Port scan
            LoadPorts();

            // Setup a timer to check connection every 2 seconds
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(2);
            _reconnectTimer.Tick += ReconnectTimer_Tick;

            this.Closed += (s, e) => {
                _reconnectTimer.Stop();
                ClosePort();
            };
        }

        private void LoadPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            PortSelector.ItemsSource = ports;
            if (ports.Length > 0) PortSelector.SelectedIndex = 0;
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e) => LoadPorts();

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (PortSelector.SelectedItem != null)
            {
                _targetPort = PortSelector.SelectedItem.ToString();
                _reconnectTimer.Start();
                TryConnect();
            }
        }

        private void ReconnectTimer_Tick(object? sender, object e)
        {
            // If port is null or closed, attempt a reconnect
            if (_picoPort == null || !_picoPort.IsOpen)
            {
                StatusText.Text = "Reconnecting...";
                StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0)); // Orange
                TryConnect();
            }
        }

        private void TryConnect()
        {
            try
            {
                ClosePort();
                _picoPort = new SerialPort(_targetPort, 115200);
                _picoPort.DtrEnable = true; // Required for Pico USB
                _picoPort.RtsEnable = true;
                _picoPort.DataReceived += SerialPort_DataReceived;
                _picoPort.Open();

                StatusText.Text = $"Connected to {_targetPort}";
                StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 0));
            }
            catch { /* Timer will retry if device is busy or missing */ }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string line = _picoPort.ReadLine();
                Match match = PicoRegex.Match(line);

                if (match.Success)
                {
                    string v = match.Groups[1].Value;
                    string i = match.Groups[2].Value;
                    string p = match.Groups[3].Value;

                    DispatcherQueue.TryEnqueue(() => {
                        VoltageText.Text = $"V: {v} V";
                        CurrentText.Text = $"I: {i} A";
                        PowerText.Text = $"P: {p} W";
                    });
                }
            }
            catch (Exception)
            {
                // On error (like a physical disconnect), close the port to trigger the reconnect timer
                ClosePort();
            }
        }

        private void ClosePort()
        {
            if (_picoPort != null)
            {
                _picoPort.DataReceived -= SerialPort_DataReceived;
                if (_picoPort.IsOpen) _picoPort.Close();
                _picoPort.Dispose();
                _picoPort = null;
            }
        }
    }
}