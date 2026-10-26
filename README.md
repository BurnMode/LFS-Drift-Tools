# LFS Drift Tools

**Version v0.897.1001.alpha**

**LFS Drift Tools** is a companion application for [Live for Speed](https://www.lfs.net/). It connects to LFS through InSim, OutGauge and OutSim2 and adds drift scoring, collision detection, an in-game HUD, a transparent overlay, a rev limiter, turn signals and a steering assist for gamepads.

## Features

- **Drift scoring**
  Score based on drift angle, speed and combo multipliers, with best laps stored per track and layout. Stats are saved per driver and reload when you switch cars or spectate someone else. Laps, track/layout changes, pit stops and restarts are detected through InSim.

- **Overlay**
  A transparent overlay drawn over the LFS window: drift angle, combo countdown, bonus texts, rev limiter status and the speedometer/tachometer gauge.

- **Speedometer and tachometer**
  A circular gauge with smoothed speed, gear and an RPM arc calibrated to the car's redline. Three preset slots store the whole look (visibility, colors, redline thickness, optional background image with pan/zoom/opacity). Presets 1 and 2 use the TACHOTYPE1 renderer. Preset 3 uses TACHOTYPE2: a thick RPM band that shifts from white to green, yellow and red, the gear in a matching circle, tall speed digits and fuel (left) and turbo boost (right) rings around the gear. LFS does not report a maximum boost, so the boost scale starts at 1.0 bar and grows to the highest boost seen, remembered per car; the ring is hidden for cars without a turbo. It uses the open-source fonts Big Shoulders Display (speed) and Doto (gear), both under the SIL Open Font License 1.1 (license files in `Data/Fonts`); they can be replaced by other files named `tachotype2_speed.ttf` and `tachotype2_gear.ttf`. All three presets show telltales for turn signals, high beam, handbrake, TC and ABS inside the dial (they can be hidden in UI elements, where the speed unit can also be centered over the speed). KPH/MPH toggle, per-element colors, adjustable position and scale. Shown only while OutGauge data is arriving.

- **Rev limiter**
  Ignition-cut limiter driven by OutGauge RPM, with adjustable cut time, auto-calibration for new cars and key/wheel-button bindings for toggle, calibrate, increase and decrease.
  Limits are stored per car. On first launch the app loads presets for all stock LFS cars and a few popular mods. A car can be flagged as not supported, which disables the limiter for it. This happens automatically when a car reports more than 20000 RPM, and the flag can be changed manually.

- **Controller Assist**
  Steering assist for Xbox/XInput gamepads. The app reads your physical pad, applies counter-steer based on rear slip angle from OutSim2, and passes the result to LFS through a virtual Xbox 360 pad (ViGEmBus).
  - Presets: Off, Race, Street and Drift.
  - An **Assist character** slider on top of any preset, from Stability to Drift (centre is neutral, double-click resets it). Towards Drift it raises the assist gain and strength, allows more steering at speed, and in the Drift preset allows larger angles with a sharper angle hold. Towards Stability it catches slides earlier, narrows high-speed steering and smooths the output, and in the Drift preset limits the angle and softens the angle hold.
    - **Race** catches slides early and firmly and narrows the steering at high speed, for grip driving.
    - **Street** is built on Race but leaves room for power slides and light drifting: the assist steps in later and softer, and spin catch and the throttle cut only react once the slide goes past the allowed angle.
    - Race and Street can also limit how fast the stick turns the wheels at speed, keep the front tyres at their grip limit, reduce steering under braking, steady the car when you let go of the stick, and catch the start of a spin. Optional throttle help on the right trigger adds traction control, cuts the throttle when the car starts to rotate and builds it up gently while you steer; throttle is only ever reduced.
    - **Drift** works like the assist in arcade drift games: you steer normally to start a slide, then the stick picks the drift angle and the assist holds it. The hand-over from direct steering to the angle hold, the maximum angle, the angle curve, the hold strength and its smoothing are all adjustable. An optional spin catch adds countersteer when the whole car starts to over-rotate.
  - The Steering Tuning window shows the settings that apply to the selected preset type, grouped into input, countersteer or drift angle hold, and output. The **?** button explains each slider.
  - A drift log can be recorded from the Steering Tuning window. It saves every assist step to a CSV file in the `AssistLogs` folder next to the app, numbers each drift on the overlay, and writes a summary with the settings used and a notes line per drift. The folder keeps the last 30 recordings (at most 200 MB), older ones are removed when a new recording starts.
  - Force-feedback style vibration from load, suspension, impacts, surface changes and gear shifts.
  - Optional hiding of the physical pad from other apps with HidHide, so LFS sees only the virtual pad.
  - Steering bar overlay in the game window.
  - A bindable hotkey next to the main switch. It toggles Steering Assist only, so the virtual pad stays connected and you don't have to reselect the device in LFS. Vibration keeps working.
  - LFS reads controllers only at startup. If LFS was already running, the app shows a notice with the exact name of the virtual pad to select in Options > Controls, after refreshing the controller list.
  - ViGEmBus and HidHide installers are bundled and can be started from the app. The app verifies the installer's digital signature (publisher Nefarius Software Solutions) before it asks Windows for administrator rights, and refuses to run a file that fails the check.

- **Turn signals and hazards**
  Steering-based automatic cancel, hazard lights and light toggle, with keyboard and wheel/gamepad bindings. Left/right signal and high beam telltales come from OutGauge.

- **Burnouts and bonuses**
  Wheelspin detection at low speed, scored in tiers (Good/High/Extreme/Insane). Bonuses for a full 360° burnout spin, donuts, clean laps, cleanly grazing a track object and smooth burnout-to-drift transitions. While the car is upside down or tumbling (rolling fast sideways or end over end), drift, fast driving and burnout detection is paused, together with bonuses, and it resumes 2 seconds after the car settles (requires OutSim2).

- **Collision detection**
  Car-to-car and car-to-object contacts from InSim. A light touch that doesn't break a drift gives a KISS bonus, a hard hit costs points depending on the impact tier. Car and object collisions can be enabled separately.

- **Achievements**
  Hidden challenges and easter eggs with funny descriptions and their own icons (speed, drifting, cones, car contacts, flights, roof landings and a few secrets). Each one is unlocked separately for wheel, gamepad and keyboard & mouse, depending on which controller was used recently. An unlock slides up from the bottom of the overlay (or, with the InSim HUD on, as a card of native LFS buttons in the same place) with an optional sound. Progress is kept in `Configs/achievements.json` and works online and offline.

- **Wheels and gamepads**
  Device detection and axis calibration through DirectInput, with a countdown during automatic axis calibration. Xbox/XInput pads get their steering axis assigned automatically.

- **In-game HUD (IS_BTN)**
  Score, run points, combo multiplier and drift labels as native InSim buttons, with an in-app color picker.

- **Settings**
  Bindings, calibration, colors and options are saved locally and restored on startup. Options that are turned off automatically because of missing data (no pad, no OutSim2, no OutGauge) return to your last choice when the data is back.
  Files are written atomically with a `.bak` copy. A file that cannot be read is moved aside as `*.corrupt_<date>` and defaults are used, and one broken group of settings does not stop the others from loading. Settings files from an incompatible older version are moved aside as `*.old_<date>` and defaults are used. Rev limiter values outside the allowed range are replaced with safe defaults on load.
  Driver scores, lap records and achievements (`Saves/<driver>.dat`, `Configs/achievements.dat`) are saved encrypted, so they cannot be changed with a text editor or copied to another driver. This stops casual editing only: the app is open source and nothing stored on your own computer can be made fully cheat-proof.
  Assigning a key or button that is already used by another function moves it to the new function and tells you in the status text.

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (if it is missing, Windows shows a message with a download link when you start the app)
- [Live for Speed](https://www.lfs.net/) with InSim, OutGauge and, for Controller Assist, OutSim2 configured (see below)
- For Controller Assist: an Xbox/XInput gamepad, ViGEmBus, and optionally HidHide
- A DirectInput/XInput device for wheel/pad bindings (optional)

## Supported languages

English, Polish, Turkish, German, Spanish.

The texts are plain JSON files in `Data\Lang` next to the app (`en.json`, `pl.json`, `tr.json`, `de.json`, `es.json`), so translations can be corrected without rebuilding. A missing text or a broken file falls back to English.

## Configuring LFS

The app uses three connections. Each one enables its own features.

| Connection | Default port | Used for |
|---|---|---|
| InSim | 29999 | scoring, HUD, laps, collisions, turn signals |
| OutGauge | 35555 | speedometer, rev limiter, burnouts |
| OutSim2 | 30020 | Controller Assist, upside-down and rollover detection |

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

Start with the **Diagnostics** button in the InSim Connection card. It checks whether the app can save its files, whether the app folder is in a problematic place (Program Files, OneDrive, a temporary folder or inside a zip), whether LFS runs with the same rights as the app, whether the OutGauge and OutSim ports are free (and which program uses them if not), the connection to LFS, `cfg.txt`, the Controller Assist drivers, programs that take over the gamepad or use the same telemetry ports, whether LFS runs in full screen (the overlay may be hidden), the installed antivirus, errors logged in this session and whether key bindings can work. Missing OutGauge or OutSim2 data is only reported while you drive your own car: in menus, the garage, replays or while watching another car LFS does not send it, which is normal. Each problem comes with a short fix. The window opens by itself at startup when it finds an error; warnings only appear in the status text. **Copy report** puts a summary on the clipboard for bug reports (your user name is removed from paths).

### Windows warns about the app

- **"Windows protected your PC" (SmartScreen):** alpha builds are not code-signed yet, so Windows does not know the publisher. Click **More info**, then **Run anyway**.
- **Before extracting the downloaded zip:** right-click it, **Properties**, tick **Unblock**, then extract. Otherwise every extracted file keeps the "downloaded from the internet" mark.
- **Antivirus removes or blocks the app:** the app watches the keyboard for your key bindings (only while LFS is running and a key is bound) and sends keys to LFS (rev limiter, turn signals). Some antivirus programs treat this as suspicious. Restore the file from quarantine, add an exception for the app folder and, if you can, report the false positive to the antivirus vendor.

### Settings, scores or achievements are not saved

The app stores its files next to `LFSDriftTools.exe` (`Configs`, `Saves`, `AssistLogs`), so the folder must be writable:

- Do not put the app in `C:\Program Files` or in a folder synced by OneDrive or similar.
- If the app is in Documents or on the Desktop and saving fails, Windows Defender **Controlled folder access** may be blocking it. Allow the app in Windows Security or move the folder, for example to `C:\Games\LFS Drift Tools`.

### Rev limiter, turn signals or key bindings do nothing

- Run LFS and the app with the same rights: both normally, or both as administrator. Windows does not let a normal program send keys to a program running as administrator.
- Some antivirus programs block keyboard watching: add an exception for the app folder.

### No data from LFS

- If the InSim switch turns itself off, the connection failed. The status text at the bottom of the window shows the reason.
- If the OutGauge or OutSim2 status stays empty although LFS is running, check that `cfg.txt` uses `127.0.0.1` for both IP lines and the same ports as the app, and that no other program (for example SimHub or another telemetry tool) uses those ports. If a port is taken, choose another one in the app and let it update `cfg.txt`, then restart LFS.

### Controller Assist behaves oddly

- Steam Input, DS4Windows, x360ce and similar tools also take over the gamepad. Turn them off for LFS while using Controller Assist.
- If LFS was already running when the virtual pad was created, select the virtual pad in LFS under Options > Controls.

### The overlay is not visible

- The overlay is drawn on top of the LFS window. If LFS runs in exclusive full screen, switch LFS to windowed mode in its display options.

### Reporting a problem

Errors are written to `crash.log` next to the executable, including errors the app recovers from (up to 1 MB, then the file is moved to `crash.log.old`). Please attach it to bug reports. A crash with no entry there is usually a native error: check Windows Event Viewer, Windows Logs > Application.

If the app reports that a settings file could not be read, the damaged file is kept next to the original as `*.corrupt_<date>`. The previous good version is usually available as `*.bak`.

If a score or achievements file was changed outside the app or is damaged, it is kept as `*.rejected_<date>` and the last valid backup copy is loaded instead; without one, progress starts from zero. Saves of the official version cannot be read by a build made from the source code and the other way round, because they use different keys.

## Disclaimer

This project uses the official InSim, OutGauge and OutSim protocols and is intended for personal use. It is not affiliated with or endorsed by the developers of Live for Speed.
