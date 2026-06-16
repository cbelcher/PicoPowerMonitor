// v16.1, 6/15/2026 Had to lock it down further, Button and MAX PEAK were now shifting left and right.
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;
using System;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using WinRT;


namespace PicoPowerMonitor
{
    public sealed partial class MainWindow : Window
    {
        // field declarations for Pico's Serial Connection
        private SerialPort? _picoPort;
        private string? _targetPort;

        // ScottPlot Fields
        // How many data points are show at one on the plot.
        private readonly int streamLength = 50;

        // Plot Marker X coordinate tracking line field
        private ScottPlot.Plottables.HorizontalLine? _currentValueLine;

        // Plot Marker Text Badge Field
        private ScottPlot.Plottables.Text? _valueBadge;

        // Instantiate a instance of the ScottPlot DataStreamer class.
        private DataStreamer? _streamerPlot;

        // Reconnect timer to handle unexpected Pico Power Monitor disconnections
        private readonly DispatcherTimer _reconnectTimer;

        // Regular expression for parsing Monitor's data stream.
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+)");

        // Fields for Acrylic Backdrop
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _backdropConfiguration;

        // Tracks the highest peak current
        private double _maxCurrent = double.MinValue;

        public MainWindow()
        {
            this.InitializeComponent();

            // Initialize ScottPlot Current Plot
            InitializeGraph();

            // Adjusted for new Vertical layout.
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(630, 712));

            // Find the Pico's COM port automatically
            // Procedure attempts to find a Pico based on its USB VID:PID.
            // If it finds one, COMx value is stored in variable detected.
            // This will be passed as a parameter to AutoConnect to establish a connection.
            var detected = AutoDetectPico();

            // Setup a Dispatch Timer to check for missing connection every 2 seconds
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(2);
            _reconnectTimer.Tick += ReconnectTimer_Tick;

            // Function will make a serial connection if passed a COM port.
            AutoConnect(detected);

            this.Closed += (s, e) =>
            {
                _reconnectTimer.Stop();
                ClosePort();
            };

