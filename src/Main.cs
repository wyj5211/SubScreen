using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SubScreen.Background;
using SubScreen.Core;
using SubScreen.Media;
using SubScreen.Net;
using SubScreen.Ui;
// System.Threading 和 System.Windows.Forms 都有 Timer，这里明确指向 WinForms 那个
using Timer = System.Windows.Forms.Timer;

namespace SubScreen
{
    public partial class Main : Form
    {
        internal static MainOptions StartupOptions = new MainOptions();

        private AppSettings _settings;
        private Theme _theme;
        private Dashboard _dashboard;

        private BackgroundSource _background;
        private Bitmap _bgFrame;
        private int _bgGeneration;

        private SmtcService _smtc;
        private QWeatherService _weatherService;
        private CancellationTokenSource _netCts;

        private Timer _uiTimer;
        private Timer _weatherTimer;
        private Timer _hitokotoTimer;
        private Timer _volumeTimer;

        /// <summary>正在拖拽的卡片（音量条），没有则为 null。</summary>
        private Tile _dragTile;

        private Screen _targetScreen;
        private Size _laidOutSize = Size.Empty;
        private bool _closing;
        private bool _captureMode;

        // ---- 托盘 ----
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private Icon _trayIcon;

        public Main()
        {
            InitializeComponent();

            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            KeyPreview = true;
        }

        // ================================================================
        // 生命周期
        // ================================================================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Program.Trace("Main.OnLoad enter");

            AppPaths.EnsureCreated();
            Program.Trace("Main.OnLoad: paths ok");

            _captureMode = !string.IsNullOrEmpty(StartupOptions.CapturePath);

            _settings = AppSettings.Current;
            Program.Trace("Main.OnLoad: settings loaded");
            _theme = Theme.Create(_settings);
            Program.Trace("Main.OnLoad: theme created");

            // 窗体自身的图标（Alt+Tab、任务栏缩略图等用得到）
            Icon = LoadAppIcon(SystemInformation.IconSize.Width);

            _dashboard = new Dashboard();
            _dashboard.Weather.RefreshRequested += delegate { FetchWeatherAsync(); };
            _dashboard.Hitokoto.RefreshRequested += delegate { FetchHitokotoAsync(); };
            _dashboard.Toolbar.SettingsRequested += ShowSettings;
            _dashboard.Toolbar.RefreshRequested += delegate { FetchWeatherAsync(); FetchHitokotoAsync(); };
            _dashboard.Toolbar.MinimizeRequested += delegate
            {
                // 直接设属性，不走 WM_SYSCOMMAND，避免被上面的 WndProc 吞掉
                WindowState = FormWindowState.Minimized;
            };
            _dashboard.Toolbar.ExitRequested += delegate { Close(); };

            _netCts = new CancellationTokenSource();

            if (_captureMode)
            {
                try
                {
                    RunCaptureMode();
                }
                catch (Exception ex)
                {
                    // 截图模式：把异常写到截图旁边的文件，方便离线排查
                    try
                    {
                        string side = StartupOptions.CapturePath + ".error.txt";
                        string dir = System.IO.Path.GetDirectoryName(side);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            System.IO.Directory.CreateDirectory(dir);
                        }
                        System.IO.File.WriteAllText(side, ex.ToString());
                    }
                    catch (Exception)
                    {
                    }
                    throw;
                }
                return;
            }

            ApplyScreenBounds();
            _dashboard.Layout(ClientSize, _settings, DpiScale);
            _laidOutSize = ClientSize;

            SetupTray();

            if (StartupOptions.Demo)
            {
                PopulateDemoData();
            }

            StartBackgroundBuild();

            _uiTimer = new Timer();
            _uiTimer.Interval = 250;
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();

            // 音量可能被键盘/其它程序改掉，定期读一次同步到界面上
            _dashboard.Media.RefreshVolume();
            _volumeTimer = new Timer();
            _volumeTimer.Interval = 1000;
            _volumeTimer.Tick += OnVolumeTick;
            _volumeTimer.Start();

