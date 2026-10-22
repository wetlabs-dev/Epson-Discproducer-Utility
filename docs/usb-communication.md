# USB communication and driver ownership

Discovery enumerates all present Windows PnP device nodes and their ancestor chains. Epson matching requires an actual `VID_04B8` token; any PID is accepted. USB printer interfaces use `GUID_DEVINTERFACE_USBPRINT` (`28d78fad-5a12-11d1-ae5b-0000f803a8c2`) and are matched to their exact instance ID. Composite interfaces remain separate selectable entries. Disabled/problem nodes can be listed with a problem code even when no interface opens. Disconnected nodes disappear on the next scan/refresh.

The transport opens the interface with `GENERIC_READ`, shared read/write access, and `OPEN_EXISTING`. It does not request write access. If a driver rejects this access mode, the utility reports the Windows error; it does not broaden permissions, replace drivers or fall back to sending printer-language bytes. An access probe closes immediately. Queries revalidate the selected path before dispatch.

Two Microsoft-documented USB printer IOCTLs provide identity/status when the installed driver exposes them. Success is shown as a **driver request/response**, not an unrestricted vendor endpoint channel. The returned byte count and response are logged. Empty/truncated responses do not count as a valid identity/status result. USB class status does not prove the PP-100 can publish discs.

The Windows spooler is a separate source. Candidate Epson queues are listed as uncorrelated, because this implementation cannot establish a verified queue-to-USB mapping. Queue names, matching driver names and `USB001` are not used as physical identity. No `OpenPrinter` handle is repurposed as a USB control channel.

## When the installed driver blocks access

Export the failing IOCTL's error code together with the exact interface path, driver provider/version, hardware/compatible IDs and parent chain (use an unredacted bundle only when appropriate). Failure can mean access policy, unsupported operation, a different driver stack, or a removed device; this app cannot determine the cause merely from a queue name.

Safe next steps:

1. Confirm the same printer works in Epson's supported software and identify which devnode actually exposes the printer interface.
2. Run the existing two queries on that interface and retain successful raw bytes, timestamps and actual returned lengths.
3. If unavailable, investigate Epson's supported SDK/bidirectional monitor interface, with read-only documented calls and permission/ABI requirements. Keep that integration separate from printer-language job submission.
4. With owner authorization on a non-production unit, capture known-safe identity/status traffic from supported software. Correlate requests with replies; distinguish cached metadata from real USB transactions.
5. Review any additional read-only command and its driver requirements before adding it to the registry.

Do not replace the driver with WinUSB/libusbK or issue arbitrary ESC/P commands. Raw descriptor retrieval through USB hub IOCTLs could be added separately after careful topology matching; it is not implemented here. The bundle explicitly marks descriptor bytes unavailable. A future WinUSB transport is appropriate only for a separately exposed, already-compatible interface that leaves Epson's publishing interface intact.
