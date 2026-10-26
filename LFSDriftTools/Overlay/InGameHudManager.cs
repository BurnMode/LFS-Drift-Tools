using System;
using System.Collections.Generic;
using System.Windows.Forms;
using LFSDriftTools.InSim;
using static LFSDriftTools.DriftEngine;

namespace LFSDriftTools
{

    public class InGameHudManager : IDisposable
    {
        private const byte BTN_SCORE = 1;
        private const byte BTN_RUN = 2;
        private const byte BTN_COMBO = 3;
        private const byte BTN_LABEL = 4;
        private const byte BTN_AWARD = 5;
        private const byte BTN_RIGHTIND = 6;
        private const byte BTN_LEFTIND = 7;
        private const byte BTN_RPMLIMIT = 8;
        private const byte BTN_RPMLIMITSTATUS = 9;
        private const byte BTN_RPMLIMITINFO = 10;

        private const byte BTN_ACH_BG = 20;
        private const byte BTN_ACH_TITLE = 21;
        private const byte BTN_ACH_METHOD = 22;
        private const byte BTN_ACH_NAME = 23;
        private const byte BTN_ACH_DESC = 24;
        private const int AchDescMaxLines = 4;
        private const byte BTN_ACH_LAST = BTN_ACH_DESC + AchDescMaxLines - 1;
        private const int AchDescLineChars = 46;
        private const int AchievementCardMs = 5500;
        private const byte AchLeft = 50;
        private const byte AchWidth = 100;
        private const byte AchPad = 3;
        private const byte AchBottom = 182;
        private const byte AchTitleH = 5;
        private const byte AchNameH = 8;
        private const byte AchDescH = 5;
        private const byte BTN_LAP_BG = 40;
        private const byte BTN_LAP_TITLE = 41;
        private const byte BTN_LAP_VALUE = 42;
        private const byte BTN_LAP_LAST_TITLE = 43;
        private const byte BTN_LAP_LAST_VALUE = 44;
        private const byte BTN_LAP_BEST_TITLE = 45;
        private const byte BTN_LAP_BEST_VALUE = 46;
        private const byte BTN_LAP_CONTEXT = 47;
        private const byte LapLeft = 1;
        private const byte LapWidth = 36;
        private const byte LapPad = 2;
        private const byte LapTitleH = 4;
        private const byte LapValueH = 7;
        private const byte LapContextH = 4;
        private const int LapCenterY = 100;
        private const byte BTN_SPD_RPM = 50;
        private const byte BTN_SPD_BAR = 51;
        private const byte BTN_SPD_SPEED = 52;
        private const byte BTN_SPD_INFO = 53;
        private const byte SpdRpmLeft = 44;
        private const byte SpdRpmWidth = 22;
        private const byte SpdBarLeft = 67;
        private const byte SpdBarWidth = 66;
        private const byte SpdSpeedLeft = 134;
        private const byte SpdSpeedWidth = 24;
        private const byte SpdRowTop = 189;
        private const byte SpdRowHeight = 7;
        private const byte SpdInfoTop = 183;
        private const byte SpdInfoHeight = 5;
        private const int SpdTicks = 90;
        private const double SpdGreenUntil = 0.70;
        private const double SpdYellowUntil = 0.90;
        private const double SpdFullRedFrom = 0.98;
        private const byte ISB_DARK = 32;
        private const byte ISB_LEFT = 64;
        private const byte ISB_RIGHT = 128;

        private const byte xPos = 108;
        private const byte xPosSec = 80;
        private const byte xPosT = 120;

        private readonly InSimConnection _insim;
        private readonly DriftEngine _drift;
        private readonly RevLimiter _revLimiter;
        private readonly MacToggleSwitch _showHudCheck;
        private readonly MacToggleSwitch _showRpmHudCheck;
        private readonly MinimalNumericUpDown _revLimiterNumeric;
        private readonly Label _angleValueLabel;

        private readonly System.Windows.Forms.Timer _awardTimer;
        private int _awardTick;
        private string _colorCurrentMain = "^7";

