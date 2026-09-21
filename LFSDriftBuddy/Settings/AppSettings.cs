using LFSDriftTools.InSim;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static LFSDriftTools.DriftEngine;
using static LFSDriftTools.IndicatorManager;
using System.Reflection;

namespace LFSDriftTools
{
    public class VehicleRevSettings
    {
        public int MaxRpm { get; set; }
        public int CutMs { get; set; }
        public bool Unsupported { get; set; }
    }

    public class SpeedoPresetData
    {
        public bool ShowRpmDigits { get; set; } = true;
        public bool ShowUnit { get; set; } = true;
        public bool ShowMinorTicks { get; set; } = true;
        public bool ShowMajorTicks { get; set; } = true;
        public bool PlainDigits { get; set; } = false;
        public bool InstantSpeed { get; set; } = false;
        public bool AirSpeed { get; set; } = false;
        public float RedlineThickness { get; set; } = 4f;
        public string BackgroundImagePath { get; set; } = "";
        public float BackgroundPanX { get; set; } = 0f;
        public float BackgroundPanY { get; set; } = 0f;
        public float BackgroundZoom { get; set; } = 1f;
        public float BackgroundOpacity { get; set; } = 1f;

        public int RedlineColorArgb { get; set; } = Color.Red.ToArgb();
        public int TextColorArgb { get; set; } = Color.White.ToArgb();
        public int IndicatorColorArgb { get; set; } = Color.FromArgb(255, 225, 225, 230).ToArgb();
        public int TickColorArgb { get; set; } = Color.FromArgb(255, 215, 215, 218).ToArgb();
        public int BackgroundColorArgb { get; set; } = Color.FromArgb(50, 15, 15, 20).ToArgb();

        public static SpeedoPresetData Default2() => new SpeedoPresetData
        {
            ShowRpmDigits = true,
            ShowUnit = false,
            ShowMinorTicks = true,
            ShowMajorTicks = true,
            RedlineThickness = 14.5f,
            BackgroundImagePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "Images", "speedo_default_background.png"),
            BackgroundPanX = 0f,
            BackgroundPanY = 0f,
            BackgroundZoom = 2.22f,
            BackgroundOpacity = 0.4f,

