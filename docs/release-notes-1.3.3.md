# v1.3.3 — Experimental fixed EEPROM candidate read

Adds **Evidence Toolkit → Analysis → Read candidate EEPROM byte (0x003C)…**. This is a single-byte diagnostic experiment, not a full history reader or a verified error decoder. The read parameters are fixed at `0A 5E`, with two-byte addressing. Their [source and limitations](research/pp100-read-parameter-candidate.md) remain applicable. No write/reset commands, arbitrary addresses, parameter scans, automatic retries, driver replacement or print jobs are added. The offline CLI remains offline.

The original PP-100 status-only D4 exchange has now succeeded in a supplied physical capture, including close acknowledgement and subsequent polling. This does not validate EEPROM compatibility. The supplied JSON and capture timestamps differ; they cannot conclusively be attributed to the same attempt.

## Windows 11 procedure

1. Extract the complete new Windows x64 package. Close any older utility instance. Launch `DiscproducerDiagnostics.App.exe`.
2. Ensure there are no active disc jobs. Close Total Disc Monitor, Maker and Setup. Start USBPcap for the PP-100 printer interface.
3. In administrator PowerShell, record the existing service states, then temporarily stop both services (no `-Force`):

   ```powershell
   $epsonServices = @('EPSON Total Disc Control Service', 'EPSON Total Disc Copy Service')
   $previouslyRunning = @(Get-Service -Name $epsonServices -ErrorAction Stop | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
   Stop-Service -Name $epsonServices -ErrorAction Stop
   Get-Service -Name $epsonServices
   ```

   Proceed only when both report `Stopped`. If stopping fails, do not run the reader; restore any services already stopped. The app queries service status and refuses unknown, running or transitional states. It never stops or starts services itself.
4. Wait five seconds and check that recurring printer bulk traffic has ceased. In the utility select the original `EPSON PP-100PRN`, VID/PID `04B8:0007`, with a printer interface path. Turn simulation off.
5. Choose **Evidence Toolkit → Analysis → Read candidate EEPROM byte (0x003C)…** and accept the experimental-read confirmation once. The worker checks driver-reported model and WLP00105 firmware, opens exclusively and sends the fixed request with bounded D4 setup/credits/cleanup. Identity may be cached; neither it nor exclusive opening establishes global session ownership.
6. Use **Export analysis JSON…** with a new filename. Save a forensic diagnostic bundle if you need the raw Communication Log, especially after cancellation or worker failure. Do not retry on timeout, rejection or incomplete cleanup.
7. Regardless of success or failure, restore the services that were running, using the same PowerShell window:

   ```powershell
   $previouslyRunning | ForEach-Object { Start-Service -Name $_ -ErrorAction Stop }
   Get-Service -Name $epsonServices
   ```

8. Reopen Total Disc Monitor and check that it refreshes. Stop and save USBPcap. Retain the JSON and capture together, and record whether monitoring recovered.

A valid addressed response exposes `Value`, such as `0x01`; it is an observed candidate byte, **not a diagnosed error**. Invalid/absent replies leave `Value` null and preserve raw evidence. Inspect `Transport.Cleanup` independently: a returned byte does not prove successful close. Address `0x003C` has only a static latest-code hypothesis; no component meanings are inferred.

Service checks reduce the observed contention but cannot exclude every other process or a race between a check and transmission. Physical cancellation/disconnect recovery and EEPROM semantics remain unverified. The first live EEPROM attempt must be captured and reviewed before extending the reader. No programmatic service changes or hardware tests were performed while developing this release.
