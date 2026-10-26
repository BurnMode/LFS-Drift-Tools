using System;
using System.Threading;
using XInputApi = SharpDX.XInput;

namespace LFSDriftTools
{
    public class AssistLoop : IDisposable
    {
        private readonly SteeringWheelInput _wheelInput;
        private readonly StateEstimator _estimator;
        private readonly AssistEngine _engine;
        private readonly VirtualPad _virtualPad;

        private XInputApi.Controller? _pad;
        private Thread? _thread;
        private volatile bool _running;
        private int _generation;
        private string? _lastErrorMessage;
        private DateTime _lastErrorLoggedUtc = DateTime.MinValue;
        private const double ErrorLogRepeatSec = 5.0;
        private bool _timerResolutionRaised;
        private const long VibrationResendMs = 100;
        private const double LoopWaitMs = 3.9;
        private ushort _lastLeftMotor;
        private ushort _lastRightMotor;
        private long _lastVibrationMs;

        public bool IsRunning => _running;
        public bool PadConnected => _pad != null && _pad.IsConnected;

        public event Action<string>? Log;

        public AssistRecorder? Recorder { get; set; }

        public AssistLoop(SteeringWheelInput wheelInput, StateEstimator estimator, AssistEngine engine, VirtualPad virtualPad)
        {
            _wheelInput = wheelInput;
            _estimator = estimator;
            _engine = engine;
            _virtualPad = virtualPad;
        }

        public bool Start()
        {
            if (_running) return true;

            int userIndex = _wheelInput.XInputUserIndex;
            if (userIndex < 0)
            {
                Log?.Invoke(Localization.T("controllerassist.nopad"));
                return false;
            }

            var pad = new XInputApi.Controller((XInputApi.UserIndex)userIndex);
            if (!pad.IsConnected)
            {
                Log?.Invoke(Localization.T("controllerassist.nopad"));
                return false;
            }

            if (!_virtualPad.Connect())
                return false;

            _engine.Reset();
            _pad = pad;
            _running = true;
            if (!_timerResolutionRaised)
                _timerResolutionRaised = TimerResolution.Acquire();
            int generation = ++_generation;
            _thread = new Thread(() => Run(generation)) { IsBackground = true, Name = "AssistLoop" };
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            if (!_running && _thread == null) return;

            _running = false;
            _generation++;
            _thread?.Join(2000);
            _thread = null;
            if (_timerResolutionRaised)
            {
                TimerResolution.Release();
                _timerResolutionRaised = false;
            }
            _pad = null;
            _virtualPad.Disconnect();
        }

        private void Run(int generation)
        {
            PerfProbe.RegisterThread(PerfThread.AssistLoop);
            using var waiter = new HighResolutionWaiter();
            while (_running && generation == _generation)
            {
                long tickStart = PerfProbe.Begin();
                try
                {
                    var pad = _pad;
                    bool padConnected = false;
                    XInputApi.Gamepad padState = default;
                    long p = PerfProbe.Begin();
                    if (pad != null)
                    {
                        lock (DeviceSync.Lock)
                        {
                            padConnected = pad.GetState(out var state);
                            padState = state.Gamepad;
                        }
                    }
                    PerfProbe.End(PerfSection.AssistPadRead, p);
                    if (pad != null && padConnected)
                    {
                        double rawSteerPercent = padState.LeftThumbX / 32767.0 * 100.0;
                        CarState car = _estimator.Sample();

                        p = PerfProbe.Begin();
                        double outputPercent = _engine.Process(rawSteerPercent, car);
                        _engine.Trace.PadLeftTriggerPercent = padState.LeftTrigger / 255.0 * 100.0;
                        _engine.Trace.PadRightTriggerPercent = padState.RightTrigger / 255.0 * 100.0;
                        padState.RightTrigger = _engine.ProcessThrottle(padState.RightTrigger, car);
                        PerfProbe.End(PerfSection.AssistEngine, p);
                        p = PerfProbe.Begin();
                        Recorder?.Record(_engine.Trace);
                        PerfProbe.End(PerfSection.AssistRecorder, p);
                        short outputAxis = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, outputPercent / 100.0 * 32767.0));

                        p = PerfProbe.Begin();
                        _virtualPad.SubmitFrame(outputAxis, padState);
                        PerfProbe.End(PerfSection.AssistVirtualPad, p);
                        p = PerfProbe.Begin();

                        double rumbleScale = Math.Max(0, Math.Min(1, _engine.Rumble.RumbleStrength));
                        var snapshot = _engine.LastSnapshot;
                        ushort leftMotor = (ushort)(snapshot.RumbleLeft01 * rumbleScale * 65535.0);
                        ushort rightMotor = (ushort)(snapshot.RumbleRight01 * rumbleScale * 65535.0);
                        long nowMs = Environment.TickCount64;
                        if (leftMotor != _lastLeftMotor || rightMotor != _lastRightMotor || nowMs - _lastVibrationMs >= VibrationResendMs)
                        {
                            lock (DeviceSync.Lock)
                            {
                                pad.SetVibration(new XInputApi.Vibration
                                {
                                    LeftMotorSpeed = leftMotor,
                                    RightMotorSpeed = rightMotor,
                                });
                            }
                            _lastLeftMotor = leftMotor;
                            _lastRightMotor = rightMotor;
                            _lastVibrationMs = nowMs;
                        }
                        PerfProbe.End(PerfSection.AssistVibration, p);
                    }
                }
                catch (Exception ex)
                {
                    LogThrottled($"AssistLoop: {ex.Message}");
                }
                PerfProbe.End(PerfSection.AssistTick, tickStart);

                waiter.Wait(LoopWaitMs);
            }
        }

        private void LogThrottled(string message)
        {
            DateTime now = DateTime.UtcNow;
            if (message == _lastErrorMessage && (now - _lastErrorLoggedUtc).TotalSeconds < ErrorLogRepeatSec)
                return;

            _lastErrorMessage = message;
            _lastErrorLoggedUtc = now;
            Log?.Invoke(message);
        }

        public void Dispose() => Stop();
    }
}
