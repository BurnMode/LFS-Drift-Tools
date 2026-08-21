# LFS Drift Tools

**LFS Drift Tools** is a companion application for [Live for Speed](https://www.lfs.net/) that turns your drift sessions into a more fun experience. It connects to LFS via InSim/OutGauge and adds real-time scoring, an in-game HUD, a transparent overlay, a configurable rev limiter, and a smarter turn signal system - all wrapped in a clean, modern UI.

## Key Features

- **DriftEngine - Real-time Scoring Engine**
  Calculates drift score based on angle, speed, and combo multipliers, with per-track and per-layout best lap tracking (persisted locally in JSON). Automatically detects laps, track/layout changes, pit stops, and race restarts via InSim events.

- **In-Game HUD (IS_BTN)**
  Displays live score, run points, combo multiplier, and drift labels directly inside LFS using native InSim buttons - fully color-customizable through an in-app InSim color picker.

- **Burnout Detection**
  Detects wheelspin while the car is essentially stationary (RPM, throttle, and gear read directly from OutGauge) and scores it with tiered labels (Good/High/Extreme/Insane) based on intensity and hold duration, plus a bonus for every completed 360° spin.

- **Forza Horizon-style Overlay**
  A transparent, layered on-screen overlay rendered over the LFS window showing drift angle, combo countdown, bonus text animations, rev limiter status, and a circular speedometer/tachometer gauge - independent of the in-game HUD.

- **Speedometer & Tachometer Gauge**
  A Forza-style circular gauge showing live (smoothed) vehicle speed, current gear, and an RPM arc that auto-calibrates to the car's redline. Fully repositionable/scalable with a KPH↔MPH toggle and per-element color customization. It only appears while real OutGauge data is flowing, so it won't sit on screen showing stale numbers while you're in a menu or garage.

- **Rev Limiter**
  A configurable ignition-cut rev limiter driven by OutGauge RPM data, with adjustable cut duration, auto-calibration, and dedicated keyboard/wheel-button bindings for toggle, calibrate, increase, and decrease actions.

- **Turn Signal & Hazard System**
  Automatic, heading-based turn signal cancellation, hazard lights, and light toggling, with support for both keyboard and steering wheel/gamepad button bindings - including improved, reliable background input detection for XInput controllers.

- **Steering Wheel & Gamepad Support**
  Device detection and axis calibration for wheels via DirectInput, with native XInput support for gamepads to ensure accurate background input handling.

- **Persistent Settings**
  All bindings, calibration values, colors, and preferences are saved locally and restored automatically on startup.

## Requirements

- Windows 10/11
- [.NET 6.0](https://dotnet.microsoft.com/download/dotnet/6.0) or later (Windows Desktop Runtime)
- [Live for Speed](https://www.lfs.net/) with **both InSim and OutGauge** configured - see [Configuring LFS](#configuring-lfs-insim--outgauge) below
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

InSim is enabled per-session, not through a config file - either:

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

**`cfg.txt` is only read when LFS starts** - fully restart LFS after editing it, or the change won't take effect. The port (`35555`) must match what the app listens on and shouldn't be shared with another OutGauge client.

### Verifying it's working

- If the Connect toggle flips back off by itself, the InSim connection attempt failed (wrong host/port, or `/insim` wasn't enabled) - check the status text at the bottom of the window for the reason.
- If InSim connects fine but the speedometer/tachometer gauge never appears and the score HUD stays locked showing only the total score, OutGauge isn't reaching the app - re-check the `cfg.txt` block above and confirm you restarted LFS afterwards. Both of these only activate once real OutGauge data is flowing, and switch back off automatically the moment it stops (e.g. while sitting in a menu or garage) - so this is also the expected, normal behavior between sessions on track, not a bug.

## Getting Started

1. Launch **LFS Drift Tools**.
2. Make sure LFS has both InSim and OutGauge configured - see [Configuring LFS](#configuring-lfs-insim--outgauge) above.
3. Enter the host, port, and admin password (if set) in the app, then click **Connect**.
4. Configure your rev limiter, HUD, overlay, and turn signal bindings from the UI.

## Disclaimer

This project interacts with LFS through the official InSim/OutGauge protocols and is intended as a companion tool for personal use. It is not affiliated with or endorsed by the developers of Live for Speed.
