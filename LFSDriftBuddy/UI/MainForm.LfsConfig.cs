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
    public partial class MainForm
    {
        private void SetLfsFolder(string folder)
        {
            _lfsGameFolder = folder;
            UpdateLfsFolderStatusLabel();
            SaveSettings();
            CheckLfsCfgAgainstSettings();
        }

        private void UpdateLfsFolderStatusLabel()
        {
            if (_lfsFolderStatusLabel == null) return;
            _lfsFolderStatusLabel.Text = string.IsNullOrWhiteSpace(_lfsGameFolder)
                ? Localization.T("general.folder.notset")
                : _lfsGameFolder;
        }

        private void BrowseLfsFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = Localization.T("general.lfsfolder"),
                SelectedPath = Directory.Exists(_lfsGameFolder) ? _lfsGameFolder : "",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SetLfsFolder(dlg.SelectedPath);
        }

        private static readonly System.Text.Encoding CfgEncoding = System.Text.Encoding.Latin1;

        private static readonly System.Text.RegularExpressions.Regex OutSimOptsPattern =
            new(@"^(0[xX])?[0-9A-Fa-f]{1,4}$");

        private static string? GetCfgValue(List<string> lines, string key)
        {
            foreach (var line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                    (trimmed.Length == key.Length || char.IsWhiteSpace(trimmed[key.Length])))
                {
                    return NormalizeCfgLine(trimmed.Substring(key.Length));
                }
            }
            return null;
        }

        private static bool TryParseCfgHex(string? text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string hex = text.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private void CheckLfsCfgAgainstSettings()
        {
            if (string.IsNullOrWhiteSpace(_lfsGameFolder)) return;

            List<string> lines;
            try
            {
                string cfgPath = Path.Combine(_lfsGameFolder, "cfg.txt");
                if (!File.Exists(cfgPath)) return;
                lines = File.ReadAllLines(cfgPath, CfgEncoding).ToList();
            }
            catch
            {
                return;
            }

            string missing = Localization.T("status.cfgcheck.missing");
            var problems = new List<string>();

            void ExpectEqual(string key, string appValue)
            {
                string? cfgValue = GetCfgValue(lines, key);
                if (!string.Equals(cfgValue, appValue, StringComparison.OrdinalIgnoreCase))
                    problems.Add(string.Format(Localization.T("status.cfgcheck.item"), key, cfgValue ?? missing, appValue));
            }

            void ExpectEnabled(string key)
            {
                string? cfgValue = GetCfgValue(lines, key);
                if (cfgValue == null || cfgValue == "0")
                    problems.Add(string.Format(Localization.T("status.cfgcheck.item"), key, cfgValue ?? missing, Localization.T("status.cfgcheck.enabled")));
            }

            ExpectEqual("InSim", ((int)_portBox.Value).ToString());

            if (_outGaugeConnectionSwitch.Checked)
            {
                ExpectEnabled("OutGauge Mode");
                ExpectEqual("OutGauge IP", "127.0.0.1");
                ExpectEqual("OutGauge Port", ((int)_outGaugePortBox.Value).ToString());
            }

            if (_outSimConnectionSwitch.Checked)
            {
                ExpectEnabled("OutSim Mode");
                ExpectEqual("OutSim IP", "127.0.0.1");
                ExpectEqual("OutSim Port", ((int)_outSimPortBox.Value).ToString());

                string appOpts = _outSimOptsBox.Text.Trim();
                string? cfgOpts = GetCfgValue(lines, "OutSim Opts");
                bool optsMatch = TryParseCfgHex(appOpts, out int appOptsValue)
                    && TryParseCfgHex(cfgOpts, out int cfgOptsValue)
                    && appOptsValue == cfgOptsValue;
                if (!optsMatch)
                    problems.Add(string.Format(Localization.T("status.cfgcheck.item"), "OutSim Opts", cfgOpts ?? missing, appOpts));
            }

            if (problems.Count == 0) return;

            AppendStatusMessage(
                string.Format(Localization.T("status.cfgcheck.mismatch"), string.Join("; ", problems)),
                ApplePalette.StatusWarn);
        }

        private void ConfigureLfsCfg()
        {
            if (string.IsNullOrWhiteSpace(_lfsGameFolder) || !Directory.Exists(_lfsGameFolder))
            {
                AppendStatusMessage(Localization.T("general.configure.nofolder"), StatusErrorColor);
                return;
            }

            try
            {
                string cfgPath = Path.Combine(_lfsGameFolder, "cfg.txt");
                List<string> lines = File.Exists(cfgPath)
                    ? File.ReadAllLines(cfgPath, CfgEncoding).ToList()
                    : new List<string>();

                int insimPort = (int)_portBox.Value;
                int outGaugePort = (int)_outGaugePortBox.Value;
                int outSimPort = (int)_outSimPortBox.Value;
                string outSimOpts = string.IsNullOrWhiteSpace(_outSimOptsBox.Text) ? "bf" : _outSimOptsBox.Text.Trim();

                if (!OutSimOptsPattern.IsMatch(outSimOpts))
                {
                    AppendStatusMessage(string.Format(Localization.T("connection.outsim.badopts"), outSimOpts), StatusErrorColor);
                    return;
                }

                if (outSimOpts.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    outSimOpts = outSimOpts.Substring(2);

                var expected = new (string Prefix, string Line)[]
                {
                    ("InSim", $"InSim {insimPort}"),
                    ("OutGauge Mode", "OutGauge Mode 1"),
                    ("OutGauge Delay", "OutGauge Delay 1"),
                    ("OutGauge IP", "OutGauge IP 127.0.0.1"),
                    ("OutGauge Port", $"OutGauge Port {outGaugePort}"),
                    ("OutGauge ID", "OutGauge ID 0"),
                    ("OutSim Mode", "OutSim Mode 2"),
                    ("OutSim Delay", "OutSim Delay 1"),
                    ("OutSim IP", "OutSim IP 127.0.0.1"),
                    ("OutSim Port", $"OutSim Port {outSimPort}"),
                    ("OutSim ID", "OutSim ID 0"),
                    ("OutSim Opts", $"OutSim Opts {outSimOpts}"),
                };

                bool alreadyValid = expected.All(e => CfgLineMatches(lines, e.Prefix, e.Line));

                if (alreadyValid)
                {
                    ShowInfoPopup(Localization.T("general.configure.title"), Localization.T("general.configure.alreadyvalid"));
                    return;
                }

                foreach (var e in expected)
                    UpsertCfgLine(lines, e.Prefix, e.Line);

                string backupPath = cfgPath + ".bak";
                if (File.Exists(cfgPath) && !File.Exists(backupPath))
                {
                    try { File.Copy(cfgPath, backupPath); } catch { }
                }

                string tempPath = cfgPath + ".tmp";
                File.WriteAllLines(tempPath, lines, CfgEncoding);

                if (File.Exists(cfgPath))
                    File.Replace(tempPath, cfgPath, null);
                else
                    File.Move(tempPath, cfgPath);

                ShowInfoPopup(Localization.T("general.configure.title"), Localization.T("general.configure.success"));

                if (LfsWindow.Find() != IntPtr.Zero)
                    AppendStatusMessage(Localization.T("general.configure.lfsrunning"), ApplePalette.StatusWarn);
            }
            catch (Exception ex)
            {
                AppendStatusMessage(string.Format(Localization.T("general.configure.error"), ex.Message), StatusErrorColor);
            }
        }

        private static bool CfgLineMatches(List<string> lines, string keyPrefix, string expectedLine)
        {
            foreach (var line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                    (trimmed.Length == keyPrefix.Length || char.IsWhiteSpace(trimmed[keyPrefix.Length])))
                {
                    return string.Equals(NormalizeCfgLine(trimmed), NormalizeCfgLine(expectedLine), StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        private static string NormalizeCfgLine(string line) =>
            string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static void UpsertCfgLine(List<string> lines, string keyPrefix, string newLine)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                    (trimmed.Length == keyPrefix.Length || char.IsWhiteSpace(trimmed[keyPrefix.Length])))
                {
                    lines[i] = newLine;
                    return;
                }
            }
            lines.Add(newLine);
        }
    }
}
