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

        public bool IsRunning => _running;
        public bool PadConnected => _pad != null && _pad.IsConnected;
        public bool VirtualPadReady => _virtualPad.IsAvailable;

        public event Action<string>? Log;

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

            _pad = pad;
            _running = true;
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
            _pad = null;
            _virtualPad.Disconnect();
        }

        private void Run(int generation)
        {
            while (_running && generation == _generation)
            {
                try
                {
                    if (_pad != null && _pad.IsConnected)
                    {
                        var padState = _pad.GetState().Gamepad;
                        double rawSteerPercent = padState.LeftThumbX / 32767.0 * 100.0;
                        CarState car = _estimator.Sample();

                        double outputPercent = _engine.Process(rawSteerPercent, car);
                        short outputAxis = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, outputPercent / 100.0 * 32767.0));

                        _virtualPad.SubmitFrame(outputAxis, padState);

                        double rumbleScale = Math.Max(0, Math.Min(1, _engine.Rumble.RumbleStrength));
                        ushort leftMotor = (ushort)(_engine.LastSnapshot.RumbleLeft01 * rumbleScale * 65535.0);
                        ushort rightMotor = (ushort)(_engine.LastSnapshot.RumbleRight01 * rumbleScale * 65535.0);
                        _pad.SetVibration(new XInputApi.Vibration
                        {
                            LeftMotorSpeed = leftMotor,
                            RightMotorSpeed = rightMotor,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"AssistLoop: {ex.Message}");
                }

                Thread.Sleep(4);
            }
        }

        public void Dispose() => Stop();
    }
}
