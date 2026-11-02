# Original PP-100 read-parameter candidate

Investigated 2026-11-02. Static/public-source research only; nothing executed against hardware.

## Finding

The strongest candidate is **P0 = 0x0A, P1 = 0x5E**, with **two-byte little-endian EEPROM addressing**. This is a model-specific third-party configuration, not a guess borrowed from another Epson printer and not yet a verified PP-100 hardware profile.

- [ReInkPy epson.toml](https://codeberg.org/atufi/reinkpy/src/commit/56af2c8277059f3ddccd9e4b9b51cfac65569531/reinkpy/epson.toml), PP-100 entry: integer `rkey = 0x5E0A`, `rlen = 2`. The PP-50 entry instead specifies `0xE6B5`; do not substitute it.
- [ReInkPy serializer and reader](https://codeberg.org/atufi/reinkpy/src/commit/56af2c8277059f3ddccd9e4b9b51cfac65569531/reinkpy/epson.py): `encode` packs the read parameter as little-endian unsigned short; `read_eeprom` uses `rlen` to choose the address width and packs the address little-endian. This resolves both byte order and what the length field means. No upstream code was executed.
- [ez-reset devices.xml](https://github.com/CiRIP/ez-reset/blob/13c02c441d147bb01e34b215b5aa824c9f6fa6f1/src/ez_reset/devices.xml): printer record maps `PP-100PRN` to `EPSON,PP-100`; the corresponding service block explicitly contains `factory` bytes `0x0A 0x5E` and `readlen` value `0x02`.

These sources agree, but their historical independence has not been established. They must not be counted as two independent hardware tests. Neither establishes applicability specifically to WLP00105 or supplies a successful PP-100 EEPROM transcript.

Downloaded-file SHA-256 values:

- ReInkPy TOML: `299b6b1466d64b82fcbde9b0585e96c0f2979730a184eca11da816d90b3f0d62`
- ez-reset XML: `10829cfe57752f79bae032e8f6e4d2490bd08185892502992ee285fdbb37553c`

## Why the earlier search missed this

The earlier inspected `epson_print_conf` source and merged EWR database did not supply a PP-100 profile. Following EWR's upstream references found one in ReInkPy. The inspected EWR `scripts/build_db.py` `extract_reinkpy` function skips entries without `wkey`; the upstream PP-100 entry contains `rkey` but no `wkey`. Absence from that merged reset database is therefore not evidence that no public read parameters exist. The previous statement that we lacked any sourced candidate is superseded by this finding.

## Relationship to the supplied DLL

The [existing static trace](pp100-eeprom-dependency-trace.md) shows `Ctl_ReplyEEP2` application framing:

`7C 7C 07 00 P0 P1 41 BE A0 AL AH`

Substituting the public profile gives the candidate template:

`7C 7C 07 00 0A 5E 41 BE A0 AL AH`

For the statically identified **candidate** latest-code address `0x003C`, the application message would be:

`7C 7C 07 00 0A 5E 41 BE A0 3C 00`

This is an offline protocol hypothesis, **not a complete USB transaction or a raw-write recipe**. It omits D4 channel negotiation, credits, transport framing and response handling. The word `factory` is the XML field name; it is not evidence that a factory reset is required. No write key, reset operation or paid application license key is needed to describe this candidate read.

## What this resolves and what remains

This narrows two unknowns to sourced candidates: the R-code bytes and address width. It does not certify the entire setup/cleanup path, distinguish all PP-100 reply variants, reconcile the history address map, or establish a component diagnosis. The application remains disabled for live history transmission; its readiness wording says parameters are unverified and should now be read alongside this follow-up.

A future authorized test should be a fixed single-byte read through a reviewed driver-preserving transport, with full capture, a bounded timeout and no automatic retry with alternate keys, modes or addresses. A successful addressed reply would validate that particular exchange—not every history address or the meaning of its value. A rejection/timeout would leave the result unknown, not justify a reset or brute-force search. Do not feed this application payload to a raw printer handle, print queue, generic USB sender or the existing offline JSON importer expecting it to transmit.
