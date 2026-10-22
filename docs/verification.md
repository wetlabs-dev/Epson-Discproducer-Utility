# Verification

The local Release cross-build passed with zero warnings/errors using .NET SDK 10.0.402; all 51 tests passed. Automated checks cover portable parsing, authorization, mocked error handling, export redaction, and actual child-process IPC, timeouts, cancellation and crashes. The Windows workflow additionally builds Debug/Release, publishes the self-contained x64 app and opens/closes its WPF window, executes native Windows enumeration through the packaged worker, and exercises all tabs plus the simulated identification/status path. Consult the [Windows workflow](https://github.com/wetlabs-dev/Epson-Discproducer-Utility/actions/workflows/windows.yml) for the exact commit and result. A successful CI build does not prove physical PP-100 compatibility.

## Hardware acceptance checklist (not yet executed)

- [ ] Launch the published package on Windows 11 x64 as a standard user.
- [ ] Record PP-100 model/firmware and installed Epson driver/provider/version.
- [ ] Compare discovered VID/PID, interface path and device instance ID with Device Manager.
- [ ] If multiple devices/interfaces exist, verify the selected node/parent relationship.
- [ ] Obtain IEEE 1284 bytes, parse model/serial, and independently compare the serial label when exposed.
- [ ] Obtain a four-byte printer status reply; compare documented class bits with supported software without inducing unsafe device errors.
- [ ] Verify access-denied/unsupported handling if the driver does not expose these IOCTLs.
- [ ] Disconnect the idle printer, refresh, verify explicit removal and reconnect handling.
- [ ] Cancel a query and confirm the application remains responsive.
- [ ] Export both redacted and intentionally unredacted bundles; confirm identifiers do not survive redaction.
- [ ] Confirm normal Total Disc Maker operation after diagnostics and unchanged driver/configuration.

Record failures as failures, including exact Windows error codes. Never substitute a mock response to satisfy a hardware checklist item. A valid driver reply is meaningful request/response evidence; proving traffic on the physical USB bus requires separate trace validation because the driver may cache data.


Version 1.1 synthetic tests include descriptor framing/decoding, endpoint direction/type, unknown-command rejection, queue/container ambiguity, PCAPNG byte order and truncation, USBPcap control stages/URB pairing, section separation, hex display bounds, centralized recording, export confirmation and payload/identifier omission. Tests do not certify the native hub/spooler ABI against a physical PP-100. Complete [the first hardware test](first-hardware-test.md) to validate hub access, actual endpoint values, Epson queue association and query responses.
