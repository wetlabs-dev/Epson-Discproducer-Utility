# Architecture

- **App**: WPF/Segoe UI shell, MVVM state, async commands, cancellation, manual device selection, mock toggle, export dialogs. No hardware requests occur on application launch.
- **Core**: immutable device/fact/status/exchange contracts, closed command registry, `IDeviceTransport` and `IPrinterProtocol`, mock transport. Every real command is selected by ID; arbitrary bytes/IOCTLs are not accepted.
- **Usb**: SetupAPI enumeration, Configuration Manager parent chains/problem codes, read-only driver metadata, local Epson candidate spooler queues, native USB printer IOCTLs, bounded worker-process client.
- **Protocols**: defensive IEEE 1284/descriptor parsers and documented status decoding. Parsing is separate from native I/O. The descriptor parser is tested but there is no claim that live descriptor retrieval is implemented.
- **Diagnostics**: Serilog session sink, full in-memory communication records, conservative export redaction and ZIP generation.
- **Tests**: portable xUnit tests exercise decoding, malformed input, identity matching, registry rejection, simulated errors/removal/timeouts/cancellation, redaction and archive contents.

## Worker and lifetime

The executable starts itself with `--worker`, exchanges one JSON request/reply through redirected stdin/stdout, and exits. No TCP listener or public IPC service is created. Requests are validated again in the worker. Device paths must match a currently enumerated Epson USB printer interface; unknown commands are rejected before IOCTL dispatch. Shared read-only handles are opened and disposed for each operation.

Native SetupAPI, spooler and printer-driver calls can block. They therefore run outside the WPF process. The parent uses asynchronous process I/O and a deadline, and terminates a timed-out/cancelled worker. Buffers remain alive in that worker while synchronous DeviceIoControl runs; no overlapped buffer is freed prematurely. This bounds UI waits but cannot guarantee how a defective kernel driver completes internal cleanup after process termination. No background retries occur. The user can explicitly refresh an idempotent query.

Discovery/probe deadlines are 20/15 seconds. Query deadlines are configurable (0.1–60 seconds) and include worker startup and re-enumeration overhead. Slow systems may need a longer deadline. Connect is a logical session with an access probe, not a permanently held device handle. There are no elevation-requiring operations implemented; access denied is reported.

## Provenance and evidence

Each displayed identity fact has a source and availability. Each status has source, timestamp, raw value, interpretation and availability/error. USB identifiers and interface numbers extracted from PnP strings are labeled metadata, never raw USB descriptors. Unknown serial and firmware remain Unavailable. Spooler candidate queue results are displayed separately and **not correlated to the selected physical device**; names/ports alone are insufficient to establish that link. Parent relationships correlate PnP nodes with the exact interface devnode; they do not fabricate queue ownership.

No ready-state arbitration hides contradictions. USB class bits cannot establish PP-100 robotics, disc burner, ink or waste counter status. A successful DeviceIoControl reply may be driver-cached; proving a physical control transaction requires a separate trace.

## Logging and privacy

Logs are memory-only until export, avoiding a persistent unredacted file on disk. They accumulate for the current application session (including earlier device selections). Exports contain current device facts/status and the full session communication log. No credentials, environment variables, host/user names or unrelated printer inventory are collected. System metadata is limited to OS/runtime/architecture/app version/time. Redaction deliberately sacrifices raw forensic content: complete payloads, identity fields and free-form text are omitted rather than attempting incomplete serial-pattern replacement. Local display and clipboard remain unredacted. Export before closing; logs are not recovered after a crash.

Future transport/protocol implementations must go through the closed registry and receive separate review; adding a protocol is not permission to add writes. WinUSB is deliberately not implemented against driver-owned interfaces. No placeholder service controls are exposed.
