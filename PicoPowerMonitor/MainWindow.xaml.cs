// Version 10.2, 6/12/2026
// Changing font to Cascadia Code, adjusted Margins.
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ScottPlot;
using ScottPlot.Plottables;
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

        // How many data points are show at one on the plot.
        private readonly int streamLength = 40; 

       // Instantiate a instance of the ScottPlot DataStreamer class.
        private DataStreamer? _streamerPlot; 

        // Reconnect timer to handle unexpected disconnections
        private readonly DispatcherTimer _reconnectTimer;

        // 6/11/2026 pulled the plug on the Power Monitor from sending power readings, adjustding regular expression.
        // private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+),\s*P:\s*([\d.-]+)");
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+)");


        public MainWindow()
        {
            this.InitializeComponent();
            
            // Initialize ScottPlot Current Plot - Added 6/10/2026
            InitializeGraph();

            // Adjusted for new Vertical layout. Changed 6/12/2026
            // this.AppWindow.Resize(new Windows.Graphics.SizeInt32(650, 500));
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(630, 712));


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

                        // Just Changed RP2040's and this one has PID of 101F, all the documentation says it should be 0005.
                        // Got out Microscope and sure enough, the USB-Serial chips are different.
                        // I have 6 of them, Some are:
                        // Chip LOGO: BYT with 2 squares around it.
                        // BYQ16E had PID: 0005
                        // FQ16ES Had PID: 101F.
                        // Couple I can even read the markings on the chip.
                        // Updated code to look for either PID.

                        if (!string.IsNullOrEmpty(hardwareId) &&
                            hardwareId.Contains("VID_2E8A") &&
                            (hardwareId.Contains("PID_0005") || hardwareId.Contains("PID_101F")) &&
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

                                // Checks if StatusText is not null before updating its text. 
                StatusText?.Text = $"Connect on {_targetPort}.";
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

                    // if (StatusText != null) StatusText.Text = $"Pico found on {_targetPort}. Connecting...";
                    StatusText?.Text = $"Pico found on {_targetPort}. Connecting...";
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
                    // Removed Power Output, just not needed.
                    //string p = match.Groups[3].Value;

                    DispatcherQueue.TryEnqueue(() => {
                        // VoltageText?.Text = $"V: {v}";
                        // CurrentText?.Text = $"I: {i}";
                        VoltageText?.Text = v;
                        CurrentText?.Text = i;


                        // Convert current string to double and pass to NewHardwareDataReceived to update plot.
                        if (double.TryParse(i, out double currentValue))
                        {
                            NewHardwareDataReceived(currentValue);
                        }
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

        private void InitializeGraph()
        {
            _streamerPlot = CurrentSignaturePlot.Plot.Add.DataStreamer(streamLength);
            
            // Plot color and width
            _streamerPlot.Color = ScottPlot.Colors.Blue;
            _streamerPlot.LineWidth = 3;

            // Configure grid to display ticks from the right Y axis
            _streamerPlot.Axes.YAxis = CurrentSignaturePlot.Plot.Axes.Right;
            CurrentSignaturePlot.Plot.Grid.YAxis = CurrentSignaturePlot.Plot.Axes.Right;

            // Style Right Y-Axis attributes
            //CurrentSignaturePlot.Plot.YLabel("Current (Amps)");
            CurrentSignaturePlot.Plot.Axes.Right.Label.Text = "Current (Amps)";

            // Remove tick generator from Left Y and Bottom X Axis
            CurrentSignaturePlot.Plot.Axes.Left.RemoveTickGenerator();
            CurrentSignaturePlot.Plot.Axes.Bottom.RemoveTickGenerator();

            // Hide Grid Lines
            CurrentSignaturePlot.Plot.Grid.XAxisStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Grid.YAxisStyle.IsVisible = false;

            // Configure the initial plot initial scale from 0 to 5A
            CurrentSignaturePlot.Plot.Axes.SetLimits(0, streamLength, 0, 5.0);
            CurrentSignaturePlot.Refresh();

            // Auto scale to fit the data.
            CurrentSignaturePlot.Plot.Axes.ContinuouslyAutoscale = true;

            // Background panel color
            CurrentSignaturePlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#111115");
            CurrentSignaturePlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#111115");

            // Gridlines and label text colors
            CurrentSignaturePlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#5C6370"));
        }

        public void NewHardwareDataReceived(double instantaneousCurrent)
        {
            // Plot new data point.
            _streamerPlot?.Add(instantaneousCurrent);
            // Scroll plot to the left as new data is placed on the right.
            _streamerPlot?.ViewScrollLeft();
            // Request ScottPlot to redraw the UI with the updated array data
            CurrentSignaturePlot.Refresh();
        }
    }
}