        private readonly System.Windows.Forms.Timer _achievementTimer;
        private readonly Queue<InSimAchievementCard> _achievementQueue = new();
        private bool _achievementShown;
        private bool _lapShown;
        private bool _speedoShown;

        private static readonly double ActiveGraceMs = DriftEngine.COMBO_TIMEOUT_SEC * 1000;
        private DateTime _lastActiveUtc = DateTime.MinValue;

        private readonly Dictionary<byte, string> _lastSent = new();

        private void Send(byte id, string text, byte l, byte t, byte w, byte h, byte bStyle)
        {
            string key = $"{text}|{l}|{t}|{w}|{h}|{bStyle}";
            if (_lastSent.TryGetValue(id, out var prev) && prev == key)
                return;

            _lastSent[id] = key;
            _insim.ShowButton(id, text, l: l, t: t, w: w, h: h, bStyle: bStyle);
        }

        public void InvalidateCache()
        {
            _lastSent.Clear();
            _rpmHudLimit = -1;
        }

        private int _rpmHudLimit = -1;
        private bool _rpmHudVisible;
        private AppLanguage _rpmHudLanguage;

        public string InSimColor1 = "^7";
        public string InSimColor2 = "^6";
        public string InSimColor3 = "^3";
        public string InSimColor4 = "^5";
        public string InSimColor5 = "^1";

        public string InSimColor6 = "^2";

        private string InSimColorForTier(int tier) => tier switch
        {
            2 => InSimColor2,
            3 => InSimColor3,
            4 => InSimColor4,
            5 => InSimColor5,
            6 => InSimColor6,
            _ => InSimColor1,
        };

        public string InSimColorIdle = "^7";

        public string DriftLabel = "";
        public string IndicatorLabelRight = "";
        public string IndicatorLabelLeft = "";
        public DriftLabelKind CurrentLabelKind;

        public InGameHudManager(
            InSimConnection insim,
            DriftEngine drift,
            RevLimiter revLimiter,
            MacToggleSwitch showHudCheck,
            MacToggleSwitch showRpmHudCheck,
            MinimalNumericUpDown revLimiterNumeric,
            Label angleValueLabel)
        {
            _insim = insim;
            _drift = drift;
            _revLimiter = revLimiter;
            _showHudCheck = showHudCheck;
            _showRpmHudCheck = showRpmHudCheck;
            _revLimiterNumeric = revLimiterNumeric;
            _angleValueLabel = angleValueLabel;

            _awardTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _awardTimer.Tick += AwardTimer_Tick;

            _achievementTimer = new System.Windows.Forms.Timer { Interval = AchievementCardMs };
            _achievementTimer.Tick += AchievementTimer_Tick;
        }

        public void ShowAchievement(string title, string name, string description, string method, AchievementRarity rarity)
        {
            if (!_insim.IsConnected || !_showHudCheck.Checked) return;

            _achievementQueue.Enqueue(new InSimAchievementCard(title, name, description, method, rarity));
            if (!_achievementShown) ShowNextAchievement();
        }

        public void ClearAchievement()
        {
            _achievementTimer.Stop();
            _achievementQueue.Clear();
            if (_achievementShown) DeleteAchievementButtons();
            _achievementShown = false;
        }

