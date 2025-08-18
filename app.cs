using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TrayBalloonAlt
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Timer _demoTimer;
        private readonly ToastManager _toasts;

        public TrayAppContext()
        {
            _toasts = new ToastManager();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show 10-minute notice", null, (_, __) =>
                _toasts.Show("Long running task", "I will stay here for 10 minutes.", TimeSpan.FromMinutes(10)));
            menu.Items.Add("Show short notice", null, (_, __) =>
                _toasts.Show("Hello", "I look like a balloon, but I’m 100% yours.", TimeSpan.FromSeconds(10)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => Exit());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "Tray Balloon Alternative",
                ContextMenuStrip = menu
            };

            // Demo: fire every 30s
            _demoTimer = new Timer { Interval = 30_000 };
            _demoTimer.Tick += (_, __) =>
                _toasts.Show("Timer Tick", $"It is {DateTime.Now:T}", TimeSpan.FromSeconds(12));
            _demoTimer.Start();
        }

        private void Exit()
        {
            _demoTimer.Stop();
            _toasts.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _demoTimer.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toasts?.Dispose();
                _tray?.Dispose();
                _demoTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Manages showing stacked toast windows near the tray.
    /// </summary>
    internal sealed class ToastManager : IDisposable
    {
        private readonly List<ToastForm> _open = new();

        public void Show(string title, string message, TimeSpan? duration = null)
        {
            var form = new ToastForm(title, message, duration ?? TimeSpan.FromSeconds(8));
            form.FormClosed += (_, __) =>
            {
                _open.Remove(form);
                RepositionAll();
            };
            _open.Add(form);
            RepositionAll();
            form.Show(); // modeless
        }

        private void RepositionAll()
        {
            var basePoint = TaskbarInfo.GetTrayAnchor(); // bottom-right-ish for bottom taskbar, etc.
            int margin = 8;
            int y = basePoint.Y;

            // Stack upwards (or sideways depending on taskbar edge)
            foreach (var f in _open.AsReadOnly())
            {
                switch (TaskbarInfo.Edge)
                {
                    case TaskbarInfo.TaskbarEdge.Bottom:
                        f.Location = new Point(basePoint.X - f.Width, y - f.Height);
                        y = f.Top - margin;
                        break;
                    case TaskbarInfo.TaskbarEdge.Top:
                        f.Location = new Point(basePoint.X - f.Width, y);
                        y = f.Bottom + margin;
                        break;
                    case TaskbarInfo.TaskbarEdge.Left:
                        f.Location = new Point(basePoint.X, y - f.Height);
                        y = f.Top - margin;
                        break;
                    case TaskbarInfo.TaskbarEdge.Right:
                        f.Location = new Point(basePoint.X - f.Width, y - f.Height);
                        y = f.Top - margin;
                        break;
                }
            }
        }

        public void Dispose()
        {
            foreach (var f in _open.ToArray())
                f.Close();
            _open.Clear();
        }
    }

    /// <summary>
    /// The custom “balloon” window. Borderless, rounded, fade in/out, click to dismiss.
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private readonly Label _title;
        private readonly Label _body;
        private readonly Timer _lifeTimer = new();
        private readonly Timer _fadeTimer = new();
        private bool _fadingIn = true;

        public ToastForm(string title, string body, TimeSpan life)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0; // start transparent, fade in
            BackColor = Color.White;
            Padding = new Padding(14);
            MinimumSize = new Size(280, 80);

            // Drop shadow
            this.CreateParams.ClassStyle |= 0x20000; // CS_DROPSHADOW

            _title = new Label
            {
                Text = title,
                AutoSize = false,
                Font = new Font(SystemFonts.CaptionFont.FontFamily, 10, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };
            _body = new Label
            {
                Text = body,
                AutoSize = false,
                Font = new Font(SystemFonts.CaptionFont.FontFamily, 9, FontStyle.Regular),
                Dock = DockStyle.Fill
            };

            var closeBtn = new Button
            {
                Text = "×",
                AutoSize = false,
                FlatStyle = FlatStyle.Flat,
                Width = 28,
                Height = 22,
                Dock = DockStyle.Right,
                TabStop = false
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.Click += (_, __) => BeginFadeOut();

            var header = new Panel { Dock = DockStyle.Top, Height = 22 };
            header.Controls.Add(closeBtn);
            header.Controls.Add(_title);

            Controls.Add(_body);
            Controls.Add(header);

            // Rounded corners
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 16, 16));
            SizeChanged += (_, __) =>
            {
                Region?.Dispose();
                Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 16, 16));
            };

            // Dismiss behaviors
            _lifeTimer.Interval = (int)Math.Clamp(life.TotalMilliseconds, 1000, int.MaxValue);
            _lifeTimer.Tick += (_, __) => BeginFadeOut();

            // Fade
            _fadeTimer.Interval = 16; // ~60fps
            _fadeTimer.Tick += (_, __) =>
            {
                if (_fadingIn)
                {
                    Opacity = Math.Min(1.0, Opacity + 0.08);
                    if (Opacity >= 0.999) { _fadeTimer.Stop(); _lifeTimer.Start(); }
                }
                else
                {
                    Opacity = Math.Max(0.0, Opacity - 0.08);
                    if (Opacity <= 0.001) Close();
                }
            };

            // Interaction
            Cursor = Cursors.Hand;
            Click += (_, __) => BeginFadeOut();
            _title.Click += (_, __) => BeginFadeOut();
            _body.Click += (_, __) => BeginFadeOut();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) BeginFadeOut(); };

            // Colors adapt to theme (simple heuristic)
            if (TaskbarInfo.IsDarkTheme())
            {
                BackColor = Color.FromArgb(32, 32, 32);
                _title.ForeColor = Color.White;
                _body.ForeColor = Color.Gainsboro;
            }

            // Start
            Shown += (_, __) => { _fadingIn = true; _fadeTimer.Start(); };
        }

        private void BeginFadeOut()
        {
            _lifeTimer.Stop();
            _fadingIn = false;
            _fadeTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;     // WS_EX_TOOLWINDOW (no alt-tab)
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED (opacity)
                return cp;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }

    /// <summary>
    /// Retrieves taskbar position and a reasonable anchor near the tray.
    /// Uses SHAppBarMessage; good enough for most setups (single monitor).
    /// </summary>
    internal static class TaskbarInfo
    {
        public enum TaskbarEdge { Bottom, Top, Left, Right }
        public static TaskbarEdge Edge { get; private set; } = TaskbarEdge.Bottom;

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("shell32.dll")]
        private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private const uint ABM_GETTASKBARPOS = 0x00000005;

        public static Point GetTrayAnchor()
        {
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != 0)
            {
                Edge = (TaskbarEdge)abd.uEdge;
                var r = abd.rc;
                switch (Edge)
                {
                    case TaskbarEdge.Bottom: return new Point(r.right - 8, r.top - 8);
                    case TaskbarEdge.Top:    return new Point(r.right - 8, r.bottom + 8);
                    case TaskbarEdge.Left:   return new Point(r.right + 8, r.bottom - 8);
                    case TaskbarEdge.Right:  return new Point(r.left - 8, r.bottom - 8);
                }
            }
            // Fallback: bottom-right of primary working area
            var wa = Screen.PrimaryScreen.WorkingArea;
            Edge = TaskbarEdge.Bottom;
            return new Point(wa.Right - 8, wa.Bottom - 8);
        }

        // Very simple dark mode check (SystemUsesLightTheme registry)
        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("SystemUsesLightTheme") as int?; // 0 = dark, 1 = light
                return val == 0;
            }
            catch { return false; }
        }
    }
}
