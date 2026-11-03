# Driver-preserving transport foundation

Implemented 2026-11-03. Physical PP-100 validation remains outstanding. Version 1.3.2 enables one fixed, experimental D4 status qualification. EEPROM requests remain disabled.

## What can be tested on Windows 11

1. Extract the Windows package and start Discproducer Diagnostics with simulation off.
2. Scan and select the original `EPSON PP-100PRN` printer interface (`04B8:0007`, not HID, a disc drive, or a queue-only entry).
3. Open **Evidence Toolkit → Analysis → Probe exclusive access (no payload)**.
4. Save the analysis result and diagnostic bundle. On failure, retain the Communication Log's native Windows error and message.

The probe re-enumerates the path, opens the existing printer interface with read/write access, zero sharing and overlapped I/O, then closes it. It sends no bulk payload or protocol command, does not stop services and does not alter drivers. The existing worker process limits the duration of blocked native calls. Error 32 (sharing violation) is a useful result: another handle prevents exclusive access. Do not replace drivers or force a shared fallback to bypass it.

An acquired handle establishes Windows share-mode exclusion during that handle's lifetime only. The probe releases the handle immediately. It neither reserves access for a later request nor proves that printer firmware has no stale D4 session. Existing Epson components may also use other interfaces. Therefore the UI explicitly distinguishes exclusive interface access from clean-session ownership. We still need physical testing with Epson software present and absent to establish the applicable behavior.

## Internal transport and session engine

`UsbPrintStream` uses the installed printer interface with `FILE_FLAG_OVERLAPPED`; async I/O accepts cancellation tokens. Exclusive opening fails closed. It provides no public arbitrary-byte API. The fixed live qualification operation retains the same exclusive handle throughout setup, request, response and cleanup; a separate probe is not authorization to send.

`D4Session` is a single-use, fixed **status-only** qualification exchange based on cap2. It performs packet entry, revision-10 Init, EPSON-CTRL lookup, channel open, bounded credit requests, one captured status request, receive-credit grants and channel close. No EEPROM payload, caller-selected command, key, address, reset, D4 Exit, fallback revision or replay is supported. It is now reachable through the fixed `d4-status-qualification` worker operation and its GUI button. Imported evidence cannot invoke it.

The engine distinguishes command/send/receive credits, waits for nonzero send credit, and handles an authorized data packet preceding the receive-credit acknowledgement. It reassembles split packets and retains coalesced packets. Packet sizes, bytes, fragments, credit polling and overall duration are bounded. A complete reply requires the message-end flag; timeout is not a substitute. Raw incoming chunks and attempted/completed outgoing writes are retained in its result.

Channel close is attempted only for an acknowledged open channel while the stream state is trustworthy and the deadline remains valid. On cancellation, a malformed packet, disconnect or uncertainty, it disposes the stream without adding protocol traffic. A close failure is reported separately from preserved response data. Disposal releases the OS handle; it is not asserted to reset or restore firmware state.

Windows cancellation is cooperative: [Microsoft documents that drivers may not honor cancellation](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations). The native D4 invocation runs within the existing isolated worker: an 8-second session budget and a 15-second outer hard deadline. User cancellation terminates that worker immediately; channel close is then unconfirmed. This boundary also protects the exclusive-open probe. A disposal or driver hang cannot be solved merely by passing a token.

## Remaining validation

- Exercise exclusive opening on the real printer and record Windows results with Total Disc Monitor/Control present. An exclusive success alone does not establish clean firmware session state.
- Verify overlapped cancellation and handle release on physical disconnect and contention without changing drivers.
- Run the fixed status-only qualification once; compare its transcript and cleanup against cap2 and Epson software behavior.
- Only then consider the sourced candidate EEPROM read. The history address map and first real EEPROM response remain unverified.

Automated tests use simulated byte streams, not printer hardware. They check split/coalesced/early replies, zero credits, deadlines, cancellation, malformed lengths, wrong sockets, cleanup failure and rejection of EEPROM and arbitrary-command authorization. No hardware compatibility claim follows from those tests.

## Run one status-only qualification (v1.3.2)

Start USBPcap capture for the PP-100 printer interface if available. Select the original PP-100 printer interface, then click **Evidence Toolkit → Analysis → Run one D4 status qualification** once. The operation acquires its own exclusive handle; a prior probe is not reused. No connection or full-diagnostics run is required first. The button is disabled in simulation and while another application operation is running.

Wait for the result. **Complete** means a complete D4-framed response, not verified printer health. Review **Error**, **Cleanup**, **Response**, and **Trace**. Export the analysis JSON and a FullForensic diagnostic bundle if you want to preserve unredacted communication bytes; TechnicalDiagnostics may omit payloads. Stop and save the USB capture. Confirm Total Disc Monitor can still refresh afterward. If the operation fails, do not repeatedly retry; retain the first result for analysis. The app does not automatically stop services, retry the status query, change drivers, issue D4 Exit or reset the USB device.

On timeout/cancellation, the UI explicitly reports that cleanup is unconfirmed. Raw progress already delivered remains in the Communication Log and bundle; bytes never delivered by the driver cannot be recovered. The status operation is experimental pending this physical validation. It does not request stored EEPROM history or decode a component error.
