using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using XInputApi = SharpDX.XInput;

namespace LFSDriftTools
{
    public class VirtualPad : IDisposable
    {
        public const string ReleasesUrl = "https://github.com/nefarius/ViGEmBus/releases";

        private ViGEmClient? _client;
        private IXbox360Controller? _controller;

        private const long ResubmitIntervalMs = 100;
        private IXbox360Controller? _lastController;
        private (short, short, short, short, byte, byte, XInputApi.GamepadButtonFlags) _lastReport;
        private long _lastSubmitMs;

        public bool IsAvailable { get; private set; }

        public event Action<byte, byte>? RumbleReceived;
        public event Action<string>? Error;

        public bool Connect()
        {
            lock (DeviceSync.Lock)
            {
                if (IsAvailable) return true;
                try
                {
                    _client = new ViGEmClient();
                    _controller = _client.CreateXbox360Controller();
                    _controller.AutoSubmitReport = false;
                    _controller.FeedbackReceived += OnFeedbackReceived;
                    _controller.Connect();
                    IsAvailable = true;
                    return true;
                }
                catch (Exception ex)
                {
                    IsAvailable = false;
                    Error?.Invoke(string.Format(Localization.T("controllerassist.vigembus_missing"), ReleasesUrl, ex.Message));
                    return false;
                }
            }
        }

        private void OnFeedbackReceived(object? sender, Xbox360FeedbackReceivedEventArgs e)
        {
            RumbleReceived?.Invoke(e.LargeMotor, e.SmallMotor);
        }

        public void Disconnect()
        {
            lock (DeviceSync.Lock)
            {
                if (_controller != null)
                {
                    try { _controller.FeedbackReceived -= OnFeedbackReceived; _controller.Disconnect(); }
                    catch { }
                }
                _controller = null;

                try { _client?.Dispose(); }
                catch { }
                _client = null;
                IsAvailable = false;
            }
        }

        public void SubmitFrame(short steeringAxis, XInputApi.Gamepad source)
        {
            lock (DeviceSync.Lock)
            {
                var c = _controller;
                if (c == null) return;

                long now = Environment.TickCount64;
                var report = (steeringAxis, source.LeftThumbY, source.RightThumbX, source.RightThumbY, source.LeftTrigger, source.RightTrigger, source.Buttons);
                if (ReferenceEquals(c, _lastController) && report.Equals(_lastReport) && now - _lastSubmitMs < ResubmitIntervalMs)
                    return;
                _lastController = c;
                _lastReport = report;
                _lastSubmitMs = now;

                c.SetAxisValue(Xbox360Axis.LeftThumbX, steeringAxis);
                c.SetAxisValue(Xbox360Axis.LeftThumbY, source.LeftThumbY);
                c.SetAxisValue(Xbox360Axis.RightThumbX, source.RightThumbX);
                c.SetAxisValue(Xbox360Axis.RightThumbY, source.RightThumbY);
                c.SetSliderValue(Xbox360Slider.LeftTrigger, source.LeftTrigger);
                c.SetSliderValue(Xbox360Slider.RightTrigger, source.RightTrigger);

                var b = source.Buttons;
                c.SetButtonState(Xbox360Button.A, (b & XInputApi.GamepadButtonFlags.A) != 0);
                c.SetButtonState(Xbox360Button.B, (b & XInputApi.GamepadButtonFlags.B) != 0);
                c.SetButtonState(Xbox360Button.X, (b & XInputApi.GamepadButtonFlags.X) != 0);
                c.SetButtonState(Xbox360Button.Y, (b & XInputApi.GamepadButtonFlags.Y) != 0);
                c.SetButtonState(Xbox360Button.Start, (b & XInputApi.GamepadButtonFlags.Start) != 0);
                c.SetButtonState(Xbox360Button.Back, (b & XInputApi.GamepadButtonFlags.Back) != 0);
                c.SetButtonState(Xbox360Button.LeftShoulder, (b & XInputApi.GamepadButtonFlags.LeftShoulder) != 0);
                c.SetButtonState(Xbox360Button.RightShoulder, (b & XInputApi.GamepadButtonFlags.RightShoulder) != 0);
                c.SetButtonState(Xbox360Button.LeftThumb, (b & XInputApi.GamepadButtonFlags.LeftThumb) != 0);
                c.SetButtonState(Xbox360Button.RightThumb, (b & XInputApi.GamepadButtonFlags.RightThumb) != 0);
                c.SetButtonState(Xbox360Button.Up, (b & XInputApi.GamepadButtonFlags.DPadUp) != 0);
                c.SetButtonState(Xbox360Button.Down, (b & XInputApi.GamepadButtonFlags.DPadDown) != 0);
                c.SetButtonState(Xbox360Button.Left, (b & XInputApi.GamepadButtonFlags.DPadLeft) != 0);
                c.SetButtonState(Xbox360Button.Right, (b & XInputApi.GamepadButtonFlags.DPadRight) != 0);

                c.SubmitReport();
            }
        }

        public void Dispose() => Disconnect();
    }
}