        private void ShowNextAchievement()
        {
            if (_achievementQueue.Count == 0 || !_insim.IsConnected || !_showHudCheck.Checked)
            {
                _achievementQueue.Clear();
                _achievementShown = false;
                return;
            }

            var card = _achievementQueue.Dequeue();
            List<string> lines = WrapText(card.Description, AchDescLineChars, AchDescMaxLines);
            if (_achievementShown) DeleteAchievementButtons();

            int height = AchPad + AchTitleH + AchNameH + lines.Count * AchDescH + AchPad;
            byte top = (byte)(AchBottom - height);
            byte innerLeft = AchLeft + AchPad;
            byte innerWidth = AchWidth - 2 * AchPad;

            Send(BTN_ACH_BG, "", l: AchLeft, t: top, w: AchWidth, h: (byte)height, bStyle: ISB_DARK);

            byte y = (byte)(top + AchPad);
            Send(BTN_ACH_TITLE, RarityColor(card.Rarity) + card.Title, l: innerLeft, t: y, w: innerWidth, h: AchTitleH, bStyle: ISB_LEFT);
            Send(BTN_ACH_METHOD, "^8" + card.Method, l: innerLeft, t: y, w: innerWidth, h: AchTitleH, bStyle: ISB_RIGHT);
            y += AchTitleH;

            Send(BTN_ACH_NAME, "^7" + card.Name, l: innerLeft, t: y, w: innerWidth, h: AchNameH, bStyle: ISB_LEFT);
            y += AchNameH;

            for (int i = 0; i < lines.Count; i++)
            {
                Send((byte)(BTN_ACH_DESC + i), "^8" + lines[i], l: innerLeft, t: y, w: innerWidth, h: AchDescH, bStyle: ISB_LEFT);
                y += AchDescH;
            }

            _achievementShown = true;
            _achievementTimer.Stop();
            _achievementTimer.Start();
        }

        private void AchievementTimer_Tick(object? sender, EventArgs e)
        {
            _achievementTimer.Stop();
            if (_insim.IsConnected) DeleteAchievementButtons();
            _achievementShown = false;
            ShowNextAchievement();
        }

        private void DeleteAchievementButtons()
        {
            _insim.DeleteButtons(BTN_ACH_BG, BTN_ACH_LAST);
            for (byte id = BTN_ACH_BG; id <= BTN_ACH_LAST; id++)
                _lastSent.Remove(id);
        }

        public void HideSpeedo()
        {
            if (!_speedoShown) return;
            if (_insim.IsConnected) _insim.DeleteButtons(BTN_SPD_RPM, BTN_SPD_INFO);
            for (byte id = BTN_SPD_RPM; id <= BTN_SPD_INFO; id++)
                _lastSent.Remove(id);
            _speedoShown = false;
        }

        public void UpdateSpeedo(float rpm, float limitRpm, bool revCut, double speed, string unit, float? turboBar, float? fuel, float? temperature, string temperatureLabel)
        {
            if (!_showHudCheck.Checked || !_insim.IsConnected)
            {
                HideSpeedo();
                return;
            }

            double fraction = limitRpm > 0 ? Math.Clamp(rpm / limitRpm, 0.0, 1.0) : 0.0;
            bool fullRed = revCut || fraction >= SpdFullRedFrom;
            string rpmColor = fullRed || fraction >= 0.95 ? "^1" : fraction >= SpdYellowUntil ? "^3" : "^7";
            Send(BTN_SPD_RPM, $"{rpmColor}{rpm:N0} ^8RPM", l: SpdRpmLeft, t: SpdRowTop, w: SpdRpmWidth, h: SpdRowHeight, bStyle: ISB_RIGHT);

            Send(BTN_SPD_BAR, fullRed ? "^1" + new string('|', SpdTicks) : BuildRpmTicks(fraction), l: SpdBarLeft, t: SpdRowTop, w: SpdBarWidth, h: SpdRowHeight, bStyle: ISB_DARK);

            Send(BTN_SPD_SPEED, $"^7{speed:0} ^8{unit}", l: SpdSpeedLeft, t: SpdRowTop, w: SpdSpeedWidth, h: SpdRowHeight, bStyle: ISB_LEFT);

            var parts = new List<string>();
            if (turboBar.HasValue)
                parts.Add($"^8{Localization.T("hud.btn.turbo")} ^7{turboBar.Value:0.00} bar");
            if (fuel.HasValue)
                parts.Add($"^8{Localization.T("hud.btn.fuel")} ^7{fuel.Value * 100:0}%");
            if (temperature.HasValue)
                parts.Add($"^8{temperatureLabel} ^7{temperature.Value:0}°C");
            Send(BTN_SPD_INFO, string.Join("   ", parts), l: SpdRpmLeft, t: SpdInfoTop,
                w: (byte)(SpdSpeedLeft + SpdSpeedWidth - SpdRpmLeft), h: SpdInfoHeight, bStyle: 0);

            _speedoShown = true;
        }

