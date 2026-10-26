using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        private readonly List<string> _settingsLoadErrors = new();

        private void RegisterLoadError(string what, Exception ex)
        {
            _settingsLoadErrors.Add($"{what}: {ex.Message}");
        }

        private T LoadJsonOrDefault<T>(string path) where T : new()
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    T? obj = JsonSerializer.Deserialize<T>(json);
                    if (obj != null) return obj;
                }
            }
            catch (Exception ex)
            {
                RegisterLoadError(Path.GetFileName(path), ex);
                try
                {
                    File.Move(path, path + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                }
                catch { }
            }
            return new T();
        }

        private void RunLoadStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                RegisterLoadError(name, ex);
            }
        }

        private void ReportSettingsLoadErrors()
        {
            if (_settingsLoadErrors.Count == 0) return;

            string backupDir = "";
            try
            {
                backupDir = Path.Combine(ConfigsDir, "loadfail_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupDir);
                foreach (string file in new[] { SettingsFile, ColorsFile, RevLimiterFile })
                {
                    if (File.Exists(file))
                        File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), overwrite: true);
                }
            }
            catch { }

            AppendStatusMessage(
                string.Format(Localization.T("status.settings.load_error"), string.Join("; ", _settingsLoadErrors), backupDir),
                StatusErrorColor);
            _settingsLoadErrors.Clear();
        }

        private void ArchiveOutdatedConfigs()
        {
            string[] files = { SettingsFile, ColorsFile, RevLimiterFile };
            bool outdated;
            try
            {
                outdated = files.Any(ConfigVersions.IsOutdated);
            }
            catch (Exception ex)
            {
                RegisterLoadError("config version", ex);
                return;
            }
            if (!outdated) return;

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            foreach (string file in files)
            {
                try
                {
                    if (File.Exists(file))
                        File.Move(file, ConfigVersions.ArchivePath(file, stamp));
                }
                catch (Exception ex)
                {
                    RegisterLoadError(Path.GetFileName(file), ex);
                }
            }

            AppendStatusMessage(string.Format(Localization.T("status.settings.reset_old_version"), "*.old_" + stamp), ApplePalette.StatusWarn);
        }

        private static void WriteJson<T>(string path, T obj)
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void LoadSettings()
        {
            try
            {
                Directory.CreateDirectory(ConfigsDir);

                ArchiveOutdatedConfigs();

                bool anyConfigExists = File.Exists(SettingsFile) || File.Exists(ColorsFile) || File.Exists(RevLimiterFile);

                bool firstRun = !anyConfigExists;

                AppSettings settings = firstRun ? new AppSettings() : LoadJsonOrDefault<AppSettings>(SettingsFile);
                ColorSettings colors = firstRun ? new ColorSettings() : LoadJsonOrDefault<ColorSettings>(ColorsFile);
                RevLimiterConfig revCfg = firstRun
                    ? new RevLimiterConfig { VehicleRevLimiterSettings = RevLimiterPresets.Create() }
                    : LoadJsonOrDefault<RevLimiterConfig>(RevLimiterFile);

                RunLoadStep("rev limiter", () =>
                {
                CalibratedMAXRPM = SanitizeRevRpm(revCfg.CalibratedMAXRPM);
                SavedMSCUT = SanitizeRevCutMs(revCfg.SavedMSCUT);
                _revCutMS.Value = SavedMSCUT;
                _revLimiter.RpmLimit = CalibratedMAXRPM;
                _revLimiter.CutMs = SavedMSCUT;
                _revLimiterNumeric.Value = CalibratedMAXRPM;

                _vehicleRevSettings = revCfg.VehicleRevLimiterSettings ?? new Dictionary<string, VehicleRevSettings>();
                _turboMaxBarByCar = revCfg.TurboMaxBarByCar ?? new Dictionary<string, float>();
                _revAutoCalibrateNewCarCheck.Checked = revCfg.AutoCalibrateNewCar;

                _revLimiter.Enabled = revCfg.RevLimiterEnabled;
                _revEnableSwitch.SetCheckedSilent(revCfg.RevLimiterEnabled);
                _revEnabledIntent.Wanted = revCfg.RevLimiterEnabled;
                });

                RunLoadStep("colors", () =>
                {
                InSimColor1 = colors.InSimColor1;
                InSimColor2 = colors.InSimColor2;
                InSimColor3 = colors.InSimColor3;
                InSimColor4 = colors.InSimColor4;
                InSimColor5 = colors.InSimColor5;
                InSimColor6 = colors.InSimColor6 ?? "^2";
                InSimColorIdle = colors.InSimColorIdle ?? "^7";
                SyncHudColors();

                if (colors.OverlayColor1 != -1) OverlayColor1 = Color.FromArgb(colors.OverlayColor1);
                if (colors.OverlayColor2 != -1) OverlayColor2 = Color.FromArgb(colors.OverlayColor2);
                if (colors.OverlayColor3 != -1) OverlayColor3 = Color.FromArgb(colors.OverlayColor3);
                if (colors.OverlayColor4 != -1) OverlayColor4 = Color.FromArgb(colors.OverlayColor4);
                if (colors.OverlayColor5 != -1) OverlayColor5 = Color.FromArgb(colors.OverlayColor5);
                if (colors.OverlayColor6 != -1) OverlayColor6 = Color.FromArgb(colors.OverlayColor6);
                if (colors.OverlayColorIdle != -1) OverlayColorIdle = Color.FromArgb(colors.OverlayColorIdle);
                RefreshHudPreview();
                });

                RunLoadStep("theme and connections", () =>
                {
                ApplyTheme(settings.DarkTheme);
                if (_themeSwitch != null)
                    _themeSwitch.SetCheckedSilent(settings.DarkTheme);

                _lfsGameFolder = settings.LfsGameFolder ?? "";
                _achievementSoundEnabled = settings.AchievementSoundEnabled;
                _recentPickerColors = (settings.RecentPickerColors ?? new List<int>()).Take(RecentPickerColorCount).Select(Color.FromArgb).ToList();
                UpdateLfsFolderStatusLabel();

                _outSimPortBox.Value = Math.Clamp(settings.OutSimPort, 1, 65535);
                _outSimOptsBox.Text = string.IsNullOrWhiteSpace(settings.OutSimOptsHex) ? "bf" : settings.OutSimOptsHex;
                if (settings.OutSimConnectionEnabled)
                {
                    _outSimConnectionSwitch.SetCheckedSilent(true);
                    ApplyOutSimConnectionState(true);
                }
                });

                RunLoadStep("controller assist", () =>
                {
                var assistSettings = settings.ControllerAssistSettings ?? AssistSettings.ForPreset(AssistPreset.Street);
                if (!Enum.IsDefined(assistSettings.Preset) || !Enum.IsDefined(assistSettings.Mode))
                    assistSettings = AssistSettings.ForPreset(AssistPreset.Drift);
                else if (assistSettings.Preset != AssistPreset.Custom)
                    assistSettings = AssistSettings.ForPreset(assistSettings.Preset);
                _assistEngine.Settings = assistSettings;
                _assistEngine.Rumble = settings.ControllerAssistRumble ?? new RumbleSettings();
                _assistEngine.AssistCharacterPercent = Math.Clamp(settings.ControllerAssistCharacterPercent, -100, 100);
                _assistEngine.MasterEnabled = settings.ControllerAssistSteeringEnabled;
                _assistEngine.RumbleEnabled = settings.ControllerAssistVibrationEnabled;
                _caSteeringAssistSwitch.SetCheckedSilent(settings.ControllerAssistSteeringEnabled);
                _caVibrationSwitch.SetCheckedSilent(settings.ControllerAssistVibrationEnabled);
                _caCharacterSlider.SetValueSilent((int)Math.Round(_assistEngine.AssistCharacterPercent));
                UpdateAssistCharacterLabel();
                _caVibrationStrengthSlider.SetValueSilent((int)Math.Round(_assistEngine.Rumble.RumbleStrength * 100));
                _caVibrationStrengthValueLabel.Text = $"{(int)Math.Round(_assistEngine.Rumble.RumbleStrength * 100)}%";
                _caOverlayBarSwitch.SetCheckedSilent(settings.ControllerAssistOverlayBarVisible);
                _overlay.ControllerAssistBarVisible = settings.ControllerAssistOverlayBarVisible;
                _caHidePadSwitch.SetCheckedSilent(settings.ControllerAssistHidePhysicalPad);
                RefreshControllerAssistPresetButtons();
                _caMasterUserWanted = settings.ControllerAssistEnabled;
                });

                _lfsNoticeDontShow = settings.LfsNoticeDontShow;

                RunLoadStep("language", () =>
                {
                    if (Enum.TryParse<AppLanguage>(settings.Language, out var savedLanguage) &&
                        Enum.IsDefined(typeof(AppLanguage), savedLanguage))
                        Localization.SetLanguage(savedLanguage);
                    else
                        throw new FormatException($"unknown language '{settings.Language}'");
                });

                RunLoadStep("bindings", () =>
                {
                ApplyRevBinding(ref _revToggleBinding, revCfg.RevToggleBinding, ExecuteRevToggle);
                ApplyRevBinding(ref _revCalibrateBinding, revCfg.RevCalibrateBinding, ExecuteRevCalibrate);
                ApplyRevBinding(ref _revDecreaseBinding, revCfg.RevDecreaseBinding, ExecuteRevDecrease);
                ApplyRevBinding(ref _revIncreaseBinding, revCfg.RevIncreaseBinding, ExecuteRevIncrease);
                ApplyRevBinding(ref _lightToggleBinding, settings.LightToggleBinding, ExecuteLightToggle);
                ApplyRevBinding(ref _caToggleBinding, settings.ControllerAssistToggleBinding, ExecuteControllerAssistToggle);
                ApplyMenuHotkey(settings.MenuHotkeyBinding);
                RefreshControllerAssistBindLabel();

                ApplyIndicatorBinding(
                    settings.IndicatorLeftBinding,
                    code => _indicators.LeftKeyCode = code,
                    wb => _indicators.LeftWheelButton = wb,
                    _indicators.LeftWheelButton,
                    () => _indicators.ToggleLeft());

                ApplyIndicatorBinding(
                    settings.IndicatorRightBinding,
                    code => _indicators.RightKeyCode = code,
                    wb => _indicators.RightWheelButton = wb,
                    _indicators.RightWheelButton,
                    () => _indicators.ToggleRight());

                ApplyIndicatorBinding(
                    settings.IndicatorHazardBinding,
                    code => _indicators.HazardKeyCode = code,
                    wb => _indicators.HazardWheelButton = wb,
                    _indicators.HazardWheelButton,
                    () => _indicators.ToggleHazard());

                RefreshIndicatorBindButtonLabels();
                });

                RunLoadStep("input device", () =>
                {
                if (Guid.TryParse(settings.SteeringWheelDeviceGuid, out var savedGuid) && savedGuid != Guid.Empty)
                {
                    if (Enum.TryParse<JoystickOffset>(settings.SteeringWheelAxis, out var savedAxis))
                    {
                        _savedWheelGuid = savedGuid;
                        _savedWheelAxis = savedAxis;
                        _wheelInput.ConnectToDevice(savedGuid, savedAxis);
                    }
                }

                SyncControllerAssistAvailability();
                });

                RunLoadStep("indicators, speedometer, HUD and scoring", () =>
                {
                    _indicatorSoundsVolume = Math.Clamp(settings.IndicatorSoundsVolume, 0, 100);
                    _indicatorVolumeSlider.SetValueSilent(_indicatorSoundsVolume);
                    _indicatorVolumeValueLabel.Text = $"{_indicatorSoundsVolume}%";
                    _indicatorSoundsIntent.Wanted = settings.IndicatorSoundsEnabled;
                    _indicatorAutoCancelCheck.Checked = settings.IndicatorAutoCancelOnCenter;
                    _indicatorArmThresholdPct = Math.Clamp(settings.IndicatorArmThresholdPct, 1, 100);
                    _indicatorArmThresholdNumeric.Value = _indicatorArmThresholdPct;
                    _indicatorCenterThresholdPct = Math.Clamp(settings.IndicatorCenterThresholdPct, 1, 90);
                    _indicatorCenterThresholdNumeric.Value = _indicatorCenterThresholdPct;

                    _speedoTachoEnabledCheck.SetCheckedSilent(settings.SpeedoTachoEnabled);
                    _overlay.SpeedoTachoEnabled = settings.SpeedoTachoEnabled;
                    _speedoTachoIntent.Wanted = settings.SpeedoTachoEnabled;
                    UpdateOverlayAttachment();
                    _speedoTachoOffsetXNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetX,
                        _speedoTachoOffsetXNumeric.Minimum, _speedoTachoOffsetXNumeric.Maximum);
                    _speedoTachoOffsetYNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoOffsetY,
                        _speedoTachoOffsetYNumeric.Minimum, _speedoTachoOffsetYNumeric.Maximum);
                    _speedoTachoScaleNumeric.Value = Math.Clamp((decimal)settings.SpeedoTachoScale,
                        _speedoTachoScaleNumeric.Minimum, _speedoTachoScaleNumeric.Maximum);

                    _overlay.SpeedoTachoEnabled = settings.SpeedoTachoEnabled;
                    _overlay.SpeedoTachoOffsetX = settings.SpeedoTachoOffsetX;
                    _overlay.SpeedoTachoOffsetY = settings.SpeedoTachoOffsetY;
                    _overlay.SpeedoTachoScale = settings.SpeedoTachoScale;

                    _useMph = settings.SpeedoTachoUseMph;
                    _overlay.SpeedoTachoUseMph = _useMph;
                    if (_speedUnitToggle != null) _speedUnitToggle.SetCheckedSilent(_useMph);
                    UpdateSpeedUnitLabels();

                    _speedoPresets[0] = settings.SpeedoPresetA ?? new SpeedoPresetData();
                    _speedoPresets[1] = settings.SpeedoPresetB ?? SpeedoPresetData.Default2();
                    _speedoPresets[2] = settings.SpeedoPresetC ?? SpeedoPresetData.Default3();
                    _activeSpeedoPresetIndex = Math.Clamp(settings.ActiveSpeedoPreset, 0, _speedoPresets.Length - 1);
                    ApplySpeedoPreset(_speedoPresets[_activeSpeedoPresetIndex]);

                    if (_advancedOutGaugeCheck != null)
                    {
                        _advancedOutGaugeCheck.SetCheckedSilent(settings.AdvancedOutGaugeEnabled);
                        _advancedOutGaugeIntent.Wanted = settings.AdvancedOutGaugeEnabled;
                    }

                    if (_showRPMHudCheck != null)
                    {
                        _showRPMHudCheck.SetCheckedSilent(settings.ShowRPMHudEnabled);
                        _showRpmHudIntent.Wanted = settings.ShowRPMHudEnabled;
                    }

                    if (_indicatorsMasterEnabledCheck != null)
                    {
                        _indicatorsMasterEnabledCheck.Checked = settings.IndicatorsMasterEnabled;
                    }

                    if (_hudMasterEnabledCheck != null)
                    {
                        _scoreOverlayIntent.Wanted = settings.ShowScoreOverlay;
                        _insimHudIntent.Wanted = settings.ShowInsimHud;

                        if (settings.ShowInsimHud) _showHudCheck.Checked = true;
                        else _showOverlayCheck.Checked = settings.ShowScoreOverlay;

                        _hudMasterEnabledCheck.Checked = settings.HudMasterEnabled;
                    }

                    _drift.SetAngleLevelThresholds(settings.AngleLevelThresholds);
                    _drift.SetSpeedLevelThresholds(settings.SpeedLevelThresholds);
                    _drift.SetBackwardDriftAngleThreshold(settings.BackwardDriftAngleThreshold);

                    _drift.SetMinDriftSpeedKmh(settings.MinDriftSpeedKmh);
                    _drift.SetMaxBurnoutSpeedKmh(settings.MaxBurnoutSpeedKmh);
                    if (_minDriftSpeedNumeric != null) _minDriftSpeedNumeric.Value = (decimal)settings.MinDriftSpeedKmh;
                    if (_maxBurnoutSpeedNumeric != null) _maxBurnoutSpeedNumeric.Value = (decimal)settings.MaxBurnoutSpeedKmh;

                    _collisions.SetHitLevelThresholds(settings.HitLevelThresholds);
                    if (_collisionDetectCheck != null)
                    {
                        _collisionDetectCheck.Checked = settings.CollisionDetectionEnabled;
                        _collisions.Enabled = settings.CollisionDetectionEnabled;
                    }

                    _objectCollisionDetectionEnabled = settings.ObjectCollisionDetectionEnabled;
                    if (_objectCollisionDetectCheck != null)
                        _objectCollisionDetectCheck.Checked = settings.ObjectCollisionDetectionEnabled;
                });

                RunLoadStep("OutGauge connection", () =>
                {
                    _outGaugeConnectionSwitch.SetCheckedSilent(settings.OutGaugeConnectionEnabled);
                    ApplyOutGaugeConnectionState(settings.OutGaugeConnectionEnabled);
                });

                RunLoadStep("colour swatches", RefreshColorButtonSwatches);
            }
            catch (Exception ex)
            {
                RegisterLoadError("settings", ex);
            }
            finally
            {
                ReportSettingsLoadErrors();
            }
        }

        private System.Windows.Forms.Timer? _settingsSaveTimer;
        private DateTime _settingsDirtySinceUtc = DateTime.MinValue;
        private DateTime _lastSaveErrorReportUtc = DateTime.MinValue;
        private const int SettingsSaveDelayMs = 400;
        private const double SettingsSaveMaxDelaySec = 2.0;

        private void SaveSettings()
        {
            if (IsDisposed) return;

            if (!IsUiThread)
            {
                PostToUi(SaveSettings);
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (_settingsDirtySinceUtc == DateTime.MinValue)
                _settingsDirtySinceUtc = now;

            if ((now - _settingsDirtySinceUtc).TotalSeconds >= SettingsSaveMaxDelaySec)
            {
                FlushSettings();
                return;
            }

            if (_settingsSaveTimer == null)
            {
                _settingsSaveTimer = new System.Windows.Forms.Timer { Interval = SettingsSaveDelayMs };
                _settingsSaveTimer.Tick += (s, e) => FlushSettings();
            }

            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        }

        private void FlushSettings()
        {
            _settingsSaveTimer?.Stop();
            if (_settingsDirtySinceUtc == DateTime.MinValue) return;

            _settingsDirtySinceUtc = DateTime.MinValue;
            SaveSettingsNow();
        }

        private void SaveSettingsNow()
        {
            try
            {
                Directory.CreateDirectory(ConfigsDir);

                CalibratedMAXRPM = (int)_revLimiterNumeric.Value;

                SavedMSCUT = (int)_revCutMS.Value;

                _speedoPresets[_activeSpeedoPresetIndex] = CaptureSpeedoPreset();

                AppSettings settings = new AppSettings()
                {
                    Language = Localization.CurrentLanguage.ToString(),

                    DarkTheme = _isDarkTheme,

                    LfsGameFolder = _lfsGameFolder,
                    AchievementSoundEnabled = _achievementSoundEnabled,
                    RecentPickerColors = _recentPickerColors.Select(c => c.ToArgb()).ToList(),

                    LightToggleBinding = _lightToggleBinding,
                    ControllerAssistToggleBinding = _caToggleBinding,
                    MenuHotkeyBinding = _menuHotkeyBinding,
                    IndicatorLeftBinding = GetIndicatorBinding(_indicators.LeftKeyCode, _indicators.LeftWheelButton),
                    IndicatorRightBinding = GetIndicatorBinding(_indicators.RightKeyCode, _indicators.RightWheelButton),
                    IndicatorHazardBinding = GetIndicatorBinding(_indicators.HazardKeyCode, _indicators.HazardWheelButton),

                    SteeringWheelDeviceGuid = _savedWheelGuid.ToString(),
                    SteeringWheelAxis = _savedWheelAxis.ToString(),

                    IndicatorSoundsEnabled = _indicatorSoundsIntent.Wanted,
                    IndicatorSoundsVolume = _indicatorVolumeSlider.Value,
                    IndicatorAutoCancelOnCenter = _indicatorAutoCancelIntent.Resolve(
                        _indicatorsMasterEnabledCheck.Checked, _indicatorAutoCancelCheck.Checked),
                    IndicatorArmThresholdPct = _indicatorArmThresholdPct,
                    IndicatorCenterThresholdPct = _indicatorCenterThresholdPct,

                    SpeedoTachoEnabled = _speedoTachoIntent.Wanted,
                    SpeedoTachoOffsetX = (float)_speedoTachoOffsetXNumeric.Value,
                    SpeedoTachoOffsetY = (float)_speedoTachoOffsetYNumeric.Value,
                    SpeedoTachoScale = (float)_speedoTachoScaleNumeric.Value,
                    SpeedoTachoUseMph = _useMph,

                    ActiveSpeedoPreset = _activeSpeedoPresetIndex,
                    SpeedoPresetA = _speedoPresets[0],
                    SpeedoPresetB = _speedoPresets[1],
                    SpeedoPresetC = _speedoPresets[2],

                    AdvancedOutGaugeEnabled = _advancedOutGaugeIntent.Wanted,

                    OutGaugeConnectionEnabled = _outGaugeConnectionSwitch?.Checked ?? false,

                    OutSimConnectionEnabled = _outSimConnectionSwitch?.Checked ?? false,
                    OutSimPort = (int)(_outSimPortBox?.Value ?? _outSim.UdpPort),
                    OutSimOptsHex = _outSimOptsBox?.Text.Trim() ?? "bf",

                    ControllerAssistEnabled = _caMasterUserWanted,
                    ControllerAssistSteeringEnabled = _caSteeringAssistSwitch?.Checked ?? true,
                    ControllerAssistVibrationEnabled = _caVibrationSwitch?.Checked ?? true,
                    ControllerAssistOverlayBarVisible = _caOverlayBarSwitch?.Checked ?? false,
                    ControllerAssistHidePhysicalPad = _caHidePadSwitch?.Checked ?? false,
                    ControllerAssistSettings = _assistEngine.Settings,
                    ControllerAssistRumble = _assistEngine.Rumble,
                    ControllerAssistCharacterPercent = _assistEngine.AssistCharacterPercent,

                    ShowRPMHudEnabled = _showRpmHudIntent.Wanted,

                    IndicatorsMasterEnabled = _indicatorsMasterEnabledCheck.Checked,

                    LfsNoticeDontShow = _lfsNoticeDontShow,
                    HudMasterEnabled = _hudMasterEnabledCheck?.Checked ?? true,
                    ShowScoreOverlay = _scoreOverlayIntent.Resolve(
                        _hudMasterEnabledCheck?.Checked ?? true, _showOverlayCheck?.Checked ?? true),
                    ShowInsimHud = _insimHudIntent.Resolve(
                        _hudMasterEnabledCheck?.Checked ?? true, _showHudCheck?.Checked ?? false),

                    AngleLevelThresholds = _drift.AngleLevelThresholds.ToArray(),
                    SpeedLevelThresholds = _drift.SpeedLevelThresholds.ToArray(),
                    BackwardDriftAngleThreshold = _drift.BackwardDriftAngleThreshold,

                    MinDriftSpeedKmh = _drift.MinDriftSpeedKmh,
                    MaxBurnoutSpeedKmh = _drift.MaxBurnoutSpeedKmh,

                    CollisionDetectionEnabled = _collisionDetectCheck?.Checked ?? false,
                    HitLevelThresholds = _collisions.HitLevelThresholds.ToArray(),
                    ObjectCollisionDetectionEnabled = _objectCollisionDetectCheck?.Checked ?? true,
                };

                ColorSettings colors = new ColorSettings()
                {
                    InSimColor1 = InSimColor1,
                    InSimColor2 = InSimColor2,
                    InSimColor3 = InSimColor3,
                    InSimColor4 = InSimColor4,
                    InSimColor5 = InSimColor5,
                    InSimColor6 = InSimColor6,
                    InSimColorIdle = InSimColorIdle,

                    OverlayColor1 = OverlayColor1.ToArgb(),
                    OverlayColor2 = OverlayColor2.ToArgb(),
                    OverlayColor3 = OverlayColor3.ToArgb(),
                    OverlayColor4 = OverlayColor4.ToArgb(),
                    OverlayColor5 = OverlayColor5.ToArgb(),
                    OverlayColor6 = OverlayColor6.ToArgb(),
                    OverlayColorIdle = OverlayColorIdle.ToArgb(),
                };

                RevLimiterConfig revCfg = new RevLimiterConfig()
                {
                    CalibratedMAXRPM = CalibratedMAXRPM,
                    SavedMSCUT = SavedMSCUT,
                    VehicleRevLimiterSettings = _vehicleRevSettings,
                    TurboMaxBarByCar = _turboMaxBarByCar,
                    RevToggleBinding = _revToggleBinding,
                    RevCalibrateBinding = _revCalibrateBinding,
                    RevDecreaseBinding = _revDecreaseBinding,
                    RevIncreaseBinding = _revIncreaseBinding,
                    AutoCalibrateNewCar = _revAutoCalibrateIntent.Wanted,

                    RevLimiterEnabled = _revEnabledIntent.Wanted,
                };

                WriteJson(SettingsFile, settings);
                WriteJson(ColorsFile, colors);
                WriteJson(RevLimiterFile, revCfg);
            }
            catch (Exception ex)
            {
                if ((DateTime.UtcNow - _lastSaveErrorReportUtc).TotalSeconds > 30)
                {
                    _lastSaveErrorReportUtc = DateTime.UtcNow;
                    AppendStatusMessage(string.Format(Localization.T("status.settings.save_error"), ex.Message), StatusErrorColor);
                }
            }
        }

        private const int DefaultVehicleMaxRpm = 9000;
        private const int RevCutMinMs = 25;
        private const int RevCutMaxMs = 500;
        private const int DefaultVehicleCutMs = 25;

        private static int SanitizeRevRpm(int rpm) =>
            rpm < RevLimitMinRpm || rpm > RevLimitMaxRpm ? DefaultVehicleMaxRpm : rpm;

        private static int SanitizeRevCutMs(int cutMs) =>
            cutMs < RevCutMinMs || cutMs > RevCutMaxMs ? DefaultVehicleCutMs : cutMs;
    }
}
