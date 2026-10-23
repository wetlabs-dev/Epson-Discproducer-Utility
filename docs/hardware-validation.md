# Windows 11 first hardware test

1. Download the `DiscproducerDiagnostics-win-x64` artifact from the successful GitHub Actions run for this commit. Extract the artifact ZIP, then extract the application ZIP inside it. Run `DiscproducerDiagnostics.App.exe` from the extracted folder. It is self-contained; no separate .NET installation is needed.
2. Leave the installed Epson driver in place. Connect and power on the intended PP-100. Keep it idle and avoid simultaneous diagnostics in other programs.
3. Turn **Simulation mode off**. Set a timeout (10 seconds initially), then **Scan for Devices**. Identify the intended unit by its Windows instance and USB Interfaces topology, especially when identical printers are present. Select its printer interface if exposed.
4. Open **Hardware Validation** and choose **Run hardware validation and export**. It checks presence, descriptors/driver metadata, printer queue association, handle access, IEEE 1284 identity and printer-class status. Every completed step is displayed. Failures do not stop unrelated subsequent steps.
5. Inspect the separate milestones. A handle opening is not a successful device response. A driver reply may be cached. Host capture records do not prove a physical bus acknowledgement. No model-specific Epson status is decoded.
6. Save a **TechnicalDiagnostics** bundle. For detailed private investigation, explicitly choose and confirm **FullForensic**. Only forensic bundles include raw payloads, original captures, identifiers, job metadata and user notes.
7. If a call hangs, use **Cancel**. Its worker is terminated; completed streamed hub transactions remain in Communication Log. Export manually after cancellation. No retry is automatic.
8. Optionally capture one status refresh using external USBPcap/Wireshark and Epson Total Disc Monitor, then import the PCAPNG in **USB Capture**. Do not print or initiate maintenance for this test.
9. In **Research**, name the session, label the observed condition, add notes and save it alongside the capture. See research-sessions.md. Send the diagnostic ZIP and, if intentionally sharing raw evidence, the session plus referenced capture together.

## Recovery

AccessDenied: close conflicting tools and check Windows permissions. DeviceDisconnected/DeviceNotFound: rescan and select the same intended device. UnsupportedTransport/DriverRejectedRequest: preserve Epson's driver and collect passive evidence. Timeout: retain completed results and check cables/printer state before a manual retry. SpoolerUnavailable: check the spooler separately; PnP scanning does not wait for it. InvalidResponse/MalformedDescriptor: preserve raw bytes; do not assume printer damage.

Hardware compatibility, descriptor permissions, queue association and Epson IOCTL support require real PP-100 testing. Hosted Windows CI only validates software packaging and simulated GUI workflows.
