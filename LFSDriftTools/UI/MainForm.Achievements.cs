using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        private const int AchievementSaveIntervalMs = 15000;

        private readonly AchievementStore _achievementStore =
            new(Path.Combine(Application.StartupPath, "Configs", "achievements.dat"));
        private readonly InputMethodDetector _inputMethods = new();
        private readonly SoundPlayer _achievementSound =
            new(Path.Combine(Application.StartupPath, "Data", "Sounds", "achievement_unlock.wav"));
        private AchievementTracker _achievements = null!;
        private System.Windows.Forms.Timer? _achievementSaveTimer;
        private bool _achievementSoundEnabled = true;

        private void InitAchievements()
        {
            _achievementStore.Load();
            if (_achievementStore.LastLoad.Error != SecureFileError.None)
                AppendStatusMessage(SecureFile.DescribeRejection(SecureFileKind.Achievements, "", _achievementStore.LastLoad));
            _achievements = new AchievementTracker(_achievementStore, _inputMethods)
            {
                OwnCarInView = AchievementOwnCarInView,
                IsUpsideDown = () => _rollover.IsUpsideDown,
                CurrentCar = () => _currentCarName ?? "",
                CurrentTrack = () => _insim.CurrentTrack ?? "",
            };
            _achievements.Unlocked += info => PostToUi(() => OnAchievementUnlocked(info));

            _wheelInput.SteeringChanged += _ =>
            {
                if (_wheelInput.IsXInputDevice) _inputMethods.NotePadActivity();
                else _inputMethods.NoteWheelActivity();
            };
            _outSim.DataReceived += sample => _achievements.OnOutSim(sample);
            _outSim.DataReceived += WheelProbe.Record;
            _outGauge.DataReceived += WheelProbe.NoteGauge;
            _drift.DriftStarted += () => _achievements.OnDriftStarted();
            _drift.DriftEnded += points => _achievements.OnDriftEnded(points);
            _drift.DonutCompleted += streak => _achievements.OnDonut(streak);
            _drift.BurnoutSpinCompleted += streak => _achievements.OnBurnoutSpin(streak);
            _drift.BurnoutToDriftCompleted += () => _achievements.OnBurnoutToDrift();
            _revLimiter.CutStarted += () => _achievements.OnRevCut();
            _insim.ObjectHit += (s, e) =>
            {
                if (e.PLID != 0 && e.PLID == _lastKnownPlayerPLID) _achievements.OnObjectHit(e.ObjectName);
            };
            _insim.CarContact += (s, e) =>
            {
                byte me = _lastKnownPlayerPLID;
                if (me != 0 && (e.A.PLID == me || e.B.PLID == me))
                    _achievements.OnCarContact(e.ClosingSpeedKmh, e.A.PLID == me ? e.B.PLID : e.A.PLID);
            };
            _insim.CarReset += (s, e) =>
            {
                if (e.PLID != 0 && e.PLID == _myOwnPlid) _achievements.OnCarReset();
            };
            _insim.TrackChanged += (s, track) => _achievements.ResetSession();
            _insim.PitLaneEntered += (s, e) => { if (e.PLID != 0 && e.PLID == _myOwnPlid) _achievements.SetInPitLane(true); };
            _insim.PitLaneExited += (s, e) => { if (e.PLID != 0 && e.PLID == _myOwnPlid) _achievements.SetInPitLane(false); };
            _insim.CarReset += (s, e) => { if (e.PLID != 0 && e.PLID == _myOwnPlid) _achievements.SetInPitLane(false); };
            _overlay.BonusShown += count =>
            {
                if (_overlay.Visible && _overlay.ScoreOverlayEnabled) _achievements.OnBonusesOnScreen(count);
            };

            _achievementSaveTimer = new System.Windows.Forms.Timer { Interval = AchievementSaveIntervalMs };
            _achievementSaveTimer.Tick += (s, e) => _achievementStore.SaveIfDirty();
            _achievementSaveTimer.Start();
        }

        private void ShutdownAchievements()
        {
            _achievementSaveTimer?.Stop();
            _achievementSaveTimer?.Dispose();
            _achievementSaveTimer = null;
            _inputMethods.Dispose();
            _achievementStore.Save();
            _achievementSound.Dispose();
        }

        private bool AchievementOwnCarInView()
        {
            if (_localPlayerParked) return false;
            if (!_insim.IsConnected) return true;
            byte own = _myOwnPlid;
            if (own == 0) return true;
            byte view = _insim.ViewPLID;
            return view == 0 || view == own;
        }

        private static string AchievementTitle(AchievementRarity rarity) => rarity switch
        {
            AchievementRarity.Rare => Localization.T("ach.unlocked.rare"),
            AchievementRarity.Epic => Localization.T("ach.unlocked.epic"),
            AchievementRarity.Legendary => Localization.T("ach.unlocked.legendary"),
            _ => Localization.T("ach.unlocked"),
        };

        private static string InputMethodLabel(InputMethod method) => method switch
        {
            InputMethod.Wheel => Localization.T("ach.method.wheel"),
            InputMethod.Pad => Localization.T("ach.method.pad"),
            _ => Localization.T("ach.method.kbm"),
        };

        private void OnAchievementUnlocked(AchievementUnlockedInfo info)
        {
            var def = info.Definition;
            string title = AchievementTitle(def.Rarity);
            string name = Localization.T(def.NameKey);
            string description = Localization.T(def.DescriptionKey);
            string method = InputMethodLabel(info.Method);

            if (_showHudCheck != null && _showHudCheck.Checked && _insim.IsConnected)
            {
                _hud.ShowAchievement(title, name, description, method, def.Rarity);
            }
            else if (_overlay.Visible)
            {
                _overlay.ShowAchievement(new AchievementToast
                {
                    Title = title,
                    Name = name,
                    Description = description,
                    MethodLabel = method,
                    Method = info.Method,
                    Icon = def.Icon,
                    Rarity = def.Rarity,
                });
            }

            if (_achievementSoundEnabled)
            {
                try { _achievementSound.Play(); }
                catch (Exception ex) { ErrorLog.Write("Achievement sound", ex); }
            }

            AppendStatusMessage($"{title} {name} ({method})");
            _achievementStore.Save();
            _achievementList?.Invalidate();
        }

        private AchievementListView? _achievementList;

        private void ShowAchievementsMenu(Form? ownerForm = null)
        {
            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 540, 600, out _);

            MakeLabel(card, Localization.T("ach.title"), 20, 15, 300, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("ach.subtitle"), 20, 40, 320, 16,
                UiColors.Muted, UiFonts.Get("Segoe UI", 7.5f));

            int unlocked = 0;
            foreach (var def in AchievementCatalog.All)
                if (_achievementStore.IsUnlockedAny(def.Id)) unlocked++;
            MakeLabel(card, string.Format(Localization.T("ach.count"), unlocked, AchievementCatalog.All.Count), 20, 60, 300, 18,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 8.5f));

            MakeLabel(card, Localization.T("ach.sound"), 300, 58, 170, 20,
                ApplePalette.Text, UiFonts.Get("Segoe UI", 8.5f), ContentAlignment.MiddleRight);
            var soundSwitch = new MacToggleSwitch
            {
                Location = new Point(478, 58),
                Size = new Size(40, 20),
                Checked = _achievementSoundEnabled,
            };
            soundSwitch.CheckedChanged += (s, e) =>
            {
                _achievementSoundEnabled = soundSwitch.Checked;
                SaveSettings();
            };
            card.Controls.Add(soundSwitch);

            var scroll = new Panel
            {
                Location = new Point(14, 90),
                Size = new Size(512, 496),
                AutoScroll = true,
                BackColor = Color.Transparent,
            };
            var list = new AchievementListView(_achievementStore)
            {
                Location = new Point(0, 0),
                Width = scroll.Width - SystemInformation.VerticalScrollBarWidth - 2,
            };
            list.Height = list.PreferredHeight;
            scroll.Controls.Add(list);
            card.Controls.Add(scroll);
            _achievementList = list;
            popup.FormClosed += (s, e) => _achievementList = null;

            ShowModalPopup(popup, ownerForm);
        }

        private sealed class AchievementListView : Control
        {
            private const int RowHeight = 84;
            private const int RowGap = 8;
            private static readonly StringFormat NameFormat = new(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            private static readonly StringFormat DescriptionFormat = new() { Trimming = StringTrimming.EllipsisWord };
            private readonly AchievementStore _store;

            public AchievementListView(AchievementStore store)
            {
                _store = store;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            public int PreferredHeight => AchievementCatalog.All.Count * (RowHeight + RowGap);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var nameFont = UiFonts.Get("Segoe UI Semibold", 10f);
                var descFont = UiFonts.Get("Segoe UI", 8.5f);
                var smallFont = UiFonts.Get("Segoe UI", 7.5f);
                var methods = (InputMethod[])Enum.GetValues(typeof(InputMethod));

                int y = 0;
                foreach (var def in AchievementCatalog.All)
                {
                    var row = new RectangleF(2, y + 1, Width - 4, RowHeight - 2);
                    using (var path = DrawingHelpers.RoundedRect(row, 12f))
                    {
                        using (var bg = new SolidBrush(ApplePalette.Card))
                            g.FillPath(bg, path);
                        using var border = new Pen(ApplePalette.Border, 1f);
                        g.DrawPath(border, path);
                    }

                    bool unlockedAny = _store.IsUnlockedAny(def.Id);
                    bool showReal = unlockedAny || def.Visibility == AchievementVisibility.Visible;
                    var icon = showReal ? def.Icon : AchievementIcon.Question;
                    AchievementIcons.DrawBadge(g, new RectangleF(row.X + 12, row.Y + 10, 62, 62), icon, def.Rarity, !unlockedAny);

                    string name, description;
                    if (showReal)
                    {
                        name = Localization.T(def.NameKey);
                        description = Localization.T(def.DescriptionKey);
                    }
                    else if (def.Visibility == AchievementVisibility.Secret)
                    {
                        name = Localization.T("ach.secret.name");
                        description = Localization.T("ach.secret.desc");
                    }
                    else
                    {
                        name = "???";
                        description = Localization.T(def.HintKey);
                    }

                    float textX = row.X + 88, textW = row.Width - 88 - 150;
                    Color nameColor = unlockedAny && def.Rarity != AchievementRarity.Common ? AchievementIcons.RarityColor(def.Rarity) : ApplePalette.Title;
                    using (var nameBrush = new SolidBrush(nameColor))
                        g.DrawString(name, nameFont, nameBrush, new RectangleF(textX, row.Y + 12, textW, 22),
                            NameFormat);
                    using (var descBrush = new SolidBrush(ApplePalette.Secondary))
                        g.DrawString(description, descFont, descBrush, new RectangleF(textX, row.Y + 34, textW, 42),
                            DescriptionFormat);

                    bool showCounter = def.Target > 0 && def.Visibility == AchievementVisibility.Visible;
                    float mx = row.Right - 146;
                    foreach (var method in methods)
                    {
                        bool lit = _store.IsUnlocked(def.Id, method);
                        var color = lit ? ApplePalette.TextGreen : Color.FromArgb(120, ApplePalette.Secondary);
                        AchievementIcons.DrawMethodGlyph(g, new RectangleF(mx + 12, row.Y + 16, 24, 24), method, color);
                        if (showCounter)
                        {
                            long value = Math.Min(def.Target, lit ? def.Target : _store.GetCounter(def.Id, method));
                            using var counterBrush = new SolidBrush(lit ? color : ApplePalette.Secondary);
                            using var center = new StringFormat { Alignment = StringAlignment.Center };
                            g.DrawString($"{value}/{def.Target}", smallFont, counterBrush, new RectangleF(mx, row.Y + 46, 48, 16), center);
                        }
                        mx += 48;
                    }

                    y += RowHeight + RowGap;
                }
            }
        }
    }
}
