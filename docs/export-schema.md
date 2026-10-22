# Diagnostic export schema 2

The main export uses `BundleExporter` and application version 1.1.0. It writes manifest/summary, device identity/PnP/descriptors/interfaces/endpoints, printer queue/driver/status/jobs, communication JSON/text, capture analysis, application log, supported/failed operations and recommended-next-steps Markdown. Successful full diagnostics opens the save dialog; cancellation leaves accumulated observations available for manual export. No export is silently uploaded.

- **RedactedSummary**: aggregate transaction/interface/endpoint/capture counts and safe status/timing data; no raw payloads, serials, device paths, queue/document names or free-text errors.
- **TechnicalDiagnostics** (default): adds safe per-transaction timing, result codes, byte lengths, validity flags and decoded endpoint/capture metadata. Identifying strings and arbitrary binary payloads are omitted, not partially anonymized.
- **FullForensic**: original available structured metadata, job names, raw request/response files, free-text logs and exact imported PCAPNG bytes. Requires explicit confirmation in the UI, and the exporter refuses the mode without a confirmation flag.

The current device/inspection snapshot and full application-session transaction history are exported. Each full transaction retains its device identifier and simulated flag; earlier devices or simulations can therefore be present. Do not infer that all historical session transactions refer to the currently selected device. Summary/status results are observations, never speculative hardware diagnoses. Capture data is not automatically linked to the selected physical device.

The legacy six-file export API remains for compatibility/tests, but the UI uses schema 2. The original privacy checkbox is replaced by the three-mode selector. Copying a transaction/tree or explicitly saving an individual payload is unredacted and labeled accordingly. Free-text logs exist only in memory before explicit export.

Preferences at `%LOCALAPPDATA%\DiscproducerDiagnostics\preferences.json` store timeout, selected-device SHA-256 (not the raw instance ID), payload/direction display choices and last export/import directories. No serials, captures, raw payloads, job names or payload-search terms are persisted automatically.
