using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LFSDriftTools.InSim;

namespace LFSDriftTools
{
    public partial class MainForm
    {
        private const int StartupDiagnosticsDelayMs = 1500;
        private const int ReportCrashLogLines = 20;

        private const double MissingTelemetrySec = 5.0;

        private static readonly (string ProcessName, string DisplayName)[] PadTools =
        {
            ("steam", "Steam (Steam Input)"),
            ("DS4Windows", "DS4Windows"),
            ("x360ce", "x360ce"),
            ("DSX", "DSX"),
            ("reWASDEngine", "reWASD"),
            ("reWASDTray", "reWASD"),
            ("JoyToKey", "JoyToKey"),
            ("BetterJoy", "BetterJoy"),
            ("InputMapper", "InputMapper"),
        };

        private static readonly (string ProcessName, string DisplayName)[] TelemetryTools =
        {
            ("SimHubWPF", "SimHub"),
            ("CrewChiefV4", "Crew Chief"),
            ("RaceLab", "RaceLab"),
            ("SimRacingStudio", "SimRacing Studio"),
        };

        private DiagnosticsListView? _diagnosticsList;
        private volatile List<string>? _antivirusProducts;
        private volatile bool _antivirusQueried;
        private DateTime _drivingOwnCarSinceUtc = DateTime.MinValue;

        private void InitDiagnostics()
        {
            LfsInputGuard.KeysBlocked += () => PostToUi(() =>
                AppendStatusMessage(Localization.T("status.lfs.keys_blocked"), StatusErrorColor));

            System.Threading.Tasks.Task.Run(() =>
            {
                _antivirusProducts = SystemProbe.QueryAntivirusProducts();
                _antivirusQueried = true;
            });
        }

        private bool IsDrivingOwnCar()
        {
            if (!_insim.IsConnected) return false;
            var state = _insim.GameState;
            if ((state & StateFlags.ISS_GAME) == 0) return false;
            if ((state & (StateFlags.ISS_FRONT_END | StateFlags.ISS_REPLAY | StateFlags.ISS_PAUSED | StateFlags.ISS_SHIFTU)) != 0) return false;
            byte own = _myOwnPlid;
            if (own == 0 || _localPlayerParked) return false;
            byte view = _insim.ViewPLID;
            return view == 0 || view == own;
        }

        private void TrackDrivingOwnCar()
        {
            if (!IsDrivingOwnCar())
                _drivingOwnCarSinceUtc = DateTime.MinValue;
            else if (_drivingOwnCarSinceUtc == DateTime.MinValue)
                _drivingOwnCarSinceUtc = DateTime.UtcNow;
        }

        private void ScheduleStartupDiagnostics()
        {
            var timer = new System.Windows.Forms.Timer { Interval = StartupDiagnosticsDelayMs };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (IsDisposed) return;

                List<CheckResult> results;
                try { results = RunSystemChecks(); }
                catch (Exception ex)
                {
                    ErrorLog.Write("Startup diagnostics", ex);
                    return;
                }

                int errors = results.Count(r => r.Status == CheckStatus.Error);
                int warnings = results.Count(r => r.Status == CheckStatus.Warning);
                if (errors == 0 && warnings == 0) return;

                AppendStatusMessage(string.Format(Localization.T("diag.summary.issues"), errors, warnings),
                    errors > 0 ? StatusErrorColor : ApplePalette.StatusWarn);
                if (errors > 0)
                    ShowSystemDiagnostics(results);
            };
            timer.Start();
        }

        private List<CheckResult> RunSystemChecks()
        {
            var results = new List<CheckResult>
            {
                CheckWriteAccess(),
                CheckFolderLocation(),
                CheckRightsComparedToLfs(),
                CheckTelemetryPorts(),
                CheckLfsConnection(),
                CheckLfsCfg(),
                CheckAssistDrivers(),
                CheckPadTools(),
                CheckTelemetryTools(),
                CheckLfsDisplayMode(),
                CheckKeyboardHook(),
                CheckAntivirus(),
                CheckSessionErrors(),
                CheckEnvironment(),
            };
            return results;
        }

        private static CheckStatus Worst(CheckStatus a, CheckStatus b) => a > b ? a : b;

