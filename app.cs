using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TrayBalloonAlt
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Surface errors if something goes wrong in timers/threads
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "UI Error");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(e.ExceptionObject?.ToString() ?? "Unknown", "Non-UI Error");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext()); // KEEP MESSAGE LOOP ALIVE
        }
    }

    /// <summary> Headless app context with tray icon + menu and a demo timer. </summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Timer _demoTimer;
        private readonly ToastManager _toasts;

        public TrayAppContext()
        {
            _toasts = new ToastManager(); // constructed on UI thread

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show 10-minute notice", null, (_, __) =>
                _toasts.Show("Long running task", "I will stay here for 10 minutes.", TimeSpan.FromMinutes(10)));
            menu.Items.Add("Show short notice", null, (_, __) =>
                _toasts.Show("Hello", "I look like a balloon, but I’m 100% yours.", TimeSpan.FromSeconds(8)));
            menu.Items.Add("Show centered (debug)", null, (_, __) =>
            {
                var screen = Screen.PrimaryScreen.WorkingArea;
                var center = new Point(screen.Left + screen.Width / 2 - 160, screen.Top + screen.Height / 2 - 60);
                _toasts.ShowAt("Centered Test", "If you see this, tray positioning was the issue.", TimeSpan.FromSeconds(10), center);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => Exit());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "Tray Balloon Alternative",
                ContextMenuStrip = menu
            };

            // Demo: toast every 30s so you can see it working
            _demoTimer = new Timer { Interval = 30_000 };
            _demoTimer.Tick += (_, __) => _toasts.Show("Timer Tick", $"It is {DateTime.Now:T}", TimeSpan.FromSeconds(12));
            _demoTimer.Start();
        }

        private void Exit()
        {
            _demoTimer.Stop();
            _toasts.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _demoTimer.Dispose();
            ExitThread(); // ends Application.Run
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
    /// Manages showing stacked toast windows near the system tray; marshals to UI thread.
    /// </summary>
    internal sealed class ToastManager : IDisposable
    {
        private readonly List<ToastForm> _open = new();
        private readonly Control _ui; // hidden control to marshal to UI thread

        public ToastManager()
        {
            _ui = new Control();
            _ui.CreateControl(); // binds to the UI thread that created ToastManager
        }

        public void Show(string title, string message, TimeSpan? duration = null)
        {
            if (_ui.InvokeRequired)
            {
                _ui.BeginInvoke((Action)(() => Show(title, message, duration)));
                return;
            }

            var life = duration ?? TimeSpan.FromSeconds(8);
            var form = new ToastForm(title, message, life);
            form.FormClosed += (_, __) => { _open.Remove(form); RepositionAll(); };

            _open.Add(form);
            RepositionAll();

            form.Show();        // modeless
            form.BringToFront();
        }

        public void ShowAt(string title, string message, TimeSpan? duration, Point location)
        {
            if (_ui.InvokeRequired)
            {
                _ui.BeginInvoke((Action)(() => ShowAt(title, message, duration, location)));
                return;
            }

            var life = duration ?? TimeSpan.FromSeconds(8);
            var form = new ToastForm(title, message, life)
            {
                StartPosition = FormStartPosition.Manual,
                Location = location
            };
            form.FormClosed += (_, __) => { _open.Remove(form); RepositionAll(); };

            _open.Add(form);
            form.Show();
            form.BringToFront();
        }

        private void RepositionAll()
        {
            if (_ui.InvokeRequired) { _ui.BeginInvoke((Action)RepositionAll); return; }

            var basePoint = TaskbarInfo.GetTrayAnchor();
            int margin = 8;
            int y = basePoint.Y;

            foreach (var f in _open.ToArray())
            {
                var size = f.Size;
                var target = new Point(basePoint.X - size.Width, y - size.Height);

                // Clamp to the screen containing the tray point (avoid off-screen)
                var scr = Screen.FromPoint(basePoint);
                var wa = scr.WorkingArea;
                target.X = Math.Max(wa.Left, Math.Min(target.X, wa.Right - size.Width));
                target.Y = Math.Max(wa.Top, Math.Min(target.Y, wa.Bottom - size.Height));

                f.StartPosition = FormStartPosition.Manual;
                f.Location = target;
                y = f.Top - margin; // stack upwards
            }
        }

        public void Dispose()
        {
            try
            {
                if (_ui.IsHandleCreated)
                    _ui.BeginInvoke((Action)(() => _open.ForEach(f => { if (!f.IsDisposed) f.Close(); })));
            }
            catch { /* ignore on shutdown */ }
            _open.Clear();
            _ui.Dispose();
        }
    }

    /// <summary>
    /// The custom “balloon” popup. Borderless, rounded, fade in/out, click-to-dismiss.
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private readonly Label _title;
        private readonly Label _body;
        private readonly Timer _lifeTimer = new();
        private readonly Timer _fadeTimer = new();
        private bool _fadingIn = true;
        private readonly TimeSpan _life;

        public ToastForm(string title, string body, TimeSpan life)
        {
            _life = life;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0; // start transparent, fade in
            BackColor = Color.White;
            Padding = new Padding(14);
            MinimumSize = new Size(280, 88);

            // Content
            _title = new Label
            {
                Text = title,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font(SystemFonts.CaptionFont.FontFamily, 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _body = new Label
            {
                Text = body,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.CaptionFont.FontFamily, 9f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
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

            // Rounded corners (updated on resize)
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 16, 16));
            SizeChanged += (_, __) =>
            {
                try
                {
                    Region?.Dispose();
                    Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 16, 16));
                }
                catch { }
            };

            // Timers
            _lifeTimer.Interval = (int)Math.Clamp(_life.TotalMilliseconds, 1000, int.MaxValue);
            _lifeTimer.Tick += (_, __) => BeginFadeOut();

            _fadeTimer.Interval = 16; // ~60fps
            _fadeTimer.Tick += (_, __) =>
            {
                try
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
                }
                catch { /* never throw out of Tick */ }
            };

            // Start fade on load (robust even if Shown order varies)
            Load += (_, __) =>
            {
                ApplyTheme();
                _fadingIn = true;
                _fadeTimer.Start();
            };

            // Interactions
            Cursor = Cursors.Hand;
            Click += (_, __) => BeginFadeOut();
            _title.Click += (_, __) => BeginFadeOut();
            _body.Click += (_, __) => BeginFadeOut();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) BeginFadeOut(); };
        }

        private void ApplyTheme()
        {
            try
            {
                if (TaskbarInfo.IsDarkTheme())
                {
                    BackColor = Color.FromArgb(32, 32, 32);
                    _title.ForeColor = Color.White;
                    _body.ForeColor = Color.Gainsboro;
                }
                else
                {
                    BackColor = Color.White;
                    _title.ForeColor = Color.Black;
                    _body.ForeColor = Color.Black;
                }
            }
            catch { }
        }

        private void BeginFadeOut()
        {
            _lifeTimer.Stop();
            _fadingIn = false;
            if (!_fadeTimer.Enabled) _fadeTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW (no Alt+Tab)
                cp.ExStyle |= 0x00080000;  // WS_EX_LAYERED (supports Opacity)
                return cp;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse);
    }

    /// <summary>
    /// Taskbar info + a reasonable tray anchor using SHAppBarMessage.
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

        [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
        private const uint ABM_GETTASKBARPOS = 0x00000005;

        public static Point GetTrayAnchor()
        {
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != 0)
            {
                Edge = (TaskbarEdge)abd.uEdge;
                var r = abd.rc;
                return Edge switch
                {
                    TaskbarEdge.Bottom => new Point(r.right - 8, r.top - 8),
                    TaskbarEdge.Top    => new Point(r.right - 8, r.bottom + 8),
                    TaskbarEdge.Left   => new Point(r.right + 8, r.bottom - 8),
                    TaskbarEdge.Right  => new Point(r.left - 8, r.bottom - 8),
                    _ => new Point(Screen.PrimaryScreen.WorkingArea.Right - 8,
                                   Screen.PrimaryScreen.WorkingArea.Bottom - 8)
                };
            }
            // Fallback: bottom-right of primary working area
            var wa = Screen.PrimaryScreen.WorkingArea;
            Edge = TaskbarEdge.Bottom;
            return new Point(wa.Right - 8, wa.Bottom - 8);
        }

        // Very simple theme probe (0 = dark, 1 = light)
        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("SystemUsesLightTheme");
                if (val is int i) return i == 0;
            }
            catch { }
            return false;
        }
    }
}
