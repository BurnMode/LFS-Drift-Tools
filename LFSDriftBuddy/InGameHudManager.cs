using System;
using System.Collections.Generic;
using System.Windows.Forms;
using LFSDriftBuddy.InSim;
using static LFSDriftBuddy.DriftEngine;

namespace LFSDriftBuddy
{
    // In-game IS_BTN HUD: score, run points, combo, drift label, indicator flash text, RPM limiter readout.
    // Wydzielone z MainForm.cs — cała logika wysyłania przycisków InSim żyje teraz tutaj.
    public class InGameHudManager : IDisposable
    {
        private const byte BTN_SCORE = 1;   // total score top-center
        private const byte BTN_RUN = 2;   // current run points
        private const byte BTN_COMBO = 3;   // combo multiplier
        private const byte BTN_LABEL = 4;   // drift label text
        private const byte BTN_AWARD = 5;   // flash on drift end
        private const byte BTN_RIGHTIND = 6;   // right indicator flash
        private const byte BTN_LEFTIND = 7;   // left indicator flash
        private const byte BTN_RPMLIMIT = 8;   // rev limiter value
        private const byte BTN_RPMLIMITSTATUS = 9;   // rev limiter status
        private const byte BTN_RPMLIMITINFO = 10;   // rev limiter info

        private const byte xPos = 108;
        private const byte xPosSec = 80;
        private const byte xPosT = 120;

        private readonly InSimConnection _insim;
        private readonly DriftEngine _drift;
        private readonly RevLimiter _revLimiter;
        private readonly MacCheckBox _showHudCheck;
        private readonly MacCheckBox _showRpmHudCheck;
        private readonly NumericUpDown _revLimiterNumeric;
        private readonly Label _angleValueLabel;

        private readonly System.Windows.Forms.Timer _awardTimer;
        private int _awardTick;
        private string _colorCurrentMain = "^7";

        // ── Tłumienie migotania RUN/COMBO/LABEL/IND ─────────────────────────────
        // "active" (IsDrifting/IsSpeeding/IsBurnout) jest liczone na surowo, klatka po
        // klatce telemetrii (~co 200ms, w rytm pakietów MCI) — pojedyncza klatka szumu
        // w kącie/prędkości tuż przy progu (np. MIN_DRIFT_ANGLE_DEG) natychmiast zbija
        // "active" na false, co czyściło te przyciski, żeby chwilę później znów je
        // wysłać — widoczne jako miganie HUD-u. Krótka "sklejka" (grace period) ignoruje
        // pojedyncze, chwilowe zniknięcie aktywności — dłuższa przerwa nadal poprawnie
        // chowa HUD. Celowo krótsza niż DriftEngine.COMBO_TIMEOUT_SEC (3s, używane przez
        // overlay do widocznego odliczania combo) — tu chodzi wyłącznie o wygaszenie
        // szumu klatka-do-klatki, nie o UX-owe "combo jeszcze żyje przez chwilę".
        private const int ActiveGraceMs = 450;
        private DateTime _lastActiveUtc = DateTime.MinValue;

        // Cache ostatnio wysłanego stanu każdego przycisku IS_BTN — pozwala pominąć wysyłkę,
        // gdy tekst/pozycja/styl się nie zmieniły. Bez tego UpdateInGameHUD() wywoływane teraz
        // na każdym ticku telemetrii (kilkadziesiąt razy/s) zalałoby InSim identycznymi pakietami.
        private readonly Dictionary<byte, string> _lastSent = new();

        private void Send(byte id, string text, byte l, byte t, byte w, byte h, byte bStyle)
        {
            string key = $"{text}|{l}|{t}|{w}|{h}|{bStyle}";
            if (_lastSent.TryGetValue(id, out var prev) && prev == key)
                return;

            _lastSent[id] = key;
            _insim.ShowButton(id, text, l: l, t: t, w: w, h: h, bStyle: bStyle);
        }

        // Wołane zawsze po _insim.DeleteAllButtons() — bez tego cache "myślałby", że przyciski
        // wciąż stoją na serwerze InSim (skoro tekst się nie zmienił) i pomijałby ich ponowną
        // wysyłkę, mimo że fizycznie zostały skasowane (np. przy wyjściu do menu/powtórki).
        public void InvalidateCache() => _lastSent.Clear();

        // Aktualizowane przez MainForm za każdym razem, gdy zmieni się paleta InSim
        // (patrz MainForm.SetInSimColorSlot / SyncHudColors).
        public string InSimColor1 = "^7";
        public string InSimColor2 = "^6";
        public string InSimColor3 = "^3";
        public string InSimColor4 = "^5";
        public string InSimColor5 = "^1";

        // Wpychane przez MainForm przed każdym UpdateInGameHUD (patrz OnDriftScored).
        public string DriftLabel = "";
        public string IndicatorLabelRight = "";
        public string IndicatorLabelLeft = "";
        public DriftLabelKind CurrentLabelKind;

