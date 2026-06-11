// Version v8.1 6/11/2026 Major UI Changes.
// Fixed regular expression, I have cut the power reading from being sent to the pc.
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

        // 1. Allocate a fixed buffer for raw data points (e.g., last 1,000 readings)  Added 6/10/2026
        private readonly double[] _currentDataBuffer = new double[1000];
        private int _nextDataIndex = 0;

        private Signal? _signalPlot;
        private VerticalLine? _currentPositionLine;
        //

        // Reconnect timer to handle unexpected disconnections
        private readonly DispatcherTimer _reconnectTimer;

        // Updated Regex for "V: 0.000, I: 0.000, P: 0.000"
        // 5/5/2026 Had to change the regex to catch the negitive current values.  Updated regex to look for optional negative sign before the current value.
        // Ever since adding the MOSFET the INA Report Negitive Current when idle, need to figure out what is going on.
        // private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.]+),\s*I:\s*([\d.]+),\s*P:\s*([\d.]+)");
        // 6/11/2026 pulled the plug on the Power Monitor from sending power readings, adjustding regular expression.
        // private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+),\s*P:\s*([\d.-]+)");
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+)");


        public MainWindow()
        {
            this.InitializeComponent();
            
            // Initialize ScottPlot Current Plot - Added 6/10/2026
            InitializeGraph();

            // Just setting to static size for now. Changed 6/11/2026
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(650, 500));

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
                        // Couple I can even ready the markings on the chip.
                        // Updated code to look for either PID.

                        // If this happens again, just dump the PID.

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


                // This resolved IDE0031 Null check can be simplified.  6/10/2026
                StatusText?.Text = "";
                // if (StatusText != null)
                // {
                    // Update GUI Status
                    // StatusText.Text = "";
                // }
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
                    // Simplified
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
                    string p = match.Groups[3].Value;

                    DispatcherQueue.TryEnqueue(() => {
                        VoltageText?.Text = $"V: {v}";
                        CurrentText?.Text = $"I: {i}";
                        // Removed Power Output, just not needed.
                        // PowerText?.Text = $"P: {p}";
                        // Convert current string to double and pass to NewHardwareDataReceived
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
            // 2. Link the buffer directly to the plot
            _signalPlot = CurrentSignaturePlot.Plot.Add.Signal(_currentDataBuffer);

            // Customize visual style for sharp diagnostic signatures
            _signalPlot.Color = ScottPlot.Colors.Green;
            _signalPlot.LineWidth = 2;

            // 3. Add a vertical indicator line showing the current write head position
            _currentPositionLine = CurrentSignaturePlot.Plot.Add.VerticalLine(0);
            _currentPositionLine.Color = ScottPlot.Colors.Red;
            _currentPositionLine.LinePattern = LinePattern.Dotted;

            // Set up axes labels for your clients
            CurrentSignaturePlot.Plot.XLabel("Sample Index");
            CurrentSignaturePlot.Plot.YLabel("Current (Amps)");
            CurrentSignaturePlot.Plot.Title("Instantaneous Current Signature");

            // Hide Vertical Grid Lines
            //CurrentSignaturePlot.Plot.?


            // Tell the plot to scale cleanly to fit our 1,000 points
            CurrentSignaturePlot.Plot.Axes.SetLimits(0, _currentDataBuffer.Length, 0, 5.0); // 0-5A scale default
            CurrentSignaturePlot.Refresh();

            // Matches ScottPlot's canvas background to the deep dark panel color (#111115)
            CurrentSignaturePlot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#111115");
            CurrentSignaturePlot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#111115");

            // Tweak gridlines and label text colors to stand out beautifully on dark background
            CurrentSignaturePlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#5C6370"));
        }

        /// <summary>
        /// Call this method whenever a new un-averaged packet arrives from your hardware
        /// </summary>
        public void NewHardwareDataReceived(double instantaneousCurrent)
        {
            // Overwrite the oldest data point in the buffer
            _currentDataBuffer[_nextDataIndex] = instantaneousCurrent;

            // Move the vertical line to show where the new data is dropping
            _currentPositionLine?.X = _nextDataIndex;

            // Increment index and wrap around at the end of the buffer
            _nextDataIndex++;
            if (_nextDataIndex >= _currentDataBuffer.Length)
            {
                _nextDataIndex = 0;
            }

            // Request ScottPlot to redraw the UI with the updated array data
            CurrentSignaturePlot.Refresh();
        }
    }
}