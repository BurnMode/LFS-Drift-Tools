namespace LFSDriftTools
{
    public class AssistSettings
    {
        public AssistPreset Preset { get; set; } = AssistPreset.Medium;

        public double DeadzonePercent { get; set; } = 3.0;
        public double ResponseCurve { get; set; } = 1.4;
        public double SpeedSensitivityRefKmh { get; set; } = 120;
        public double MinHighSpeedSteerFraction { get; set; } = 0.35;
        public double SlipActivationDeg { get; set; } = 4.0;
        public double SlipFullDeg { get; set; } = 20.0;
        public double CountersteerGain { get; set; } = 1.6;
        public double AssistStrength { get; set; } = 0.7;
        public double MinActivationSpeedKmh { get; set; } = 5.0;
        public double MaxSteerRatePerSec { get; set; } = 8.0;
        public double SmoothingMs { get; set; } = 35;
        public double SelfCenterStrength { get; set; } = 0.5;

        public static AssistSettings ForPreset(AssistPreset preset)
        {
            return preset switch
            {
                AssistPreset.Off => new AssistSettings
                {
                    Preset = AssistPreset.Off,
                    AssistStrength = 0,
                    SelfCenterStrength = 0,
                },
                AssistPreset.Light => new AssistSettings
                {
                    Preset = AssistPreset.Light,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.6,
                    SpeedSensitivityRefKmh = 150,
                    MinHighSpeedSteerFraction = 0.35,
                    CountersteerGain = 1.6,
                    SelfCenterStrength = 0.60,
                    MinActivationSpeedKmh = 5.0,
                    SlipActivationDeg = 1.5,
                    SlipFullDeg = 15.0,
                    AssistStrength = 0.35,
                    MaxSteerRatePerSec = 20.0,
                    SmoothingMs = 65,
                },
                AssistPreset.Medium => new AssistSettings
                {
                    Preset = AssistPreset.Medium,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.3,
                    SpeedSensitivityRefKmh = 100,
                    MinHighSpeedSteerFraction = 0.35,
                    CountersteerGain = 2.4,
                    SelfCenterStrength = 0.60,
                    MinActivationSpeedKmh = 5.0,
                    SlipActivationDeg = 1.0,
                    SlipFullDeg = 15.0,
                    AssistStrength = 0.50,
                    MaxSteerRatePerSec = 35.0,
                    SmoothingMs = 50,
                },
                AssistPreset.Strong => new AssistSettings
                {
                    Preset = AssistPreset.Strong,
                    DeadzonePercent = 3.0,
                    ResponseCurve = 1.4,
                    SpeedSensitivityRefKmh = 100,
                    MinHighSpeedSteerFraction = 0.35,
                    CountersteerGain = 3.2,
                    SelfCenterStrength = 0.60,
                    MinActivationSpeedKmh = 5.0,
                    SlipActivationDeg = 1.0,
                    SlipFullDeg = 10.0,
                    AssistStrength = 0.75,
                    MaxSteerRatePerSec = 35.0,
                    SmoothingMs = 55,
                },
                _ => new AssistSettings { Preset = AssistPreset.Custom },
            };
        }

        public AssistSettings Clone() => (AssistSettings)MemberwiseClone();
    }
}