        public InGameHudManager(
            InSimConnection insim,
            DriftEngine drift,
            RevLimiter revLimiter,
            MacCheckBox showHudCheck,
            MacCheckBox showRpmHudCheck,
            NumericUpDown revLimiterNumeric,
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
                Send(BTN_SCORE, InSimColor1 + _drift.TotalScore.ToString("N0") + InSimColor1,
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

            // Sklejka (patrz ActiveGraceMs powyżej) — nie zbijaj HUD-u do stanu idle na
            // pojedynczej klatce ciszy tuż po aktywności, tylko po realnym zaniku.
            bool active = activeNow || (DateTime.UtcNow - _lastActiveUtc).TotalMilliseconds < ActiveGraceMs;

            // Główny licznik wyniku ma być widoczny cały czas, gdy HUD jest włączony —
            // niezależnie od tego, czy w danej chwili trwa drift. Wcześniej gałąź "idle"
            // poniżej kasowała też BTN_SCORE, co w połączeniu z tym, że UpdateInGameHUD()
            // wywoływane jest tylko podczas aktywnego driftu/przyspieszenia, dawało efekt
            // "HUD błyska na pół sekundy i znika" — score znikał w każdej chwili poza
            // samym driftem, a odświeżenie przychodziło tylko sporadycznie.
            if (_showHudCheck.Checked && !active)
            {
                Send(BTN_SCORE, $"{InSimColor1}{_drift.TotalScore:N0}{InSimColor1}",
                    l: 70, t: 3, w: 60, h: 15, bStyle: 5);
            }

            if (active)
            {
                string angleCol = CurrentLabelKind switch
                {
                    DriftLabelKind.AngleHigh => InSimColor3,
                    DriftLabelKind.AngleExtreme => InSimColor4,
                    DriftLabelKind.AngleBackward => InSimColor3,
                    DriftLabelKind.AngleUltraExtreme => InSimColor5,
                    DriftLabelKind.AngleHighE => InSimColor3,
                    DriftLabelKind.AngleExtremeE => InSimColor4,
                    DriftLabelKind.AngleBackwardE => InSimColor3,
                    DriftLabelKind.AngleUltraExtremeE => InSimColor5,
                    DriftLabelKind.Fast2 => InSimColor3,
                    DriftLabelKind.Fast3 => InSimColor4,
                    DriftLabelKind.AngleGood => InSimColor2,
                    DriftLabelKind.AngleGoodE => InSimColor2,
                    DriftLabelKind.Fast1 => InSimColor2,
                    DriftLabelKind.BurnoutGood => InSimColor2,
                    DriftLabelKind.BurnoutHigh => InSimColor3,
                    DriftLabelKind.BurnoutExtreme => InSimColor4,
                    DriftLabelKind.BurnoutInsane => InSimColor5,
                    _ => InSimColor1,
                };

                _colorCurrentMain = angleCol;

                // NOTE: oryginalnie liczone dwa razy (raz na IsSpeeding, potem nadpisywane
                // identyczną logiką na IsDrifting) — martwy kod usunięty, zostaje tylko IsDrifting.
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

            // BUG (naprawiony): wcześniej ta metoda IGNOROWAŁA przekazany `text` ilekroć
            // _drift.LastAwardedText było niepuste (co trwa do BONUS_TIMEOUT_SEC=2.5s po
            // KAŻDYM punktowanym zdarzeniu driftu) — pokazywała wtedy zawsze stary tekst
            // z DriftEngine zamiast tego, co faktycznie poprosił wywołujący. Efekt: komunikaty
            // kalibracji rev limitera, testowe przyciski kierownicy itd. potrafiły zostać po
            // cichu podmienione/zjedzone, jeśli gracz akurat coś wcześniej wydriftował —
            // wyglądało to jak "HUD się nie odświeża". Teraz zawsze wyświetlamy dokładnie to,
            // co przyszło w parametrze — kolor dobierany tylko na podstawie tego, czy pokrywa
            // się z aktualnym tekstem nagrody z DriftEngine (wtedy kolor etykiety driftu),
            // czy to inny, zewnętrzny komunikat (wtedy stały kolor).
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
                Send(BTN_RPMLIMIT, "^6RPM LIMIT: " + text, l: 0, t: 196, w: 25, h: 5, bStyle: 65);
            }
            else
            {
                Send(BTN_RPMLIMITSTATUS, "", l: 0, t: 180, w: 35, h: 4, bStyle: 65);
                Send(BTN_RPMLIMIT, "", l: 0, t: 185, w: 35, h: 4, bStyle: 65);
                Send(BTN_RPMLIMITINFO, "", l: 0, t: 190, w: 35, h: 3, bStyle: 65);
            }
        }

        // Wywoływane po zakończeniu driftu — pokazuje award i po chwili sam się chowa (patrz AwardTimer_Tick).
        public void StartAwardFlash()
        {
            _awardTick = 0;
            _awardTimer.Start();
        }

        // Czyści wszystkie przyciski HUD-a (np. gdy użytkownik wyłączy checkbox "Show ingame HUD").
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

            // NOTE: taki sam martwy podwójny warunek jak w UpdateInGameHUD istniał tutaj — zredukowany do IsDrifting.
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