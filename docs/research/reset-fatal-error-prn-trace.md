# ResetFatalErrorInfoPRN: original PP-100 static trace

Investigated 2026-11-04. This is an offline host-code trace, not an implemented reset or a hardware test. No EEPROM was modified. The firmware's interpretation of writes is outside the supplied application's code and cannot be established by this trace.

## Conclusion

The original-model export clears multiple EEPROM fields and verifies each byte by reading it back. It is not a dedicated reboot/clean-start operation. It includes both 0x003C and 0x009D, but does not establish which field controls active startup errors. It neither directly clears the older 0x0100–0x013B records nor explicitly invokes carriage initialization, a mechanical reset or cold start on the inspected normal USB path. The routine's outer error handling can hide partial failure.

## Provenance and method

Owner-supplied `PP-100_N_AP_DEMO.zip`, SHA-256 `39795a8fede14160bda673d0e36befdd4a4a196d95c687100ecfaca80689997d`.

- CtrDev.dll: `61d0bc6a4543387a1e4ac8356736aeb157f105aee0a050b668dcbe03b2e64f32`.
- EprisDrvR.dll: `beadc04b5527c1285e2d39eea5748fb25932452f7f8c1fc17593bb954405d23c`.

PE imports/exports, x86 disassembly, arguments, loops and return paths were inspected without loading the libraries. Addresses below are **host-code RVAs**, except the explicitly identified EEPROM addresses. Preferred image base is 0x10000000. Objdump's nearest-export annotations on unnamed helpers are not function identities: labels containing `Ctl_ColdStart+...` on CRT thunks do not establish a cold-start call.

## Export and model dispatch

1. `CtrDev!ResetFatalErrorInfoPRN`, RVA **0x2970**, calls `IsUSBDevice` at 0x5430. For a USB model it returns 2 if the connection-related member reached through object +0x10, then +0x20, is null. This is a host precondition, not firmware validation.
2. It tail-calls dispatcher **0x26950** through object +0x0C.
3. Model selector 0 uses object +4 and **0x29210** (original PP-100); selector 2 uses object +0x0C and the same implementation (AP). Selector 1 uses object +8 and **0x2BAB0** (N). Other selectors return 1. The original-model selection was established in the earlier dependency trace.
4. 0x29210 zeroes a 64-byte local buffer and sets host global 0x45FCC (`m_nNewEEPControl`) to 1. It passes that zero buffer, lengths and addresses to **0x28EC0**, always with a null progress callback.

## Complete original-model write schedule

Every requested value is zero. These are static write destinations, not instructions to execute them.

| Order | EEPROM destination | Length | Call-site RVA |
| --- | --- | ---: | --- |
| 1 | 0x006C–0x006F | 4 | 0x29245 |
| 2 | 0x0098–0x0099 | 2 | 0x2925A |
| 3 | 0x009A | 1 | 0x2926F |
| 4 | 0x009B | 1 | 0x29284 |
| 5 | 0x009C | 1 | 0x29299 |
| 6 | 0x003C | 1 | 0x292AB |
| 7 | 0x00B8–0x00B9 | 2 | 0x292C0 |
| 8–11 | 0x009C, 0x009D, 0x009E, 0x009F | 1 each | loop 0x292C7–0x292E2 |
| 12 | 0x00F8 | 1 | 0x292F4 |

This is **17 byte writes over 16 unique addresses** on the fully successful path; 0x009C is written twice. A failed block can end early, so 17 is not a guarantee of actual writes. The four-iteration loop is explicitly `0x9C + index`, not `0x100 + index*16`. There is no direct erase of the four older record blocks here. Clearing an index may affect how the firmware/tool presents old records; that effect is not established.

The destinations overlap the previously reconstructed latest-record fields and candidate history-position byte. Including 0x009D is a concrete connection to the manual's address reference, but does not prove that 0x003C and 0x009D are aliases or interchangeable.

## Block helper and readback

**0x28EC0** loops over the supplied length. For these addresses, all below its 0x01FB threshold, each iteration:

- Sets host global 0x45FCC to 1.
- Calls byte writer **0x159E0** with the current address and zero byte.
- Depending on the parent object's +0x1C flag, clears that host global back to zero before verification. This is a host transport-mode flag, not an EEPROM write or a proven device error latch.
- Stops the block if the write reports failure.
- Calls byte reader **0x15920** for the same address, then compares the returned byte with the intended zero. Read failure also ends the block. A mismatch assigns internal error 100.
- Advances address/data pointers only after successful verification. No per-byte retry or rollback is present in this helper.

