# v1.3.1 — Observed Windows response compatibility

A PP-100 driver query returned a single status byte, but v1.3.0 required exactly four bytes and labeled it malformed. v1.3.1 accepts exactly one or four returned bytes, preserves them unchanged and interprets only the low-byte printer-class flags. It still requests Microsoft's documented four-byte output buffer. Zero-, two-, three- and oversized replies remain invalid. API failures remain failures even when bytes are present. A valid response reporting an error is not printer readiness.

A readable identity with invalid length framing now records `ResponseWarning`, `ValidResponse=false`, and `Interpreted=true` only when printable, complete, non-duplicate manufacturer/model fields can be recovered. Raw bytes are never repaired. Identity facts remain marked unverified; incomplete/non-ASCII/otherwise malformed replies remain invalid. Historical imports keep their original outcomes. Export schema stays 3; consumers should retain unknown outcome strings. The connection notice now distinguishes decoded status from identity warnings.

The [cap1 analysis](research/cap1-analysis.md) records 41 identical ST2 responses and a cross-model generic fatal-error hypothesis. No specific mechanism fault or numeric service code is established. No new hardware commands, driver changes, USB writes, resets, EEPROM access or replay were added.

## Retest on Windows 11

Extract the new Windows package into a separate folder. Keep the original exports. Select the same PP-100 USB printer interface with Simulation off, click Connect once, and inspect Communication Log and Status. If the replies recur, expect `ResponseWarning` for `6B 6B` identity framing and `Success` with raw `10` / generic error asserted for the one-byte status reply. This is successful interpretation of an error indication, not a repaired printer. Export the new bundle for comparison.
