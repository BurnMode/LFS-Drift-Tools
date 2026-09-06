# LFS Drift Tools

**Version 0.711.0906**

**LFS Drift Tools** is a companion application for [Live for Speed](https://www.lfs.net/) that turns your drift sessions into a more fun experience. It connects to LFS via InSim/OutGauge and adds real-time scoring, collision detection, an in-game HUD, a transparent overlay, a configurable rev limiter, and a smarter turn signal system, all wrapped in a clean, modern UI.

## Key Features

- **DriftEngine - Real-time Scoring Engine**
  Calculates drift score based on angle, speed, and combo multipliers, with per-track and per-layout best lap tracking. Stats are saved per driver and reload instantly when you switch cars or spectate someone else. Automatically detects laps, track/layout changes, pit stops, and race restarts via InSim events.

- **Forza Horizon-style Overlay**
  A transparent, layered on-screen overlay rendered over the LFS window, showing drift angle, combo countdown, bonus text animations, rev limiter status, and a circular speedometer/tachometer gauge.

- **Speedometer & Tachometer Gauge**
  A Forza-style circular gauge showing live (smoothed) vehicle speed, current gear, and an RPM arc that auto-calibrates to the car's redline, with a redline-thickness slider that grows inward so the ring never spills past the dial edge. Two independent preset slots store the whole look — visibility toggles, colors, redline thickness, and an optional background image with its own pan/zoom/opacity — switchable on the fly via arrows next to the live preview, each with its own one-click reset to defaults. Repositionable and scalable, with a KPH↔MPH toggle, per-element color customization, and drop shadows on every readout, appearing automatically whenever real OutGauge data is flowing.

- **Rev Limiter**
  A configurable ignition-cut rev limiter driven by OutGauge RPM data, with adjustable cut duration, auto-calibration, and dedicated keyboard/wheel-button bindings for toggle, calibrate, increase, and decrease actions.

- **Turn Signal & Hazard System**
  Automatic, steering-based turn signal cancellation, hazard lights, and light toggling, with reliable background input detection and support for both keyboard and steering wheel/gamepad button bindings. Live dashboard telltales for the left/right signal and high beam mirror your car's actual dash, read straight from OutGauge.

- **Burnout Detection & Bonus System**
  Detects wheelspin while the car is essentially stationary (RPM, throttle, and gear read directly from OutGauge) and scores it with tiered labels (Good/High/Extreme/Insane) based on intensity and hold duration. Bonus points reward a full 360° burnout spin, a Donut (a full loop completed while drifting), a Clean Lap, grazing a track object cleanly (Unstoppable), and smooth Burnout↔Drift transitions. Minimum drift speed and maximum burnout speed are both configurable.

- **Vehicle & Object Collision Detection**
  Reads live car-to-car contact from InSim: a light touch that doesn't break a stable drift scores a KISS bonus, while a hit that spins you, kills your speed, or lands too hard costs points, tagged with the other driver's name and impact tier. It's purely a modifier on your existing drift score, not a separate scoring system, and outside of a drift a hit is shown for information only, with no effect on points. Hits against track objects (walls, cones, etc.) use the same KISS/penalty logic and can be toggled independently.

- **Steering Wheel & Gamepad Support**
  Device detection and axis calibration for wheels via DirectInput. Xbox/XInput gamepads get their steering axis assigned automatically, skipping manual calibration, with native XInput support to keep background input handling accurate.

- **Multiplayer-aware Driver Identity**
  Tracks your driver profile by your LFS nickname, staying accurate as you switch cars, tab-cycle through spectated drivers, or rename yourself in-game, even on a busy multiplayer server.

- **In-Game HUD (IS_BTN)**
  Displays live score, run points, combo multiplier, and drift labels directly inside LFS using native InSim buttons, fully color-customizable through an in-app InSim color picker.

- **Persistent Settings**
  All bindings, calibration values, colors, and preferences are saved locally and restored automatically on startup.

## Requirements

- Windows 10/11
- [.NET 6.0](https://dotnet.microsoft.com/download/dotnet/6.0) or later (Windows Desktop Runtime)
- [Live for Speed](https://www.lfs.net/) with **both InSim and OutGauge** configured (see [Configuring LFS](#configuring-lfs-insim--outgauge) below)
- A DirectInput/XInput-compatible steering wheel or gamepad (optional, for wheel/pad bindings)

## Supported Languages

- English
- Polish
- Turkish
- German
- Spanish

## Configuring LFS (InSim + OutGauge)

The app needs **two separate connections** to LFS to work fully, and they're set up differently. Almost every "it doesn't do anything" / "the speedometer or rev limiter never shows up" report comes down to only one of these being configured.

### 1. InSim (required for scoring, HUD, turn signals, lap/track events)

Enable InSim per session, either by:

- Type `/insim <port>` in the LFS chat/console (e.g. `/insim 29999`), **or**
- Launch LFS with the command-line option `LFS /insim=<port>` (handy in a shortcut, so it's always on).

Then enter the same host (`127.0.0.1` for a local install) and port in the app's **Connection** panel and click **Connect**.

### 2. OutGauge (required for the speedometer/tachometer, rev limiter, and burnout detection)

OutGauge is a separate telemetry stream configured through `cfg.txt` in your LFS installation folder (not through an in-game command). Open it in a text editor and add or edit these lines:

```
OutGauge Mode 2
OutGauge Delay 1
OutGauge IP 127.0.0.1
OutGauge Port 35555
OutGauge ID 1
```

**`cfg.txt` is only read when LFS starts.** Fully restart LFS after editing it, or the change won't take effect. The port (`35555`) must match what the app listens on and shouldn't be shared with another OutGauge client.

### Verifying it's working

- If the Connect toggle flips back off by itself, the InSim connection attempt failed (wrong host/port, or `/insim` wasn't enabled). Check the status text at the bottom of the window for the reason.
- If InSim connects fine but the speedometer/tachometer gauge never appears and the score HUD stays locked showing only the total score, OutGauge isn't reaching the app. Re-check the `cfg.txt` block above and confirm you restarted LFS afterwards. Both features activate automatically once real OutGauge data is flowing, and hide again the moment it stops, such as while sitting in a menu or garage.

## Getting Started

1. Launch **LFS Drift Tools**.
2. Make sure LFS has both InSim and OutGauge configured, per [Configuring LFS](#configuring-lfs-insim--outgauge) above.
3. Enter the host, port, and admin password (if set) in the app, then click **Connect**.
4. Configure your rev limiter, HUD, overlay, and turn signal bindings from the UI.

## Disclaimer

This project interacts with LFS through the official InSim/OutGauge protocols and is intended as a companion tool for personal use. It is not affiliated with or endorsed by the developers of Live for Speed.
