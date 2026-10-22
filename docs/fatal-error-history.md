# What is needed for PP-100 fatal-error history

IEEE 1284 identification, Windows PnP metadata and standard printer-class status do not define a PP-100 fatal-error history facility. They cannot legitimately be interpreted as EEPROM or service-history access. This version implements no service command, EEPROM read, write, reset or maintenance cycle.

Before adding history retrieval, obtain:

1. A model/firmware-specific Epson SDK or service protocol reference identifying the read-only history operation, device/interface endpoint, framing, lengths, sequence numbers/checksums and response schema.
2. Evidence that the operation does not clear history, change service mode, move mechanisms or write persistent storage. A command called “read” is insufficient evidence if it requires a state-changing setup sequence.
3. A supported way to reach that protocol while preserving Epson's driver: vendor API, bidirectional monitor, or a separately exposed interface. A spooler job is not a substitute for a vendor control channel.
4. Representative response fixtures for empty/full/corrupt history and known errors across relevant firmware revisions. Establish record boundaries, ordering, wraparound, timestamps and error-code meanings.
5. Owner-authorized controlled validation on a non-production PP-100, with before/after checks and reference output from supported tooling. Do not infer memory addresses from other Epson models.

If proprietary documentation is unavailable, a separately authorized passive traffic study of an official read-history action could establish request/reply candidates. Every command still needs individual safety review; passive capture alone does not prove a replay is harmless. No capture is performed by this utility.

The existing transport/protocol separation, command registry, raw-response logs and mock fixtures provide the integration points once that evidence exists. Any future state-changing maintenance feature would require a different explicit authorization/safety design and is outside version 1.
