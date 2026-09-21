# LFS Drift Tools v0.786.0921.alpha

This release is a stability and quality pass over the whole application. It adds no big new feature. It makes the app more reliable when files or packets are damaged, smoother to use, and safer when it touches your LFS folder and installs drivers. The main window source was also split into several files, which makes future changes easier.

Version scheme: `0.XYZ.MMDD`. X counts large new features, Y medium changes, rebuilds and smaller feature changes, Z bug fixes.

## Highlights

- Smoother mouse-wheel scrolling: half the previous step, eased animation, cards move in one pass and card backgrounds are cached.
- Safer **Configure LFS cfg**: it keeps every other byte of `cfg.txt` (including non-ASCII characters), validates OutSim Opts, writes atomically, keeps your original as `cfg.txt.bak`, and warns when LFS is running.
- The app now compares `cfg.txt` with its own InSim, OutGauge and OutSim settings on startup and warns about differences.
- Driver installers (ViGEmBus, HidHide) are started only after their digital signature is verified.
- Settings, rev limiter data and driver statistics survive damaged or half-written files.
- Rev limiter cut time and Controller Assist loop timing are now consistent and much closer to the configured values.

## Added

- Startup check of `cfg.txt` against the app settings (InSim port, OutGauge and OutSim mode, IP, port, OutSim Opts), with a status warning in five languages.
- Signature check for the bundled driver installers (publisher Nefarius Software Solutions).
- Warning after **Configure LFS cfg** when LFS is running, because the game may overwrite the change.
- Key and button binding conflicts are resolved automatically: assigning a key that another function already uses moves it and shows a status message.
- Debug builds check the translation dictionary at startup and show missing keys as `[key]`, and print the measured rev limiter cut timing (Debug only).
- Air Speed is translated to Polish, Turkish, German and Spanish.

## Changed

- Scrolling: wheel step reduced by 50%, eased animation, atomic movement of all cards, cached rounded card background, labels inside cards use the card color instead of transparency (the color follows the light and dark theme).
- Timer resolution is raised to 1 ms while the rev limiter or Controller Assist is running. Controller Assist now runs its loop at about 250 Hz instead of about 64 Hz. The rev limiter cut wait is exact instead of rounded to 15.6 ms steps. The real ignition-off time is about 40 ms plus the configured cut time.
- OutGauge and OutSim2 listen on `127.0.0.1` only (LFS and the app must run on the same computer, which is what **Configure LFS cfg** sets up).
- OutSim2 packets must match the size implied by OutSim Opts, otherwise they are rejected with a warning.
- Settings load in independent groups, are saved with a short delay and atomically, and are saved immediately on exit.
- Overlay renders into a persistent bitmap and updates the window only when the frame changes, and it slows down when nothing changes or when it is hidden.
- Device lists are refreshed on Windows device-change events instead of every 250 ms, and several background timers idle when there is nothing to do.
- Status text keeps at most 500 lines, and `crash.log` is capped at 1 MB.
- Main window code is split into `MainForm.*.cs`, `Controls`, `Settings` and helper files. The build has no compiler warnings from the code.

## Fixed

- Rev limiter: the ignition key can no longer be pressed twice by the watchdog and the cut at the same time, a failed key press no longer leaves the engine off, and a missing or invalid limit or cut time in the configuration no longer causes constant ignition cuts.
- InSim: a stale receive thread can no longer close a newer connection, and the receive buffer starts empty for every connection.
- OutGauge and OutSim2: receive loops survive errors in status handlers, and NaN or infinite values are dropped.
- Drift scoring: points for fast driving, drift and burnout are no longer truncated on every update, fast driving without drift no longer writes the driver file about five times per second, the drift side is remembered correctly across pauses, and 0.5 s drift ticks are exact.
- Driver files: written atomically, a damaged file is moved aside as `*.corrupt_<date>` instead of being overwritten with empty data, and driver names cannot produce reserved Windows file names.
- Controller Assist derivatives are calculated only from new OutSim2 samples and use the real sample interval.
- Turn signal keys work only while LFS is in the foreground and ignore key auto-repeat.
- Text set from background threads no longer touches controls before the window exists, and no longer throws while the app is closing.
- The key binding dialog now shows its confirmation text before it closes.
- Translations: two missing keys (raw key names were shown in wheel and driver installer messages), texts that did not refresh after a language change, and messages glued together with spaces.
- `cfg.txt` characters outside ASCII were previously replaced with a placeholder when **Configure LFS cfg** was used.
- Unhandled errors in a settings group, in an event handler or in a background timer no longer take down the rest of the app.

## Upgrade notes

- Requires Windows 10/11 and the .NET 6.0 Desktop Runtime, as before.
- If you tuned the rev limiter **Cut ms** by feel, check it again. Without Controller Assist the cut used to be about 15 ms longer because of timer rounding, and now it matches the value more closely.
- If your `cfg.txt` sends OutGauge or OutSim to a different IP than `127.0.0.1`, or your OutSim Opts differ from the app, run **Configure LFS cfg** or change the values so they match. Otherwise data is not received.
- Settings files from earlier versions are read as before. A file that is damaged is kept as `*.corrupt_<date>`.

## Known limitations

- The cause of the engine stalls seen with an earlier attempt to shorten the cut time was not found, so the cut time keeps its original meaning.
- The application targets .NET 6.0, which is out of support. Moving to a newer runtime is planned separately.
