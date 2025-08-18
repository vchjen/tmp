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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }

    /// <summary> Headless app context with tray + menu + demo timer. </summary>
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
                _toasts.Show("Long running task", "This stays for 10 minutes.", TimeSpan.FromMinutes(10)));
            menu.Items.Add("Show short notice", null, (_, __) =>
                _toasts.Show("Hello", "I look like a balloon.", TimeSpan.FromSeconds(8)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => Exit());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "Tray Balloon Alternative",
                ContextMenuStrip = menu
            };

            // demo toast every 30s
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
            ExitThread();
        }
    }

    /// <summary> Manages toast popups; ensures UI-thread marshaling. </summary>
    internal sealed class ToastManager : IDisposable
    {
        private readonly List<ToastForm> _open = new();
        private readonly Control _ui;

        public ToastManager()
        {
            _ui = new Control();
            _ui.CreateControl();
        }

        public void Show(string title, string message, TimeSpan? duration = null)
        {
            if (_ui.InvokeRequired) { _ui.BeginInvoke((Action)(() => Show(title, message, duration))); return; }

            var life = duration ?? TimeSpan.FromSeconds(8);
            var form = new ToastForm(title, message, life);
            form.FormClosed += (_, __) => { _open.Remove(form); RepositionAll(); };

            _open.Add(form);
            RepositionAll();
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

                var scr = Screen.FromPoint(basePoint);
                var wa = scr.WorkingArea;
                target.X = Math.Max(wa.Left, Math.Min(target.X, wa.Right - size.Width));
                target.Y = Math.Max(wa.Top, Math.Min(target.Y, wa.Bottom - size.Height));

                f.StartPosition = FormStartPosition.Manual;
                f.Location = target;
                y = f.Top - margin;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_ui.IsHandleCreated)
                    _ui.BeginInvoke((Action)(() => _open.ForEach(f => { if (!f.IsDisposed) f.Close(); })));
            }
            catch { }
            _open.Clear();
            _ui.Dispose();
        }
    }

    /// <summary> Custom balloon form. </summary>
    internal sealed class ToastForm : Form
    {
        private readonly Label _title;
        private readonly Label _body;
        private readonly Panel _header;
        private readonly Button _closeBtn;

        private readonly Timer _lifeTimer = new();
        private readonly Timer _fadeTimer = new();
        private bool _fadingIn = true;
        private readonly TimeSpan _life;

        // layout constants
        private const int CornerRadius = 12;
        private const int HPAD = 14;
        private const int VPAD = 14;
        private const int HeaderHeight = 28;
        private const int MaxContentWidth = 360;
        private const int MaxContentHeight = 200;
        private const int MinWidth = 220;

        public ToastForm(string title, string body, TimeSpan life)
        {
            _life = life;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Opacity = 0;

            _title = new Label { Text = title, Dock = DockStyle.Fill,
                                 TextAlign = ContentAlignment.MiddleLeft,
                                 Font = new Font("Segoe UI", 9.5f, FontStyle.SemiBold) };

            _closeBtn = new Button { Text = "✕", Dock = DockStyle.Right, Width = 28,
                                     FlatStyle = FlatStyle.Flat, TabStop = false };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (_, __) => BeginFadeOut();

            _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, Padding = new Padding(HPAD, 0, HPAD, 0) };
            _header.Controls.Add(_closeBtn);
            _header.Controls.Add(_title);

            _body = new Label { Text = body, Dock = DockStyle.Fill, Padding = new Padding(HPAD, 6, HPAD, VPAD),
                                Font = new Font("Segoe UI", 9f, FontStyle.Regular) };

            Controls.Add(_body);
            Controls.Add(_header);

            SizeToContent();
            UpdateRoundedRegion();
            SizeChanged += (_, __) => UpdateRoundedRegion();

            _lifeTimer.Interval = (int)Math.Clamp(_life.TotalMilliseconds, 1000, int.MaxValue);
            _lifeTimer.Tick += (_, __) => BeginFadeOut();

            _fadeTimer.Interval = 16;
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

            Load += (_, __) =>
            {
                ApplyTheme();
                _fadingIn = true;
                _fadeTimer.Start();
            };

            Cursor = Cursors.Hand;
            Click += (_, __) => BeginFadeOut();
            _title.Click += (_, __) => BeginFadeOut();
            _body.Click += (_, __) => BeginFadeOut();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) BeginFadeOut(); };
            Resize += (_, __) => Invalidate();
            Paint += (_, __) => DrawBorder();
        }

        private void SizeToContent()
        {
            var titleSize = TextRenderer.MeasureText(_title.Text, _title.Font, new Size(MaxContentWidth, int.MaxValue),
                                                     TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var bodySize = TextRenderer.MeasureText(_body.Text, _body.Font, new Size(MaxContentWidth, int.MaxValue),
                                                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            int contentWidth = Math.Max(titleSize.Width, bodySize.Width);
            int finalWidth = Math.Max(MinWidth, Math.Min(contentWidth + HPAD * 2, MaxContentWidth + HPAD * 2));
            int bodyHeight = Math.Min(bodySize.Height, MaxContentHeight);

            int finalHeight = HeaderHeight + 6 + bodyHeight + VPAD;
            ClientSize = new Size(finalWidth, finalHeight);
        }

        private void ApplyTheme()
        {
            bool dark = TaskbarInfo.IsDarkTheme();
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.White;
            _title.ForeColor = dark ? Color.White : Color.Black;
            _body.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _header.BackColor = dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(245, 245, 245);

            _closeBtn.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _closeBtn.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(230, 230, 230);
            _closeBtn.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(76, 76, 76) : Color.FromArgb(210, 210, 210);
        }

        private void BeginFadeOut()
        {
            _lifeTimer.Stop();
            _fadingIn = false;
            if (!_fadeTimer.Enabled) _fadeTimer.Start();
        }

        private void DrawBorder()
        {
            using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var gp = new System.Drawing.Drawing2D.GraphicsPath();
            int r = CornerRadius;
            gp.AddArc(rect.X, rect.Y, r, r, 180, 90);
            gp.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            gp.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            gp.CloseAllFigures();
            Region = new Region(gp);
            using var g = CreateGraphics();
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawPath(pen, gp);
        }

        private void UpdateRoundedRegion()
        {
            using var gp = new System.Drawing.Drawing2D.GraphicsPath();
            var rect = new Rectangle(0, 0, Width, Height);
            int r = CornerRadius;
            gp.AddArc(rect.X, rect.Y, r, r, 180, 90);
            gp.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            gp.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            gp.CloseAllFigures();
            Region = new Region(gp);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;
                cp.ExStyle |= 0x00080000;
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }
    }

    /// <summary> Taskbar info helper. </summary>
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
            var wa = Screen.PrimaryScreen.WorkingArea;
            Edge = TaskbarEdge.Bottom;
            return new Point(wa.Right - 8, wa.Bottom - 8);
        }

        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("SystemUsesLightTheme");
                if (val is int i) return i == 0;
            }
            catch { }
            return false;
        }
    }
}
