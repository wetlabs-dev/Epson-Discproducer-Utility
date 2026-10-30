# PP-100 power-on capture analysis

## Scope and provenance

The owner started USBPcap before powering on the PP-100 and stopped after Total Disc Monitor displayed its internal printer error. There is no timestamped marker for the physical power-button press or GUI error appearance. Times below are seconds from capture start, not from power-on.

- Input: `cap2.pcapng`, SHA-256 `46413259380497a2b924a297d4116bff866a2a670d4a856ff3612b318ad35766`.
- 9,123 records over approximately 40.049 seconds, including unrelated USB devices.
- Analysis: offline USBPcap header/payload extraction and independent TShark examination of decisive frames 6945, 7205, 7227 and 6760/6763. No replay, demo execution, or hardware requests.
- Webcam records account for the 2,480 truncated records. None of the records for the four Epson addresses below is truncated. This does not certify capture completeness or absence of dropped events.
- Raw captures, serial numbers, host pointer values, unrelated device payloads and full transaction exports are excluded from the repository.

## Devices present

All addresses are capture-local on bus 1; they differ from cap1.

| Address | Identity | First observed (s) | Records |
| --- | --- | ---: | ---: |
| 9 | Epson 04B8:061D optical drive | 7.328172 | 674 |
| 13 | Epson 04B8:061D optical drive | 18.942626 | 564 |
| 14 | Epson 04B8:0007 PP-100PRN | 20.133025 | 572 |
| 12 | Epson 04B8:0311 HID; reply identifies PP-100LDR | 20.138984 | 818 |

The loader HID is the important addition over cap1. Printer and loader continue returning data after the error-state transition. This is host-captured request/completion evidence, not electrical bus-acknowledgement verification.

## Printer status transition

Six bulk IN replies contain `@BDC ST2` with internally consistent body lengths and complete tag/length/value framing.

| Frame | Time (s) | Payload bytes | Tag 01 | Tag 02 | Tag 2B |
| --- | ---: | ---: | --- | --- | --- |
| 6945 | 33.410341 | 105 | 03 | absent | 00 00 |
| 7205 | 34.499323 | 108 | 00 | 00 | 00 00 |
| 7697 | 35.766376 | 108 | 00 | 00 | 01 00 |
| 8023 | 36.929848 | 108 | 00 | 00 | 01 00 |
| 8401 | 38.118001 | 108 | 00 | 00 | 01 00 |
| 8907 | 39.296275 | 108 | 00 | 00 | 01 00 |

The original author's [epson_print_conf status parser](https://github.com/Ircama/epson_print_conf/blob/main/epson_print_conf.py) maps tag 01/value 03 to Waiting, tag 01/value 00 to Error, and tag 02/value 00 to Fatal error. This is a cross-model interpretation, not a verified PP-100 service-code definition. It agrees with the owner's Monitor observation. A waiting sample does not establish that the mechanism was healthy.

The observed transition is bracketed by 33.410341–34.499323 seconds (1.088982 seconds). The first captured error-pattern reply is 14.366298 seconds after the first printer record, **not necessarily 14.366298 seconds after power-on or fault onset**. Polling and internal reporting may delay observation.

The last four replies match the error-state payload recorded in cap1. Tag 2B changes later; its meaning is unverified. Neither tag 02/value 00 nor the unchanged tag 15/value 26 32 identifies a motor, encoder, tray, ink-pressure, maintenance or controller fault. No specific stored service error has been decoded.

## Loader HID observations

The HID report descriptor declares input/feature reports; captured feature replies identify PP-100LDR. Among the report-1 replies, a recurring nine-byte status region changes:

| Frame | Time (s) | Nine-byte region |
| --- | ---: | --- |
| 6143 | 31.386380 | second event: 83 80 84 80 80 80 90 80 80 |
| 6377 | 32.127592 | 83 80 84 80 80 80 90 80 80 |
| 7227 | 34.536811 | 83 80 84 80 80 81 90 80 80 |

The sixth byte changes 80 to 81, 37.488 milliseconds after the first printer error-pattern reply. This is a correlated observation in one startup, not proof that the loader caused the printer error, nor a decoded component-fault bit. Frame 6143 also contains an earlier, different status event within the same report.

Frequent report-3 replies contain 18/1A and a following 00/01. Those are not established service error codes. Identity replies and strings such as `indef` and `not-full` also occur; they do not establish the printer's mechanical condition. The useful next static-analysis target is the legacy loader's status decoder and its mapping of the nine-byte region, rather than guessing meanings from numeric similarities.

## Transport observations and EEPROM limits

Printer address 14 has no nonzero USBD completion status in this capture. HID address 12 has one 0xC0000004 completion during SET_IDLE at frame 3586, followed by many successful report exchanges. Drive addresses 9 and 13 have 49 and 38 nonzero-status records respectively. These must not be conflated with the printer's later ST2 fatal-error indication or assumed to identify its cause.

Frame 6760 at 32.802422 seconds contains the exact 27-byte EJL/1284.4 entry sequence identified in the [static dependency trace](pp100-eeprom-dependency-trace.md); completion frame 6761 reports success. Frame 6763 returns an eight-byte reply and service negotiation follows. This supplies a previously missing observation of the startup transport sequence. It does not certify all connection/recovery paths or prove that the demo DLL produced the traffic.

A per-payload search found no `7C 7C 06 00`, `7C 7C 07 00` or `1B 7C 00 06 00` printer EEPROM-read template prefix. This bounded search does not exclude a fragmented or different protocol. No verified EEPROM-history exchange or missing model-specific P0/P1 configuration was recovered. The capture therefore does not make a live EEPROM reader ready to implement.

## Reproduction and next step

With the original capture, inspect the decisive packets offline:

```powershell
& 'C:\Program Files\Wireshark\tshark.exe' -r .\cap2.pcapng -Y 'frame.number == 6945 || frame.number == 7205 || frame.number == 7227 || frame.number == 6760 || frame.number == 6763' -V -x
```

For this capture only, Wireshark display filter `usb.bus_id == 1 && (usb.device_address == 12 || usb.device_address == 14)` isolates loader/printer records. Display filtering does not remove unrelated data from the original file.

This capture is sufficient to establish a startup status transition; another identical full-system capture is not the immediate priority. Continue offline mapping of the loader status region, retaining the distinction between an error indicator and a specific stored service code. Do not infer EEPROM access keys or send speculative commands from this evidence.
