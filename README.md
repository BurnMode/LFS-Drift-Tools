# LFS Drift Tools

**Version v0.786.0921.alpha**

**LFS Drift Tools** is a companion application for [Live for Speed](https://www.lfs.net/). It connects to LFS through InSim, OutGauge and OutSim2 and adds drift scoring, collision detection, an in-game HUD, a transparent overlay, a rev limiter, turn signals and a steering assist for gamepads.

## Features

- **Drift scoring**
  Score based on drift angle, speed and combo multipliers, with best laps stored per track and layout. Stats are saved per driver and reload when you switch cars or spectate someone else. Laps, track/layout changes, pit stops and restarts are detected through InSim.

- **Overlay**
  A transparent overlay drawn over the LFS window: drift angle, combo countdown, bonus texts, rev limiter status and the speedometer/tachometer gauge.

- **Speedometer and tachometer**
  A circular gauge with smoothed speed, gear and an RPM arc calibrated to the car's redline. Two preset slots store the whole look (visibility, colors, redline thickness, optional background image with pan/zoom/opacity). KPH/MPH toggle, per-element colors, adjustable position and scale. Shown only while OutGauge data is arriving.

- **Rev limiter**
  Ignition-cut limiter driven by OutGauge RPM, with adjustable cut time, auto-calibration for new cars and key/wheel-button bindings for toggle, calibrate, increase and decrease.
  Limits are stored per car. On first launch the app loads presets for all stock LFS cars and a few popular mods. A car can be flagged as not supported, which disables the limiter for it. This happens automatically when a car reports more than 20000 RPM, and the flag can be changed manually.

- **Controller Assist**
  Steering assist for Xbox/XInput gamepads. The app reads your physical pad, applies counter-steer based on rear slip angle from OutSim2, and passes the result to LFS through a virtual Xbox 360 pad (ViGEmBus).
  - Presets: Off, Light, Medium, Strong, and adjustable trims for counter-steer and assist strength.
  - Force-feedback style vibration from load, suspension, impacts, surface changes and gear shifts.
  - Optional hiding of the physical pad from other apps with HidHide, so LFS sees only the virtual pad.
  - Steering bar overlay in the game window.
  - A bindable hotkey next to the main switch. It toggles Steering Assist only, so the virtual pad stays connected and you don't have to reselect the device in LFS. Vibration keeps working.
  - LFS reads controllers only at startup. If LFS was already running, the app shows a notice with the exact name of the virtual pad to select in Options > Controls, after refreshing the controller list.
  - ViGEmBus and HidHide installers are bundled and can be started from the app. The app verifies the installer's digital signature (publisher Nefarius Software Solutions) before it asks Windows for administrator rights, and refuses to run a file that fails the check.

- **Turn signals and hazards**
  Steering-based automatic cancel, hazard lights and light toggle, with keyboard and wheel/gamepad bindings. Left/right signal and high beam telltales come from OutGauge.

- **Burnouts and bonuses**
  Wheelspin detection at low speed, scored in tiers (Good/High/Extreme/Insane). Bonuses for a full 360° burnout spin, donuts, clean laps, cleanly grazing a track object and smooth burnout-to-drift transitions. Bonuses are not counted while the car is upside down (requires OutSim2).

- **Collision detection**
  Car-to-car and car-to-object contacts from InSim. A light touch that doesn't break a drift gives a KISS bonus, a hard hit costs points depending on the impact tier. Car and object collisions can be enabled separately.

- **Wheels and gamepads**
  Device detection and axis calibration through DirectInput, with a countdown during automatic axis calibration. Xbox/XInput pads get their steering axis assigned automatically.

- **In-game HUD (IS_BTN)**
  Score, run points, combo multiplier and drift labels as native InSim buttons, with an in-app color picker.

- **Settings**
  Bindings, calibration, colors and options are saved locally and restored on startup. Options that are turned off automatically because of missing data (no pad, no OutSim2, no OutGauge) return to your last choice when the data is back.
  Files are written atomically with a `.bak` copy. A file that cannot be read is moved aside as `*.corrupt_<date>` and defaults are used, and one broken group of settings does not stop the others from loading. Rev limiter values outside the allowed range are replaced with safe defaults on load.
  Assigning a key or button that is already used by another function moves it to the new function and tells you in the status text.

## Requirements

- Windows 10/11
- [.NET 6.0](https://dotnet.microsoft.com/download/dotnet/6.0) or later (Windows Desktop Runtime)
- [Live for Speed](https://www.lfs.net/) with InSim, OutGauge and, for Controller Assist, OutSim2 configured (see below)
- For Controller Assist: an Xbox/XInput gamepad, ViGEmBus, and optionally HidHide
- A DirectInput/XInput device for wheel/pad bindings (optional)

## Supported languages

English, Polish, Turkish, German, Spanish.

## Configuring LFS

The app uses three connections. Each one enables its own features.

| Connection | Default port | Used for |
|---|---|---|
| InSim | 29999 | scoring, HUD, laps, collisions, turn signals |
| OutGauge | 35555 | speedometer, rev limiter, burnouts |
| OutSim2 | 30020 | Controller Assist, upside-down detection |

### Automatic

Set the LFS game folder in the app and click **Configure LFS cfg**. The app changes only the lines below in `cfg.txt` and keeps every other byte of the file as it was, including non-ASCII characters. The first time it saves the original file as `cfg.txt.bak` (an existing backup is never overwritten), and the new file is written atomically. Close LFS before you run it, otherwise the game may overwrite the change. Restart LFS afterwards.

```
InSim 29999
OutGauge Mode 1
OutGauge Delay 1
OutGauge IP 127.0.0.1
OutGauge Port 35555
OutGauge ID 0
OutSim Mode 2
OutSim Delay 1
OutSim IP 127.0.0.1
OutSim Port 30020
OutSim ID 0
OutSim Opts bf
```

The ports and OutSim Opts follow the values set in the app. OutSim Opts must be a hexadecimal number (for example `bf`), otherwise nothing is written.

OutGauge and OutSim2 packets are accepted only on `127.0.0.1`, so LFS and the app must run on the same computer. On startup, and after you change the LFS folder, the app compares the InSim, OutGauge and OutSim lines in `cfg.txt` with its own settings and shows a warning in the status text when they differ.

### Manual

- InSim: type `/insim <port>` in LFS, or start LFS with `LFS /insim=<port>`. Enter the same host and port in the app and click **Connect**.
- OutGauge and OutSim2: edit `cfg.txt` as shown above.

`cfg.txt` is read only when LFS starts, so restart LFS after every change. The OutSim Opts value in the app must match `OutSim Opts` in `cfg.txt` exactly. OutSim2 packets whose size does not match the Opts are rejected, and the status text shows a warning about the mismatch.

### Checking the connections

- If the InSim switch turns itself off, the connection failed. The status text at the bottom of the window shows the reason.
- If InSim works but the gauge never appears, OutGauge is not reaching the app. Check `cfg.txt` and restart LFS.
- If Controller Assist stays locked, check that OutSim2 is on and packets arrive.

## Getting started

1. Launch **LFS Drift Tools**.
2. Configure LFS as described above and restart it.
3. Enter host, port and admin password (if set), then click **Connect**.
4. Set up the rev limiter, HUD, overlay and bindings in the UI.
5. For Controller Assist: install ViGEmBus from the app, connect the pad, turn on OutSim2 and the main switch. If LFS was already running, select the virtual pad in Options > Controls.

## Troubleshooting

Unhandled exceptions are written to `crash.log` next to the executable (up to 1 MB, then the file is moved to `crash.log.old`). A crash with no entry there is usually a native error. Check Windows Event Viewer, Windows Logs > Application.

If the app reports that a settings or driver file could not be read, the damaged file is kept next to the original as `*.corrupt_<date>`. The previous good version is usually available as `*.bak`.

If the OutGauge or OutSim2 status stays empty although LFS is running, check that `cfg.txt` uses `127.0.0.1` for both IP lines and the same ports as the app, and that no other program uses those ports.

## Disclaimer

This project uses the official InSim, OutGauge and OutSim protocols and is intended for personal use. It is not affiliated with or endorsed by the developers of Live for Speed.
