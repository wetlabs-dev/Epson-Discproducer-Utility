# PP-100 cap1 analysis — 2026-10-23

## Evidence and scope

The user confirmed that Epson Total Disc Monitor displayed an internal printer error throughout this capture; the error light flashed and power light remained on. This is a single error-state session, not a normal/error comparison or independent reproduction. Raw customer capture and transaction exports are not distributed in this repository.

- `cap1.pcapng` SHA-256: `de5f6e3fe326d1d97efa89e785f72fd9479d2d0f0d6844cce9157cadac381938`.
- 3,510 host USBPcap records over 47.735758 seconds, one capture interface.
- Independently examined with TShark/capinfos 4.2.6 and the application's `usbpcap-1.3.0` parser. The latter reports complete with no warnings. No nonzero USBD status values were observed; this does not mean the printer mechanism is healthy or establish electrical USB acknowledgements.
- Address 13 accounts for 3,498 records; addresses 6 and 12 each contribute six initial records at time zero, consistent with the enabled descriptor-injection option. Do not treat those initial records as proof of fresh bus queries. No address-14 records occur.
- 3,328 bulk records and 182 control records. Runtime printer bulk endpoints are OUT `0x01`, IN `0x82`. Capture-local bus/address does not establish permanent device identity.

## Identity discrepancy

Frames 19/20 are a nonzero-time GET_DEVICE_ID request/completion pair. Frame 20 contains 107 response bytes beginning **00 6B**, followed by the Epson/PP-100PRN identity text. The earlier `ieee.json` driver-query export contains 107 bytes beginning **6B 6B**, with the same following identity text. These are separate queries, separated in time and potentially using different driver paths; this is not proof of corruption of one particular transaction.

Both 6B bytes individually equal decimal 107, but the two-byte big-endian value is 27499. A duplicated length byte is only a hypothesis. Review of the utility's native worker found a fresh zero-filled output array, unchanged output-buffer/count handling, a checked returned count and a byte-for-byte slice; no identity-byte rewriting was found. No workaround rewrites the prefix. A future simultaneous capture of one utility identity query could locate the discrepancy more precisely; it is not required to interpret the existing error-state session.

## Repeated status exchange

41 bulk IN payloads contain `@BDC ST2` followed by CR/LF. They are byte-identical, 108 bytes each. Representative frame: **56** (0.609490 seconds); last: **3462**. The six leading bytes have a length field consistent with the entire 108-byte payload. At payload offsets 16–17, `5A 00` represents 90 in little endian, exactly the remaining body length. Interpreting the body as one-byte tag, one-byte length and value consumes all 90 bytes without truncation, producing 12 fields in each sample.

| Payload offset (decimal, zero based) | Candidate tag | Length | Observed value | Interpretation status |
|---|---|---|---|---|
| 18 | 01 | 1 | 00 | Cross-model status-field hypothesis: Error |
| 21 | 02 | 1 | 00 | Cross-model error-field hypothesis: generic Fatal error |
| 24 | 06 | 2 | 01 FF | Meaning not verified for PP-100 |
| 28 | 0B | 13 | Retained in original | Meaning not verified for PP-100 |
| 43 | 0F | 19 | Retained in original | Do not infer usable ink levels |
| 64 | 10 | 3 | 01 4E 4E | Meaning not verified for PP-100 |
| 69 | 13 | 1 | 01 | Meaning not verified for PP-100 |
| 72 | 19 | 12 | Retained in original | Meaning not verified for PP-100 |
| 86 | 2B | 2 | 01 00 | Meaning not verified for PP-100 |
| 90 | 15 | 2 | 26 32 | Not established as a service error code |
| 94 | 1B | 1 | 00 | Meaning not verified for PP-100 |
| 97 | 08 | 9 | Retained in original | Do not promote text to verified firmware version |

The cross-model status/error interpretation is supported by the original authors' [epson_print_conf ST2 field table](https://github.com/Ircama/epson_print_conf#st2-status-reply-codes) and [status_parser implementation](https://github.com/Ircama/epson_print_conf/blob/main/epson_print_conf.py). These are third-party protocol research, not original PP-100 documentation. Its byte-length framing and tag 01/02 interpretation match this sample structurally; that does not certify all field meanings. Gutenprint's [older ASCII status documentation](https://gimp-print.sourceforge.io/reference-html/x952.html) independently uses 00 for error/fatal error, but describes a different reply format and model family.

This interpretation is consistent with Epson Monitor's internal-error message. It does **not** identify a motor, encoder, tray, pressure, waste-counter or controller fault. In particular, neither `00` nor `26 32` is evidence for any entry in the PP-100 service-code reference. No idle capture exists to demonstrate which bytes vary with the fault. Identical samples may include cached information and do not constitute independent reproduction.

There are also 41 repeated `@BDC PS` responses containing `II:00;` and repeated EPSON-DATA / EPSON-CTRL negotiation strings. These observations do not authorize replay, EEPROM access, or new native commands. External research software was read as source only, never installed or executed.

## Reproduction

Use the immutable original capture. The CLI output is bounded; TShark supplies an independent packet-level view:

```powershell
.\discdiag-cli.exe analyze .\cap1.pcapng --json > cap1-analysis.json
& 'C:\Program Files\Wireshark\tshark.exe' -r .\cap1.pcapng -Y 'frame.number == 20 || frame.number == 56' -V -x
& 'C:\Program Files\Wireshark\tshark.exe' -r .\cap1.pcapng -T fields -e frame.number -e frame.time_relative -e usb.device_address -e usb.endpoint_address -e usb.transfer_type -e usb.usbd_status -e usb.capdata > cap1-fields.tsv
```

Read-only offline analysis; no capture replay. Private detailed per-sample offsets/values were produced locally as `artifacts/cap1/status-observations.json` (excluded from Git).
