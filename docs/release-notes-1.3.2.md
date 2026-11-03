# v1.3.2 — fixed D4 status qualification

Adds **Evidence Toolkit → Analysis → Run one D4 status qualification**. It acquires exclusive access through the installed USB printer driver, sends the fixed cap2-derived status sequence once, captures raw host I/O, and attempts a channel close when state and deadline permit. There are no command/address/key inputs and no EEPROM queries, USB resets, print jobs or driver changes.

The session budget is 8 seconds; the isolated worker has a 15-second hard deadline. User cancellation stops the worker. Partial streamed evidence survives; interrupted cleanup is explicitly unconfirmed. Response bytes, failure details and cleanup outcome can be exported as analysis JSON. Raw progress is retained in the Communication Log and existing forensic bundle format.

Corrects bootstrap command-credit accounting so a missing Init credit cannot be replaced by an invented credit. Rejects unexpected buffered data before issuing another command. Automated tests cover authorization, streamed partial evidence, framing, credits, cancellation and cleanup. Hardware compatibility and the first live status qualification remain unverified.

See [Windows instructions](driver-preserving-transport.md#run-one-status-only-qualification-v132).
