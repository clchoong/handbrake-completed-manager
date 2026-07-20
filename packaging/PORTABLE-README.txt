HandBrake Completed Manager - Portable Edition

Requirements
------------
- Windows 10 build 17763+ on a Microsoft-supported Enterprise/IoT/LTSC
  release, or a supported Windows 11 release
- 64-bit Windows matching the package architecture
- No administrator rights or separate .NET installation required

Start
-----
Run HandBrakeCompletedManager.exe. The application creates a data folder beside
the executables for its history database, settings, connections, and logs.

Connect HandBrake
-----------------
Open HandBrake Completed Manager and expand "Completion capture setup".
Copy the displayed receiver path and arguments into HandBrake under:

Tools > Preferences > When Done > Encode Completed > Send File To

Keep the entire portable folder together. If it is moved, update the receiver
path in HandBrake. Do not place this application inside the HandBrake program
folder.

Back up or move
---------------
Exit the application from its notification-area menu before copying the folder.
The data folder contains the durable completed-encode history and local settings.
