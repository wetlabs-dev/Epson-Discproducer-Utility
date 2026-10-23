# Diagnostic export schema 3

The main export uses `BundleExporter` and application version 1.3.1. It writes manifest/summary, device identity/PnP/descriptors/interfaces/endpoints, printer queue/driver/status/jobs, communication JSON/text, capture analysis, application log, supported/failed operations and recommended-next-steps Markdown. Successful full diagnostics opens the save dialog; cancellation leaves accumulated observations available for manual export. No export is silently uploaded.

- **RedactedSummary**: aggregate transaction/interface/endpoint/capture counts and safe status/timing data; no raw payloads, serials, device paths, queue/document names or free-text errors.
- **TechnicalDiagnostics** (default): adds safe per-transaction timing, result codes, byte lengths, validity flags and decoded endpoint/capture metadata. Identifying strings and arbitrary binary payloads are omitted, not partially anonymized.
- **FullForensic**: original available structured metadata, job names, raw request/response files, free-text logs and exact imported PCAPNG bytes. Requires explicit confirmation in the UI, and the exporter refuses the mode without a confirmation flag.

The current device/inspection snapshot and full application-session transaction history are exported. Each full transaction retains its device identifier and simulated flag; earlier devices or simulations can therefore be present. Do not infer that all historical session transactions refer to the currently selected device. Summary/status results are observations, never speculative hardware diagnoses. Capture data is not automatically linked to the selected physical device.

The legacy six-file export API remains for compatibility/tests, but the UI uses schema 3. The original privacy checkbox is replaced by the three-mode selector. Copying a transaction/tree or explicitly saving an individual payload is unredacted and labeled accordingly. Free-text logs exist only in memory before explicit export.

Preferences at `%LOCALAPPDATA%\DiscproducerDiagnostics\preferences.json` store timeout, selected-device SHA-256 (not the raw instance ID), payload/direction display choices and last export/import directories. No serials, captures, raw payloads, job names or payload-search terms are persisted automatically.


Schema 3 adds a stable research SessionId, hardware verification status, imported capture hash, raw-response availability and structured error categories/HRESULTs. `evidence-sha256.json` indexes every raw request/response file and original capture in forensic mode. `research/session.json` contains research metadata only in forensic mode; other modes retain only session ID and an omission notice. No job metadata (including timestamps/pages/size) is included in either redacted mode.

Offline Evidence accepts schema 2 or 3. It checks archive names, duplicates, expanded size (256 MiB total / 128 MiB per entry), count (10,000) and available raw hashes without extraction. All retained documents can be reviewed; forensic identity/topology/transaction tabs reconstruct supported views. Missing redacted data remains unavailable. Imported views never replace live device state. Text documents over 16 MiB are explicitly omitted from display. Hashes detect accidental changes, not authenticated authorship.

`ResponseWarning` indicates recoverable identity text with unverified framing: `ValidResponse=false`, `Interpreted=true`. It is distinct from a fully valid response and preserves all original bytes in forensic mode. Historical imports are not reclassified.
