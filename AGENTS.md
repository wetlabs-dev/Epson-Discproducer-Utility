# Project working instructions

- Keep the application strictly read-only. Never add driver replacement, print jobs, arbitrary command bytes, writes, resets, cleaning, calibration or movement operations.
- Treat PP-100 hardware compatibility as unverified until documented physical testing exists. Do not label enumeration or a driver-cached response as proven live USB traffic.
- Run relevant tests/builds for changes and update user-facing documentation when behavior changes.
- User preference: at the completion of every implementation prompt, commit and push the changes to `https://github.com/wetlabs-dev/Epson-Discproducer-Utility.git`. Do not force-push or overwrite unrelated work.
