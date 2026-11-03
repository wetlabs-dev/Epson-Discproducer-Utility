# Observed EEPROM byte 0x52: carriage-lock lead

Analyzed 2026-11-03. Static inspection only; no vendor program was executed, no licensing logic modified, and no additional printer requests sent. Raw binaries, disassembly and captures remain private.

## Observed exchange

Owner-supplied `eeprom.pcapng`, SHA-256 `10ea119fc1666da4496eadaeda36cec7986015bfc36ff12892d55c267c922191`, records a single fixed read at frame 4133, addressed reply `EE:003C52;` at frame 4140, and channel-close acknowledgement at frame 4144. Its timestamps match the supplied JSON. Thus address 0x003C returned byte 0x52 in this exchange. There were no observed printer USBD errors or truncated packets. Subsequent monitoring recovery is not shown by the capture.

## Demo trace

Archive SHA-256: `39795a8fede14160bda673d0e36befdd4a4a196d95c687100ecfaca80689997d`. CtrDev.dll SHA-256: `61d0bc6a4543387a1e4ac8356736aeb157f105aee0a050b668dcbe03b2e64f32`.

The original-model selector-1 history reader at RVA 0x29010 passes a one-byte read of address 0x003C into output structure offset 0x09. In particular, RVAs 0x290F2–0x290FC specify length one, destination +9, address 0x3C, and call the byte reader at 0x28DC0. This path returns the byte without translating 0x52 into descriptive text.

The original-model `Parameter/PP-100/English.dat` contains `strPrnFatalInfo5` with the label “Fatal error code”, adjacent to encoder, sensor and retry fields. It contains no 0x52 description. The PP-100N/AP resource variants also provide field labels rather than a matching description. The included PDF is adjustment-program release notes, not a fatal-code reference.

Readable ASCII/UTF-16 strings in the application-specific EXE/DLLs did not reveal a 0x52-to-description mapping. CtrDev's immediate 0x52 occurrences are not evidence of a fatal-code lookup. The executable's history GUI consumer remains unresolved; absence of a readable string does not prove absence of an encoded table. Consequently, do not claim the demo itself was observed displaying an error name. The association between structure +9 and the fatal-code label still relies on the reconstructed layout/resource ordering.

## Original-model documentation match

The Epson-authored **PP-100 Service Manual, Rev. D, printed page 3-25**, lists printer mechanism code **0x00000052** as **“CR lock set error”**. It groups this with carriage sequence failures. Source: [third-party mirror of the original PP-100 manual](https://www.scribd.com/document/808984453/PP-100-eng-sm-revD). This is an original-model reference, not a mapping borrowed from PP-100III or a consumer printer.

Interpretation: a failure in the carriage-lock engagement sequence is a supported diagnostic lead **if the read field is the relevant stored fatal code**. It is not proof that a particular lock, motor, sensor or board has failed. The documented inspection areas include the carriage route, movement/position-feedback components, cabling and maintenance mechanism. The manual also lists service operations; this finding does not authorize calibration, resets, forced carriage movement or parts replacement.

Outstanding: reconcile the demo's 0x003C layout with the manual's separate printer-history address reference (0x9D), establish historical/current attribution, and confirm symptoms against the carriage-lock mechanism. Do not automatically promote this stored byte to a confirmed current diagnosis or broaden the live reader.

A useful next owner observation is a short startup recording showing the carriage/parking area and sounds, from outside the mechanism with covers in their normal operating position. Note whether the carriage moves at all before the error. Preserve the current evidence and avoid repeated power cycling solely to reproduce it.
