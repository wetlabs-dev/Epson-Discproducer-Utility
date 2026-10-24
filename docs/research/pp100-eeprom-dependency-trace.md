# Original PP-100 error-history dependency trace and implementation specification

Date: 2026-10-24. Status: offline research; native transmission is **not authorized or implemented** by these findings.

## Decision

A fixed-purpose printer-history reader is technically plausible, but this archive does not supply a complete verified profile. Do not enable a live request using guessed parameters, default zeros, or fallback command probing. This investigation resolves the library call chain, configuration dependency and mode-selection mechanism; it does not certify the complete transport or the device firmware's behavior.

The concrete missing inputs are the applicable model configuration and an independently verified request/reply sequence. The original executable might generate configuration dynamically; static inspection of the ordinary imports and readable resources has not established that path. No demo execution, unpacking of protections or licensing modification was performed.

## Evidence provenance

Source: owner-supplied `PP-100_N_AP_DEMO.zip`, SHA-256 `39795a8fede14160bda673d0e36befdd4a4a196d95c687100ecfaca80689997d`.

| File | SHA-256 |
| --- | --- |
| CtrDev.dll | 61d0bc6a4543387a1e4ac8356736aeb157f105aee0a050b668dcbe03b2e64f32 |
| EprisDrvR.dll | beadc04b5527c1285e2d39eea5748fb25932452f7f8c1fc17593bb954405d23c |
| LoaderCtl10.dll | 666890a55100d9bad386616b5cbb908482b62f989734f85bb4f6bab28b5c90b5 |

Addresses below are RVAs within the identified DLL, not device memory addresses unless explicitly labeled. Static disassembly used the PE preferred base 0x10000000. Nearest-export labels printed by objdump for unnamed functions are not their true function names. Proprietary binaries, disassembly, and customer captures are not included in the repository.

## Configuration dependency: resolved source, unresolved values

`CtrDev!CEpris02Ini::SetINIFileName` (RVA 0x5370) selects the default `Epris02.ini`, `Epris02N.ini` for selector 1, and `Epris02AP.ini` for selector 2. The supplied default profile identifies PP-100. The constructor forwards that selector into the subsystem dispatch objects. The default printer-history branch is RVA 0x29010.

