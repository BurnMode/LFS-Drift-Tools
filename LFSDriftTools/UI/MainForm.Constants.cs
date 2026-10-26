namespace LFSDriftTools
{
    public partial class MainForm
    {
        private const int LfsNoticePollIntervalMs = 400;
        private const int ParkedCheckDelayMs = 5500;
        private const int OverlayDetachDelayMs = 5200;
        private const int ObjectHitResolveDelayMs = 1250;
        private const int DriftCollisionResolveDelayMs = 2000;
        private const int OverlayReloadConfirmDelayMs = 300;
        private const int StartupFadeTickMs = 15;

        private const int RevCalibrationMeasureMs = 4000;
        private const int RevCalibrationStepMs = 250;
        private const int RevCalibrationFinalHoldMs = 750;
        private const int RevLimitMinRpm = 500;
        private const int RevLimitMaxRpm = 20000;
        private const int InSimHintDelayMs = 5000;
        private const int PlayerListRequestIntervalMs = 2000;
        private const int IsBtnSpeedoIntervalMs = 100;
        private const int IsBtnRevCutHoldMs = 250;

        private const int HudClearSettleMs = 80;
        private const int ShutdownButtonsSettleMs = 120;
        private const int IndicatorKeyDelayMs = 50;

        private const long LastLapPopupMinScore = 1000;
        private const int AxisCalibrationMinRange = 2000;
    }
}