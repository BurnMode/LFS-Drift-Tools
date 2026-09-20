namespace LFSDriftTools
{
    public class RumbleSettings
    {
        public double RumbleStrength { get; set; } = 0.55;
        public double RumbleLoadWeight { get; set; } = 0.13;
        public double RumbleRoadWeight { get; set; } = 1.0;
        public double RumbleImpactWeight { get; set; } = 0.50;
        public double RumbleSurfaceChangeWeight { get; set; } = 1.0;
        public double RumbleShiftWeight { get; set; } = 0.05;
        public double RumbleJerkSensitivity { get; set; } = 6.0;
        public double RumbleLoadSlipActivationDeg { get; set; } = 5.0;
        public double RumbleLoadSlipFullDeg { get; set; } = 12.0;
        public double RumbleRoadGain { get; set; } = 1.15;
        public double RumbleImpactGain { get; set; } = 0.35;
        public double RumbleResponseCurve { get; set; } = 1.35;
        public double RumbleDirectionalBias { get; set; } = 1.0;
    }
}