`CParamIni::SetInfFileName` (0x61F0) builds the model configuration path from the module directory, `Parameter\`, the profile, and `\Parameter.ini`. For the supplied original-model profile this is `Parameter\PP-100\Parameter.ini`.

A complete inventory of the ZIP's 79 entries contains **no Parameter.ini**. `Parameter/PP-100/CtrDev.ini` is a different 40-byte file containing a carriage-measurement setting, not the missing control profile.

The parameter descriptors identify:

- `[CONTROL] pnRNumber`: storage at RVA 0x44BF4, with count storage at 0x43804. A descriptor includes a `0 0` fallback string. This is not evidence of the correct device values.
- `[CONTROL] nADRSMax`: storage at 0x45FD0; controls single-byte versus extended addressing in the byte reader.
- `m_nNewEEPControl`: storage at 0x45FCC; reset during parameter loading and also modified by connection-result handling.

`GetParamAll` delegates to RVA 0x64B0, which attempts to open the parameter file before parsing it and can return early on failure. Consequently, neither descriptor defaults nor zero-initialized globals establish a valid original-model profile.

RVA 0x28680 copies the low bytes of the first two RNumber entries into the printer communication object. Those become the P0/P1 fields of the read message. We have identified where the parameters come from, but not their applicable values.

## Connection and mode selection

The original-model path is:

1. `CControlDevice::ConnectPrinter` (0x1820).
2. Model dispatch (0x263D0), default branch 0x28680.
3. Copy configured parameter bytes, then call 0x16110.
4. For interface selections 11 through 14, resolve/pass a device path to the string overload of `EprisDrvR!Open` (0xB2E0). Other interface selections use a different overload and must not silently substitute for this path.
5. `Open` opens the existing device path with read/write access, shared read/write, OPEN_EXISTING; checks device identification; loads the ECBTEG communication layer; and opens a transport session. Read/write handle access is required for request/reply transport and does not itself establish EEPROM-write behavior.
6. It opens channel 0x02, closes it, then attempts channel 0x40. Failure of the latter returns the special value 1. CtrDev converts that value into a successful connection while setting its new-EEP-control flag. Other success paths retain the alternate mode.

This explains selection of Ctl versus Prod reply routines. It does **not** justify using every channel failure as proof of device capability. Timeout, contention and transport failure must remain failures in a modern reader. Do not replicate this as automatic fallback probing.

`EprisDrvR!CBT_Init` (0x8190) dynamically resolves ECBTEG open/close, channel open/close, read/write, awake/sleep and service-name functions. The library names EPSON-CTRL, EPSON-STS and EPSON-DATA services. Its Ctl send/receive routine manages channels and response collection; the ten or eleven read bytes are application messages, not full USB packets.

`ECBTEG!ECBT_Open` calls a helper at RVA 0x3FF0 that constructs and transmits a 27-byte EJL/1284.4 entry sequence. Thus connection is not passive handle acquisition. A byte-for-byte search of the decoded payloads in the existing cap1 found no match for that entire sequence. Similar service-name negotiation in cap1 is insufficient to certify this exact initialization path. Fragmentation or an already-established session may explain absence; neither explanation is proven.

No complete certification of ECBTEG worker-thread behavior, recovery branches, awake/sleep semantics or cleanup effects is claimed. The smallest future implementation should reproduce only independently verified necessary session operations, not embed the entire legacy control stack.

## Printer read path and response handling

The traced call chain is:

`GetFatalErrorInfoPRN (0x2920) -> dispatch (0x26910) -> default history reader (0x29010) -> byte loop (0x28DC0) -> byte reader (0x15920) -> EprisDrvR reply routine`.

Static application-message templates are:

| Routine | Template |
| --- | --- |
| Ctl_ReplyEEP, 0xF7C0 | `7C 7C 06 00 P0 P1 41 BE A0 AL` |
| Ctl_ReplyEEP2, 0xF940 | `7C 7C 07 00 P0 P1 41 BE A0 AL AH` |
| Prod_ReplyEEP, 0xB630 | `1B 7C 00 06 00 P0 P1 41 BE A0 AL` |

P0/P1 are configuration bytes; AL/AH are address bytes. These are observations, not executable recipes. The original DLL selects the extended variant using the configured maximum address, not merely whether the individual address exceeds 0xFF. Neither format nor P0/P1 may be guessed.

The legacy parser searches for `EE`, locates a subsequent semicolon, and parses the preceding two characters as hexadecimal. This does not define a sufficiently strict response schema. Full framing, response correlation, timeout behavior and malformed data handling remain mandatory validation requirements.

The high-level history reader clears the output structure and ignores several intermediate read return values. Zero-valued fields may therefore represent failed reads. A replacement must retain per-read validity and never turn missing data into a zero error code.

## Record layout: observed addresses, tentative meanings

The selector-1 printer record reads these device-memory arguments:

| Address | Bytes | Structure offset |
| --- | ---: | --- |
| 0xF8 | 1 | 0x10 |
| 0x6C | 4 | 0x00 |
| 0x98 | 2 | 0x04 |
| 0x9A | 1 | 0x06 |
| 0x9B | 1 | 0x08 |
| 0x9C | 1 | 0x07 |
| 0x3C | 1 | 0x09 |
| 0xB8 | 2 | 0x0A |

Selectors 2-5 use bases 0x100, 0x110, 0x120 and 0x130, with fields at offsets 0, 4, 6, 7, 8, 9 and 10. Matching the English resource's displayed field ordering to the structure suggests latest code 0x3C, older codes 0x109/0x119/0x129/0x139, and a history-position field at 0xF8. These assignments are **hypotheses**, not independently confirmed device semantics. Do not present them as a documented EEPROM map or conflate them with offsets from a different manual/layout.

The auto-loader branch is independent: CtrDev 0xB080 -> helper 0xF660 -> LoaderCtl10 `ReadFSMemory` at 0x9330. It needs its own transport and response validation. Do not expand an initial printer-only implementation to the loader merely because both have error histories.

## Proposed implementation contract

This is a specification, not a new enabled command.

- Keep offline CLI commands hardware-free. Place any future hardware operation in the existing Windows native-worker boundary, with a dedicated fixed command ID and no caller-supplied request bytes or addresses.
- Use an immutable, reviewed PP-100 profile containing exact identity/firmware applicability, verified P0/P1, framing variant, necessary session sequence, allowed memory reads, response schema, provenance and validation state. Research-registry imports cannot grant transmit permission.
- Before opening a session, require a complete profile and an unambiguous selected device. Missing parameters produce `UnsupportedUnverified` without transport writes. No default profile substitution, address guessing or channel-failure mode inference.
- Use the installed Epson driver. No replacement, spooler print jobs, EEPROM-enable operations, service-mode transitions, resets, initialization of mechanisms, movement, cleaning or calibration. If the verified protocol cannot meet these constraints, stop; do not add a workaround.
- Prefer a freshly implemented minimal transport over calling mixed-purpose legacy DLLs. Do not redistribute these DLLs or load them into the x64 GUI. If native isolation is needed, reuse the existing bounded worker-process pattern.
- Once verified, begin with the smallest fixed read needed for the current-code candidate. Do not read all EEPROM or automatically progress to other addresses after an error. History expansion requires independently established ordering and wraparound semantics.
- Bound session duration and response sizes. No automatic retries or fallback protocols. Cancellation must terminate a blocked worker; any cleanup transmissions must themselves be verified and bounded. Preserve partial evidence without starting a new session.
- Log profile/version, selected device, actual request/response bytes subject to privacy mode, native return values, elapsed time, and each field's validity. Distinguish a successful transport read, a structurally valid reply, a tentative interpretation, and an independently confirmed fault code.
- Report a retrieved historical code as historical; it is not automatically the cause of the current fault.

### Required validation before enabling transmission

1. Complete configuration provenance, including the actual original-model parameter bytes and applicable firmware. A trustworthy configuration/reference can resolve this without running the demo GUI.
2. Independent confirmation of the exact session and request/reply sequence, including absence of forbidden setup/cleanup actions. A trace from an established read-only reader or authoritative protocol documentation can supply this; repeated generic status captures cannot.
3. Offline tests: exact approved byte framing; rejection of unknown profiles/identities; malformed, truncated, stale and unrelated replies; no zero substitution; bounded cancellation; no retry/fallback; privacy-mode logging.
4. Controlled physical verification, with every emitted message captured and audited, using the installed Epson driver. This has not happened. A successful DLL return is insufficient.
5. Windows Debug/Release builds, existing tests and GUI/worker smoke checks for the eventual code change.

## What is available now

The supplied archive resolves substantial implementation structure but cannot establish a complete safe live command. The missing configuration and independent transaction validation are concrete evidence requirements, not additional user permission requests. No hardware experiment, driver change or executable command has been added. Existing application safety gates remain intact.
