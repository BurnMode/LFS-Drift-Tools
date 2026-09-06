using System;
using System.Collections.Generic;
using System.Windows.Forms;
using LFSDriftBuddy.InSim;
using static LFSDriftBuddy.DriftEngine;

namespace LFSDriftBuddy
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

        private const int ActiveGraceMs = 450;
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

        public void InvalidateCache() => _lastSent.Clear();

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
        }

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

        public void ShowInGameRPMLimitter(string text)
        {
            if (!_insim.IsConnected) return;

            if (_showRpmHudCheck.Checked && _revLimiter.Enabled)
            {
                Send(BTN_RPMLIMIT, "^6" + Localization.T("rev.limit") + text, l: 0, t: 196, w: 25, h: 5, bStyle: 65);
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
        }
    }
}