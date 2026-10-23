# v1.3.0 — Evidence Analysis Toolkit

- Added standalone offline `discdiag-cli` with inspect, analyze, compare, report, validate and searchable PP-100 reference commands; documented exit codes and deterministic JSON.
- Added passive fingerprints, multi-session analysis, bounded byte profiles, entropy/frequency measurements and highlighted side-by-side differences.
- Added an interactive capture timeline with explicit user markers, timestamp offsets, filters, CSV/JSON export and original timestamps.
- Added versioned findings storage; no path from a hypothesis to executable USB dispatch.
- Added script-free, escaped standalone HTML reports respecting source privacy.
- Strengthened archive path, expansion, schema and raw-evidence hash checks without changing bundle export schema 3.
- Added parser-version provenance; legacy evidence is marked unspecified rather than assigned a new parser version retroactively.
- Preserved existing printer operations and all baseline tests. Added independent encapsulation rejection data and additional malformed-container, analysis and CLI tests.

No physical PP-100 validation, EEPROM operations, fatal-history retrieval, waste-counter query, driver modification or traffic replay is included. Reference codes are documentation, never live device findings. See evidence-toolkit.md for limits and exact commands.
