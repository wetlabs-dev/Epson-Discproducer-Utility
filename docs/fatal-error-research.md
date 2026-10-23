# PP-100 research targets: fatal history and waste ink

Neither target is implemented. No EEPROM reads, vendor requests, resets or writes are authorized by v1.2.

## Last five printer fatal errors

Needed evidence: exact model/firmware, documented read-only service operation or independently validated trace, transport/interface requirements, request/response framing, lengths/checksums, return status, and multiple samples with independently known historical entries. A passive Total Disc Monitor status refresh may expose only current status, not history. Do not interpret a changing byte as a fatal-error code.

Safety review must establish that the entire operation (including any unlock/initialization sequence) is read-only. Observed traffic alone is insufficient. Historical records may need ordered slots, validity flags, timestamps/indexes, endianness and code lookup tables. Validate ordering and persistence across observations against an independent trusted readout; never induce faults or clear history to test. If fewer than five valid records exist, report that fact without inventing entries.

## Waste-ink counter inspection

Separate research target: read-only counter value, units, scaling, capacity reference and applicable firmware/model. A generic EEPROM offset from another Epson printer is not evidence. Require documented semantics and independent reference readings. Do not send speculative EEPROM requests. Counter resets, EEPROM modifications, cleaning, ink charging and mechanical movement remain outside the diagnostic framework; no maintenance-write extension point is exposed.

## Evidence submission

Provide a technical diagnostic bundle first, the actual model/driver versions and observed condition, then optionally an explicitly approved forensic bundle plus session/capture pair. Describe what the operator did and when. Preserve SHA-256 hashes. Compare an idle/ready condition with an independently observed internal-error condition without actively causing an error. Redacted exports intentionally cannot reconstruct raw protocol data.
