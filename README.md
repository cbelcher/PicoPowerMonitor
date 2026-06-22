# Pico Power Monitor


A high-performance, real-time hardware diagnostic and power-monitoring utility designed for component-level electronics repair. This application interfaces with a custom Raspberry Pi Pico-like MCU hardware monitor, to capture, visualize, and analyze microsecond-scale power-up signatures, transient voltage spikes, and current draw on motherboards, GPUs, and other complex circuits.  The UI was intentionally targeted for content providers, to visually overlay real-time readings in their videos without blocking what is behind them.

## Project is broken up into 4 sub-projects.

  - PicoPowerMonitor - This Repo, it's the Windows 11 UI interface.
 
  - KiCAD Custom PCB Design.
     - Repo: /PicoPowerMonitor_KiCad (Direct link [here](https://github.com/cbelcher/PicoPowerMonitor_KiCad))
  	
  - MicroPhython code to drive the hardware side of things.
		    
    - Repo: /RP2040PowerMonitor  (Direct link [here](https://github.com/cbelcher/RP2040PowerMonitor})
        This is the project that runs on the RP2040-Zero.  Current MicroPython firmware 1.28.
      
	
  - FreeCAD 3D Printed Case Design. (Project should be uploaded this weekend.)

    - **Houses the custom PCB, using M3 threaded inserts and screws.**
	
    - **4 - 4 mm Female Banana jacks. 2 for PSU input and 2 to the device under test.**

    - **8 - 5 x 2 mm magnets to secure lid to its base.**
   
    - **3 - M3 Threaded inserts.**
	
	- **3 - M3 x 8 Screws.**
	
    - **Aitrip 2.4" SSD1309 based 128x64 OLED Display Module, via 4 Pin I2C interface.**
			  - Fast, ultra-high contrast, black and white OLED display.
	
    - **Momentary SPST reset button.**

  ##  This native Widows 11 multi-threaded application utilizing Microsoft .NET10 and WinUI 3 UI framework.
	
  - **The ultra-responsive application with configurable Alpha-channel translucency does come at a price, will only run on Windows 11 build 10.0.22621.0 or later.**
    
Built natively for Windows 11 using C# and WinUI 3 (Windows App SDK).
I do have plans to rework the UI using Avalonia UI, this will give it the ability to run on MAC and Linux, shouldn't be to hard.

## Screenshots

Two Images to show the applications translucent effect.

- 1st image is the application running and has a black Windows Command Prompt sitting on the lower right hand side.
<p align="center">
<img width="630" height="712" alt="Screenshot 2026-06-20 09 47 20" src="https://github.com/user-attachments/assets/88d37030-c175-486c-9b1b-a94b9e77e63a" />
</p>

- 2nd has a sunset wallpaper behind the app, you can see the orange haze against the dark silhouette of the horizon.
<p align="center">
<img width="630" height="712" alt="Screenshot 2026-06-20 09 46 05" src="https://github.com/user-attachments/assets/c187d127-4869-423f-a4a4-831f3c1439fc" />
</p>

- One Screenshot of the app searching for its Power Monitor.  Ellipse is Red indicating no connection, and Amber text giving feedback on its status.
<p align="center">
<img width="630" height="712" alt="Screenshot 2026-06-20 09 55 49" src="https://github.com/user-attachments/assets/2d25e413-6262-4977-a537-93e90529c41f" />
</p>

## Several shots of the actual PowerMonitor.

- Top showing 2.4" OLED Display
<p align="center">
<img width="4032" height="3024" alt="Top with OLED Display" src="https://github.com/user-attachments/assets/dca643aa-f8da-4411-ba86-9a7e6f64c693" />
</p>

- OCP Tripped Alert Message
<p align="center">
<img width="4032" height="3024" alt="IMG_2825" src="https://github.com/user-attachments/assets/4bf2972e-275e-4cc9-aeae-07763dba0f1a" />
</p>

- Front with Dual 4 mm DUT banana jacks.
<p align="center">
<img width="4032" height="3024" alt="Front" src="https://github.com/user-attachments/assets/fd5922bc-aab0-42b5-a119-ecc8808b88b5" />
</p>

- Right Side with Dual 4 mm PSU input banana jacks.
<p align="center">
<img width="4032" height="3024" alt="Right" src="https://github.com/user-attachments/assets/50e41564-711f-4d8c-acdb-29d3f4b43522" />
</p>

- Rear view, with USB Type-C receptacle and OCP reset button
<p align="center">
<img width="4032" height="3024" alt="Rear" src="https://github.com/user-attachments/assets/e915d757-f136-446a-a2da-f37a417cb0d3" />
</p>

- Closeup of monitor's USB Receptacle
<p align="center">
<img width="4032" height="3024" alt="USB Receptacle" src="https://github.com/user-attachments/assets/d8fb62c9-1024-464e-9aca-cddaedc41f51" />
</p>

- Top cover flipped open.  Revealing back of display, I2C connection, lid-to-base mounting magnets, custom PCB with the castellated RP2040-Zero.
<p align="center">
<img width="4032" height="3024" alt="Top Flipped Open" src="https://github.com/user-attachments/assets/3367d22d-17f4-41c2-b8d4-8b66517e471b" />
</p>

- Internal closeup
<p align="center">
<img width="4032" height="3024" alt="Custom PCB Closeup" src="https://github.com/user-attachments/assets/ffc21280-112b-4299-aa9a-3efcade10222" />
</p>

- Back of OLED Display
<p align="center">
<img width="4032" height="3024" alt="Back of OLED" src="https://github.com/user-attachments/assets/140ec834-18e0-4245-869b-dadcf625227b" />
</p>



## Key Features

- High-Speed Hardware Telemetry: Interfaces directly via high-speed serial/I2C with a custom monitoring rig (utilizing precision TI INA260 Digital Current and Power Monitor)
    to pull raw, instantaneous data with zero averaging.  Over a any run of the mil UART @ 115,200 it will send 100 samples per second, without breaking a sweat.
    
- Real-Time Data Visualization: Displays instantaneous voltage, current values, and simultaneously displays visual plot of last x (configurable) number of data-points.
    Y-Axis auto scales, to cover all displayed current values.

- Tracks Peak Current detected.  Resetable any time, with convenient Reset button on the UI.

- Beautiful fully configurable translucent UI, specifically for content providers.  Allows the viewers to see the readings, but not block whatever is behind them.

- Fault-Tolerant Connection Engine: Zero configuration, automatically scans and monitors active COM ports searching for USB PID of the PowerMonitor.
    If the hardware monitor is disconnected, the UI gracefully prompts the user that the device needs to be connected.  When the application sees a COM port with a valid
    PID, it establishes the connection and updates the UI to show connected status and the COM port in use.

- Designed for the Bench and Content Providers: Built specifically to help technicians isolate shorted rails, catch brief power rail transients, and observe exact
    power-on sequencing signatures.

- Unique OCP: I could have used a fuse to save the monitors hardware components during a Over Current event.  A fuse blows and out comes the soldering iron, could have used a resetable device but that still entails opening up the device, but I had a better solution.  The TI's INA260 is rated for 15 A of continuous current, it can withstand over 50 A for up to 1(s), 30 A for over a minute without failing.  It also has a nice trick up its sleeve, on every sample it compares the reading to a value stored in its OCL (Over Current Limit) register.  You cross that threshold and it's open-drain ALERT Pin is pulled low.  Utilizing the incredible Infineon BSC007N04LS6 N-Channel MOSFET, with it's ultra-low RDS(on) max of 0.7 mΩ (don't even need a passive heatsink).  When that ALERT pin goes to ground so does the Gate of the MOSFET, shutting the system down preventing component damage.  This also triggers an interrupt on the RP2040 to update the display that an OCP event has occurred.  A single press of the momentary SPST reset button, mounted on the back of the case clears the ALERT and resets the unit for normal operation.  And the best part is that limit is completely configurable, I have it set to 16 A as the default.

• Standard 19 mm pitch Banana Jacks, both pairs, input and output..

>  **CRITICAL WARNING:** Do not exceed 40 V, or you risk damaging the INA260 logic layer.

## Installation & Deployment

This application is distributed as a standalone, self-contained desktop installation package that works on any modern Windows 11 system with zero external runtime dependencies.
	Must be running Windows 11 Build 10.0.22621.0 or later.

For Users / Clients
1. Download the latest PicoPowerMonitor.exe from the Releases section.
2. Run the installer and follow the on-screen wizard.
3. Gives you the option to create a Desktop shortcut.
4. Launch Pico Power Monitor from your Desktop or Start Menu.
4. Plug your PicoPowerMonitor hardware into any USB port; the application will automatically detect the device and begin streaming data.

For Developers (Compiling from Source)
• IDE: Visual Studio 2022 / 2026 (v18.7+ recommended).
• Workloads Required: .NET Desktop Development, Windows Application Development.
• Target Framework: .NET10.0-windows10.0.22621.0` (Unpackaged configuration).
• Primary Libraries: Windows App SDK (WinUI 3), ScottPlot.

1. **Clone the repository:**
    First you need to download the sources from Github. From the command line do:
   ```
   > git clone https://github.com/cbelcher/PicoPowerMonitor.git
   ```
2. Open PicoPowerMonitor.sln in Visual Studio.
3. Change the debug profile configuration drop-down from PicoPowerMonitor (Package) to PicoPowerMonitor (Unpackaged).
4. Press F5 to compile and run.


## Architecture Overview

The software is split into a responsive, hardware-accelerated frontend and a dedicated background thread architecture to handle incoming telemetry packets safely without blocking the UI layout:

```
+-------------------------------------------------------------------+
|                            WinUI 3 Interface                    	|
|  - Real-Time ScottPlot Canvas   |       Diagnostic Control Panel	|
+---------------------------------+---------------------------------+
|                                 | 	(Thread Safe Dispatch)      |
+---------------------------------+---------------------------------+
|                   High-Speed Telemetry Processing               	|
|  - COM Auto-Detection Layer     |  Instantaneous Data Parser    	|
+---------------------------------+---------------------------------+
|                                 |      (Serial Streams)         	|
+---------------------------------+---------------------------------+
|			      		Power Module Hardware                      	|
+-------------------------------------------------------------------+
```

## License

This project is licensed under the MIT License - see the [LICENSE](https://www.google.com/search?q=LICENSE) file for details.

## Acknowledgments

* Built as a dedicated utility for hardware repair professionals and electrical engineers.
* Powered by the exceptional open-source plotting performance of ScottPlot. https://github.com/ScottPlot/ScottPlot
* Display powered by open-source driver by rdagger / micropython-ssd1309.  https://github.com/rdagger/micropython-ssd1309
* Inno Setup open-source installer.  https://github.com/jrsoftware/issrc

## Would like to thank ScottPlot, rdagger and jrsoftware for making this project possible.

