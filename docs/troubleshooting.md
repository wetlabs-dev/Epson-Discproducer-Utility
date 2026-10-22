# Troubleshooting on Windows 11

| Symptom | Meaning / next step |
|---|---|
| No Epson nodes | Check power, USB cable and Device Manager. Install/repair Epson's official driver using Epson's instructions. Rescan. The app only includes present nodes with Epson VID in their ancestry. |
| Several rows | Composite device nodes/interfaces can belong to one printer. Select a row with a USB printer interface path; use parent chains to distinguish physical devices. |
| Metadata only | No supported printer interface on that selected node. Try the appropriate interface row. If none exists, use metadata diagnostics and Epson's supported software. |
| Windows error 5 | Access denied by device/driver policy. Standard user access is insufficient on this system or the interface disallows this access. The app does not elevate automatically; ask the administrator/vendor to assess the policy. |
| Error 32 | Sharing conflict. Finish existing work, close other diagnostic tools and retry manually. Do not interrupt publishing jobs. |
| Error 1 / 50 | Driver does not implement the requested operation. Preserve the driver and export the evidence. |
| Error 2 / 6 / 31 / 433 / 1167 | Interface missing, invalid, failed or removed. Reconnect the printer, rescan and connect. |
| Error 23 | Driver/device transfer error; the ID request already uses a bounded 4094-byte buffer. Retain raw diagnostic evidence; do not experiment with vendor requests. |
| Error 1460 / timeout | Worker exceeded the deadline and was stopped. Increase the query timeout if the system is slow; default includes startup/enumeration. No automatic retry occurs. |
| Ready queue but class error | Both sources are shown. Queue state can lag or refer to another device; candidate queues are explicitly uncorrelated. |
| Serial unavailable | No recognized serial field in a successful IEEE 1284 response. The app deliberately does not guess from a Windows instance suffix. |
| No log after closing | Logs are session memory only. Export before closing. |
| Redacted export lacks bytes | Intentional. Turn redaction off only when the recipient needs raw diagnostics and you intend to share identifiers. Clipboard copying is always unredacted. |

Cancellation may leave earlier timestamped observations on screen; it does not turn them into fresh measurements. Disconnect/reconnect before switching mock scenarios or devices. No printer configuration change is needed to run this utility.