The null callback means the helper's virtual progress callback cannot issue extra actions on this path. Its final call to **0x2A820** maps status values: zero stays zero; internal mismatch 100 maps to 14. The generic mapping for write-parser errors -21/-22/-23 is 2.

## Byte writer and lower-level boundary

**0x159E0** chooses one-byte versus two-byte addressing using host configuration at 0x45FD0 (maximum address), not the current address alone. Because the block helper sets 0x45FCC to 1 before every write, its write calls take the control-mode branch:

- thunk 0x2CB82 -> IAT 0x320B0 -> **EprisDrvR!Ctl_WriteEEP**, RVA **0xF3A0**;
- or thunk 0x2CB7C -> IAT 0x320B4 -> **EprisDrvR!Ctl_WriteEEP2**, RVA **0xF500**.

The writer obtains two configuration bytes from object +0x100001C/+0x100001D and an existing connection object at +0x1000008. It constructs a write-family application message and calls **Ctl_SendReceive (0xA060)**. There is no intervening call to `Ctl_ReadWriteEEP` in this reset path. Successful validation of the separate read profile does not validate these write parameters or authorize these functions.

Both write functions propagate a negative send/receive result. Otherwise they search the response for `OK`, `NA`, then `BUSY`, returning 0, -21 or -22 respectively; unmatched text gives -23. This is substring-based acknowledgement handling, not the strict addressed validation used by our reader.

Readback at **0x15920** chooses `Ctl_ReplyEEP[2]` or `Prod_ReplyEEP[2]` based on the host mode flag and configured address width. Thus verification can use a different framing family if the parent flag causes the mode to be cleared after the write. The legacy reply parser and its limitations are described in the earlier dependency trace.

For the existing USB/ECB transport branch, `Ctl_SendReceive` opens channel 2 using the dynamically resolved **ECBT_OpenChannel** pointer (0x1A22C), can retry channel opening with sleeps, sends through **Send (0x7D40)** / **ECBT_Write**, receives through **Receive (0x8880)** / **ECBT_Read**, and normally calls **ECBT_CloseChannel** (0x1A230). Close's return is not promoted into the final successful result. Send contains partial-transfer/error retry logic; a caller cannot equate one helper invocation with one bus transaction.

Transport receive failures can invalidate host connection state and close handles. One conditional cleanup branch calls helper 0x10254, which issues a driver-specific DeviceIoControl; its firmware effect has not been validated. Alternate transport modes also exist inside `Ctl_SendReceive`. Consequently the trace establishes the reset's host write schedule and normal USB API chain, **not every possible driver/firmware side effect or a reusable safe reset transport**. No complete write payload or executable reset path has been added to the utility.

The PP-100N branch at **0x2BAB0** has the same high-level zero-write schedule; it uses helper **0x2B760**, then byte writer **0x17CB0** and reader **0x17BF0** rather than the original-model USB helpers. It is not a substitute for the PP-100 path.

## Failure propagation and final state

The outer function **does not check any block-helper return before starting the next block**. A failed four-byte block can therefore be partially cleared, followed by subsequent blocks. There is no transaction, saved backup or rollback. At the end it conditionally clears host global 0x45FCC based on object +0x1C; it does not restore an independently saved previous value.

On the normal return path, EAX retains the final 0x00F8 helper result. The subsequent stack-cookie check at 0x2CE2E preserves EAX on success. Thus a successful return principally establishes that the **last block** succeeded; earlier failures may be hidden. An exception/abnormal termination can leave additional partial host/device state.

## Answer to the clean-boot question

No explicit reboot, cold-start or carriage-initialization request is present in the original-model reset routine's normal path. It performs EEPROM writes plus readback and ordinary transport operations. Firmware could still consult these bytes during boot or react to a write; the supplied host application cannot prove or exclude that behavior. Therefore clearing them is not an evidence-backed way to force a clean boot.

The next evidence-preserving option is a separately reviewed, fixed read of the additional relevant fields (especially 0x009D), correlated with the manual and latest-record context. This document does not implement or authorize that expansion. The existing application remains read-only and unchanged.
