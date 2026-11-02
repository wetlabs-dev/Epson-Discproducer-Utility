# Cross-model Epson read-query investigation

Date: 2026-11-02. Offline research only; no hardware commands or application changes.

## Conclusion

A genuine EEPROM-read command family is supported by Epson-authored documentation, public implementations and the owner's PP-100 library. The uncertainty is not simply whether every unknown command could be a write. It is whether we can construct the correct complete PP-100 exchange, including model parameters, transport, addressing and response validation. A fixed-purpose history reader remains plausible; an enabled live reader is not yet justified by this evidence.

## Source comparison

| Models/family | Evidence | Relevance and limits |
| --- | --- | --- |
| LX-300+II / LX-1170II | Epson service manual Revision D, section 1.5.3.2, Table 1-39, printed p.34: EEPROM request uses `7C 7C 06 00 r1 r2 41 BE A0 address`; example R codes differ: `A8 5A` versus `A8 6A`. Reply contains `@BDC PS` and `EE:`. | Manufacturer documentation of the same read structure found in the PP-100 DLL; these are dot-matrix model examples, not PP-100 parameters. [Epson-authored manual, third-party mirror](https://manuals.plus/m/67acf7fe77e81ac4d42ca7ee7f5b768e0b0395e23a6d29db96ab0aa90b8e6cdd.pdf). |
| Stylus Photo R220 | Contributor's USB trace shows parameter bytes `00 00` returning `||:41:NA;`, then `10 3B` returning an `EE:0000;` record for address zero. | Primary field report of extended read framing and model-parameter sensitivity; not a PP-100 test or safety guarantee. [Original trace](https://github.com/lion-simba/reink/issues/10). |
| Stylus Photo R800 / PX-G900 | Epson programming guide documents remote-mode uppercase `ST` enabling/disabling automatic status replies. | A status-related command can change communication settings. Do not confuse it with a one-shot query or copy a print-job initialization sequence. [Epson-authored guide, mirror, section 6.1.6](https://manualzz.com/doc/986673/epson-px-g900--stylus-photo-r800-ink-jet-printer-programm...). |
| Epson TM receipt printers | Official ESC/POS reference documents `DLE EOT` status queries and model-dependent `FS g 2` NV user-memory reads (obsolete). | Genuine read operations, but a different command family and memory purpose. No basis for sending these to the PP-100. [Status](https://download4.epson.biz/sec_pubs/pos/reference_en/escpos/dle_eot.html), [NV user memory](https://download4.epson.biz/sec_pubs/pos/reference_en/escpos/fs_lg_2.html). |

The public [epson_print_conf source](https://github.com/Ircama/epson_print_conf/blob/main/epson_print_conf.py), inspected on the date above, supplies these configurations. These are implementation claims, not independently tested hardware here. Values below are byte order as stored, in hexadecimal:

| Model | Read parameter bytes |
| --- | --- |
| L355 | `41 09` |
| L386 | `10 08` |
| XP-205 | `19 07` |
| ET-4700 | `97 07` |
| Stylus Photo PX720WD | `36 06` |
| Stylus Photo PX730WD | `77 08` |
| WF-7525 | `65 00` |

Its L386 and PX730WD fatal-history address lists differ substantially. No PP-100 profile was found in this inspected source. Related repositories frequently inherit the same databases; model count is not independent validation count.

The [jxnata implementation documentation](https://github.com/jxnata/epson-ink-pad-reset/blob/master/README.md) distinguishes the `41 BE A0` read family from a different write family and reports an L120 USB test. It also distinguishes configured models from hardware-tested models. Its reset programs are not appropriate tools to run for this investigation; no code from them was executed.

## Meaning for this PP-100

The [local dependency trace](pp100-eeprom-dependency-trace.md) already identifies both single- and extended-address read templates with `41 BE A0`. The external evidence strengthens their identification as EEPROM reads. It does not resolve:

1. The PP-100's two configured `pnRNumber` bytes. The supplied archive lacks `Parameter.ini`; default zeros are not established device parameters.
2. The intended address-width/mode choice and complete minimal exchange through the installed Epson driver.
3. Which stored locations and code meanings apply to printer firmware WLP00105. Static candidates and service-manual addresses remain unreconciled.
4. A verified PP-100 EEPROM response schema, including address association and failure replies.

The [cap2 analysis](cap2-analysis.md) establishes working Epson status traffic and an observed transport-entry exchange. The [installed-software trace](installed-tdm-analysis.md) establishes that `GetFatalCode` returns a software cache, not a fresh EEPROM history read. Neither substitutes for an EEPROM exchange.

## Risk distinction and proposed implementation boundary

Sending request bytes through an OUT endpoint or a Windows write API is necessary even for many read-only queries. It does not itself mean writing EEPROM. A correct read command is intended to retrieve data, not modify calibration or counters.

Incorrect protocol framing, channel selection or initialization can nevertheless alter communication state, stall a session, interfere with Epson software or feed bytes to the wrong interpreter. A plausible response can also be misattributed or decoded against the wrong model's memory map. No quantified probability of physical damage is established here; it would be misleading to claim that a wrong R code necessarily becomes a write. The R220 report instead shows a rejected read for one wrong value, which is useful but not universal proof.

Next implementation work should stay offline: test a fixed serializer/parser against captured or documented exchanges, with no public arbitrary-byte sender. Before enabling transmission, obtain an original PP-100 parameter profile and an independently verified exchange (a legitimate existing capture can suffice). Restrict the eventual reader to identified firmware/model, one verified transport, a small allowlist of history addresses, bounded timeouts, strict response validation, and explicit failures. Exclude key guessing, address sweeps, resets, print jobs, status-configuration changes and fallback probing. Preserve the Epson driver. Report raw values separately from tentative error meanings.

The immediate usable read-only route remains the existing identification/port-status queries and passive decoding of Epson's status traffic. These confirm the generic error but have not identified the failing component. Repeating them is not a substitute for resolving the history profile.
