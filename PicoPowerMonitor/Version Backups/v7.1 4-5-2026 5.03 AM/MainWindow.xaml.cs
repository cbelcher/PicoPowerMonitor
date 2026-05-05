// Version 7.1
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
// going to need System.IO.Ports for the serial port stuff
using System.IO.Ports;
using System.Linq;
// Used to detect RP2040 disconnects and attempt to reconnect via WMI
// Requires System.Management NuGet package
using System.Management;
using System.Runtime.InteropServices.WindowsRuntime;
// Going to need the use of Regular Expressions to parse the serial data.
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinRT.Interop;


namespace PicoPowerMonitor
{
    public sealed partial class MainWindow : Window
    {
        // field declarations
        private SerialPort? _picoPort;
        private string? _targetPort;

        // Reconnect timer to handle unexpected disconnections
        private readonly DispatcherTimer _reconnectTimer;

        // Updated Regex for "V: 0.000, I: 0.000, P: 0.000"
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.]+),\s*I:\s*([\d.]+),\s*P:\s*([\d.]+)");
        
        public MainWindow()
        {

            this.InitializeComponent();

            // in constructor, after InitializeComponent()
            if (Content is FrameworkElement root)
            {
                 root.Loaded += (s, e) => AdjustWindowToContent();
            }


            // Find the Pico's COM port automatically
            // Procedure attemps to find a Pico based on its USB VID:PID.
            // If it finds one, its COMx value is stored in detected.
            // This will be passed as a parameter to AutoConnect to estabish a connection.
            var detected = AutoDetectPico();

            // Setup a Dispatch Timer to check connection every 2 seconds
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(2);
            _reconnectTimer.Tick += ReconnectTimer_Tick;

            // Function will make a serial connection if passed a COM port.
            AutoConnect(detected);

            this.Closed += (s, e) => {
                _reconnectTimer.Stop();
                ClosePort();
            };
        }

        // Measure content and resize/center the window at runtime (WinUI 3)
        private void AdjustWindowToContent()
        {
            if (Content is FrameworkElement root)
            {
                // allow the content to measure itself unconstrained
                root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double desiredW = root.DesiredSize.Width;
                double desiredH = root.DesiredSize.Height;

                // enforce sensible minimums (previously in XAML)
                const int minW = 300;
                const int minH = 160;

                // convert to integer pixel sizes for AppWindow
                int newW = Math.Max((int)Math.Ceiling(desiredW), minW);
                int newH = Math.Max((int)Math.Ceiling(desiredH), minH);

                try
                {
                    // center and resize using AppWindow APIs (WinUI)
                    var hwnd = WindowNative.GetWindowHandle(this);
                    var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);

                    // Resize the window via AppWindow (Window doesn't expose Width/Height in WinUI 3)
                    appWindow.Resize(new Windows.Graphics.SizeInt32(newW, newH));

                    var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                    var work = displayArea.WorkArea;
                    int x = work.X + (work.Width - newW) / 2;
                    int y = work.Y + (work.Height - newH) / 2;

                    appWindow.Move(new Windows.Graphics.PointInt32(x, y));
                }
                catch
                {
                    // ignore if AppWindow APIs not available or call fails
                }
            }
        }

        // Auto-detect the Pico's COM port by looking for its unique VID/PID in the system's PnP devices
        private string? AutoDetectPico()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                {
                    var ports = searcher.Get();
                    foreach (var port in ports)
                    {
                        string? hardwareId = port["PNPDeviceID"]?.ToString();
                        string? caption = port["Caption"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(hardwareId) &&
                            hardwareId.Contains("VID_2E8A") &&
                            hardwareId.Contains("PID_0005") &&
                            !string.IsNullOrEmpty(caption))
                        {
                            var m = Regex.Match(caption, @"\((COM\d+)\)");
                            // if (m.Success) return m.Groups[1].Value;
                            // Set selected to string from m.Groups[1].Value.  This string just contains COMx, no parenthisis.
                            if (m.Success)
                            {
                                var selected = m.Groups[1].Value;
                                return m.Groups[1].Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText?.DispatcherQueue?.TryEnqueue(() => StatusText.Text = "Discovery Error: " + ex.Message);
            }
            return null;
        }

        // Procedure if passed a COM port.  Will attempt to connect to that COM port and update the status text if successful.
        private void AutoConnect(string? selected)
        {
            if (!string.IsNullOrEmpty(selected))
            {
                // Found a Pico on a COM port.  Start the reconnect timer and attempt a connection.
                _targetPort = selected;
                _reconnectTimer.Start();
                TryConnect();
            }
            else
            {
                // Didn't find a Pico on any COM port.  Start the reconnect timer and keep trying.
                _reconnectTimer.Start();
            }
        }

        private void TryConnect()
        {
            try
            {
                if (string.IsNullOrEmpty(_targetPort)) return;
                ClosePort();
                _picoPort = new SerialPort(_targetPort, 115200);
                _picoPort.DtrEnable = true; // Required for Pico USB
                _picoPort.RtsEnable = true;
                _picoPort.DataReceived += SerialPort_DataReceived;
                _picoPort.Open();

                if (StatusText != null)
                {
                    // Update GUI Status
                    StatusText.Text = "";
                }
            }
            catch
            {
                /* Timer will retry if device is busy or missing */
            }
        }

        
        private void ReconnectTimer_Tick(object? sender, object e)
        {
            if (_picoPort == null || !_picoPort.IsOpen)
            {
                string? discoveredPort = AutoDetectPico();

                if (!string.IsNullOrEmpty(discoveredPort))
                {
                    _targetPort = discoveredPort;
                    if (StatusText != null) StatusText.Text = $"Pico found on {_targetPort}. Connecting...";
                    TryConnect();
                }
                else
                {
                    if (StatusText != null)
                    {
                        StatusText.Text = "Searching for Ammeter...";
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