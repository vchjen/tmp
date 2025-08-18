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

    // Choose a notification type (maps to color + icon)
    internal enum ToastIcon { None, Info, Warning, Error }

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
            menu.Items.Add("Info toast", null, (_, __) =>
                _toasts.Show("Heads up", "This is an informational message.", TimeSpan.FromSeconds(8), ToastIcon.Info));
            menu.Items.Add("Warning toast", null, (_, __) =>
                _toasts.Show("Be careful", "Something may need your attention.", TimeSpan.FromSeconds(8), ToastIcon.Warning));
            menu.Items.Add("Error toast", null, (_, __) =>
                _toasts.Show("Oops!", "An error occurred running the task.", TimeSpan.FromSeconds(8), ToastIcon.Error));
            menu.Items.Add("10-minute notice", null, (_, __) =>
                _toasts.Show("Long running task", "This stays for 10 minutes.", TimeSpan.FromMinutes(10), ToastIcon.Info));
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
            _demoTimer.Tick += (_, __) =>
                _toasts.Show("Timer Tick", $"It is {DateTime.Now:T}", TimeSpan.FromSeconds(12), ToastIcon.Info);
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

        public void Show(string title, string message, TimeSpan? duration = null, ToastIcon icon = ToastIcon.Info)
        {
            if (_ui.InvokeRequired) { _ui.BeginInvoke((Action)(() => Show(title, message, duration, icon))); return; }

            var life = duration ?? TimeSpan.FromSeconds(8);
            var form = new ToastForm(title, message, life, icon);
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

    /// <summary> Custom balloon form: min/max height, icon, rounded, fade. </summary>
    internal sealed class ToastForm : Form
    {
        // Layout constants
        private const int CornerRadius = 12;
        private const int HPAD = 14;
        private const int VPAD = 14;
        private const int HeaderHeight = 28;
        private const int MaxContentWidth = 360;
        private const int MinContentHeight = 72;   // ~like classic balloon minimum
        private const int MaxContentHeight = 200;  // clamp tall notifications
        private const int MinWidth = 240;          // allow room for icon + text

        private readonly Panel _header;
        private readonly Label _title;
        private readonly Button _closeBtn;

        private readonly PictureBox _glyph;   // icon
        private readonly Label _body;

        private readonly Panel _accent;       // colored stripe per icon type
        private readonly Timer _lifeTimer = new();
        private readonly Timer _fadeTimer = new();
        private bool _fadingIn = true;
        private readonly TimeSpan _life;
        private readonly ToastIcon _iconKind;

        public ToastForm(string title, string body, TimeSpan life, ToastIcon iconKind)
        {
            _life = life;
            _iconKind = iconKind;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Opacity = 0;

            // ===== Header =====
            _title = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.SemiBold)
            };

            _closeBtn = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 28,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (_, __) => BeginFadeOut();

            _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, Padding = new Padding(HPAD, 0, HPAD, 0) };
            _header.Controls.Add(_closeBtn);
            _header.Controls.Add(_title);

            // ===== Accent stripe =====
            _accent = new Panel { Dock = DockStyle.Left, Width = 4 };

            // ===== Body with icon + text =====
            _glyph = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.CenterImage,
                Dock = DockStyle.Left,
                Width = 36, // reserve space for 24px glyph + margins
                Margin = new Padding(HPAD, 0, 6, 0)
            };

            _body = new Label
            {
                Text = body,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, HPAD, VPAD),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            var bodyPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(HPAD, 0, 0, 0) };
            bodyPanel.Controls.Add(_body);
            bodyPanel.Controls.Add(_glyph);

            Controls.Add(bodyPanel);
            Controls.Add(_header);
            Controls.Add(_accent);

            // Size to content and theme
            SizeToContent();
            ApplyTheme();
            UpdateRoundedRegion();
            SizeChanged += (_, __) => UpdateRoundedRegion();

            // Timers
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

            Load += (_, __) => { _fadingIn = true; _fadeTimer.Start(); };

            // interactions
            Cursor = Cursors.Hand;
            Click += (_, __) => BeginFadeOut();
            _title.Click += (_, __) => BeginFadeOut();
            _body.Click  += (_, __) => BeginFadeOut();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) BeginFadeOut(); };
            Resize += (_, __) => Invalidate();
            Paint += (_, __) => DrawBorder();
        }

        private void SizeToContent()
        {
            // measure title (single line) and body (wrapped)
            var titleSize = TextRenderer.MeasureText(_title.Text, _title.Font, new Size(MaxContentWidth, int.MaxValue),
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            var bodySize = TextRenderer.MeasureText(_body.Text, _body.Font, new Size(MaxContentWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            int contentWidth = Math.Max(titleSize.Width, bodySize.Width);
            int finalWidth = Math.Max(MinWidth, Math.Min(contentWidth + HPAD * 2, MaxContentWidth + HPAD * 2 + _glyph.Width));

            // clamp body height between min and max
            int bodyHeight = Math.Max(MinContentHeight, Math.Min(bodySize.Height, MaxContentHeight));
            int finalHeight = HeaderHeight + 6 + bodyHeight + VPAD;

            ClientSize = new Size(finalWidth, finalHeight);
        }

        private void ApplyTheme()
        {
            bool dark = TaskbarInfo.IsDarkTheme();

            // window colors
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.White;
            _header.BackColor = dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(245, 245, 245);
            _title.ForeColor = dark ? Color.White : Color.Black;
            _body.ForeColor = dark ? Color.Gainsboro : Color.Black;

            // close button states
            _closeBtn.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _closeBtn.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(230, 230, 230);
            _closeBtn.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(76, 76, 76) : Color.FromArgb(210, 210, 210);

            // icon + accent per type
            Color accent;
            Icon sysIcon;
            switch (_iconKind)
            {
                default:
                case ToastIcon.None:
                    accent = dark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(210, 210, 210);
                    sysIcon = null;
                    break;
                case ToastIcon.Info:
                    accent = Color.FromArgb(0, 120, 215); // Win blue-ish
                    sysIcon = SystemIcons.Information;
                    break;
                case ToastIcon.Warning:
                    accent = Color.FromArgb(202, 138, 4); // amber
                    sysIcon = SystemIcons.Warning;
                    break;
                case ToastIcon.Error:
                    accent = Color.FromArgb(200, 40, 40); // red
                    sysIcon = SystemIcons.Error;
                    break;
            }
            _accent.BackColor = accent;

            if (sysIcon != null)
            {
                // render SystemIcon to 24x24 bitmap for consistent sizing
                _glyph.Image?.Dispose();
                _glyph.Image = new Bitmap(24, 24);
                using (var g = Graphics.FromImage(_glyph.Image))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawIcon(sysIcon, new Rectangle(0, 0, 24, 24));
                }
                _glyph.Visible = true;
            }
            else
            {
                _glyph.Visible = false;
            }
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