            // Set up the persistent Desktop Acrylic Backdrop.
            // OS will try to override tint/opacity settings when window is moved or loses focus
            // have to reapply after every activation.
            ConfigurePersistentAcrylic();
        }

        private void ConfigurePersistentAcrylic()
        {
            if (DesktopAcrylicController.IsSupported())
            {
                // Establish the configuration source tracking window states
                _backdropConfiguration = new SystemBackdropConfiguration();

                // Hook into Activated so we re-apply colors if the OS tries to override them
                this.Activated += Window_Activated;
                this.Closed += Window_Closed;

                _backdropConfiguration.IsInputActive = true;
                _backdropConfiguration.Theme = SystemBackdropTheme.Dark;

                // Initialize the controller
                _acrylicController = new DesktopAcrylicController();

                // Apply your custom styling rules
                ApplyCustomAcrylicSettings();

                // Connect it directly to this window
                _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
            }
        }

        private void ApplyCustomAcrylicSettings()
        {
            if (_acrylicController != null)
            {
                // Lock in your preferred deep dashboard color tone (#1E1E24)
                _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 30, 30, 36);

                // Force your targeted opacity settings to stay bound
                _acrylicController.TintOpacity = 0.25f;
                _acrylicController.LuminosityOpacity = 0.10f;
            }
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_backdropConfiguration != null)
            {
                // Keeps track of window focus changes
                // Forced this True. Without it, the app would revert to defaults if it went out of focus.
                _backdropConfiguration.IsInputActive = true;

                // Every time the window activation state cycles (like clicking/dragging),
                // Have to push my custom mix rules back over the OS defaults.
                ApplyCustomAcrylicSettings();
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (_acrylicController != null)
            {
                _acrylicController.Dispose();
                _acrylicController = null;
            }
            this.Activated -= Window_Activated;
            _backdropConfiguration = null;
        }

        // Auto-detect the Pico's COM port by looking for its unique VID/PID.
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

                        // USB PID's I've seen from different RP2040's.
                        // BYQ16E had PID: 0005
                        // FQ16ES Had PID: 101F.
                        
                        if (!string.IsNullOrEmpty(hardwareId) &&
                            hardwareId.Contains("VID_2E8A") &&
                            (hardwareId.Contains("PID_0005") || hardwareId.Contains("PID_101F")) &&
                            !string.IsNullOrEmpty(caption))
                        {
                            var m = Regex.Match(caption, @"\((COM\d+)\)");
                            // if (m.Success) return m.Groups[1].Value;
                            // This string just contains COMx, no parenthesis.
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
                if (StatusText != null)
                {
                    StatusText.Text = $"Connected on {_targetPort}.";
                    // StatusText and Ellispse to Green to indicate successful connection.  Changed 6/13/2026
                    StatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 61, 157, 36));
                    StatusEllipse.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 61, 157, 36));
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

                    if (StatusText != null)
                    {
                        StatusText.Text = $"Pico found on {_targetPort}. Connecting...";
                        // Green Status Text. Amber StatusEllipse while connection is established.
                        StatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 61, 157, 36));
                        StatusEllipse.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 191, 0));
                    }

                    TryConnect();
                }
                else
                {
                    if (StatusText != null)
                    {
                        StatusText.Text = "Searching for Pico Power Monitor...";
                        // Set StatusText color to Amber to indicate searching
                        // Set StatusEllipse color to red to indicate not connected.
                        StatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 191, 0));
                        StatusEllipse.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0));
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

                    // Plot and max current tracking require a doubles.
                    // 
                    if (double.TryParse(i, out double currentValue))
                    {
                        // Boolean used to monitor change in max current
                        bool isNewMax = false;

                        // Check if current value is a new max, now on the background thread.
                        if (currentValue > _maxCurrent)
                        {
                            _maxCurrent = currentValue;
                            isNewMax = true;
                        }

                        // Hand over data updates to the UI thread.
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            VoltageText?.Text = v;
                            CurrentText?.Text = i;

                            // Pass the currentValue to ScottPlot update function.
                            NewHardwareDataReceived(currentValue);

                            // If this is a new max current, update the MaxCurrentText UI block.
                            if (isNewMax)
                            {
                                // Format to 3-decimal
                                MaxCurrentText.Text = _maxCurrent.ToString("F3");
                            }
                        });
                    }
                    else
                    {
                        // If parsing fails, still push string to UI.
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            VoltageText?.Text = v;
                            CurrentText?.Text = i;
                        });
                    }
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
            // Create streamer
            _streamerPlot = CurrentSignaturePlot.Plot.Add.DataStreamer(streamLength);
            _streamerPlot.Color = ScottPlot.Colors.Blue;
            _streamerPlot.LineWidth = 3;
            _streamerPlot.Axes.YAxis = CurrentSignaturePlot.Plot.Axes.Right;

            // Removed all but Right Frame
            CurrentSignaturePlot.Plot.Axes.Bottom.FrameLineStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Axes.Top.FrameLineStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Axes.Left.FrameLineStyle.IsVisible = false;

            // Remove tick generator from Left Y and Bottom X Axis
            CurrentSignaturePlot.Plot.Axes.Left.RemoveTickGenerator();
            CurrentSignaturePlot.Plot.Axes.Bottom.RemoveTickGenerator();

            // New rightAxis
            var rightAxis = CurrentSignaturePlot.Plot.Axes.Right;
            rightAxis.FrameLineStyle.IsVisible = true;
            rightAxis.TickLabelStyle.FontName = "Cascadia Code";
            rightAxis.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#5C6370");

            // Hide tick marks
            rightAxis.MajorTickStyle.Length = 0;
            rightAxis.MinorTickStyle.Length = 0;

            // Force Right Axis to 1-digit after decimal
            ((NumericAutomatic)rightAxis.TickGenerator).LabelFormatter = x => x.ToString("F1");

            // Hide Grid Lines
            CurrentSignaturePlot.Plot.Grid.XAxisStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Grid.YAxisStyle.IsVisible = false;

            // Configure the initial plot initial scale from 0 to 5A
            CurrentSignaturePlot.Plot.Axes.SetLimits(0, streamLength, 0, 5.0);
            CurrentSignaturePlot.Plot.Axes.ContinuouslyAutoscale = true;

            // Background panel color with transparency
            // SkiaSharp, doesn't take any queues from the grids Alpha Channel, so need to deal with it yourself.
            CurrentSignaturePlot.Plot.FigureBackground.Color = ScottPlot.Color.FromARGB(0x78111115);
            CurrentSignaturePlot.Plot.DataBackground.Color = ScottPlot.Colors.Transparent;

            // Marker configuration on Right side, displaying Current X value.
            _currentValueLine = CurrentSignaturePlot.Plot.Add.HorizontalLine(0);
            _currentValueLine.Axes.YAxis = rightAxis;

            // Hide default marker line
            _currentValueLine.LineWidth = 0;

            // Create a Marker text badge that sits INSIDE the data plot area
            _valueBadge = CurrentSignaturePlot.Plot.Add.Text("0.00", streamLength, 0);
            _valueBadge.Axes.YAxis = rightAxis;

            // Anchor Marker using its LowerRight corner.
            _valueBadge.LabelAlignment = ScottPlot.Alignment.LowerRight;
            _valueBadge.LabelRotation = 0;
            _valueBadge.LabelOffsetX = -30;
            _valueBadge.LabelOffsetY = 30;

            // Style Marker text block
            _valueBadge.LabelStyle.BackgroundColor = ScottPlot.Color.FromARGB(0xFF00A2FF);  //Solid Blue 
            _valueBadge.LabelStyle.ForeColor = ScottPlot.Colors.White;
            _valueBadge.LabelStyle.Padding = 5;
            _valueBadge.LabelStyle.FontName = "Cascadia Code";
            _valueBadge.LabelStyle.Bold = true;

            // Configure Figure and Data background colors to be transparent, allowing the Acrylic backdrop to show through.
            CurrentSignaturePlot.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            CurrentSignaturePlot.Plot.DataBackground.Color = ScottPlot.Colors.Transparent;

            // Initial plot paint
            CurrentSignaturePlot.Refresh();
        }


        public void NewHardwareDataReceived(double instantaneousCurrent)
        {
            // Plot new data point.
            _streamerPlot?.Add(instantaneousCurrent);

            if (_currentValueLine != null && _valueBadge != null)
            {
                // Move the math coordinate plane tracker
                _currentValueLine.Y = instantaneousCurrent;

                // Update the Plot Marker Label TextBox string, format with 2 decimal places.
                // Lock its position to the Right Edge of the Plot.
                _valueBadge.LabelText = instantaneousCurrent.ToString("F2");
                _valueBadge.Location = new ScottPlot.Coordinates(streamLength, instantaneousCurrent);
            }

            // Scroll plot to the left as new data is placed on the right.
            _streamerPlot?.ViewScrollLeft();
            // Request ScottPlot to redraw the UI with the updated array data
            CurrentSignaturePlot.Refresh();
        }

        public void ResetMaxCurrent_Click(object sender, RoutedEventArgs e)
        {
            _maxCurrent = double.MinValue;
            MaxCurrentText.Text = "0.000";
        }
    }
}