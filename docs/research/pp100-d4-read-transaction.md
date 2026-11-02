# PP-100 candidate EEPROM transaction over D4

Date: 2026-11-02. Implementation specification, not a verified EEPROM exchange. No hardware traffic was sent. The existing live-history gate remains disabled.

## Evidence and scope

This specifies a **single candidate byte read**, with a bounded D4 state machine. It is not a blind byte replay, a full EEPROM dump or proof of live-history compatibility.

- Owner's cap2, SHA-256 `46413259380497a2b924a297d4116bff866a2a670d4a856ff3612b318ad35766`, supplies the exact original PP-100 connection, control-channel, credit, status exchange and close packets below. Packet numbers refer to that capture. USBPcap host completions are not electrical bus-ACK evidence.
- [Read-parameter finding](pp100-read-parameter-candidate.md): original-model candidate `0A 5E`, two-byte address, from model-specific public profiles. Firmware WLP00105 compatibility has not been independently demonstrated for EEPROM reads.
- [ReInkPy D4 implementation](https://codeberg.org/atufi/reinkpy/src/commit/56af2c8277059f3ddccd9e4b9b51cfac65569531/reinkpy/d4.py) supplies header and revision-specific command structure. It calls itself partial; do not copy its permissive parsing or failure handling.
- [ReInk D4 implementation](https://github.com/lion-simba/reink/blob/master/d4lib.c) and [transaction caller](https://github.com/lion-simba/reink/blob/master/reink.c) provide comparison material. Public implementations differ in revision handling, credits and control flags; observed PP-100 revision-0x10 framing takes priority over generic examples.

## USB and Windows layers

Capture descriptor frame 3570 gives configuration 1, interface 0, alternate 0, printer class 07/subclass 01/protocol 02, bulk OUT **0x01** and bulk IN **0x82**, both maximum USB packet size **512 bytes**. Descriptor evidence and observed endpoints agree. Re-discover and verify these properties at runtime; bus/address `1:14` is capture-local, not a persistent identifier. The interface descriptor's protocol 02 and negotiated D4 revision 10 are distinct fields.

D4 messages below are bulk-transfer payloads, not endpoint-zero setup packets. The OS/controller supplies USB token/CRC/handshake details. One USB transfer may contain partial or multiple D4 packets; 512-byte USB maximum packets are not D4 message boundaries.

Windows implementation candidate: re-enumerate the selected `GUID_DEVINTERFACE_USBPRINT` device and use its existing usbprint.sys byte stream through cancellable read/write I/O, preserving Epson's driver. [Microsoft describes usbprint.sys as a communication conduit](https://learn.microsoft.com/en-us/windows-hardware/drivers/print/usb-printing). The supplied library's static trace opens an existing printer interface for read/write; the current application worker only opens it for read and uses fixed IOCTLs. Therefore a native byte-stream adapter is **new work**, not an existing capability unlocked by these bytes. Endpoints are driver-owned on this route, not passed to a new WinUSB handle. Do not install WinUSB, create a spooler print job, select another configuration, reset the interface or automatically stop Epson services.

Exclusive session ownership is required: application-local serialization alone cannot exclude Total Disc Monitor/Control. An implementation must fail on contention rather than claim that successful shared CreateFile proves ownership. An existing active Epson session must not be silently reinitialized or have its replies consumed. Reliable coordination with that software remains a Windows integration validation item.

## D4 framing

Six-byte header: `PSID SSID length-high length-low credit control`. Total length includes the header and is **big-endian**. The EEPROM application length and address are **little-endian**. Header credit and control are different fields. Command channel is `00 00`; the observed control data socket pair is `02 02`.

The observed PP-100 status-data send and reply use control `01`, matching the message-end flag used by D4 implementations. The candidate single EEPROM message below retains that flag and zero piggyback credit. EEPROM-specific acceptance of this framing is not yet observed.

## Conditional exchange sequence

Every arrow requires a correctly framed, matching successful reply before advancing. Bytes are hexadecimal. Observed replies are examples, not permission to ignore negotiated values.

### 1. Enter packet mode — observed

OUT, frame 6760:

`00 00 00 1B 01 40 45 4A 4C 20 31 32 38 34 2E 34 0A 40 45 4A 4C 0A 40 45 4A 4C 0A`

IN, frame 6763:

`00 00 00 08 01 00 C5 00`

This 27-byte entry string precedes ordinary D4 framing. Do not parse it as a normal header. Recognize the entry acknowledgement; this is not the Init reply. A missing acknowledgement is a failure, not permission to reset or continue.

### 2. Init revision 0x10 — observed

OUT frame 6764: `00 00 00 08 01 00 00 10`

IN frame 6767: `00 00 00 09 01 00 80 00 10`

Require InitReply result zero and revision 10. Do not silently fall back to revision 20; Open/Close/CreditRequest shapes differ. The capture later repeats Init at frame 6800; a minimal dedicated reader should not reproduce unexplained duplicate initialization.

### 3. Resolve EPSON-CTRL — observed

OUT frame 6774: `00 00 00 11 01 00 09 45 50 53 4F 4E 2D 43 54 52 4C`

IN frame 6777: `00 00 00 13 01 00 89 00 02 45 50 53 4F 4E 2D 43 54 52 4C`

Require result zero, service name EPSON-CTRL and socket 02 for this narrowly supported profile. EPSON-DATA resolves to 40 in this capture, but is unnecessary for this read; do not open a print-data channel or reproduce the failed XCTRL/DOWNCONTENT probes. Omitting those probes is a proposed minimal sequence, not a sequence independently captured in isolation.

### 4. Open control channel — observed

OUT frame 6822:

`00 00 00 11 01 00 01 02 02 02 00 02 00 00 00 00 00`

IN frame 6825:

`00 00 00 10 01 00 81 00 02 02 00 40 01 00 00 00`

This is the observed **revision-10** request (17 bytes), not the revision-20 variant. Proposed size fields are 0x0200 in each direction, with the last two 16-bit fields zero. The reply grants sizes 0x0040 host-to-printer and 0x0100 printer-to-host, and zero initial data-send credit. Validate returned socket pair, lengths and sizes; the 17-byte candidate read fits the observed 64-byte D4 limit. Do not equate that limit with the 512-byte USB endpoint size.

### 5. Obtain permission to send — observed, state dependent

OUT frame 6826 (also 6928): `00 00 00 0D 01 00 04 02 02 00 80 FF FF`

IN frame 6829 grants zero: `00 00 00 0C 01 00 84 00 02 02 00 00`

IN frame 6932 later grants one: `00 00 00 0C 01 00 84 00 02 02 00 01`

A zero grant means **do not send data**. The capture itself demonstrates why a fixed packet script is wrong. Track credits, use a small bounded wait/poll budget, and fail if none arrive. One data packet consumes one send credit. Keep command-channel credits distinct from data-channel credits. Do not copy public implementations' different CreditRequest layouts into this revision-10 trace.

### 6. Send the single EEPROM request — inferred, NOT observed

Candidate address 0x003C; 17-byte D4 bulk OUT payload:

`02 02 00 11 00 01 7C 7C 07 00 0A 5E 41 BE A0 3C 00`

Breakdown: six-byte D4 header; two-byte application marker; two-byte application payload length 7; two model parameter bytes; read triplet; two-byte address. The capture's corresponding status request at frame 6933 was `02 02 00 0B 00 01 73 74 01 00 01`. Replacing that application message with an EEPROM read is the unverified step. Do not also send the status query in this one-operation reader.

The 0x003C role is a static latest-error hypothesis, not a verified address meaning. A reply validates at most the read exchange and returned byte, not its diagnostic interpretation.

### 7. Grant reply credit — observed pattern

OUT frame 6935: `00 00 00 0B 01 00 03 02 02 00 01`

IN frame 6939: `00 00 00 0A 01 00 83 00 02 02`

This allows one printer-to-host data packet. Match result and socket pair. Keep a separate receive-credit ledger. A previously granted, unconsumed credit must not be granted twice. Dispatch incoming data and command packets by their headers; do not assume the next ReadFile completion is the expected command reply.

### 8. Receive and validate EEPROM data — inferred

Expected application grammar, from cross-model evidence: `@BDC PS\r\nEE:003Cvv;\f`, where `vv` is **two ASCII hexadecimal digits**, not a literal byte. With this exact 20-byte application reply, total D4 length is 26 (`0x001A`). Expected socket pair is 02/02. Treat returned credit as a field to account for, not a fixed constant to compare blindly.

Require exact address match, valid hex, terminator, bounded framing and a complete message. `||:41:NA;` is a rejection, not an EEPROM value. Empty, malformed, mismatched, truncated or timed-out responses remain unavailable. Preserve raw bytes. Never substitute zero or consume a late reply as the answer to a later query. Unsolicited ST2 is not an EEPROM reply.

The existing offline HistoryReply parser implements the strict candidate application grammar, not USB/D4 reassembly. Actual PP-100 response grammar remains unobserved and must not be loosened merely to make a reply pass.

### 9. Close the channel — observed pattern

OUT frame 6946: `00 00 00 0A 01 00 02 02 02 00`

IN frame 6949: `00 00 00 0A 01 00 82 00 02 02`

Close only a channel this operation successfully opened, and only while the protocol state is trustworthy. Log cleanup failure separately; do not invalidate preserved raw evidence or claim successful cleanup. Revision-10 close has the extra zero byte; do not substitute a revision-20 nine-byte request.

A D4 Exit command is described in public implementations (`00 00 00 07 01 00 08`, reply command 88/result zero), but was **not found in the inspected cap2 printer stream**. It is not a USB reset, yet its effects on Epson software's session have not been validated here. Keep full session-exit policy an explicit unresolved integration item rather than silently adding it to a supposedly observed recipe. No USB soft reset or mechanical reset is part of this specification.

## Implementation contract and outstanding verification

Implement a fixed operation ID, no public address/key/byte arguments, and no native send path callable from imported evidence. Before transmission, verify original-model identity, firmware, installed interface and session ownership. Use the existing isolated-worker/deadline pattern, cancellable I/O, bounded packet/message buffers, a maximum packet count, and one overall deadline covering setup, read and bounded cleanup. Handle short reads, coalesced packets, header length below six, over-limit lengths, unexpected sockets/replies, disconnects and cancellation without automatic restart or query replay. A partial timeout is not success. Log every request and actual response with stage, timestamps and provenance.

First build pure frame parsing/state-machine tests using the observed control transcript and a clearly labelled synthetic EEPROM response. Test zero-credit handling and each failure transition before adding native I/O. The specification is now detailed enough for that work, but a complete **verified** live transaction still requires Windows transport/ownership and cleanup validation plus the first PP-100 EEPROM response. Nothing in the existing captures establishes the stored component error.