        private CheckResult CheckLfsConnection()
        {
            string title = Localization.T("diag.lfs.title");
            if (LfsWindow.Find() == IntPtr.Zero)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.lfs.notrunning") };

            var lines = new List<string>();
            var status = CheckStatus.Ok;

            if (_insim.IsConnected)
                lines.Add(Localization.T("diag.lfs.insim.ok"));
            else if (!_connectionSwitch.Checked)
            {
                lines.Add(Localization.T("diag.lfs.insim.off"));
                status = CheckStatus.Info;
            }
            else
            {
                lines.Add(string.Format(Localization.T("diag.lfs.insim.waiting"), (int)_portBox.Value));
                status = CheckStatus.Warning;
            }

            bool drivingLongEnough = _drivingOwnCarSinceUtc != DateTime.MinValue
                && (DateTime.UtcNow - _drivingOwnCarSinceUtc).TotalSeconds >= MissingTelemetrySec;

            void Telemetry(bool enabled, DateTime lastPacketUtc, string okKey, string idleKey, string missingKey)
            {
                if (!enabled) return;
                double age = lastPacketUtc == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - lastPacketUtc).TotalSeconds;
                if (age < MissingTelemetrySec)
                    lines.Add(Localization.T(okKey));
                else if (drivingLongEnough)
                {
                    lines.Add(string.Format(Localization.T(missingKey), (int)MissingTelemetrySec));
                    status = Worst(status, CheckStatus.Warning);
                }
                else
                {
                    lines.Add(Localization.T(idleKey));
                    status = Worst(status, CheckStatus.Info);
                }
            }

            Telemetry(_outGaugeConnectionEnabled, _outGauge.LastPacketUtc, "diag.lfs.og.ok", "diag.lfs.og.idle", "diag.lfs.og.missing");
            Telemetry(_outSimConnectionSwitch.Checked, _outSim.LastSample?.ReceivedAtUtc ?? DateTime.MinValue,
                "diag.lfs.os.ok", "diag.lfs.os.idle", "diag.lfs.os.missing");

