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
        // Regex to extract numbers from "Voltage: 0.000 V, Current: 0.001 A, Power: 0.000 W"
        private static readonly Regex PicoRegex = new Regex(@"Voltage:\s*([\d.]+)\s*V,\s*Current:\s*([\d.]+)\s*A,\s*Power:\s*([\d.]+)\s*W");

        public MainWindow()
        {
            this.InitializeComponent();
            // Ensure the port is closed when the app exits
            this.Closed += (s, e) => _picoPort?.Dispose();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Adjust "COM7" to match your Pico's actual port
                _picoPort = new SerialPort("COM7", 115200);

                // CRITICAL for Pico USB CDC
                _picoPort.DtrEnable = true;
                _picoPort.RtsEnable = true;

                _picoPort.DataReceived += SerialPort_DataReceived;
                _picoPort.Open();

                StatusText.Text = "Connected";
                StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 0));
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // Read the line sent by Pico's print() statement
                string line = _picoPort.ReadLine();
                Match match = PicoRegex.Match(line);

                if (match.Success)
                {
                    string v = match.Groups[1].Value;
                    string i = match.Groups[2].Value;
                    string p = match.Groups[3].Value;

                    // Update UI on the main thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        VoltageText.Text = $"V: {v} V";
                        CurrentText.Text = $"I: {i} A";
                        PowerText.Text = $"P: {p} W";
                    });
                }
            }
            catch { /* Ignore malformed packets during streaming */ }
        }
    }
}