        private static string BuildRpmTicks(double fraction)
        {
            int lit = (int)Math.Round(fraction * SpdTicks);
            var text = new System.Text.StringBuilder(SpdTicks * 2);
            string current = "";
            for (int i = 0; i < SpdTicks; i++)
            {
                double position = (i + 1) / (double)SpdTicks;
                string color = i >= lit ? "^0" : position <= SpdGreenUntil ? "^2" : position <= SpdYellowUntil ? "^3" : "^1";
                if (color != current)
                {
                    text.Append(color);
                    current = color;
                }
                text.Append('|');
            }
            return text.ToString();
        }

        public void UpdateLapBox(bool visible, long lapScore, long lastLapScore, long bestLapScore, string context)
        {
            if (!visible || !_showHudCheck.Checked || !_insim.IsConnected)
            {
                if (_lapShown)
                {
                    if (_insim.IsConnected) _insim.DeleteButtons(BTN_LAP_BG, BTN_LAP_CONTEXT);
                    for (byte id = BTN_LAP_BG; id <= BTN_LAP_CONTEXT; id++)
                        _lastSent.Remove(id);
                    _lapShown = false;
                }
                return;
            }

            bool hasContext = !string.IsNullOrEmpty(context);
            int height = LapPad * 2 + 3 * (LapTitleH + LapValueH) + (hasContext ? LapContextH + 1 : 0);
            byte top = (byte)(LapCenterY - height / 2);
            byte innerLeft = LapLeft + LapPad;
            byte innerWidth = LapWidth - 2 * LapPad;

            Send(BTN_LAP_BG, "", l: LapLeft, t: top, w: LapWidth, h: (byte)height, bStyle: ISB_DARK);

            byte y = (byte)(top + LapPad);
            Send(BTN_LAP_TITLE, "^8" + Localization.T("hud.lapscore"), l: innerLeft, t: y, w: innerWidth, h: LapTitleH, bStyle: ISB_LEFT);
            y += LapTitleH;
            Send(BTN_LAP_VALUE, "^7" + lapScore.ToString("N0"), l: innerLeft, t: y, w: innerWidth, h: LapValueH, bStyle: ISB_LEFT);
            y += LapValueH;
            Send(BTN_LAP_LAST_TITLE, "^8" + Localization.T("hud.lastlapscore"), l: innerLeft, t: y, w: innerWidth, h: LapTitleH, bStyle: ISB_LEFT);
            y += LapTitleH;
            Send(BTN_LAP_LAST_VALUE, "^6" + lastLapScore.ToString("N0"), l: innerLeft, t: y, w: innerWidth, h: LapValueH, bStyle: ISB_LEFT);
            y += LapValueH;
            Send(BTN_LAP_BEST_TITLE, "^8" + Localization.T("hud.bestlapscore"), l: innerLeft, t: y, w: innerWidth, h: LapTitleH, bStyle: ISB_LEFT);
            y += LapTitleH;
            Send(BTN_LAP_BEST_VALUE, "^3" + bestLapScore.ToString("N0"), l: innerLeft, t: y, w: innerWidth, h: LapValueH, bStyle: ISB_LEFT);
            y += LapValueH;
            Send(BTN_LAP_CONTEXT, hasContext ? "^8" + context : "", l: innerLeft, t: (byte)(y + 1), w: innerWidth, h: LapContextH, bStyle: ISB_LEFT);

            _lapShown = true;
        }

        private static string RarityColor(AchievementRarity rarity) => rarity switch
        {
            AchievementRarity.Rare => "^6",
            AchievementRarity.Epic => "^5",
            AchievementRarity.Legendary => "^3",
            _ => "^7",
        };

        private static List<string> WrapText(string text, int maxChars, int maxLines)
        {
            var lines = new List<string>();
            var line = new System.Text.StringBuilder();
            foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) lines.Add(line.ToString());