            return new CheckResult
            {
                Status = status,
                Title = title,
                Detail = string.Join("\n", lines),
                Help = status == CheckStatus.Warning ? Localization.T("diag.lfs.help") : "",
            };
        }

        private CheckResult CheckLfsCfg()
        {
            string title = Localization.T("diag.cfg.title");
            if (string.IsNullOrWhiteSpace(_lfsGameFolder))
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.cfg.nofolder") };

            var problems = GetLfsCfgProblems();
            if (problems == null)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.cfg.nofile") };
            if (problems.Count == 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.cfg.ok") };

            return new CheckResult
            {
                Status = CheckStatus.Warning,
                Title = title,
                Detail = string.Format(Localization.T("diag.cfg.mismatch"), string.Join("; ", problems)),
                Help = Localization.T("diag.cfg.help"),
            };
        }

        private CheckResult CheckAssistDrivers()
        {
            string title = Localization.T("diag.drivers.title");
            if (!_caMasterUserWanted)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.drivers.notused") };

            var lines = new List<string>();
            bool vigem = DriverManager.IsViGEmBusInstalled();
            if (!vigem) lines.Add(Localization.T("diag.drivers.novigem"));
            if (_caHidePadSwitch.Checked && !DriverManager.IsHidHideInstalled()) lines.Add(Localization.T("diag.drivers.nohidhide"));

            if (lines.Count == 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.drivers.ok") };

            return new CheckResult
            {
                Status = CheckStatus.Warning,
                Title = title,
                Detail = string.Join("\n", lines),
                Help = Localization.T("diag.drivers.help"),
            };
        }

        private CheckResult CheckPadTools()
        {
            string title = Localization.T("diag.padtools.title");
            var found = SystemProbe.FindRunningPrograms(PadTools);
            if (found.Count == 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.padtools.none") };

            return new CheckResult
            {
                Status = _caMasterUserWanted ? CheckStatus.Warning : CheckStatus.Info,
                Title = title,
                Detail = string.Format(Localization.T("diag.padtools.found"), string.Join(", ", found)),
                Help = Localization.T("diag.padtools.help"),
            };
        }

        private CheckResult CheckTelemetryTools()
        {
            string title = Localization.T("diag.telemetry.title");
            var found = SystemProbe.FindRunningPrograms(TelemetryTools);
            if (found.Count == 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.telemetry.none") };

            return new CheckResult
            {
                Status = CheckStatus.Info,
                Title = title,
                Detail = string.Format(Localization.T("diag.telemetry.found"), string.Join(", ", found)),
            };
        }

        private CheckResult CheckLfsDisplayMode()
        {
            string title = Localization.T("diag.window.title");
            if (!_insim.IsConnected || _insim.GameState == 0)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.window.unknown") };

            if ((_insim.GameState & StateFlags.ISS_WINDOWED) != 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.window.ok") };

            bool overlayUsed = _overlay.ScoreOverlayEnabled || _overlay.SpeedoTachoEnabled || _overlay.ControllerAssistBarVisible;
            return new CheckResult
            {
                Status = overlayUsed ? CheckStatus.Warning : CheckStatus.Info,
                Title = title,
                Detail = Localization.T("diag.window.fullscreen"),
                Help = Localization.T("diag.window.help"),
            };
        }

        private CheckResult CheckAntivirus()
        {
            string title = Localization.T("diag.av.title");
            if (!_antivirusQueried)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.av.checking") };

            var products = _antivirusProducts;
            string detail = products == null ? Localization.T("diag.av.unknown")
                : products.Count == 0 ? Localization.T("diag.av.none")
                : string.Format(Localization.T("diag.av.list"), string.Join(", ", products));
            return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = detail };
        }

        private CheckResult CheckSessionErrors()
        {
            string title = Localization.T("diag.errors.title");
            int count;
            string lastSource;
            try
            {
                DateTime started = System.Diagnostics.Process.GetCurrentProcess().StartTime;
                count = SystemProbe.CountErrorLogEntries(ErrorLog.FilePath, started.AddSeconds(-1), out lastSource);
            }
            catch (Exception ex)
            {
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = ex.Message };
            }

            if (count == 0)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.errors.none") };

            return new CheckResult
            {
                Status = CheckStatus.Warning,
                Title = title,
                Detail = string.Format(Localization.T("diag.errors.some"), count, lastSource),
                Help = Localization.T("diag.errors.help"),
            };
        }

        private CheckResult CheckWriteAccess()
        {
            string appDir = Application.StartupPath;
            var failures = new List<string>();
            foreach (string dir in new[] { ConfigsDir, Path.Combine(appDir, "Saves"), AssistLogDir, DataImagesDir })
            {
                if (!SystemProbe.TryWrite(dir, out string error))
                    failures.Add(Path.GetFileName(dir) + ": " + error);
            }

            if (failures.Count == 0)
                return new CheckResult
                {
                    Status = CheckStatus.Ok,
                    Title = Localization.T("diag.write.title"),
                    Detail = string.Format(Localization.T("diag.write.ok"), appDir),
                };

            string help = Localization.T("diag.write.help");
            if (SystemProbe.IsInUserLibraryFolder(appDir))
                help += " " + Localization.T("diag.write.help.cfa");

            return new CheckResult
            {
                Status = CheckStatus.Error,
                Title = Localization.T("diag.write.title"),
                Detail = string.Format(Localization.T("diag.write.error"), appDir, string.Join("; ", failures)),
                Help = help,
            };
        }

        private CheckResult CheckFolderLocation()
        {
            var risk = SystemProbe.ClassifyLocation(Application.StartupPath);
            string title = Localization.T("diag.location.title");
            if (risk == FolderLocationRisk.None)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.location.ok") };

            string detailKey = risk switch
            {
                FolderLocationRisk.ProgramFiles => "diag.location.programfiles",
                FolderLocationRisk.OneDrive => "diag.location.onedrive",
                FolderLocationRisk.Zip => "diag.location.zip",
                _ => "diag.location.temp",
            };
            return new CheckResult
            {
                Status = CheckStatus.Warning,
                Title = title,
                Detail = Localization.T(detailKey),
                Help = Localization.T("diag.location.help"),
            };
        }

        private CheckResult CheckRightsComparedToLfs()
        {
            string title = Localization.T("diag.rights.title");
            string help = Localization.T("diag.rights.help");

            if (LfsInputGuard.KeysWereBlocked)
                return new CheckResult { Status = CheckStatus.Error, Title = title, Detail = Localization.T("diag.rights.blocked"), Help = help };

            IntPtr lfs = LfsWindow.Find();
            if (lfs == IntPtr.Zero)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.rights.nolfs") };

            bool appElevated = SystemProbe.IsCurrentProcessElevated();
            bool? lfsElevated = SystemProbe.IsWindowProcessElevated(lfs);

            if (lfsElevated == null)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.rights.unknown") };
            if (lfsElevated.Value && !appElevated)
                return new CheckResult { Status = CheckStatus.Error, Title = title, Detail = Localization.T("diag.rights.error"), Help = help };
            if (!lfsElevated.Value && appElevated)
                return new CheckResult { Status = CheckStatus.Info, Title = title, Detail = Localization.T("diag.rights.appadmin") };
            return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.rights.ok") };
        }

        private CheckResult CheckTelemetryPorts()
        {
            var lines = new List<string>();
            var status = CheckStatus.Ok;

            void Check(string name, UdpTelemetryReceiver receiver, bool enabled)
            {
                if (!enabled)
                {
                    lines.Add(string.Format(Localization.T("diag.ports.off"), name));
                    return;
                }
                if (receiver.IsRunning)
                {
                    lines.Add(string.Format(Localization.T("diag.ports.ok"), name, receiver.UdpPort));
                    return;
                }

                string? owner = receiver.PortBlockedBy;
                if (string.IsNullOrEmpty(owner))
                    owner = SystemProbe.FindUdpPortOwner(receiver.UdpPort);
                status = CheckStatus.Error;
                lines.Add(string.IsNullOrEmpty(owner)
                    ? string.Format(Localization.T("diag.ports.failed"), name, receiver.UdpPort)
                    : string.Format(Localization.T("diag.ports.busy"), name, receiver.UdpPort, owner));
            }

            Check("OutGauge", _outGauge, _outGaugeConnectionEnabled);
            Check("OutSim", _outSim, _outSimConnectionSwitch?.Checked ?? false);

            return new CheckResult
            {
                Status = status,
                Title = Localization.T("diag.ports.title"),
                Detail = string.Join("\n", lines),
                Help = status == CheckStatus.Ok ? "" : Localization.T("diag.ports.help"),
            };
        }

        private CheckResult CheckKeyboardHook()
        {
            string title = Localization.T("diag.hook.title");
            if (KeyboardHookHub.IsActive)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.hook.ok") };
            if (!KeyboardHookHub.IsRequested)
                return new CheckResult { Status = CheckStatus.Ok, Title = title, Detail = Localization.T("diag.hook.idle") };

            return new CheckResult
            {
                Status = CheckStatus.Error,
                Title = title,
                Detail = string.Format(Localization.T("diag.hook.error"), KeyboardHookHub.InstallError),
                Help = Localization.T("diag.hook.help"),
            };
        }

        private CheckResult CheckEnvironment()
        {
            string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int scaling = (int)Math.Round(DeviceDpi * 100.0 / 96.0);
            string windows = $"{RuntimeInformation.OSDescription.Trim()} ({Environment.OSVersion.Version})";
            return new CheckResult
            {
                Status = CheckStatus.Info,
                Title = Localization.T("diag.env.title"),
                Detail = string.Format(Localization.T("diag.env.detail"), windows, Environment.Version, version.Split('+')[0],
                    scaling, Screen.AllScreens.Length)
                    + "\n" + Localization.T(SecureFile.IsReleaseKey ? "diag.env.savekey_release" : "diag.env.savekey_dev")
                    + (_insim.IsConnected
                        ? "\n" + string.Format(Localization.T("diag.env.gamestate"), _insim.GameState, _myOwnPlid, _insim.ViewPLID,
                            Localization.T(IsDrivingOwnCar() ? "common.yes" : "common.no"))
                        : ""),
            };
        }

        private void ShowSystemDiagnostics(List<CheckResult>? results = null)
        {
            results ??= RunSystemChecks();

            Form popup = CreatePopupForm();
            var card = BuildPopupChrome(popup, 600, 600, out _);

            MakeLabel(card, Localization.T("diag.title"), 20, 15, 400, 24,
                ApplePalette.Title, UiFonts.Get("Segoe UI Semibold", 11f));
            MakeLabel(card, Localization.T("diag.subtitle"), 20, 40, 520, 18,
                UiColors.Muted, UiFonts.Get("Segoe UI", 8f));

            var scroll = new Panel
            {
                Location = new Point(14, 66),
                Size = new Size(572, 468),
                AutoScroll = true,
                BackColor = Color.Transparent,
            };
            var list = new DiagnosticsListView
            {
                Location = new Point(0, 0),
                Width = scroll.Width - SystemInformation.VerticalScrollBarWidth - 2,
            };
            list.SetResults(results);
            scroll.Controls.Add(list);
            card.Controls.Add(scroll);
            _diagnosticsList = list;
            popup.FormClosed += (s, e) => _diagnosticsList = null;

            var recheck = MakeButton(card, Localization.T("diag.recheck"), 20, 546, 200, 32);
            recheck.Click += (s, e) => list.SetResults(RunSystemChecks());

            var copy = MakeButton(card, Localization.T("diag.copyreport"), 380, 546, 200, 32, ApplePalette.Blue);
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(BuildDiagnosticsReport(list.Results));
                    AppendStatusMessage(Localization.T("diag.reportcopied"), ApplePalette.StatusOk);
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Diagnostics report", ex);
                }
            };

            ShowModalPopup(popup);
        }

        private string BuildDiagnosticsReport(IReadOnlyList<CheckResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LFS Drift Tools diagnostics report");
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            foreach (var r in results)
            {
                sb.AppendLine($"[{r.Status}] {r.Title}");
                foreach (string line in r.Detail.Split('\n'))
                    sb.AppendLine("  " + line);
                if (!string.IsNullOrEmpty(r.Help))
                    sb.AppendLine("  > " + r.Help);
            }

            try
            {
                if (File.Exists(ErrorLog.FilePath))
                {
                    var lines = File.ReadAllLines(ErrorLog.FilePath);
                    sb.AppendLine();
                    sb.AppendLine("crash.log (last lines):");
                    foreach (string line in lines.Skip(Math.Max(0, lines.Length - ReportCrashLogLines)))
                        sb.AppendLine(line);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("crash.log: " + ex.Message);
            }

            return SanitizeReport(sb.ToString());
        }

        private static string SanitizeReport(string text)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            string user = Environment.UserName;
            if (user.Length >= 3)
                text = text.Replace(user, "%USER%", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        private sealed class DiagnosticsListView : Control
        {
            private const int RowGap = 8;
            private const int PadX = 14;
            private const int PadY = 10;
            private const int DotSize = 12;
            private const int TextLeft = PadX + DotSize + 12;
            private static readonly StringFormat WrapFormat = new();

            private List<CheckResult> _results = new();

            public IReadOnlyList<CheckResult> Results => _results;

            public DiagnosticsListView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            private static Font TitleFont => UiFonts.Get("Segoe UI Semibold", 10f);
            private static Font TextFont => UiFonts.Get("Segoe UI", 8.5f);

            public void SetResults(List<CheckResult> results)
            {
                _results = results.OrderByDescending(r => r.Status).ToList();
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Height = _results.Sum(r => RowHeight(g, r) + RowGap);
                Invalidate();
            }

            private int TextWidth => Math.Max(50, Width - TextLeft - PadX - 4);

            private int RowHeight(Graphics g, CheckResult r)
            {
                float h = PadY + g.MeasureString(r.Title, TitleFont, TextWidth, WrapFormat).Height + 2f;
                h += g.MeasureString(r.Detail, TextFont, TextWidth, WrapFormat).Height;
                if (!string.IsNullOrEmpty(r.Help))
                    h += 4f + g.MeasureString(r.Help, TextFont, TextWidth, WrapFormat).Height;
                return (int)Math.Ceiling(h + PadY);
            }

            private static Color StatusColor(CheckStatus status) => status switch
            {
                CheckStatus.Error => ApplePalette.StatusError,
                CheckStatus.Warning => ApplePalette.StatusWarn,
                CheckStatus.Ok => ApplePalette.StatusOk,
                _ => ApplePalette.StatusIdle,
            };

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int y = 0;
                foreach (var r in _results)
                {
                    int height = RowHeight(g, r);
                    var row = new RectangleF(2, y + 1, Width - 4, height - 2);
                    using (var path = DrawingHelpers.RoundedRect(row, 12f))
                    {
                        using (var bg = new SolidBrush(ApplePalette.Card))
                            g.FillPath(bg, path);
                        using var border = new Pen(ApplePalette.Border, 1f);
                        g.DrawPath(border, path);
                    }

                    using (var dot = new SolidBrush(StatusColor(r.Status)))
                        g.FillEllipse(dot, row.X + PadX, row.Y + PadY + 4, DotSize, DotSize);

                    float textX = row.X + TextLeft;
                    float textY = row.Y + PadY;
                    var titleSize = g.MeasureString(r.Title, TitleFont, TextWidth, WrapFormat);
                    using (var titleBrush = new SolidBrush(ApplePalette.Title))
                        g.DrawString(r.Title, TitleFont, titleBrush, new RectangleF(textX, textY, TextWidth, titleSize.Height + 2f), WrapFormat);
                    textY += titleSize.Height + 2f;

                    var detailSize = g.MeasureString(r.Detail, TextFont, TextWidth, WrapFormat);
                    using (var detailBrush = new SolidBrush(ApplePalette.Text))
                        g.DrawString(r.Detail, TextFont, detailBrush, new RectangleF(textX, textY, TextWidth, detailSize.Height + 2f), WrapFormat);
                    textY += detailSize.Height;

                    if (!string.IsNullOrEmpty(r.Help))
                    {
                        textY += 4f;
                        var helpSize = g.MeasureString(r.Help, TextFont, TextWidth, WrapFormat);
                        using var helpBrush = new SolidBrush(UiColors.Muted);
                        g.DrawString(r.Help, TextFont, helpBrush, new RectangleF(textX, textY, TextWidth, helpSize.Height + 2f), WrapFormat);
                    }

                    y += height + RowGap;
                }
            }
        }
    }
}
