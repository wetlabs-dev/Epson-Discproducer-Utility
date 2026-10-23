# Capture correlation and evidence

USBPcap pseudoheaders are little endian regardless of PCAPNG section endian. The base header is 27 bytes: length at 0, IRP ID at 2, USBD status at 10, URB function at 14, information at 16, bus at 17, address at 19, endpoint at 21, transfer at 22, payload length at 23. Control stage is byte 27. Stage 0 supplies setup; stage 1 is legacy data; 2 is legacy status; 3 is modern completion. Information bit 0 describes PDO/FDO direction, distinct from USB payload direction.

Correlation includes section/interface/bus/address/endpoint/transfer/IRP ID. Duplicate pending IDs are ambiguous. Orphan stages and truncated payloads remain visible. Control data stages are grouped with the matched request and completion; bulk grouping stops at an individual URB. Application-level messages are never invented. No capture is replayed.

Find device candidates in Research checks successful captured device descriptor operations and compares VID/PID. A match is only a candidate: identical devices, reconnections and address reuse cannot be resolved automatically. Windows hub connection index is never used as a USBPcap device address. Select a capture record and the intended Windows node, enter supporting observation text, and use manual association if necessary. The session records capture hash, section, interface, bus/address, selected node, evidence and manual/unverified status. It does not promote the association to verified or silently apply it across sessions.

Control filter searches setup packet hex (spaces ignored). Existing endpoint, transfer, direction, time and text/hex filters remain available. Completion rows may not repeat setup; select the request and inspect its related packet.

Independent parser validation uses Wireshark's public `usb_u3v_sample.pcapng` (110 camera records) against TShark 4.2.6 output, including legacy control stages. This is not PP-100 evidence. Tests also exercise malformed/truncated blocks, byte orders, reused URBs and section isolation. Limits remain 128 MiB, 32 MiB per block, 100,000 packets and 10,000 displayed matches.