            if (lines.Count > maxLines)
            {
                string last = string.Join(" ", lines.GetRange(maxLines - 1, lines.Count - maxLines + 1));
                lines.RemoveRange(maxLines - 1, lines.Count - maxLines + 1);
                lines.Add(last);
            }
            return lines;
        }

        private readonly record struct InSimAchievementCard(
            string Title, string Name, string Description, string Method, AchievementRarity Rarity);

        public void InitInGameHUD()
        {
            if (_showHudCheck.Checked)
            {
                Send(BTN_SCORE, InSimColorIdle + _drift.TotalScore.ToString("N0") + InSimColorIdle,
                    l: 70, t: 3, w: 60, h: 15, bStyle: 5);
            }
            ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());
        }

        public void UpdateInGameHUD()
        {
            if (!_insim.IsConnected) return;

            ShowInGameRPMLimitter(_revLimiterNumeric.Value.ToString());

            bool activeNow = _drift.IsDrifting || _drift.IsSpeeding || _drift.IsBurnout;
            if (activeNow) _lastActiveUtc = DateTime.UtcNow;

            bool active = activeNow || (DateTime.UtcNow - _lastActiveUtc).TotalMilliseconds < ActiveGraceMs;

            if (_showHudCheck.Checked && !active)
            {
                Send(BTN_SCORE, $"{InSimColorIdle}{_drift.TotalScore:N0}{InSimColorIdle}",
                    l: 70, t: 3, w: 60, h: 15, bStyle: 5);
            }

            if (active)
            {
                string angleCol = InSimColorForTier(DriftEngine.GetColorTier(CurrentLabelKind));

                _colorCurrentMain = angleCol;

                string scoreText = _drift.IsDrifting
                    ? $"{angleCol}{_drift.TotalScore:N0}"
                    : $"{angleCol}{_drift.TotalScore:N0}{angleCol}";

                if (_showHudCheck.Checked)
                    Send(BTN_SCORE, scoreText, l: 70, t: 3, w: 60, h: 15, bStyle: 5);

                if (_showHudCheck.Checked) ShowInGameAward(_drift.LastAwardedText);

                Send(BTN_LABEL, angleCol + DriftLabel, l: 90, t: 15, w: 20, h: 7, bStyle: 5);

                string spaceInd = _drift.TotalScore >= 1000000 ? "   "
                    : _drift.TotalScore >= 100000 ? "  "
                    : _drift.TotalScore >= 10000 ? " " : "";

                Send(BTN_RIGHTIND, $"{angleCol}{spaceInd}{IndicatorLabelRight}",
                    l: 100, t: 3, w: 30, h: 15, bStyle: 5);
                Send(BTN_LEFTIND, $"{angleCol}{IndicatorLabelLeft}{spaceInd}",
                    l: 70, t: 3, w: 30, h: 15, bStyle: 5);

                string comboCol = _drift.ComboMultiplier >= 5 ? InSimColor4
                    : _drift.ComboMultiplier >= 3 ? InSimColor3
                    : _drift.ComboMultiplier >= 2 ? InSimColor2 : InSimColor1;

                Send(BTN_COMBO,
                    _drift.IsDrifting
                        ? $"{angleCol} {_angleValueLabel.Text} {comboCol}x{_drift.ComboMultiplier}"
                        : $"  {comboCol}x{_drift.ComboMultiplier}",
                    l: xPosSec, t: 15, w: 10, h: 7, bStyle: 5);

                string runCol = _drift.CurrentRunPoints >= 5000 ? InSimColor4
                    : _drift.CurrentRunPoints >= 1500 ? InSimColor3
                    : _drift.CurrentRunPoints >= 500 ? InSimColor2 : InSimColor1;

                Send(BTN_RUN, $"{runCol}{_drift.CurrentRunPoints:N0}",
                    l: xPos, t: 15, w: 10, h: 7, bStyle: 5);
            }
            else
            {
                Send(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                Send(BTN_COMBO, "", l: xPosSec, t: 0, w: 1, h: 1, bStyle: 2);
                Send(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                Send(BTN_RIGHTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
                Send(BTN_LEFTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
            }

            if (!_showHudCheck.Checked)
                ClearAllButtons();
        }

        public void ShowInGameAward(string text)
        {
            if (!_showHudCheck.Checked)
            {
                ClearAllButtons();
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                Send(BTN_AWARD, "", l: 60, t: 22, w: 80, h: 6, bStyle: 5);
                return;
            }

            string colorPrefix = text == _drift.LastAwardedText ? _colorCurrentMain : "^5";
            Send(BTN_AWARD, colorPrefix + text, l: 60, t: 22, w: 80, h: 6, bStyle: 5);
        }

        public void ShowInGameRPMLimitter(int limit)
        {
            if (!_insim.IsConnected) return;

            bool visible = _showRpmHudCheck.Checked && _revLimiter.Enabled;
            AppLanguage language = Localization.CurrentLanguage;

            if (limit == _rpmHudLimit && visible == _rpmHudVisible && language == _rpmHudLanguage)
                return;

            _rpmHudLimit = limit;
            _rpmHudVisible = visible;
            _rpmHudLanguage = language;

            ShowInGameRPMLimitter(limit.ToString());
        }

        public void ShowInGameRPMLimitter(string text)
        {
            if (!_insim.IsConnected) return;

            if (_showRpmHudCheck.Checked && _revLimiter.Enabled)
            {
                Send(BTN_RPMLIMIT, "^6" + string.Format(Localization.T("rev.limit.value"), text), l: 0, t: 196, w: 25, h: 5, bStyle: 65);
            }
            else
            {
                Send(BTN_RPMLIMITSTATUS, "", l: 0, t: 180, w: 35, h: 4, bStyle: 65);
                Send(BTN_RPMLIMIT, "", l: 0, t: 185, w: 35, h: 4, bStyle: 65);
                Send(BTN_RPMLIMITINFO, "", l: 0, t: 190, w: 35, h: 3, bStyle: 65);
            }
        }

        public void StartAwardFlash()
        {
            _awardTick = 0;
            _awardTimer.Start();
        }

        public void ClearAllButtons()
        {
            Send(BTN_SCORE, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
            Send(BTN_AWARD, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
            Send(BTN_COMBO, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
            Send(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 5);
            Send(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
            Send(BTN_RIGHTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
            Send(BTN_LEFTIND, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
        }

        private void AwardTimer_Tick(object? sender, EventArgs e)
        {
            _awardTick++;
            if (_awardTick < 30) return;

            _awardTimer.Stop();
            if (_insim.IsConnected)
                Send(BTN_AWARD, "", l: xPosT, t: 0, w: 1, h: 1, bStyle: 2);
            Send(BTN_RUN, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);
            Send(BTN_COMBO, "", l: xPosSec, t: 0, w: 1, h: 1, bStyle: 2);
            Send(BTN_LABEL, "", l: xPos, t: 0, w: 1, h: 1, bStyle: 2);

            string scoreText = _drift.IsDrifting
                ? $"{InSimColor2}{_drift.TotalScore:N0}"
                : $"{InSimColor1}{_drift.TotalScore:N0}{InSimColor1}";
            Send(BTN_SCORE, scoreText, l: 70, t: 3, w: 60, h: 15, bStyle: 5);

            string indR = _drift.IsDrifting ? $"{InSimColor2}{IndicatorLabelRight}" : "";
            Send(BTN_RIGHTIND, indR, l: 100, t: 3, w: 30, h: 15, bStyle: 5);

            string indL = _drift.IsDrifting ? $"{InSimColor2}{IndicatorLabelLeft}" : "";
            Send(BTN_LEFTIND, indL, l: 70, t: 3, w: 30, h: 15, bStyle: 5);
        }

        public void Dispose()
        {
            _awardTimer?.Stop();
            _awardTimer?.Dispose();
            _achievementTimer?.Stop();
            _achievementTimer?.Dispose();
        }
    }
}