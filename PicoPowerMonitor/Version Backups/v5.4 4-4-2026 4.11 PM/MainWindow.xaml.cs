// Version 5.4
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
// Used to detect RP2040 disconnects and attempt to reconnect via WMI
// Requires System.Management NuGet package
using System.Management;
// Going to need the use of Regular Expressions to parse the serial data.
using System.Text.RegularExpressions;
using System.Diagnostics;


namespace PicoPowerMonitor
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // field declarations
        private SerialPort? _picoPort;
        private string? _targetPort;

        // Reconnect timer to handle unexpected disconnections
        private DispatcherTimer _reconnectTimer;

        // Updated Regex for "V: 0.000, I: 0.000, P: 0.000"
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.]+),\s*I:\s*([\d.]+),\s*P:\s*([\d.]+)");
        
        public MainWindow()
        {

            this.InitializeComponent();
            // debug            Debug.WriteLine("MainWindow initialized.");
            Debug.WriteLine("MainWindow initialized.");


            // Port scan
            Debug.WriteLine("Main Calls LoadPorts...");
            LoadPorts();
            Debug.WriteLine("LoadPorts returned to Main...");

            // Find the Pico's COM port automatically
            Debug.WriteLine("Main Calls AutoDetectPico...");
            // 4/4/2026 changed so that it stores the return value in a variable called detected.
            // I'll pass detected to AutoConnect so it auto connect.
            // AutoDetectPico();
            var detected = AutoDetectPico();
            Debug.WriteLine("AutoDetectPico returned to Main...");
            Debug.WriteLine($"AutoDetectPico returned value: {detected}");


            // Setup a timer to check connection every 2 seconds
            Debug.WriteLine("Main setting up new DispatchTimer");
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(2);
            _reconnectTimer.Tick += ReconnectTimer_Tick;
            Debug.WriteLine("Main done setting up new DispatchTimer");

            Debug.WriteLine($"AutoConnect function called, passing it the value: {detected}");
            // 4/4/2026 changed, now passing AutoConnect the detected variable as an argument.
            // AutoConnect();
            AutoConnect(detected);
            Debug.WriteLine("AutoConnect returned to Main.");

            this.Closed += (s, e) => {
                _reconnectTimer.Stop();
                ClosePort();
            };
        }

        private void LoadPorts()
        {
            // debug
            Debug.WriteLine("LoadPorts called...");
            string[] ports = SerialPort.GetPortNames();
            // debug
            Debug.WriteLine("Available COM ports: " + string.Join(", ", ports));
            Debug.WriteLine($"LoadPorts ports.Length is: {ports.Length}");
            if (PortSelector != null)
            {
                PortSelector.ItemsSource = ports;
                if (ports.Length > 0) PortSelector.SelectedIndex = 0;
            }
        }

        // Auto-detect the Pico's COM port by looking for its unique VID/PID in the system's PnP devices
        private string? AutoDetectPico()
        {
            // debug
            Debug.WriteLine("Attempting to auto-detect Pico...");
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                {
                    var ports = searcher.Get();
                    foreach (var port in ports)
                    {
                        string? hardwareId = port["PNPDeviceID"]?.ToString();
                        string? caption = port["Caption"]?.ToString();
                        // debug
                            Debug.WriteLine($"Found device: {caption} with Hardware ID: {hardwareId}");

                        if (!string.IsNullOrEmpty(hardwareId) &&
                            hardwareId.Contains("VID_2E8A") &&
                            hardwareId.Contains("PID_0005") &&
                            !string.IsNullOrEmpty(caption))
                        {
                            // debug
                            Debug.WriteLine("AutoDetectPico If Statement True");
                            var m = Regex.Match(caption, @"\((COM\d+)\)");
                            Debug.WriteLine($"m variable contains: {m}");
                            Debug.WriteLine($"Pico detected: {caption}, extracting port...");
                            // if (m.Success) return m.Groups[1].Value;
                            // Set selected to string from m.Groups[1].Value.  This string just contains COMx, no parenthisis.
                            if (m.Success)
                            {
                                var selected = m.Groups[1].Value;
                                Debug.WriteLine($"selected variable contains: {selected}");
                                Debug.WriteLine($"m.Group[1].Value is: {m.Groups[1].Value}");
                                return m.Groups[1].Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // debug
                Debug.WriteLine("Auto-detection error:" + ex.Message);
                StatusText?.DispatcherQueue?.TryEnqueue(() => StatusText.Text = "Discovery Error: " + ex.Message);
            }
            return null;
        }

        // 4/4/2026 changed so that AutoConnect takes the detected COM port as an argument.
        // private void AutoConnect()
        private void AutoConnect(string? selected)
        {
            // debug
            Debug.WriteLine("AutoConnect Function called...");
            // var selected = PortSelector?.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selected))
            {
                // debug
                Debug.WriteLine("AutoConnect if string evaluated true");
                Debug.WriteLine($"Pico just detected on {_targetPort}. Selected: {selected}");
                _targetPort = selected;
                Debug.WriteLine($"Pico just detected on {_targetPort}. Selected: {selected}");
                _reconnectTimer.Start();
                Debug.WriteLine("_reconnectTimer Started");
                Debug.WriteLine("Calling TryConnect()");
                TryConnect();
            }
            else
            {
                // Didn't find a Pico on any COM port.  Start the reconnect timer and keep trying.
                Debug.WriteLine("AutoConnect if string evaluated false");
                _reconnectTimer.Start();
                Debug.WriteLine("_reconnectTimer Started");
            }
        }

        private void TryConnect()
        {
            try
            {
                Debug.WriteLine("TryConnect() started");
                if (string.IsNullOrEmpty(_targetPort)) return;
                Debug.WriteLine("Calling ClosePort()");
                ClosePort();
                Debug.WriteLine("Returned from ClosePort()");
                _picoPort = new SerialPort(_targetPort, 115200);
                _picoPort.DtrEnable = true; // Required for Pico USB
                _picoPort.RtsEnable = true;
                _picoPort.DataReceived += SerialPort_DataReceived;
                Debug.WriteLine("Opening _picoPort Communications");
                _picoPort.Open();

                if (StatusText != null)
                {
                    Debug.WriteLine($"Changing to Connected status on {_targetPort}");
                    StatusText.Text = $"Connected to {_targetPort}";
                    StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 0));
                    Debug.WriteLine("Exiting TryConnect()");
                }
            }
            catch
            {
                /* Timer will retry if device is busy or missing */
                Debug.WriteLine("Hit TryConnect() catch");
            }
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e) => LoadPorts();

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // debug
            Debug.WriteLine("ConnectButton_Click called...");
            var selected = PortSelector?.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selected))
            {
                Debug.WriteLine("ConnectButton_Click if selected is NOT Null or Empty evaluated True");
                _targetPort = selected;
                Debug.WriteLine($"ConnectButton_Click {_targetPort}. Selected: {selected}");
                Debug.WriteLine("ConnectButton_Click Starting _reconnectTimer");
                _reconnectTimer.Start();
                Debug.WriteLine("ConnectButton_Click calling TryConnect()");
                TryConnect();
            }
        }

        private void ReconnectTimer_Tick(object? sender, object e)
        {
            // debug
            Debug.WriteLine("ReconnectTimer_Tick called...");
            if (_picoPort == null || !_picoPort.IsOpen)
            {
                string? discoveredPort = AutoDetectPico();

                if (!string.IsNullOrEmpty(discoveredPort))
                {
                    _targetPort = discoveredPort;
                    if (StatusText != null) StatusText.Text = $"Pico found on {_targetPort}. Connecting...";
                    // debug
                    Debug.WriteLine($"Pico found on {_targetPort}. Attempting to connect...");
                    Debug.WriteLine("ReconnectTimer_Tick calling TryConnect()");
                    TryConnect();
                }
                else
                {
                    if (StatusText != null)
                    {
                        // debug
                        Debug.WriteLine("ReconnectTimer_Tick Setting StatusText to (Plug it in now)");
                        StatusText.Text = "Searching for Pico... (Plug it in now)";
                        StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0));
                    }
                }
            }
        }


        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var port = _picoPort;
                if (port == null) return;

                string line = port.ReadLine();
                Match match = PicoRegex.Match(line);

                if (match.Success)
                {
                    string v = match.Groups[1].Value;
                    string i = match.Groups[2].Value;
                    string p = match.Groups[3].Value;

                    DispatcherQueue.TryEnqueue(() => {
                        if (VoltageText != null) VoltageText.Text = $"V: {v}";
                        if (CurrentText != null) CurrentText.Text = $"I: {i}";
                        if (PowerText != null) PowerText.Text = $"P: {p}";
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