# Verification

The local Debug and Release cross-builds passed with zero warnings/errors using .NET SDK 10.0.402; all 95 tests passed. Automated checks cover portable parsing, authorization, mocked error handling, export redaction, and actual child-process IPC, timeouts, cancellation and crashes. The Windows workflow additionally builds Debug/Release, publishes the self-contained x64 app and opens/closes its WPF window, executes native Windows enumeration through the packaged worker, and exercises all tabs plus the simulated identification/status path. Consult the [Windows workflow](https://github.com/wetlabs-dev/Epson-Discproducer-Utility/actions/workflows/windows.yml) for the exact commit and result. A successful CI build does not prove physical PP-100 compatibility.

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


v1.2 adds independent USBPcap/TShark fixture comparison, conservative reconstruction/association, baseline byte differences, research-session schema/hash/missing-file tests, schema 3 export/import and privacy checks, unsafe ZIP rejection, streamed worker timeout preservation, repeated worker lifecycle, central native authorization and malformed reply validation. Two tests exercise Windows-only stale paths/authorization and return early on non-Windows hosts; Windows CI is required for their native assertions. The packaged GUI smoke test also visits nested offline-view tabs populated from an exported simulated bundle.

Known limits: no physical PP-100 test; no automatic identity across identical units/address reuse; no capture replay; no vendor fatal-history or waste-ink query; model modules are analysis-only. Offline redacted bundles cannot reconstruct omitted bytes. Capture comparison is conservative occurrence alignment, not a validated Epson protocol decoder.


v1.3 local validation: 141 portable tests passed, preserving all 95 baseline tests. Debug and Release cross-builds passed with zero warnings/errors; actual CLI process checks passed for inspect, analyze, compare, report, validate and errors. Windows validation of this revision is pending; the workflow now also runs all packaged CLI verbs and populated toolkit GUI views. The added Wireshark GPL-licensed Linux USB fixture verifies unsupported-link handling, not USBPcap decoding; the existing USBPcap fixture retains its independent TShark comparison. No new fixture is presented as PP-100 hardware evidence.
