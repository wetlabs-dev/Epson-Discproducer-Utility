# Research sessions (schema 1)

Import a capture in USB Capture. In Research, set a name, condition scenario and notes; Save session writes a JSON file with `SchemaVersion=1`, `SessionId`, creation UTC, application/Windows versions, model, driver, VID/PID, verified-source identity when available, simulation/hardware flags, capture references, associations and annotations. Firmware is unavailable until independently verified. Scenario and notes are user observations, never protocol facts.

Capture references contain Path, SHA-256 and byte size. Raw captures are not embedded or copied into session JSON. Session saves are atomic and limited to 4 MiB, 100 capture references and 10,000 annotations. Keep the external files; references may be absolute or relative to the session file. Moving evidence may require updating a reference path while retaining its hash. Network/URL references are not fetched automatically. Sessions themselves contain private identifiers/notes and are not privacy-redacted.

Open session restores annotations and metadata, clears the previous imported capture and does not modify live device state. Missing capture paths are reported but do not prevent opening. Compare sessions A/B asks for two session files, each with exactly one reference. It loads each capture only after size/hash verification. Missing or changed evidence blocks comparison with an explanation.

Comparison groups host operations by transfer type, endpoint, direction and exact setup packet, then aligns occurrences. It reports counts, lengths, completion errors, mean timing, repeated adjacent sequences and changed bytes (offset and old/new values). Caps are displayed in the report. Comparison includes all devices present in each referenced capture: prepare single-device captures externally when necessary. No application message boundaries or error-code meanings are inferred.

To compare individual payloads, select a packet in USB Capture, pin A in Research, select B, then compare. Annotations attach to the original capture hash and packet number. Export filtered records writes the visible (maximum 10,000) records, their capture hash and session observations, explicitly unredacted.
