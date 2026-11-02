# Candidate history reader — unreleased

Adds a fixed-purpose **offline** PP-100 WLP00105 history-evidence reader in the GUI and CLI, and a four-gap readiness report. Live EEPROM queries remain unavailable. Recognized addresses and reply grammar are explicitly hypotheses; no error-code meanings are inferred.

Evidence inputs are bounded, schema checked, and hashed. Rejected exchanges retain raw data. Missing replies remain unavailable; conflicts and mixed read profiles suppress combined values. No arbitrary command sender, native transport, driver changes or printer operations were added. Existing diagnostic bundle formats are unchanged.

See [Windows usage and remaining prerequisites](fatal-error-history.md#fixed-purpose-offline-history-reader). This does not deliver a live history reader: verified original-model parameters, framing, reply evidence and address semantics are still required.
