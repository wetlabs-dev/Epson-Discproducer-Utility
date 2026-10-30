# PP-100 offline follow-up, 2026-10-30

## Provenance and scope

The re-uploaded `PP-100_N_AP_DEMO.zip` matches the previously recorded SHA-256 `39795a8fede14160bda673d0e36befdd4a4a196d95c687100ecfaca80689997d`. LoaderCtl10.dll matches `666890a55100d9bad386616b5cbb908482b62f989734f85bb4f6bab28b5c90b5`. Analysis used static PE disassembly and the saved cap2 USBPcap payload extraction. No executable or DLL was run or loaded, no license checks were changed, and no device commands were sent.

## New transport finding

All 44 captured HID SET_REPORT submissions with the report-1/128-byte setup can be divided into 52 length-delimited submessages. Report payload length and every submessage boundary were checked. Twelve submessages have the framing of the demo library's **ReadUserAreaMemory**, not its **ReadFSMemory** history-reading path.

Evidence chain in LoaderCtl10.dll (addresses are RVAs):

- ReadUserAreaMemory at 0x7370 selects command-table entry 0x12.
- Its construction at 0x74B5–0x754E appends a 0x20 selector, little-endian address, and little-endian requested length.
- The serializer at 0x33C0 uses the table at 0x1A198, 16-byte entries. Entry 0x12 starts `01 42 31`; the serializer inserts the parenthesis framing and length before the selector/address/length body.
- This matches the twelve captured messages in frames 6382–6522, including their varying address and length arguments.

| Capture frame | Candidate user-area address | Requested bytes |
| --- | --- | ---: |
| 6382 | 0x0032 | 2 |
| 6392 | 0x0034 | 2 |
| 6406 | 0x0036 | 2 |
| 6416 | 0x0038 | 2 |
| 6428 | 0x003A | 2 |
| 6440 | 0x0000 | 3 |
| 6464 | 0x0005 | 4 |
| 6474 | 0x0009 | 4 |
| 6486 | 0x000D | 3 |
| 6498 | 0x0012 | 4 |
| 6510 | 0x0016 | 4 |
| 6522 | 0x001B | 1 |

These requests occur before the first captured printer error pattern. Their content and memory persistence are not established. A matching serializer does not prove that the demo generated this traffic; installed Epson software may use the same protocol. It does not establish that these user-area addresses share the EEPROM namespace described by the service manual.

The previously traced fatal-history path calls ReadFSMemory at 0x9330. That function assembles a different eight-byte parameter block, including transformed and model-dependent bytes. Therefore, changing a user-area request's address to 0x76 is not a justified way to retrieve loader history. Do not replay these observations or infer interchangeable memory banks.

Frame 7376 contains a different, seven-byte body under the same outer message family. It is not one of the twelve read matches and is not approved for replay.

## Status is distinct from history

GetLoaderStatus at 0x66A0, selector zero, obtains a cached buffer pointer and length through helpers 0xA660/0xA680 and copies it to the caller. The other selectors inspect object state. This function does not directly call the EEPROM/history reader.

The status helper at 0x1300 reads individual bits by byte index and bit position. CheckAsbStatus at 0x9B10 consumes triples of bit index, expected value, and return code. AsbStatusCheck at 0xA490 tests bits 16–19 and invalidates cached values. None of this establishes a component-level meaning for the sixth-byte 80-to-81 change observed in cap2. The caller's field definitions and the complete status-buffer transformation still need to be traced. Do not label the observed 0x81 as service error 0x81.

## Address discrepancy remains unresolved

The original PP-100 service manual Rev. D, page 3-21, references loader 0x76 and printer 0x9D; page 5-55 documents the adjustment tool's information-retrieval workflow. [Epson-authored manual, third-party mirror](https://www.scribd.com/document/808984453/PP-100-eng-sm-revD).

The supplied library's default printer-history branch uses a different layout, including a candidate latest-code field at 0x3C. The default loader-history branch reads fields including 0x02 through ReadFSMemory. No proven bank translation, firmware mapping, or constant offset reconciles those values. The ZIP still contains no Parameter.ini; the eight INI files in it do not supply that missing file. Neither the manual's addresses nor the packet framing resolves the printer's missing P0/P1 configuration.

## Next evidence to collect without operating the demo

1. Record the installed Total Disc Maker version and the PP-100 printer/loader firmware versions displayed by the installed Epson software. Screenshots are sufficient; do not update firmware.
2. Locate the installed Epson application directory via its shortcut's Open file location. Provide a file-name/version inventory first, especially LoaderCtl10.dll, ALD_Com_Lib.dll, Hdidapi.dll, EprisDrvR.dll, CtrDev.dll and any model-specific INI files. Missing names are useful information; do not substitute files into the installation. The installed stack may differ from the 2010 demo.
3. If present, supply copies of those DLLs and model INI files for offline comparison. Do not send license keys or unrelated user files. No assumption is made that the regular Epson installation includes the adjustment tool's Parameter.ini.
4. A successful error-history read from a legitimate, functioning tool remains the strongest evidence for validating a future reader. The user has confirmed that the demo gates that operation. Do not spend time repeating the gated action or ordinary startup captures to recover its command sequence.

No new live command is ready for implementation. The next useful investigation is the installed library's status-field mapping and comparison against the matching demo library, followed by resolving the model-specific history profile. This work has narrowed the transport distinction but has not identified the user's failed component.
