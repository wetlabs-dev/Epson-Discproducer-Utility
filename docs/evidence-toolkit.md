# Evidence Analysis Toolkit — v1.3.0

The toolkit reuses `UsbCaptureImporter`, `CaptureAnalysis`, `ResearchSessionStore`, `BundleReader` and the existing transaction recorder. It adds no native APIs, printer commands, driver changes or network dependencies. GUI inspection remains available; `discdiag-cli` references Diagnostics/Core/Protocols only, never Usb or WPF.

## CLI

Extract the Windows x64 package and open PowerShell in its directory:

```powershell
.\discdiag-cli.exe inspect .\diagnostics.zip --json
.\discdiag-cli.exe validate .\diagnostics.zip --json
.\discdiag-cli.exe analyze .\capture.pcapng --json
.\discdiag-cli.exe compare .\ready.json .\error.json .\repeat.json --json
.\discdiag-cli.exe report .\diagnostics.zip --output .\report.html
.\discdiag-cli.exe errors 0x01 --json
```

Omit `--json` for indented human-readable output. Compare accepts 2–8 captures, saved research sessions or forensic bundles containing original PCAPNG; each session can reference local hashed capture files. Total expanded capture count cannot exceed eight, with a 256 MiB aggregate input budget (each ZIP reserves 128 MiB). A missing capture or mismatched hash blocks analysis. Captures are never replayed. `errors` searches a documented reference subset; it never queries the device.

Stable process exit codes:

| Code | Meaning |
|---|---|
| 0 | Completed successfully |
| 2 | Usage/argument error; unsupported command |
| 3 | Invalid evidence, schema, hash, archive or analysis limit |
| 4 | File missing, inaccessible, or output I/O failure |
| 5 | Analysis/report produced, but capture parsing/correlation is incomplete; inspect warnings |
| 6 | Unexpected internal failure |
| 130 | Cancelled |

Errors go to stderr. Successful JSON goes to stdout; incomplete analyses still produce JSON with exit 5. No wall-clock generation timestamps or random IDs are added to analysis/report output. Determinism applies to identical inputs, their input order and the same toolkit version. Findings created interactively receive an analyst-generated ID and are separate from deterministic analysis.

## Analysis and byte comparison

Import PCAPNG in USB Capture. Use Evidence Toolkit → Analysis to fingerprint the capture, compare multiple saved sessions, or profile the currently filtered payloads. To view a side-by-side diff, pin packet A using Research, select packet B in USB Capture, then choose Diff in the toolkit. Changed or missing bytes are highlighted with hexadecimal offsets.

Fingerprints group by capture section/interface/bus/address, transport/type, endpoint/direction, exact control setup, payload length and a two-byte bulk/payload prefix. Prefix grouping is a hypothesis. The displayed common prefix is computed from sampled payloads, not assumed. Timing uses matched host completion records only. Repeated adjacent operation sequences may be candidates for a status refresh only when supported by user observations.

Byte profiles report input lengths, non-overlapping common prefix/suffix, byte-value counts, missing sample counts, Shannon entropy in bits, adjacent-sample change frequency, and candidate stable/variable-region boundaries. Two differing observations do not identify a field meaning or establish independent reproduction. One sample is explicitly labeled. Cross-session comparison retains each capture's context; matching frame signatures never establish physical identity or synchronized clocks.

Bounds: 128 fingerprint groups/sequences, first 32 samples per fingerprint or comparison group, 1024 byte offsets per profile; direct filtered profile uses at most 512 payload samples. Full prefixes/suffixes and lengths are measured, but offsets beyond the displayed range are explicitly marked truncated. Export JSON preserves the bounded analysis; GUI previews are capped at 131072 characters. Limits are not claims that remaining evidence was inspected.

## Timeline

Build the timeline from the imported capture. Filter exact bus:address, endpoint, event type and ISO timestamps including UTC offset. Click plotted points or sortable rows to inspect original/aligned times and provenance. Blue denotes captured host traffic, orange user markers, red failures/errors. The first 10000 filtered events are displayed and exported.

A global offset changes displayed times only. Markers have their own offsets to align an external clock; original timestamps remain unchanged. Offsets are finite and limited to ten years. Add Status refresh, Device disconnection, Printer error or User marker observations explicitly. Missing URB completion does not establish a physical disconnection. Section starts become session boundaries. Research annotations remain user observations, using annotation creation time unless aligned manually.

Export CSV/JSON for the displayed filtered timeline. CSV quotes text and neutralizes leading spreadsheet formula characters. Save a findings registry to persist markers and their capture hashes; a marker for another capture is never silently applied.

## Findings and reference

Findings are a separate, versioned JSON registry (`SchemaVersion=1`): ID, description, model context, capture/session/source references, transaction IDs, offsets/values, confidence, status and notes. Status values are Observation, Hypothesis, IndependentlyReproduced, DocumentedProtocolBehavior and RejectedHypothesis. Reproduced/documented assertions require references, but remain analyst assertions rather than automatic certification. Saving/updating never registers a command. Import replaces the displayed registry after validation; export persists it atomically. Limits: 4 MiB, 10000 findings and 10000 markers. These files can contain private evidence and are explicitly exported, not automatically uploaded.

The PP-100 reference contains seven selected mappings from the original model's Epson service manual 411384703 Rev. D, printer mechanism table pp. 3-23–3-27. The [manufacturer-authored document is available through a third-party host](https://www.scribd.com/document/808984453/PP-100-eng-sm-revD); each entry records that source status and page. Descriptions are short paraphrases; the manual itself is not redistributed. The list is intentionally incomplete and excludes ambiguous OCR entries. Do not borrow PP-100II/III codes, confuse these with Windows errors, or search arbitrary captured bytes for apparent matches. Inspections recommended by this utility are non-destructive visual checks and referral to qualified service; no adjustment, movement or reset instructions are supplied.

## Reports and privacy

Generate HTML from a selected bundle using the GUI or CLI. Reports contain the retained identity/provenance, topology, driver/spooler data, methods, queries/errors, capture metadata, observations and next steps. Forensic captures can supply fingerprints; repeated forensic driver replies supply byte comparisons. Absent or redacted data stays unavailable. Reports do not mix in currently selected live-device state or unrelated captures.

HTML is standalone, script-free and text-escaped, with a restrictive Content Security Policy. Imported Markdown/HTML is rendered as text, never executed. No CDNs, remote images, scripts or external capture fetches are used. Per-source document previews are bounded to 65536 characters; retain original bundles. Forensic reports remain private and may contain identifiers/payloads.

Existing bundle schemas 2 and 3 remain supported and export schema remains 3. Archive validation rejects traversal, absolute/drive/ADS paths, duplicate names, raw payloads in privacy-safe modes, missing schema-3 raw hashes, mismatches, invalid JSON, excessive expansion ratios and size/count violations. Maximum expanded/compressed archive size is 256 MiB; per-entry limit 128 MiB; JSON validation limit 16 MiB; compression ratio over 1000:1 is rejected for entries over 1 MiB. Hash checks establish consistency, not authenticated authorship. Legacy bundles may lack an index and say so. No archive is extracted or used to launch code.