            if (!StartupOptions.Demo)
            {
                StartSmtc();
                FetchWeatherAsync();
                FetchHitokotoAsync();
                StartPeriodicTimers();
            }
        }


        /// <summary>
        /// app.ico 的资源流。
        ///
        /// 注意：new Icon(Stream) **要求流在 Icon 的整个生命周期内保持打开** ——
        /// Icon 是延迟从流里读像素的。之前用 using 包住流，构造完就关掉了，
        /// 结果托盘图标渲染出来是一团彩色噪声。这里显式持有引用，直到进程结束。
        /// </summary>
        private static readonly List<Stream> IconStreams = new List<Stream>();

        /// <summary>
        /// 取程序图标。
        ///
        /// app.ico 是嵌入的多尺寸图标（16..256），这里按调用方要的尺寸取值 ——
        /// 直接 Icon.ExtractAssociatedIcon 只会给一张 32x32，缩小到托盘用的 16x16 会发虚。
        /// </summary>
        private static Icon LoadAppIcon(int size)
        {
            try
            {
                Stream stream = typeof(Main).Assembly
                    .GetManifestResourceStream("SubScreen.app.ico");
                if (stream != null)
                {
                    lock (IconStreams)
                    {
                        IconStreams.Add(stream);
                    }
                    return new Icon(stream, size, size);
                }
            }
            catch (Exception)
            {
            }

            try
            {
                Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
            catch (Exception)
            {
            }

            // SystemIcons 是共享的，不能 Dispose
            return SystemIcons.Application;
        }

        private float DpiScale
        {
            get
            {
                int dpi = NativeMethods.GetWindowDpi(Handle);
                return dpi <= 0 ? 1f : dpi / 96f;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_captureMode)
            {
                // 截图在 OnLoad 里已经写盘了，这里只需要安静地退出，
                // 避免窗口在用户屏幕上闪一下。
                Hide();
                BeginInvoke((MethodInvoker)delegate { Close(); });
            }
        }

        /// <summary>
        /// 托盘图标与右键菜单（照 https://www.cnblogs.com/vipsoft/p/18665897 的写法）。
        ///
        /// 只加托盘，**不动任务栏显示** —— 万一托盘图标被 Windows 收进溢出面板，
        /// 任务栏按钮还在，程序不会变得够不着。
        /// 图标直接取窗体自己的 Icon（文章里就是 this.Icon），保证 HICON 有效。
        /// </summary>
        private void SetupTray()
        {
            var show = new ToolStripMenuItem("显示主页面");
            show.Click += delegate { ShowMainWindow(); };

            var exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add(show);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exit);

            _trayIcon = Icon;

            _tray = new NotifyIcon();
            // Text / Icon / 菜单都设好再 Visible，避免第一次 NIM_ADD 带空数据
            _tray.Text = "SubScreen 副屏信息面板";
            _tray.Icon = _trayIcon;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += delegate { ShowMainWindow(); };
            _tray.Visible = true;

            Program.Trace("tray: visible=" + _tray.Visible
                + " hicon=0x" + (_trayIcon == null ? "0" : _trayIcon.Handle.ToInt64().ToString("X"))
                + " size=" + (_trayIcon == null ? "?" : _trayIcon.Width + "x" + _trayIcon.Height));
        }

        /// <summary>把主窗口显示出来并恢复到目标屏幕的完整区域。</summary>
        private void ShowMainWindow()
        {
            if (_closing || IsDisposed)
            {
                return;
            }

            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Bounds = DesiredBounds();
            Activate();
            BringToFront();
        }

        private void DisposeTray()
        {
            if (_tray != null)
            {
                // 先隐藏，否则图标会残留到鼠标划过才消失
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }

            // _trayIcon 就是窗体自己的 Icon，归窗体管，这里不要释放
            _trayIcon = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;

            DisposeTray();

            if (_netCts != null)
            {
                try
                {
                    _netCts.Cancel();
                }
                catch (Exception)
                {
                }
            }

            if (_uiTimer != null) _uiTimer.Stop();
            if (_weatherTimer != null) _weatherTimer.Stop();
            if (_hitokotoTimer != null) _hitokotoTimer.Stop();

            if (_smtc != null)
            {
                _smtc.Dispose();
                _smtc = null;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_dashboard != null)
            {
                _dashboard.Dispose();
                _dashboard = null;
            }
            if (_background != null)
            {
                _background.Dispose();
                _background = null;
            }
            if (_netCts != null)
            {
                _netCts.Dispose();
                _netCts = null;
            }

            base.OnFormClosed(e);
        }

        // ================================================================
        // 窗口定位：只允许最大化，不给还原/最小化
        // ================================================================

        private void ApplyScreenBounds()
        {
            _targetScreen = PickScreen(_settings);
            StartPosition = FormStartPosition.Manual;
            Bounds = DesiredBounds();
        }

        /// <summary>窗口应该占据的矩形（按当前设置和屏幕）。</summary>
        private Rectangle DesiredBounds()
        {
            Screen screen = _targetScreen ?? PickScreen(_settings);
            return _settings.CoverTaskbar ? screen.Bounds : screen.WorkingArea;
        }

        private static Screen PickScreen(AppSettings settings)
        {
            Screen[] all = Screen.AllScreens;
            if (all.Length == 0)
            {
                return Screen.PrimaryScreen;
            }

            if (settings.ScreenChoice == ScreenChoice.Primary)
            {
                return Screen.PrimaryScreen ?? all[0];
            }

            if (settings.ScreenChoice == ScreenChoice.Specific && !string.IsNullOrEmpty(settings.ScreenDevice))
            {
                foreach (Screen s in all)
                {
                    if (string.Equals(s.DeviceName, settings.ScreenDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }
            }

            // 自动：非主屏里面积最大的那块
            Screen best = null;
            long bestArea = 0;
            foreach (Screen s in all)
            {
                if (s.Primary)
                {
                    continue;
                }
                long area = (long)s.Bounds.Width * s.Bounds.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = s;
                }
            }

            return best ?? Screen.PrimaryScreen ?? all[0];
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE = 0xF000;
        private const int SC_MOVE = 0xF010;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_RESTORE = 0xF120;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;

                if (cmd == SC_RESTORE)
                {
                    // 只有"从最小化恢复"（点任务栏）才放行；
                    // 其它来源的"还原"一律吞掉，保证窗口始终铺满目标屏幕。
                    if (WindowState != FormWindowState.Minimized)
                    {
                        return;
                    }
                }
                else if (cmd == SC_MINIMIZE || cmd == SC_MAXIMIZE || cmd == SC_MOVE || cmd == SC_SIZE)
                {
                    return;
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_closing || _settings == null || _dashboard == null)
            {
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }

            // 从最小化恢复时，Windows 可能给回一个别的尺寸，这里重新钉回目标屏幕
            Rectangle want = DesiredBounds();
            if (Bounds != want)
            {
                Bounds = want;
                return; // 会再触发一次 OnResize，那一次再走下面的重排
            }

            if (ClientSize != _laidOutSize)
            {
                _laidOutSize = ClientSize;
                _dashboard.Layout(ClientSize, _settings, DpiScale);
                StartBackgroundBuild();
                Invalidate();
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyScreenBounds();
            if (_dashboard != null)
            {
                _dashboard.Layout(ClientSize, _settings, DpiScale);
            }
            StartBackgroundBuild();
            Invalidate();
        }

        // ================================================================
        // 绘制
        // ================================================================

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景由 OnPaint 统一画，避免 WinForms 再刷一遍造成闪烁
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_dashboard == null || _theme == null)
            {
                base.OnPaint(e);
                return;
            }

            PaintScene(e.Graphics);
        }

        private void PaintScene(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            Size size = ClientSize;

            Bitmap frame = _bgFrame;
            if (frame != null && frame.Width == size.Width && frame.Height == size.Height)
            {
                g.DrawImageUnscaled(frame, 0, 0);
            }
            else
            {
                Color solid = (_background != null && _background.IsSolid)
                    ? _background.SolidColor
                    : _settings.BackgroundColor;
                using (var brush = new SolidBrush(solid))
                {
                    g.FillRectangle(brush, ClientRectangle);
                }
            }

            if (_theme.Scrim.A > 0)
            {
                using (var brush = new SolidBrush(_theme.Scrim))
                {
                    g.FillRectangle(brush, ClientRectangle);
                }
            }

            _dashboard.Blit(g, _theme, _settings);
        }

        private void InvalidateTile(Tile tile)
        {
            if (tile == null || _closing)
            {
                return;
            }

            Rectangle r = Rectangle.Round(tile.Bounds);
            r.Inflate(3, 3);
            Invalidate(r);
        }

        // ================================================================
        // 鼠标
        // ================================================================

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dashboard == null)
            {
                return;
            }

            var p = new PointF(e.X, e.Y);

            // 悬浮在可点击项上时用系统的链接选择光标（Cursors.Hand）。
            // 每次都设置，不要只在 hover 状态变化时设置 —— 否则窗口被激活、
            // 系统重设光标之后就不会再更新了。
            Cursor = _dashboard.CursorAt(p);

            if (_dragTile != null)
            {
                if (_dragTile.MouseMove(p))
                {
                    InvalidateTile(_dragTile);
                }
                return;
            }

            if (_dashboard.SetMouse(p))
            {
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            // 拖拽中鼠标划出卡片是正常的，不要在这里收尾
            if (_dragTile != null)
            {
                return;
            }

            if (_dashboard != null && _dashboard.SetMouse(new PointF(-9999f, -9999f)))
            {
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_dashboard == null || e.Button != MouseButtons.Left)
            {
                return;
            }

            var p = new PointF(e.X, e.Y);
            _dashboard.SetMouse(p);
            _dragTile = _dashboard.MouseDown(p);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dashboard == null)
            {
                return;
            }

            var p = new PointF(e.X, e.Y);

            if (_dragTile != null)
            {
                Tile finished = _dragTile;
                _dragTile = null;
                finished.MouseMove(p);
                finished.MouseUp(p);
                InvalidateTile(finished);
                return;
            }

            if (e.Button == MouseButtons.Left && _dashboard.MouseUp(p))
            {
                Invalidate();
            }
        }

        // ================================================================
        // 定时器
        // ================================================================

        private void OnUiTick(object sender, EventArgs e)
        {
            if (_dashboard == null)
            {
                return;
            }

            bool dirty = _dashboard.Clock.Update(DateTime.Now);
            if (_dashboard.Calendar.Tick())
            {
                dirty = true;
            }

            if (dirty)
            {
                InvalidateTile(_dashboard.Clock);
                InvalidateTile(_dashboard.Calendar);
            }
        }

        /// <summary>音量可能被键盘或别的程序改掉，定期读回来同步界面。</summary>
        private void OnVolumeTick(object sender, EventArgs e)
        {
            if (_dashboard == null || _closing)
            {
                return;
            }

            if (_dashboard.Media.RefreshVolume())
            {
                InvalidateTile(_dashboard.Media);
            }
        }

        private void StartPeriodicTimers()
        {
            _weatherTimer = new Timer();
            _weatherTimer.Interval = ClampMinutes(_settings.WeatherRefreshMinutes, 30) * 60000;
            _weatherTimer.Tick += delegate { FetchWeatherAsync(); };
            _weatherTimer.Start();

            _hitokotoTimer = new Timer();
            _hitokotoTimer.Interval = ClampMinutes(_settings.HitokotoRefreshMinutes, 60) * 60000;
            _hitokotoTimer.Tick += delegate { FetchHitokotoAsync(); };
            _hitokotoTimer.Start();
        }

        private static int ClampMinutes(int minutes, int fallback)
        {
            if (minutes < 1) return fallback;
            if (minutes > 1440) return 1440;
            return minutes;
        }

        // ================================================================
        // 背景
        // ================================================================

        private void StartBackgroundBuild()
        {
            if (_captureMode)
            {
                // 截图模式同步构建，保证渲染时背景已经就绪
                BuildBackgroundSync(ClientSize);
                return;
            }

            _bgGeneration++;
            int generation = _bgGeneration;
            Size target = ClientSize;
            AppSettings snapshot = _settings.Clone();

            ThreadPool.QueueUserWorkItem(delegate
            {
                BackgroundSource source = null;
                try
                {
                    source = new BackgroundSource();
                    source.Rebuild(target, snapshot);
                }
                catch (Exception ex)
                {
                    try
                    {
                        source = new BackgroundSource();
                        System.Diagnostics.Debug.WriteLine("背景构建失败: " + ex.Message);
                    }
                    catch (Exception)
                    {
                    }
                }

                BackgroundSource built = source;
                try
                {
                    BeginInvoke((MethodInvoker)delegate { FinishBackgroundBuild(generation, built); });
                }
                catch (Exception)
                {
                    if (built != null)
                    {
                        built.Dispose();
                    }
                }
            });
        }

        private void FinishBackgroundBuild(int generation, BackgroundSource built)
        {
            if (_closing || IsDisposed)
            {
                if (built != null)
                {
                    built.Dispose();
                }
                return;
            }

            if (generation != _bgGeneration)
            {
                // 期间又发起了一次构建，本次结果丢弃
                if (built != null)
                {
                    built.Dispose();
                }
                return;
            }

            if (_background != null)
            {
                _background.Dispose();
            }

            _background = built;
            _bgFrame = _background != null ? _background.GetFrame() : null;
            Invalidate();
        }

        private void BuildBackgroundSync(Size target)
        {
            if (_background != null)
            {
                _background.Dispose();
            }

            _background = new BackgroundSource();
            _background.Rebuild(target, _settings);
            _bgFrame = _background.GetFrame();
        }

        // ================================================================
        // 数据：天气 / 一言 / 播控
        // ================================================================

        private async void FetchWeatherAsync()
        {
            if (_closing || _dashboard == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(_settings.WeatherHost) || string.IsNullOrEmpty(_settings.WeatherKey))
            {
                // 传 null 而不是文字：卡片会显示"天气未配置，请在设置里填写 Host 与 Key"，
                // 比"天气获取失败"贴切 —— 这不是失败，只是还没配。
                _dashboard.Weather.SetError(null);
                InvalidateTile(_dashboard.Weather);
                return;
            }

            try
            {
                // Host 或 Key 任一变了都要重建，否则会拿着旧 Key 去请求
                string wantKey = (_settings.WeatherKey ?? string.Empty).Trim();
                if (_weatherService == null
                    || !string.Equals(_weatherService.ApiHost, NormalizeHost(_settings.WeatherHost), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_weatherService.ApiKey, wantKey, StringComparison.Ordinal))
                {
                    _weatherService = new QWeatherService(_settings.WeatherHost, _settings.WeatherKey);
                }

                WeatherData data = await _weatherService
                    .GetWeatherAsync(_settings.WeatherCity, _netCts.Token)
                    .ConfigureAwait(true);

                if (_closing || _dashboard == null)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(data.LocationId) && data.LocationId != _settings.WeatherLocationId)
                {
                    _settings.WeatherLocationId = data.LocationId;
                    try
                    {
                        _settings.Save();
                    }
                    catch (Exception)
                    {
                    }
                }

                _dashboard.Weather.SetData(data, _settings.WeatherCityDisplay);
                InvalidateTile(_dashboard.Weather);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (_closing || _dashboard == null)
                {
                    return;
                }
                _dashboard.Weather.SetError(Explain(ex));
                InvalidateTile(_dashboard.Weather);
            }
        }

        /// <summary>
        /// 取最内层异常的消息。
        /// TypeInitializationException / TargetInvocationException 这类包装异常，
        /// 外层 Message 全是套话，真正有用的原因只在 InnerException 里。
        /// </summary>
        private static string Explain(Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null)
            {
                inner = inner.InnerException;
            }
            return inner.Message;
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return string.Empty;
            }

            string h = host.Trim();
            int scheme = h.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                h = h.Substring(scheme + 3);
            }
            return h.TrimEnd('/');
        }

        private async void FetchHitokotoAsync()
        {
            if (_closing || _dashboard == null)
            {
                return;
            }

            try
            {
                Hitokoto hitokoto = await HitokotoService
                    .GetAsync(_settings.HitokotoCategories, _netCts.Token)
                    .ConfigureAwait(true);

                if (_closing || _dashboard == null)
                {
                    return;
                }

                _dashboard.Hitokoto.SetSentence(hitokoto);
                InvalidateTile(_dashboard.Hitokoto);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (_closing || _dashboard == null)
                {
                    return;
                }
                _dashboard.Hitokoto.SetError("一言获取失败：" + Explain(ex));
                InvalidateTile(_dashboard.Hitokoto);
            }
        }

        private void StartSmtc()
        {
            try
            {
                _smtc = new SmtcService();
                _dashboard.Media.AttachService(_smtc);
                _smtc.Updated += OnMediaUpdated;
                _smtc.Start(SynchronizationContext.Current, _settings.MediaRefreshSeconds);
            }
            catch (Exception ex)
            {
                var info = new MediaSessionInfo();
                info.Error = Explain(ex);
                _dashboard.Media.Update(info);
            }
        }

        private void OnMediaUpdated(MediaSessionInfo info)
        {
            if (_closing || _dashboard == null)
            {
                return;
            }

            _dashboard.Media.Update(info);
            InvalidateTile(_dashboard.Media);
        }

        // ================================================================
        // 设置
        // ================================================================

        private void ShowSettings()
        {
            if (_closing)
            {
                return;
            }

            AccentColor.InvalidateCache();

            // 记下与天气有关的设置，确定后据此判断要不要立刻重新拉一次
            string hostBefore = _settings.WeatherHost;
            string keyBefore = _settings.WeatherKey;
            string cityBefore = _settings.WeatherCity;
            bool celsiusBefore = _settings.WeatherUseCelsius;

            using (var dialog = new Settings(_settings))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK)
                {
                    return;
                }
            }

            if (_settings != null)
            {
                // 设置页改的就是 AppSettings.Current 这个实例，这里直接用
                AccentColor.InvalidateCache();
                ApplyAll();

                // 首次打开时没有 config\settings.ini，用户填完 Host/Key 点确定，
                // 这里必须立刻拉一次，否则要等定时器（默认 30 分钟）或手动点刷新。
                bool weatherChanged =
                    !string.Equals(hostBefore, _settings.WeatherHost, StringComparison.Ordinal)
                    || !string.Equals(keyBefore, _settings.WeatherKey, StringComparison.Ordinal)
                    || !string.Equals(cityBefore, _settings.WeatherCity, StringComparison.Ordinal)
                    || celsiusBefore != _settings.WeatherUseCelsius;

                if (weatherChanged || !_dashboard.Weather.HasData)
                {
                    FetchWeatherAsync();
                }
            }
        }

        /// <summary>设置变化后整体重应用。</summary>
        private void ApplyAll()
        {
            _theme = Theme.Create(_settings);
            IconFont.ClearCache();

            ApplyScreenBounds();
            _dashboard.Layout(ClientSize, _settings, DpiScale);
            _laidOutSize = ClientSize;

            if (_weatherTimer != null)
            {
                _weatherTimer.Interval = ClampMinutes(_settings.WeatherRefreshMinutes, 30) * 60000;
            }
            if (_hitokotoTimer != null)
            {
                _hitokotoTimer.Interval = ClampMinutes(_settings.HitokotoRefreshMinutes, 60) * 60000;
            }
            if (_smtc != null)
            {
                _smtc.SetRefreshSeconds(_settings.MediaRefreshSeconds);
            }

            StartBackgroundBuild();
            _dashboard.MarkAllDirty();
            Invalidate();
        }

        // ================================================================
        // 截图模式：渲染一张 PNG 后退出，用于核对界面
        // ================================================================

        private void RunCaptureMode()
        {
            var size = new Size(
                Math.Max(320, StartupOptions.CaptureWidth),
                Math.Max(240, StartupOptions.CaptureHeight));

            _dashboard.Layout(size, _settings, 1f);

            if (StartupOptions.Demo)
            {
                PopulateDemoData();
                BuildBackgroundSync(size);
            }
            else
            {
                BuildBackgroundSync(size);
                try
                {
                    if (!string.IsNullOrEmpty(_settings.WeatherHost) && !string.IsNullOrEmpty(_settings.WeatherKey))
                    {
                        var svc = new QWeatherService(_settings.WeatherHost, _settings.WeatherKey);
                        WeatherData data = svc.GetWeatherAsync(_settings.WeatherCity, _netCts.Token)
                            .GetAwaiter().GetResult();
                        _dashboard.Weather.SetData(data, _settings.WeatherCityDisplay);
                    }
                }
                catch (Exception ex)
                {
                    _dashboard.Weather.SetError(Explain(ex));
                }

                try
                {
                    Hitokoto h = HitokotoService.GetAsync(_settings.HitokotoCategories, _netCts.Token)
                        .GetAwaiter().GetResult();
                    _dashboard.Hitokoto.SetSentence(h);
                }
                catch (Exception ex)
                {
                    _dashboard.Hitokoto.SetError("一言获取失败：" + Explain(ex));
                }

                // 真实媒体会话也采一次，这样 --capture 出来的就是实际画面（不含网络数据）
                try
                {
                    _dashboard.Media.Update(SmtcService.CaptureOnce(8000));
                }
                catch (Exception ex)
                {
                    _dashboard.Media.Update(new MediaSessionInfo { Error = Explain(ex) });
                }
            }

            _dashboard.Clock.Update(DateTime.Now);
            _dashboard.Calendar.Tick();
            _dashboard.Media.RefreshVolume();

            using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(_settings.BackgroundColor);
                    PaintSceneTo(g, size);
                }

                string dir = System.IO.Path.GetDirectoryName(StartupOptions.CapturePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                bmp.Save(StartupOptions.CapturePath, ImageFormat.Png);
            }
        }

        /// <summary>截图模式下用指定的画布尺寸绘制（ClientSize 此时还是设计尺寸）。</summary>
        private void PaintSceneTo(Graphics g, Size size)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            Bitmap frame = _bgFrame;
            if (frame != null)
            {
                g.DrawImageUnscaled(frame, 0, 0);
            }
            else
            {
                using (var brush = new SolidBrush(_settings.BackgroundColor))
                {
                    g.FillRectangle(brush, new Rectangle(0, 0, size.Width, size.Height));
                }
            }

            if (_theme.Scrim.A > 0)
            {
                using (var brush = new SolidBrush(_theme.Scrim))
                {
                    g.FillRectangle(brush, new Rectangle(0, 0, size.Width, size.Height));
                }
            }

            _dashboard.Blit(g, _theme, _settings);
        }

        private void PopulateDemoData()
        {
            var now = new WeatherNow();
            now.ObsTime = DateTime.Now.ToString("s");
            now.Temp = 23;
            now.FeelsLike = 21;
            now.Icon = 100;
            now.Text = "晴";
            now.WindDir = "西北风";
            now.WindScale = "3";
            now.WindSpeed = 16;
            now.Humidity = 45;
            now.Precip = 0;
            now.Pressure = 1015;
            now.Vis = 16;
            now.Cloud = 10;
            now.Dew = 11;

            var daily = new System.Collections.Generic.List<WeatherDaily>();
            string[] texts = { "晴", "多云", "小雨" };
            int[] codes = { 100, 101, 305 };
            double[] maxs = { 26, 25, 22 };
            double[] mins = { 18, 17, 15 };
            for (int i = 0; i < 3; i++)
            {
                var d = new WeatherDaily();
                d.Date = DateTime.Today.AddDays(i);
                d.TempMax = maxs[i];
                d.TempMin = mins[i];
                d.IconDay = codes[i];
                d.TextDay = texts[i];
                d.IconNight = 150;
                d.TextNight = texts[i];
                d.Humidity = 45 + i * 5;
                d.UvIndex = 3;
                daily.Add(d);
            }

            var data = new WeatherData();
            data.CityName = "北京";
            data.LocationId = "101010100";
            data.FetchedAt = DateTime.Now;
            data.Now = now;
            data.Daily = daily;
            _dashboard.Weather.SetData(data, string.Empty);

            var h = new Hitokoto();
            h.Text = "生活不止眼前的苟且，还有诗和远方的田野。";
            h.From = "生活不止眼前的苟且";
            h.FromWho = "高晓松";
            h.Uuid = "demo";
            h.FetchedAt = DateTime.Now;
            _dashboard.Hitokoto.SetSentence(h);

            var media = new MediaSessionInfo();
            media.HasSession = true;
            media.SourceAppId = "Spotify.exe";
            media.Title = "夜航西飞";
            media.Artist = "陈粒";
            media.AlbumTitle = "洄游";
            media.IsPlaying = true;
            media.NewThumbnail = MakeDemoCover(512);
            _dashboard.Media.Update(media);
        }

        private static Bitmap MakeDemoCover(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, size, size),
                Color.FromArgb(0x2B, 0x4C, 0x7E),
                Color.FromArgb(0xC2, 0x6B, 0x8A),
                35f))
            {
                g.FillRectangle(brush, 0, 0, size, size);

                using (var soft = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                {
                    g.FillEllipse(soft, size * 0.16f, size * 0.16f, size * 0.34f, size * 0.34f);
                }
                using (var soft = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                {
                    g.FillEllipse(soft, size * 0.52f, size * 0.5f, size * 0.4f, size * 0.4f);
                }

                // 一个简单的音符标记，让占位封面看得出是专辑图
                Draw.Icon(g, Icons.Music, size * 0.3f, Color.FromArgb(200, 255, 255, 255),
                    new RectangleF(0, 0, size, size));
            }
            return bmp;
        }
    }
}
