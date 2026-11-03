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

Offline research now traces the original-model library dependencies and identifies missing configuration. See [PP-100 dependency trace and implementation specification](research/pp100-eeprom-dependency-trace.md). It does not enable or authorize hardware queries.

## Fixed-purpose offline history reader

The `history` CLI command and **Evidence Toolkit → Analysis → History read readiness** show four concrete unresolved gates: PP-100 R-code parameters, framing/session selection, a verified reply fixture, and the firmware-specific history map. Live EEPROM transmission remains disabled. This is an offline evidence reader, not a completed live hardware reader.

On Windows 11, open PowerShell in the extracted application directory:

```powershell
.\discdiag-cli.exe history --json
.\discdiag-cli.exe history .\history-evidence.json --json
```

Both commands return exit **5** (incomplete/unverified), including when candidate values parse. Invalid evidence returns 3; file access errors return 4. No-file mode lists gaps. The GUI's **Read offline history evidence…** opens the same input; **Export analysis JSON…** saves results. Inputs and exports contain unredacted bytes; keep them private as appropriate.

Input is a maximum 64 KiB JSON file with exact schema 1, model `PP-100`, firmware `WLP00105`, a 64-character `SourceSha256` identifying the original evidence, and up to 64 `Exchanges`. Each exchange has unique `Id`, `RequestHex`, and `ResponseHex`. These must be manually extracted **application messages**, without USB/D4 wrappers. The source hash/identity and pairing are analyst-supplied references, not independently verified by this reader. Do not fabricate missing replies or use this schema to describe another firmware.

Synthetic format example (not printer evidence, not a command to transmit; `1234` is deliberately a placeholder):

```json
{
  "SchemaVersion": 1,
  "Model": "PP-100",
  "Firmware": "WLP00105",
  "SourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
  "Exchanges": [{
    "Id": "synthetic-only",
    "RequestHex": "7C7C0700123441BEA03C00",
    "ResponseHex": "404244432050530D0A45453A3030334330313B0C"
  }]
}
```

Only candidate addresses `003C`, `0109`, `0119`, `0129`, `0139`, and `00F8` are recognized, from the static original-model history trace. Their meanings remain hypotheses. The reader validates the read opcode, exact application-message length, addressed `EE` reply framing and address match. It never builds or sends requests. Different PP-100 reply variants may be rejected until actual fixtures establish their format. Missing/rejected replies stay unavailable; conflicting or mixed-profile observations do not yield a combined value. No value is automatically mapped to the error-code reference. Raw rejected messages and evidence counts remain in the result.

The existing cap1/cap2 investigations have not established such a history exchange, so those captures cannot currently supply this input. The next prerequisite is a legitimate original PP-100 parameter profile plus an independently verified history-read exchange—not another ordinary status capture. See [cross-model findings](research/cross-model-read-queries.md).

Research update: a public original-model profile now supplies candidate read bytes **`0A 5E`** and two-byte addressing. These remain unverified on WLP00105; live transmission is still disabled. See [the sourced candidate and remaining validation](research/pp100-read-parameter-candidate.md). No additional ordinary status capture is needed to establish this candidate.

Transport implementation update: the [driver-preserving foundation and Windows exclusive-access probe](driver-preserving-transport.md) are available. The fixed status-only D4 qualification is enabled in v1.3.2; EEPROM operations remain outside the live worker allowlist. Probe success is not proof of clean firmware session ownership.
