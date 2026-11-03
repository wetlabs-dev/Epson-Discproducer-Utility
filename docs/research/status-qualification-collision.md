# First live status qualification: interleaved host traffic

Analyzed 2026-11-03. Source evidence remains private and is not committed.

- `capture.pcapng`: SHA-256 `8783c2dcd680e4d1ae46c1de9c359c3ce28354f236bcda4779a4891dfc4b86cd`.
- `capture.json`: SHA-256 `bf8287b686d68c88ac5274852a47f25712a7522bfee02aa106840e347156e14d`.
- Capture: 1,262 packets, 10.294594 seconds, one USBPcap interface. No packet truncation reported by the captured/original length comparison. No nonzero USBD status on device address 14. TShark 4.6.9 capture metadata; analysis used installed TShark.

## Result

The qualification successfully reached packet-mode entry, D4 revision-10 initialization, EPSON-CTRL resolution and channel opening. It failed while waiting for send credit, before issuing its fixed status request. No EEPROM request was made by this operation. Its refusal to continue was appropriate; the returned packet was not a credit reply.

The capture strongly supports competing host traffic. It cannot identify the originating Windows process or prove which driver-layer path bypassed the exclusive file handle. Prior exclusive-open success did not establish global USB/D4 ownership and must not be treated as such.

| Frame | Relative seconds | Observation |
| --- | ---: | --- |
| 601 / 606 | 4.382707 / 4.414823 | Qualification packet-entry request / acknowledgement |
| 609 / 612 | 4.434481 / 4.444206 | Qualification Init / successful revision-10 reply |
| 613 / 616 | 4.447948 / 4.453238 | EPSON-CTRL lookup / socket 02 reply |
| 619 / 622 | 4.459975 / 4.468061 | Open 02/02 / successful channel-open reply |
| 623 | 4.483328 | Qualification CreditRequest, `00 00 00 0D 01 00 04 02 02 00 80 FF FF` |
| 625 | 4.487678 | Additional host Init, absent from the qualification's JSON trace |
| 628 | 4.488203 | IN transfer contains a 9-byte InitReply followed by three zero bytes; do not infer those surplus bytes' meaning |
| 630 | 4.488597 | Another complete InitReply |
| 631 | 4.488650 | Host EPSON-DATA lookup, not issued by qualification |
| 636 | 4.493542 | Successful EPSON-DATA lookup reply; exact bytes match the qualification JSON's unexpected IN packet |

The additional Init occurs **4.350 ms** after the qualification CreditRequest. The lookup reply is `00 00 00 13 01 00 89 00 40 45 50 53 4F 4E 2D 44 41 54 41`: command 89, result zero, socket 40, EPSON-DATA. A credit reply would have command 84 and socket fields 02/02. This is a successful answer to a different host operation, not a printer refusal of the intended query.

IRP identifiers distinguish contemporaneous requests in this interval, but they are kernel request identifiers, **not process IDs**, and may be reused. Process attribution must not be inferred from them. Payload order and the absent commands in the app trace are the stronger evidence.

## Background behavior and cleanup

Before qualification, the same device shows repeated Init, EPSON-DATA/CTRL/XCTRL/DOWNCONTENT lookup, status/information query and close patterns. These resemble the earlier Epson software capture, making Total Disc Monitor/Control a plausible competing issuer, but USBPcap alone cannot prove its process identity.

Three pre-qualification status replies (frames 164, 332, 492) are identical. Their application-payload SHA-256 is `6621e3ede2962fb62d0a590583c92b58260295acc555bd396b87c06ebb9ef58a`; fields 01=00, 02=00 and 2B=0100 match the previously observed generic error pattern. They do not identify a component failure or stored history code.

The qualification emitted neither its status request nor its close after the mismatch. JSON explicitly reports uncertain cleanup. There are no further device-14 packets after frame 636 for the remaining approximately 5.8 seconds, although HID traffic on device 15 continues. This does not prove disconnection, recovery or successful subsequent monitoring. A waiting competing reader is plausible, not established.

## Next controlled step

Do not retry repeatedly with the same background activity, bypass reply validation, discard the other client's replies until something matches, or enable EEPROM traffic. The problem precedes the EEPROM operation.

Identify the Epson processes/services actually active on Windows. A read-only PowerShell inventory is:

```powershell
Get-Service | Where-Object { $_.DisplayName -match 'Epson|Disc' } |
    Select-Object Status, Name, DisplayName
Get-Process | Where-Object { $_.ProcessName -match 'Epson|Disc|TDMon|Pub' } |
    Select-Object ProcessName, Id
```

Names are filters, not confirmed attribution. The app should not automatically terminate processes or stop services. After identifying the responsible monitoring component, a controlled test can temporarily pause it, retain its original state for restoration, verify that periodic printer bulk traffic has ceased, and perform one status qualification. Closing only the monitor window may leave a background service active. Keep USBPcap running through the test and the restoration of monitoring, then verify refresh still works. If the background cannot be made quiet without changing drivers, stop at that ownership limitation.

No application changes or hardware operations were performed during this analysis. Existing fail-closed behavior prevented an uncorrelated response from becoming a status result.
