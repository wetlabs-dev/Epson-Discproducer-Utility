# Epson Discproducer Diagnostics

A read-only Windows 11 x64 WPF application targeting .NET 10. It discovers Epson USB device nodes, reads Windows metadata, and attempts documented IEEE 1284 identification and USB printer-class status queries through the **existing Windows USB printer driver**. It never installs or replaces a driver, submits print jobs, or runs Epson maintenance commands.

**Compatibility is conditional:** PP-100 hardware testing is still required. Discovery or opening an interface does not prove bidirectional USB communication. Successful queries establish a Windows driver request/response; the driver may cache information. The app does not claim to capture USB wire packets. USB hub descriptors are retrieved when Windows permits verified hub/port access. Firmware, fatal-error history, EEPROM, direct standard GET_STATUS and vendor-specific status remain unavailable.

## Windows 11: download and run

1. Keep Epson's official PP-100 driver and Total Disc Maker installed. Connect the PP-100 by USB and turn it on. For the first diagnostic session, wait until disc publishing is idle.
2. Open the repository's **Actions → Windows build and test**, select a successful run, and download the **DiscproducerDiagnostics-win-x64** artifact. GitHub may require sign-in. Extract the artifact ZIP, then extract the application ZIP inside it.
3. Run **DiscproducerDiagnostics.App.exe** from the extracted folder. No .NET runtime, Python, Visual Studio, or administrator rights are required for the published self-contained package. The package is unsigned; follow your organization's policy if Windows SmartScreen blocks it. Do not disable Windows security.
4. Leave **Simulation mode** off. Click **Scan for Devices**. Select the Epson node with a USB printer interface path if one is available. VID `04B8` identifies Epson; the PID is discovered, not assumed. Multiple rows can belong to one physical unit. Compare instance IDs and parent chains rather than relying on names alone.
5. Click **Connect**. The app checks interface access and requests IEEE 1284 identity and printer-class status. Read **Device information**, **Status**, and **Communication log**. An actual serial number appears only if the identification response exposes a recognized serial field; a PnP instance suffix is never treated as a serial number.
6. Click **Refresh Status** for a new observation. Results are snapshots, not continuous monitoring. If you unplug the device, refresh to detect removal. **Cancel** stops an active operation; **Disconnect** ends the logical session. Handles are closed after each operation.
7. Click **Run Full Diagnostics** to collect interfaces/descriptors, queues/jobs and safe identity/status responses with separate progress milestones, then save the offered bundle. **Export Diagnostic Bundle…** is also available at any time. Choose RedactedSummary, TechnicalDiagnostics (default), or explicitly confirmed FullForensic. Individual copies and binary/JSON saves are **unredacted**.

**If no supported interface is exposed:** the app still displays metadata and reports the query limitation. Do not use Zadig, WinUSB or libusbK to replace Epson's driver. See [troubleshooting](docs/troubleshooting.md).

**Without a printer:** enable Simulation mode, choose a scenario, Scan, then Connect. Disconnect before changing scenarios. Every mock device and response is marked simulated; PID `FFFF` and serial `DEMO-ONLY-0001` are invented test fixtures, never PP-100 identifiers.


## Protocol investigation features (1.3.0)

- **USB Interfaces**: expandable PnP/composite topology, driver service/INF/provider/version, container/location data, and actual device/configuration/string/interface/endpoint descriptors through the Windows hub API. Driver-key validation prevents querying an unrelated hub port. Access limitations remain explicit.
- **Protocol Explorer**: allowlisted printer identity/status, hub descriptors, PnP and spooler inspection. Displays framing, source, requirements and interpreted outcomes. Standard GET_STATUS is explicitly unsupported on the current driver path; no arbitrary sender is exposed.
- **Windows Printer**: queue/driver/port monitor, bidirectional configuration, pending jobs and evidence-based instance/container association with ambiguity reporting.
- **Communication Log**: correlated host transactions, exact buffers, bounded offset/hex/ASCII view, filters, copy, binary and JSON save.
- **USB Capture**: built-in offline USBPcap PCAPNG import, filters, URB correlation, setup/payload view and comparison to application transactions. No Wireshark dependency to import; no capture driver installation or packet replay.
- **Diagnostic bundles**: schema 3 structured evidence and generated next steps, with three privacy modes. No claims that arbitrary binary data can be safely redacted.

- **Hardware Validation**: guided collection with separate discovery, association, handle, request and response milestones; categorized failures and recovery guidance.
- **Research**: versioned sessions, manual annotations/associations, two-session comparison, per-byte payload differences and evidence hashes. Captures remain external files.
- **Offline Evidence**: reopen schema 2/3 diagnostic ZIPs without hardware. Forensic identity, topology and transaction views are reconstructed separately from live state; all modes expose their retained documents.
- **Reliability**: bounded worker progress preserves completed descriptor calls on timeout; native operations are serialized; PnP discovery is independent of spooler hangs. Redacted exports omit all job metadata.

Start with the [hardware validation guide](docs/hardware-validation.md), [research sessions](docs/research-sessions.md), [capture correlation](docs/capture-correlation.md), [protocol research](docs/protocol-research.md), [fatal-error research targets](docs/fatal-error-research.md), [v1.2 audit](docs/v1.2-gap-analysis.md), [capture guide](docs/usb-capture.md), [protocol reference](docs/protocol-explorer.md), and [export/privacy specification](docs/export-schema.md).

## Offline evidence toolkit (1.3.0)

The Windows package now includes `discdiag-cli.exe` alongside the GUI. Use `discdiag-cli inspect bundle.zip --json`, `discdiag-cli analyze capture.pcapng --json`, or `discdiag-cli report bundle.zip --output report.html` without connecting hardware. The CLI has no USB transport dependency.

The **Evidence Toolkit** tab provides passive fingerprints, multi-session comparison, highlighted byte differences, entropy/variation profiles, a filterable timeline, persistent findings, a documented PP-100 reference subset and offline HTML reports. [CLI commands, exit codes, privacy and analysis limits](docs/evidence-toolkit.md) · [Release notes](docs/release-notes-1.3.0.md).

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
