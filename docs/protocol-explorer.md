# Protocol explorer and evidence model

`DiagnosticCommands` is the immutable high-level allowlist; the original native `CommandRegistry` remains the closed usbprint.sys allowlist. No text box accepts raw control bytes, IOCTL numbers, vendor requests, resets or printer-language output. An unsupported entry is explanatory and sends nothing.

| Operation | Exact request framing | Access requirement | Result / interpretation |
|---|---|---|---|
| GET_DEVICE_ID | `DeviceIoControl(0x00220034)`, NULL input, length 0; output capacity 4094 | Existing USB printer interface, shared GENERIC_READ handle | Original length-prefixed IEEE 1284 reply. Actual USB setup is driver-owned and **unobserved**. |
| GET_PORT_STATUS | `DeviceIoControl(0x00220030)`, NULL input, length 0; output capacity 4 | Existing USB printer interface | DWORD; decode only documented low-byte class bits. Actual USB setup is unobserved. |
| GET_DESCRIPTOR | Hub `DeviceIoControl(0x00220410)`; `USB_DESCRIPTOR_REQUEST` consists of LE DWORD connection index + 8-byte setup + output-capacity buffer | Exact physical USB parent and hub, PnP port address, verified matching hub-port driver key; hub opened with zero desired access and shared R/W | Supplied setup is `80 06 ii tt ll hh nn nn`: descriptor index/type, LE language ID, LE length. Only types 1, 2, 3. These are actual host-buffer bytes, not claimed captured wire traffic. |
| Hub-port validation | `DeviceIoControl(0x00220420)`, input starts with LE DWORD connection index in a 4096-byte zero-filled buffer | Parent hub | Driver key name must exactly match the selected physical devnode before **each** descriptor query; mismatch blocks retrieval. |
| PnP properties | SetupAPI/Configuration Manager and read-only driver registry properties | Present Epson node | Host metadata, no observed USB request/response bytes. |
| Queue/driver/jobs | EnumPrinters, OpenPrinter with default use access, GetPrinter, GetPrinterDriver level 6, EnumJobs level 2, EnumPorts level 2, GetPrinterDataEx | Local spooler access | Structured Windows metadata. `PnPData/DeviceInstanceId` is used only when actually returned; unavailable keys remain uncorrelated. |
| USB standard GET_STATUS | **Not sent** | No compatible direct control API implemented for the Epson-owned interface | The hub descriptor API only issues GET_DESCRIPTOR. No WinUSB replacement/fallback. |

Descriptor sequence: device descriptor (18 bytes); each reported configuration header (9 bytes), then its `wTotalLength`; language descriptor index 0; nonzero manufacturer/product/serial indices from the returned device descriptor, using the first returned LANGID. No strings or endpoints are synthesized. The Windows USB stack enforces standard device-recipient GET_DESCRIPTOR semantics for this IOCTL. Missing language data means no guessed language ID. Configuration descriptors do not establish the active configuration/alternate setting; endpoint visibility does not establish access.

The central recorder stores UTC timestamp, correlation GUID, selected device and interface, actual host input/output bytes, supplied setup when available, monotonic elapsed time, configured timeout, outcome, native code and provenance. For hub descriptor calls the response includes the Windows request header; decoded descriptor data begins at offset 12. For printer IOCTLs input bytes are genuinely empty. Host metadata calls have structured inspector output rather than fabricated USB bytes. Worker termination can prevent delivery of partial child traces; the parent records the timeout/cancellation and does not reconstruct missing exchanges.

Milestones are separate: node enumerated; interface handle opened; host API invoked; valid reply received; reply interpreted. Physical request transmission and bus acknowledgement remain unknown unless independently observed with an appropriate trace, and USBPcap itself is labeled as host-controller evidence.

The descriptor parser preserves raw blobs and decodes device/class fields, configurations, alternate settings and endpoints. Maximum packet size shows the low 11-bit size field; original endpoint descriptor bytes retain additional transaction bits. Polling interval is displayed in raw descriptor units, not converted without speed context. Unknown descriptor types remain in raw data.

## Sources

- [Microsoft USB_DESCRIPTOR_REQUEST](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ns-usbioctl-_usb_descriptor_request)
- [Microsoft hub-port driver-key query](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_node_connection_driverkey_name)
- [Windows SDK IOCTL indices](https://github.com/tpn/winsdk-10/blob/master/Include/10.0.16299.0/shared/usbiodef.h)
- [Microsoft USBView sample](https://github.com/microsoft/Windows-driver-samples/tree/main/usb/usbview)
- [Microsoft GetPrinterDataEx](https://learn.microsoft.com/en-us/windows/win32/printdocs/getprinterdataex)
- [Microsoft DRIVER_INFO_6](https://learn.microsoft.com/en-us/windows/win32/printdocs/driver-info-6)
- [Microsoft JOB_INFO_2](https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2)
- Original printer-class sources remain in [supported commands](supported-commands.md).
