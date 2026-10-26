namespace LFSDriftTools
{
    public class AssistSettings
    {
        public AssistPreset Preset { get; set; } = AssistPreset.Street;

        public double DeadzonePercent { get; set; } = 3.0;
        public double ResponseCurve { get; set; } = 1.4;
        public double SpeedSensitivityRefKmh { get; set; } = 120;
        public double MinHighSpeedSteerFraction { get; set; } = 0.35;
        public double SlipActivationDeg { get; set; } = 4.0;
        public double SlipFullDeg { get; set; } = 20.0;
        public double CountersteerGain { get; set; } = 1.6;
        public double AssistStrength { get; set; } = 0.7;
        public double MinActivationSpeedKmh { get; set; } = 5.0;
        public double SmoothingMs { get; set; } = 35;
        public double SteerRateAtSpeedPercentPerSec { get; set; } = 0;
        public double FrontGripLimitDeg { get; set; } = 0;
        public double BrakeSteerReductionPercent { get; set; } = 0;
        public double StraightStabilityPercent { get; set; } = 0;
        public double ThrottleSpinCutPercent { get; set; } = 0;
        public double ThrottleRampPercent { get; set; } = 0;
        public double SpinCatchAllowedSlipDeg { get; set; } = 0;
        public double TractionControlPercent { get; set; } = 0;

        public SteeringAssistMode Mode { get; set; } = SteeringAssistMode.Countersteer;
        public double TargetAngleMaxDeg { get; set; } = 75.0;
        public double TargetAngleCurve { get; set; } = 1.25;
        public double TargetAngleGain { get; set; } = 2.1;
        public double TargetAngleSmoothingMs { get; set; } = 75;
        public double TargetAngleCorrectionRatePerSec { get; set; } = 0;
        public double TargetAngleHandoverReleaseMs { get; set; } = 0;
        public double TargetAngleStickRateDegPerSec { get; set; } = 0;
        public double TargetAngleSoftSaturation { get; set; } = 0;
        public double TargetAngleSpinCatch { get; set; } = 0;

        public static AssistSettings ForPreset(AssistPreset preset)
        {
            return preset switch
            {
                AssistPreset.Off => new AssistSettings
                {
                    Preset = AssistPreset.Off,
                    AssistStrength = 0,
                },
                AssistPreset.Race => new AssistSettings
                {
                    Preset = AssistPreset.Race,
                    Mode = SteeringAssistMode.Countersteer,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.42,
                    SpeedSensitivityRefKmh = 130,
                    MinHighSpeedSteerFraction = 0.50,
                    MinActivationSpeedKmh = 10.0,
                    SlipActivationDeg = 8.0,
                    SlipFullDeg = 12.0,
                    CountersteerGain = 2.4,
                    AssistStrength = 0.70,
                    SmoothingMs = 20,
                    SteerRateAtSpeedPercentPerSec = 478,
                    FrontGripLimitDeg = 7.0,
                    BrakeSteerReductionPercent = 30,
                    StraightStabilityPercent = 50,
                    TargetAngleSpinCatch = 50,
                    ThrottleSpinCutPercent = 60,
                    ThrottleRampPercent = 50,
                    TractionControlPercent = 60,
                },
                AssistPreset.Street => new AssistSettings
                {
                    Preset = AssistPreset.Street,
                    Mode = SteeringAssistMode.Countersteer,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.35,
                    SpeedSensitivityRefKmh = 130,
                    MinHighSpeedSteerFraction = 0.55,
                    MinActivationSpeedKmh = 10.0,
                    SlipActivationDeg = 10.0,
                    SlipFullDeg = 16.0,
                    CountersteerGain = 2.2,
                    AssistStrength = 0.60,
                    SmoothingMs = 20,
                    SteerRateAtSpeedPercentPerSec = 600,
                    FrontGripLimitDeg = 8.0,
                    BrakeSteerReductionPercent = 20,
                    StraightStabilityPercent = 40,
                    TargetAngleSpinCatch = 35,
                    SpinCatchAllowedSlipDeg = 10.0,
                    ThrottleSpinCutPercent = 40,
                    ThrottleRampPercent = 30,
                    TractionControlPercent = 40,
                },
                AssistPreset.Drift => new AssistSettings
                {
                    Preset = AssistPreset.Drift,
                    Mode = SteeringAssistMode.TargetAngle,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.3,
                    SpeedSensitivityRefKmh = 120,
                    MinHighSpeedSteerFraction = 0.60,
                    MinActivationSpeedKmh = 10.0,
                    SlipActivationDeg = 2.0,
                    SlipFullDeg = 6.0,
                    AssistStrength = 1.0,
                    SmoothingMs = 25,
                    TargetAngleMaxDeg = 60.0,
                    TargetAngleCurve = 1.25,
                    TargetAngleGain = 1.9,
                    TargetAngleSmoothingMs = 100,
                    TargetAngleCorrectionRatePerSec = 700,
                    TargetAngleHandoverReleaseMs = 265,
                    TargetAngleStickRateDegPerSec = 220,
                    TargetAngleSoftSaturation = 25,
                    TargetAngleSpinCatch = 35,
                },
                _ => new AssistSettings { Preset = AssistPreset.Custom },
            };
        }

        public AssistSettings Clone() => (AssistSettings)MemberwiseClone();
    }
}
