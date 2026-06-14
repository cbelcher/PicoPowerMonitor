// <!-- v12.5, 6/14/2026 ScottPlot Marker works but I'm going to rebuild it -->
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using WinRT;


namespace PicoPowerMonitor
{
    public sealed partial class MainWindow : Window
    {
        // field declarations
        private SerialPort? _picoPort;
        private string? _targetPort;

        // ScottPlot Fields
        // How many data points are show at one on the plot.
        private readonly int streamLength = 50;

        // Plot Marker Field
        private ScottPlot.Plottables.HorizontalLine? _currentValueLine;

        // Instantiate a instance of the ScottPlot DataStreamer class.
        private DataStreamer? _streamerPlot; 

        // Reconnect timer to handle unexpected Pico Power Monitor disconnections
        private readonly DispatcherTimer _reconnectTimer;

        // 6/11/2026 pulled the plug on the Power Monitor from sending power readings, adjusting regular expression.
        // private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+),\s*P:\s*([\d.-]+)");
        private static readonly Regex PicoRegex = new Regex(@"V:\s*([\d.-]+),\s*I:\s*([\d.-]+)");

        // Fields for Acrylic Backdrop
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _backdropConfiguration;


        public MainWindow()
        {
            this.InitializeComponent();
            
            // Initialize ScottPlot Current Plot - Added 6/10/2026
            InitializeGraph();

            // Adjusted for new Vertical layout. Changed 6/12/2026
             this.AppWindow.Resize(new Windows.Graphics.SizeInt32(630, 712));

            // Find the Pico's COM port automatically
            // Procedure attempts to find a Pico based on its USB VID:PID.
            // If it finds one, its COMx value is stored in detected.
            // This will be passed as a parameter to AutoConnect to establish a connection.
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

            // Set up the persistent Desktop Acrylic Backdrop.
            // OS will try to override tint/opacity settings when window is moved, have to reapply after every activation.
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
                // _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
                _backdropConfiguration.IsInputActive = true;

                // FORCED FIX: Every time the window activation state cycles (like clicking/dragging),
                // aggressively push your custom mix rules back over the OS defaults.
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
            // _configurationSource = null;
            _backdropConfiguration = null;
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
                            // Set selected to string from m.Groups[1].Value.  This string just contains COMx, no parenthesis.
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
                    // Removed Power Output, just not needed.
                    //string p = match.Groups[3].Value;

                    DispatcherQueue.TryEnqueue(() => {
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

            // Removed all but Right Frame
            CurrentSignaturePlot.Plot.Axes.Bottom.FrameLineStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Axes.Top.FrameLineStyle.IsVisible = false;
            CurrentSignaturePlot.Plot.Axes.Left.FrameLineStyle.IsVisible = false;

            // Plot color and width
            _streamerPlot.Color = ScottPlot.Colors.Blue;
            _streamerPlot.LineWidth = 3;

            // Configure grid to display ticks from the right Y axis
            _streamerPlot.Axes.YAxis = CurrentSignaturePlot.Plot.Axes.Right;
            // Scales just fine without this next line.
            //CurrentSignaturePlot.Plot.Grid.YAxis = CurrentSignaturePlot.Plot.Axes.Right;

            // Style Right Y-Axis attributes
            //CurrentSignaturePlot.Plot.Axes.Right.Label.Text = "Current (Amps)";

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
           
            // Background panel color with transparency
            // SkiaSharp, doesn't take any queues from its grid Alpha Channel, so need to deal with it yourself.
            CurrentSignaturePlot.Plot.FigureBackground.Color = ScottPlot.Color.FromARGB(0x78111115);
            CurrentSignaturePlot.Plot.DataBackground.Color = ScottPlot.Colors.Transparent;

            // Grid line and label text colors
            CurrentSignaturePlot.Plot.Axes.Color(ScottPlot.Color.FromHex("#5C6370"));

            // Marker Configuration
            _currentValueLine = CurrentSignaturePlot.Plot.Add.HorizontalLine(0);

            // Explicitly bind to the active Right Axis frame
            _currentValueLine.Axes.YAxis = CurrentSignaturePlot.Plot.Axes.Right;


            // Hide style Tracking Line entirely
            // _currentValueLine.LineWidth = 0;

            // --- THE TRICK ---
            // Do NOT use LineWidth = 0. Instead, give it a width but make it completely invisible (Alpha = 0).
            // This forces ScottPlot to calculate the line length to the right edge without showing a line.
            _currentValueLine.LineWidth = 1;
            _currentValueLine.Color = ScottPlot.Color.FromARGB(0x00000000);


            // Set a temp value
            _currentValueLine.Text = "0.00";

            // --- THE POSITION FIXES ---
            // Tell the line object where its label belongs
            // _currentValueLine.LabelAlignment = ScottPlot.Alignment.MiddleRight;
            _currentValueLine.LabelAlignment = ScottPlot.Alignment.LowerCenter;

            // Tell the label's internal rendering block to anchor to the right horizontally
            _currentValueLine.LabelStyle.Alignment = ScottPlot.Alignment.LowerCenter;

            // Force it to use horizontal text rendering mode instead of vertical axis mode
            _currentValueLine.LabelStyle.Rotation = 0;

            // Force the line to explicitly pull its positioning from the right-side layout engine
            //_currentValueLine.LabelPositionByAxisTicks = true;

            // Format Marker
            _currentValueLine.LabelStyle.BackgroundColor = ScottPlot.Colors.Blue;
            _currentValueLine.LabelStyle.ForeColor = ScottPlot.Colors.White;
            _currentValueLine.LabelStyle.Padding = 4;
            _currentValueLine.LabelStyle.FontName = "Cascadia Code";
            _currentValueLine.LabelOppositeAxis = true;

            // Request ScottPlot to redraw the UI with the updated array data
            CurrentSignaturePlot.Refresh();
        }


        public void NewHardwareDataReceived(double instantaneousCurrent)
        {
            // Plot new data point.
            _streamerPlot?.Add(instantaneousCurrent);

            // start marker test here
            if (_currentValueLine != null)
            {
                // 1. Move the line and right-axis box to the exact current value
                _currentValueLine.Y = instantaneousCurrent;

                // 2. Format the label string to match your mockup (e.g., "2.16")
                _currentValueLine.Text = instantaneousCurrent.ToString("F2");
            }

            // Scroll plot to the left as new data is placed on the right.
            _streamerPlot?.ViewScrollLeft();
            // Request ScottPlot to redraw the UI with the updated array data
            CurrentSignaturePlot.Refresh();
        }


    }
}