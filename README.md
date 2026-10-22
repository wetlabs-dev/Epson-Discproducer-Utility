# Epson Discproducer Diagnostics

A read-only Windows 11 x64 WPF application targeting .NET 10. It discovers Epson USB device nodes, reads Windows metadata, and attempts documented IEEE 1284 identification and USB printer-class status queries through the **existing Windows USB printer driver**. It never installs or replaces a driver, submits print jobs, or runs Epson maintenance commands.

**Compatibility is conditional:** PP-100 hardware testing is still required. Discovery or opening an interface does not prove bidirectional USB communication. Successful queries establish a Windows driver request/response; the driver may cache information. The app does not claim to capture USB wire packets. Raw descriptors, firmware, fatal-error history, EEPROM and vendor-specific status are unavailable in version 1.

## Windows 11: download and run

1. Keep Epson's official PP-100 driver and Total Disc Maker installed. Connect the PP-100 by USB and turn it on. For the first diagnostic session, wait until disc publishing is idle.
2. Open the repository's **Actions → Windows build and test**, select a successful run, and download the **DiscproducerDiagnostics-win-x64** artifact. GitHub may require sign-in. Extract the artifact ZIP, then extract the application ZIP inside it.
3. Run **DiscproducerDiagnostics.App.exe** from the extracted folder. No .NET runtime, Python, Visual Studio, or administrator rights are required for the published self-contained package. The package is unsigned; follow your organization's policy if Windows SmartScreen blocks it. Do not disable Windows security.
4. Leave **Simulation mode** off. Click **Scan for Devices**. Select the Epson node with a USB printer interface path if one is available. VID `04B8` identifies Epson; the PID is discovered, not assumed. Multiple rows can belong to one physical unit. Compare instance IDs and parent chains rather than relying on names alone.
5. Click **Connect**. The app checks interface access and requests IEEE 1284 identity and printer-class status. Read **Device information**, **Status**, and **Communication log**. An actual serial number appears only if the identification response exposes a recognized serial field; a PnP instance suffix is never treated as a serial number.
6. Click **Refresh Status** for a new observation. Results are snapshots, not continuous monitoring. If you unplug the device, refresh to detect removal. **Cancel** stops an active operation; **Disconnect** ends the logical session. Handles are closed after each operation.
7. Click **Export Diagnostic Bundle…** to save a ZIP. Redaction is enabled by default and removes identifiers, raw bytes and free text that could contain identifiers. Uncheck it only when intentionally sharing the full diagnostic evidence. Copying an individual log entry is explicitly **unredacted**.

**If no supported interface is exposed:** the app still displays metadata and reports the query limitation. Do not use Zadig, WinUSB or libusbK to replace Epson's driver. See [troubleshooting](docs/troubleshooting.md).

**Without a printer:** enable Simulation mode, choose a scenario, Scan, then Connect. Disconnect before changing scenarios. Every mock device and response is marked simulated; PID `FFFF` and serial `DEMO-ONLY-0001` are invented test fixtures, never PP-100 identifiers.

## Build from source (developers only)

Install the .NET 10 SDK on Windows 11 x64, then use PowerShell in this repository:

```powershell
./scripts/build.ps1                   # Release, unit tests, portable EXE + ZIP
./scripts/build.ps1 -Configuration Debug
./scripts/smoke-test.ps1             # Open and close the packaged WPF app
```

Output: `artifacts/win-x64/DiscproducerDiagnostics.App.exe` and `artifacts/DiscproducerDiagnostics-win-x64.zip`. Use a .NET 10-compatible IDE (for example Visual Studio 2026) or VS Code with the SDK. Visual Studio is not needed by end users. An MSI is optional and is not provided; the portable package requires no installation. The runtime may extract native libraries into the user's temporary directory.

To run tests independently:

```powershell
dotnet test tests/DiscproducerDiagnostics.Tests -c Release
```

The non-UI libraries and tests target `net10.0`; Windows native methods execute only in the Windows app's worker. A macOS/Linux cross-build can check compilation with Windows targeting enabled, but cannot validate the WPF UI or native APIs. Windows CI builds Debug/Release, tests the portable libraries, publishes x64, and smoke-tests the actual window.

## Design and limitations

- [Architecture and safety boundary](docs/architecture.md)
- [USB communication and verification](docs/usb-communication.md)
- [Command registry and original sources](docs/supported-commands.md)
- [Troubleshooting and Windows errors](docs/troubleshooting.md)
- [Fatal-error history: research required](docs/fatal-error-history.md)
- [Verification checklist and current evidence](docs/verification.md)

The MIT-licensed Serilog and xUnit dependencies are restored from NuGet with their own license metadata. This project is an independent diagnostic utility, not an Epson product.
