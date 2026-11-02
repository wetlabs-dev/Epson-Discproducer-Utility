# Installed Total Disc Maker evidence analysis

Completed 2026-11-02. Offline inspection only; no supplied Windows executable or DLL was executed, no printer requests were sent, and no installation files were modified.

## Version evidence

Owner-provided screenshots show Total Disc Monitor 6.00, loader firmware WLA00105, printer PP-100PRN WLP00105, and both DVR-112D PP100 drives at 1.10. These are software-displayed versions, not independently authenticated firmware images. The Monitor screenshot also shows partially obscured communication-error text; it must not be treated as the same observation as the earlier internal-printer-error screenshot.

The copied installation contains x64 LoaderCtl64.dll and PubCtl64.dll, unlike the demo's older 32-bit libraries. No Parameter.ini was found in the supplied directory.

| File | SHA-256 |
| --- | --- |
| PubCtl64.dll | 191654d944bee52d4832d837ade661ec85c9d4804b2b326386134ed8e0fb4252 |
| LoaderCtl64.dll | 03de67a3bb422a9c64db37a56ba3663ac0e5e08f16055f62e70a05e71f209e64 |

## Status-bit trace

PubCtl64.dll exports AnalyzeASBStatus at RVA 0x72FF0 and CheckASB at RVA 0x3220. CheckASB selects byte index `bit / 8` and tests `bit % 8`. In AnalyzeASBStatus, RVA 0x73675 selects bit 0x28 (decimal 40). When set, the branch at 0x7369A–0x736AE adds internal factor 0x04000000.

Thus the sixth-byte 80-to-81 transition in cap2 maps to a specific internal status flag. NotifySpecificFactor, around RVA 0x4CC8A–0x4CCD5, detects this factor's rising edge and invokes a callback with event argument 0x1A. Neither that event number nor the bit's numeric value is a retrieved EEPROM service error. The component-level meaning is not established by this trace; in particular, 0x81 must not be interpreted as the manual's pressure-decreasing error merely because the byte values coincide.

LoaderCtl64.dll retains separate exports for GetLoaderStatus, ReadUserAreaMemory, ReadFSMemory and ReadErrorInfo, together with write/movement functions. Their presence does not make loading the full library a verified read-only integration. The installed publisher imports user-area access and drive-history functions; these imports do not establish a complete printer EEPROM-history route.

## Existing Epson logs supply repeatability evidence

The supplied publisher log contains 7,340 lines and 1,798 ASB status entries. It includes additional occurrences of the same status-region change seen in cap2:

- 2026-10-28 11:30:22.525: sixth byte 80; at 11:30:24.935: sixth byte 81, with the other eight bytes unchanged between those two entries.
- 2026-10-30 11:30:48.554: sixth byte 80; at 11:30:59.644: sixth byte 81, with the other eight bytes unchanged between those entries. At 11:30:59.701 it becomes 91, setting another bit.

These are log-local timestamps with no timezone field. They are not synchronized to the capture by assumption. They corroborate recurrence on additional dates, but are from the same installation and are not independent device replication. The logs also contain printer-status values, drive/service messages, and job-history fields. A job field named ErrorHistory is not automatically EEPROM fatal history. No verified component-specific stored fault code was obtained from the inspected logs.

Raw logs contain job names and identifiers and are intentionally excluded from the repository. Only the relevant status transitions and file hashes are recorded here.

## Outcome and next steps

The requested installed-library comparison is complete at the level supported by this evidence. It establishes repeatability and maps the captured change into the installed software's internal flag/callback path. It does not establish a failed component or resolve the missing printer EEPROM profile and history exchange.

No further ordinary startup capture, software reinstall, driver replacement or firmware update is indicated by these findings. Keep the original captures and copied logs intact.

For an exact stored service code, the decisive missing evidence is a successful information-only history retrieval from a legitimate working reader, or model-specific protocol/configuration documentation. The demo's gated history operation cannot provide that evidence. If the owner seeks external help, request only the latest five printer and loader fatal-error codes, with a screenshot/export and firmware versions, explicitly excluding reset, adjustment, initialization and firmware operations. A USBPcap trace of that same successful read would support validating an independent implementation.

If no such reader is available without cost, provide a short video of an otherwise normal startup only if another startup is already necessary: include the LEDs, sounds and visible mechanism positions without reaching inside or forcing any mechanism. This could narrow a mechanical investigation but is not a substitute for stored codes and should not be represented as one.
