# Passive USBPcap / Wireshark workflow

The app can import and analyze USBPcap **PCAPNG** files without Wireshark or TShark installed. It detects common Wireshark/TShark/USBPcap executable locations and the registered USBPcap service using read-only filesystem/registry inspection. Detection is best-effort (custom install locations may not be found); a registered service is not proof that capture is running. There are no automatic installations or live-capture controls.

## Capture one controlled observation

1. Record the device's parent, location path and port in **USB Interfaces**. Use Wireshark/USBPcap's device/root-hub listing to identify the corresponding capture interface; the Windows hub connection index is not necessarily the capture's USB device address.
2. Install/configure USBPcap only if you separately choose to do so through its official distribution and your administrator's policy. Capture access may require elevation; the diagnostics app itself does not elevate or install a filter driver.
3. Explicitly start the appropriate USBPcap capture in Wireshark before opening or refreshing Epson Total Disc Monitor. Avoid collecting unrelated USB devices where possible.
4. Perform **one normal status refresh** in Epson's software. Do not invoke cleaning, publishing, service resets or other maintenance while collecting this baseline.
5. Stop capture and save **PCAPNG**. If USBPcapCMD wrote classic PCAP, open it in Wireshark and Save As PCAPNG. Export Enhanced Packet Blocks for timestamped analysis.
6. Import the file in the app's **USB Capture** tab. Filter by bus:address, endpoint (`0x81` format), direction, transfer type, ISO timestamp range and payload (literal UTF-8 text or `hex: 80 06`). Imported packets have no automatic association with the currently selected Epson device.
7. Select a record to inspect setup, payload, status and URB correlation. Select an application transaction in **Communication Log**, return to the capture tab and click **Compare**. The comparison is observational: it does not replay, inject or synthesize commands.

## What is parsed

The built-in parser handles PCAPNG Section Header Blocks, Interface Description Blocks (decimal/binary timestamp resolution and signed time offset), and Enhanced Packet Blocks with USBPcap link type **249**. Both PCAPNG byte orders and multiple sections are supported; USBPcap's packed pseudoheaders always use little endian. Control stages before and after USBPcap 1.5 are recognized. Other link types and legacy/simple packet blocks are skipped with warnings, not silently interpreted as USB.

Records expose UTC time (rounded to .NET's 100 ns resolution), bus/address, endpoint, direction, transfer type, URB ID, control stage/setup, payload and native completion status. USB interface number is available only when a setup packet explicitly addresses an interface; it is otherwise unknown. Setup/data/completion records are paired within the same section/capture interface/bus/address/endpoint/transfer/URB identity. Reused active URB IDs are marked ambiguous; missing requests/completions remain explicit. Unknown or truncated bytes are retained in the original imported file.

USBPcap represents **host-controller requests and completions**, not proof of an electrical USB ACK or a specific successful hardware action. A status of zero on a request is not a successful completion. Bulk endpoint-to-interface mapping is not guessed. Endpoint zero control transfers are not bulk writes. Windows IOCTL input buffers should not be compared as if they were captured USB payloads.

## Bounds and privacy

Imports are limited to 128 MiB, individual blocks to 32 MiB, and parsed records to 100,000. Filtered UI rendering shows at most 10,000 records; hex previews show at most 16 KiB. Parsing/filtering runs off the UI thread with cancellation checks. Split or filter larger files in Wireshark. The exact original bytes and SHA-256 are retained in memory and included only in an explicitly confirmed FullForensic bundle. Full capture preservation is independent of parser completeness.

Captures may contain print jobs, filenames, disc metadata, serials and unrelated USB traffic. RedactedSummary and TechnicalDiagnostics never include raw capture payloads or the original file. Binary payloads are **omitted**, not claimed to have been safely anonymized. Individual copy/binary-save actions are clearly unredacted.

Primary format references:

- [USBPcap packed format and control stages](https://github.com/desowin/usbpcap/blob/master/USBPcapDriver/include/USBPcap.h)
- [USBPcap project](https://github.com/desowin/usbpcap)
- [PCAPNG format work in progress, revision 05](https://www.ietf.org/archive/id/draft-ietf-opsawg-pcapng-05.html)
- [Wireshark User's Guide](https://www.wireshark.org/docs/wsug_html_chunked/)
