using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LFSDriftTools
{
    public class InGameSettingsOverlay : Form
    {
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        public Panel ContentHost { get; }

        public event Action? SettingsOverlayClosed;

        public InGameSettingsOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.Black;
            Opacity = 0.94;
            DoubleBuffered = true;

            ContentHost = new Panel { BackColor = Color.Black };
            Controls.Add(ContentHost);
        }

        public void ShowOverWindow(IntPtr targetHwnd, Action<int> reflowContent, Action refreshValues)
        {
            if (targetHwnd == IntPtr.Zero || !GetWindowRect(targetHwnd, out var r)) return;

            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return;

            Bounds = new Rectangle(r.Left, r.Top, w, h);

            reflowContent?.Invoke(w);
            refreshValues?.Invoke();
            CenterContent();

            Show();
            Activate();
            Focus();
        }

        public void CenterContent()
        {
            ContentHost.Location = new Point(
                Math.Max(0, (Width - ContentHost.Width) / 2),
                Math.Max(0, (Height - ContentHost.Height) / 2));
        }

        public void RestoreAndHide()
        {
            Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!e.Cancel)
            {
                e.Cancel = true;
                RestoreAndHide();
                SettingsOverlayClosed?.Invoke();
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                RestoreAndHide();
                SettingsOverlayClosed?.Invoke();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