            RedlineColorArgb = Color.FromArgb(255, 255, 0, 70).ToArgb(),
            TextColorArgb = Color.White.ToArgb(),
            IndicatorColorArgb = Color.FromArgb(255, 255, 255, 255).ToArgb(),
            TickColorArgb = Color.FromArgb(255, 255, 255, 255).ToArgb(),
            BackgroundColorArgb = Color.FromArgb(0, 15, 15, 15).ToArgb(),
    };
    }

    public class AppSettings
    {
        public string Language { get; set; } = "English";

        public bool OutGaugeConnectionEnabled { get; set; } = true;

        public bool OutSimConnectionEnabled { get; set; } = true;
        public int OutSimPort { get; set; } = 30020;
        public string OutSimOptsHex { get; set; } = "bf";

        public string LfsGameFolder { get; set; } = "";

        public bool ControllerAssistEnabled { get; set; } = true;
        public bool ControllerAssistSteeringEnabled { get; set; } = true;
        public bool ControllerAssistVibrationEnabled { get; set; } = true;
        public bool ControllerAssistOverlayBarVisible { get; set; } = true;
        public bool ControllerAssistHidePhysicalPad { get; set; } = true;
        public AssistSettings ControllerAssistSettings { get; set; } = AssistSettings.ForPreset(AssistPreset.Medium);
        public RumbleSettings ControllerAssistRumble { get; set; } = new RumbleSettings();
        public double ControllerAssistCountersteerTrimPercent { get; set; } = 0.0;
        public double ControllerAssistStrengthTrimPercent { get; set; } = 0.0;

        public bool DarkTheme { get; set; } = true;

        public InputBinding IndicatorLeftBinding { get; set; } = InputBinding.FromKey(Keys.D7);
        public InputBinding IndicatorRightBinding { get; set; } = InputBinding.FromKey(Keys.D8);
        public InputBinding IndicatorHazardBinding { get; set; } = InputBinding.FromKey(Keys.D9);
        public InputBinding LightToggleBinding { get; set; } = InputBinding.None;
        public InputBinding ControllerAssistToggleBinding { get; set; } = InputBinding.None;

        public string SteeringWheelDeviceGuid { get; set; } = "";
        public string SteeringWheelAxis { get; set; } = "X";

        public bool IndicatorSoundsEnabled { get; set; } = true;
        public int IndicatorSoundsVolume { get; set; } = 100;
        public bool IndicatorAutoCancelOnCenter { get; set; } = true;
        public int IndicatorArmThresholdPct { get; set; } = 35;
        public int IndicatorCenterThresholdPct { get; set; } = 5;

        public bool SpeedoTachoEnabled { get; set; } = true;
        public float SpeedoTachoOffsetX { get; set; } = 0f;
        public float SpeedoTachoOffsetY { get; set; } = 0f;
        public float SpeedoTachoScale { get; set; } = 1.0f;
        public bool SpeedoTachoUseMph { get; set; } = false;

        public bool SpeedoTachoShowRpmDigits { get; set; } = true;
        public bool SpeedoTachoShowUnit { get; set; } = true;
        public bool SpeedoTachoShowMinorTicks { get; set; } = true;
        public bool SpeedoTachoShowMajorTicks { get; set; } = true;
        public bool SpeedoTachoPlainDigits { get; set; } = false;
        public bool SpeedoTachoInstantSpeed { get; set; } = false;
        public bool SpeedoTachoAirSpeed { get; set; } = false;
        public float RedlineThickness { get; set; } = 4f;

        public string SpeedoBackgroundImagePath { get; set; } = "";
        public float SpeedoBackgroundPanX { get; set; } = 0f;
        public float SpeedoBackgroundPanY { get; set; } = 0f;
        public float SpeedoBackgroundZoom { get; set; } = 1.0f;
        public float SpeedoBackgroundOpacity { get; set; } = 1.0f;

        public int ActiveSpeedoPreset { get; set; } = 0;
        public SpeedoPresetData? SpeedoPresetA { get; set; } = null;
        public SpeedoPresetData? SpeedoPresetB { get; set; } = null;

        public bool AdvancedOutGaugeEnabled { get; set; } = true;

        public bool ShowRPMHudEnabled { get; set; } = false;

        public bool IndicatorsMasterEnabled { get; set; } = true;

        public bool LfsNoticeDontShow { get; set; } = false;
        public bool HudMasterEnabled { get; set; } = true;
        public bool ShowScoreOverlay { get; set; } = true;
        public bool ShowInsimHud { get; set; } = false;

        public double[] AngleLevelThresholds { get; set; } = { 25.0, 40.0, 50.0, 60.0, 70.0 };
        public double[] SpeedLevelThresholds { get; set; } = { 100.0, 150.0, 200.0, 250.0, 300.0 };
        public double BackwardDriftAngleThreshold { get; set; } = 95.0;

        public double MinDriftSpeedKmh { get; set; } = 20.0;
        public double MaxBurnoutSpeedKmh { get; set; } = 20.0;

        public bool CollisionDetectionEnabled { get; set; } = true;
        public double[] HitLevelThresholds { get; set; } = { 5.0, 10.0, 30.0, 50.0, 100.0 };
        public bool ObjectCollisionDetectionEnabled { get; set; } = true;
    }

    public class ColorSettings
    {
        public string InSimColor1 { get; set; } = "^7";
        public string InSimColor2 { get; set; } = "^6";
        public string InSimColor3 { get; set; } = "^3";
        public string InSimColor4 { get; set; } = "^5";
        public string InSimColor5 { get; set; } = "^1";
        public string InSimColor6 { get; set; } = "^2";
        public string InSimColorIdle { get; set; } = "^7";

        public int OverlayColor1 { get; set; } = -1;
        public int OverlayColor2 { get; set; } = -1;
        public int OverlayColor3 { get; set; } = -1;
        public int OverlayColor4 { get; set; } = -1;
        public int OverlayColor5 { get; set; } = -1;
        public int OverlayColor6 { get; set; } = -1;
        public int OverlayColorIdle { get; set; } = -1;

        public int SpeedoTachoRedlineColor { get; set; } = Color.Red.ToArgb();
        public int SpeedoTachoTextColor { get; set; } = Color.White.ToArgb();
        public int SpeedoTachoIndicatorColor { get; set; } = Color.FromArgb(255, 225, 225, 230).ToArgb();
        public int SpeedoTachoTickColor { get; set; } = Color.FromArgb(255, 215, 215, 218).ToArgb();
        public int SpeedoTachoBackgroundColor { get; set; } = Color.FromArgb(50, 15, 15, 20).ToArgb();
    }

    public class RevLimiterConfig
    {
        public int CalibratedMAXRPM { get; set; } = 7600;

        public int SavedMSCUT { get; set; } = 40;

        public int CutMsVersion { get; set; } = 0;

        public Dictionary<string, VehicleRevSettings> VehicleRevLimiterSettings { get; set; } = new();

        public InputBinding RevToggleBinding { get; set; } = InputBinding.None;
        public InputBinding RevCalibrateBinding { get; set; } = InputBinding.None;
        public InputBinding RevDecreaseBinding { get; set; } = InputBinding.None;
        public InputBinding RevIncreaseBinding { get; set; } = InputBinding.None;

        public bool AutoCalibrateNewCar { get; set; } = true;

        public bool RevLimiterEnabled { get; set; } = true;
    }
}
