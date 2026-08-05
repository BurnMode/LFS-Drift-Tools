# LFS Drift Tools

**LFS Drift Tools** is a companion application for [Live for Speed](https://www.lfs.net/) that turns your drift sessions into a more fun experience. It connects to LFS via InSim/OutGauge and adds real-time scoring, an in-game HUD, a transparent overlay, a configurable rev limiter, and a smarter turn signal system - all wrapped in a clean, modern UI.

## Key Features

- **DriftEngine - Real-time Scoring Engine**
  Calculates drift score based on angle, speed, and combo multipliers, with per-track and per-layout best lap tracking (persisted locally in JSON). Automatically detects laps, track/layout changes, pit stops, and race restarts via InSim events.

- **In-Game HUD (IS_BTN)**
  Displays live score, run points, combo multiplier, and drift labels directly inside LFS using native InSim buttons - fully color-customizable through an in-app InSim color picker.

- **Forza Horizon-style Overlay**
  A transparent, layered on-screen overlay rendered over the LFS window showing drift angle, combo countdown, bonus text animations, and rev limiter status - independent of the in-game HUD.

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
- [Live for Speed](https://www.lfs.net/) with InSim enabled (`/insim <port>` in-game)
- A DirectInput/XInput-compatible steering wheel or gamepad (optional, for wheel/pad bindings)

## Supported Languages

- English
- Polish
- Turkish
- German
- Spanish

## Getting Started

1. Launch **LFS Drift Tools**.
2. In LFS, type `/insim <port>` (default port used by the app is shown in the Connection panel).
3. Enter the host, port, and admin password (if set) in the app, then click **Connect**.
4. Configure your rev limiter, HUD, overlay, and turn signal bindings from the UI.

## Disclaimer

This project interacts with LFS through the official InSim/OutGauge protocols and is intended as a companion tool for personal use. It is not affiliated with or endorsed by the developers of Live for Speed.
