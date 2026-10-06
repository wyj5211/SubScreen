using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SubScreen
{
    internal static class Program
    {
        private const string SingleInstanceName = "SubScreen.SingleInstance.{4E2C6B1A-9D5F-4C77-9A31-2B8E5F0C7D31}";

        private static bool _traceEnabled;

        /// <summary>
        /// 启动阶段逐步打点。只在截图模式或带 --trace 时写文件，
        /// 正常使用时不会在程序目录留下日志。
        /// </summary>
        internal static void Trace(string step)
        {
            if (!_traceEnabled)
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        private static bool WantsTrace(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string a in args)
            {
                if (a == null)
                {
                    continue;
                }
                if (a.StartsWith("--capture", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--trace", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            _traceEnabled = WantsTrace(args);
            Trace("Main enter");

            // DPI 感知由 App.config 的 System.Windows.Forms.ApplicationConfigurationSection
            // 声明为 PerMonitorV2，WinForms 初始化时会自动应用。
            Application.EnableVisualStyles();
            Trace("EnableVisualStyles done");

            Application.SetCompatibleTextRenderingDefault(false);
            Trace("SetCompatibleTextRenderingDefault done");

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            MainOptions options = ParseArgs(args);
            SubScreen.Main.StartupOptions = options;
            Trace("args parsed, capture=" + options.CapturePath);

            // 离线自检：不创建窗口，跑完写报告直接退出
            if (!string.IsNullOrEmpty(options.SelfTestPath))
            {
                RunSelfTest(options.SelfTestPath);
                return;
            }

            // 截图模式是离线自检用的，不参与单实例限制
            if (!string.IsNullOrEmpty(options.CapturePath))
            {
                Trace("capture mode: Application.Run");
                Application.Run(new SubScreen.Main());
                Trace("capture mode: Application.Run returned");
                return;
            }

            Trace("normal mode: acquiring mutex");
            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经有一个实例在跑了。把它叫到前面来，然后自己退出。
                    Trace("another instance is running");
                    TryActivateExisting();
                    return;
                }

                Trace("normal mode: Application.Run");
                Application.Run(new SubScreen.Main());
            }
        }

        // ---------------- 离线自检 ----------------

        /// <summary>
        /// 离线自检：不创建窗口、不联网，检查那些"光看界面看不出来"的集成点。
        /// 主要价值是让配置持久化、农历、字体、背景合成这些基础能力可以被重复验证。
        /// </summary>
        private static void RunSelfTest(string outPath)
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            Action<bool, string> check = delegate (bool ok, string name)
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("PASS  " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("FAIL  " + name);
                }
            };

            sb.AppendLine("SubScreen 离线自检");
            sb.AppendLine("时间 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("exe  : " + Application.ExecutablePath);
            sb.AppendLine("配置 : " + Core.AppPaths.SettingsFile);
            sb.AppendLine();

            sb.AppendLine("--- 1. 设置持久化（config\\settings.ini）---");
            try
            {
                Core.AppPaths.EnsureCreated();
                Core.AppSettings original = Core.AppSettings.Load();
                original.Save(); // 先确保文件存在

                Core.AppSettings probe = original.Clone();
                probe.Theme = Core.AppTheme.Light;
                probe.BackgroundBlur = 42;
                probe.BackgroundBrightness = -37;
                probe.BackgroundFit = Core.ImageFitMode.Tile;
                probe.CardOpacity = 66;
                probe.CornerRadius = 17;
                probe.CustomAccent = Color.FromArgb(0x12, 0x34, 0x56);
                probe.AccentMode = Core.AccentMode.Custom;
                probe.WeatherCity = "测试城市·北京";
                probe.WeatherRefreshMinutes = 123;
                probe.HitokotoCategories = "d&i&k";
                probe.MediaRefreshSeconds = 7;
                probe.ShowSolarTerm = false;
                probe.ClockUse24Hour = false;
                probe.ScreenChoice = Core.ScreenChoice.Primary;
                probe.Save();

                string firstPass = File.ReadAllText(Core.AppPaths.SettingsFile, Encoding.UTF8);

                Core.AppSettings.Invalidate();
                Core.AppSettings reloaded = Core.AppSettings.Load();

                check(reloaded.Theme == Core.AppTheme.Light, "Theme 往返");
                check(reloaded.BackgroundBlur == 42, "BackgroundBlur 往返（实际 " + reloaded.BackgroundBlur + "）");
                check(reloaded.BackgroundBrightness == -37, "BackgroundBrightness 负值往返（实际 " + reloaded.BackgroundBrightness + "）");
                check(reloaded.BackgroundFit == Core.ImageFitMode.Tile, "BackgroundFit 枚举往返");
                check(reloaded.CardOpacity == 66, "CardOpacity 往返");
                check(reloaded.CornerRadius == 17, "CornerRadius 往返");
                check(reloaded.CustomAccent.R == 0x12 && reloaded.CustomAccent.G == 0x34 && reloaded.CustomAccent.B == 0x56,
                    "强调色 #RRGGBB 往返（实际 " + Core.ColorUtil.ToHex(reloaded.CustomAccent) + "）");
                check(reloaded.AccentMode == Core.AccentMode.Custom, "AccentMode 往返");
                check(reloaded.WeatherCity == "测试城市·北京", "中文城市名往返（实际 " + reloaded.WeatherCity + "）");
                check(reloaded.WeatherRefreshMinutes == 123, "WeatherRefreshMinutes 往返");
                check(reloaded.MediaRefreshSeconds == 7, "MediaRefreshSeconds 往返");
                check(!reloaded.ShowSolarTerm, "bool 往返");
                check(!reloaded.ClockUse24Hour, "bool 往返（第二个）");
                check(reloaded.ScreenChoice == Core.ScreenChoice.Primary, "ScreenChoice 往返");

                // 写-读-写 必须幂等，否则说明有字段被静默改写
                reloaded.Save();
                string secondPass = File.ReadAllText(Core.AppPaths.SettingsFile, Encoding.UTF8);
                check(firstPass == secondPass, "保存-读取-再保存 内容完全一致（幂等）");

                // 越界值要被夹紧而不是原样落盘
                Core.AppSettings clamp = reloaded.Clone();
                clamp.CardOpacity = 999;
                clamp.BackgroundBlur = -50;
                clamp.WeatherRefreshMinutes = 99999;
                clamp.Save();
                Core.AppSettings.Invalidate();
                Core.AppSettings clamped = Core.AppSettings.Load();
                check(clamped.CardOpacity <= 100 && clamped.CardOpacity >= 0, "CardOpacity 越界被夹紧（实际 " + clamped.CardOpacity + "）");
                check(clamped.BackgroundBlur >= 0, "BackgroundBlur 越界被夹紧（实际 " + clamped.BackgroundBlur + "）");
                check(clamped.WeatherRefreshMinutes <= 1440, "WeatherRefreshMinutes 越界被夹紧（实际 " + clamped.WeatherRefreshMinutes + "）");

                // 还原成进来时的样子
                Core.AppSettings.Invalidate();
                original.Save();
                check(true, "（已还原原始设置）");
            }
            catch (Exception ex)
            {
                check(false, "设置持久化异常：" + ex.GetType().Name + " " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 2. 农历 / 节日 / 节气 ---");
            try
            {
                var cny = SubScreen.Calendar.LunarCalendarService.Get(new DateTime(2025, 1, 29));
                check(cny != null, "2025-01-29 农历可解析");
                if (cny != null)
                {
                    check(cny.LunarMonth == 1 && cny.LunarDay == 1, "2025-01-29 = 正月初一（实际 " + cny.ShortText + "）");
                    check(cny.GanZhiYear == "乙巳", "干支 = 乙巳（实际 " + cny.GanZhiYear + "）");
                    check(cny.Zodiac == "蛇", "生肖 = 蛇（实际 " + cny.Zodiac + "）");
                }

                check(SubScreen.Calendar.ChineseHolidays.GetFestival(new DateTime(2025, 10, 1)) == "国庆节", "2025-10-01 = 国庆节");
                check(SubScreen.Calendar.ChineseHolidays.GetFestival(new DateTime(2026, 4, 5)) == "清明节", "2026-04-05 = 清明节");
                check(SubScreen.Calendar.ChineseHolidays.GetFestival(new DateTime(2025, 1, 28)) == "除夕", "2025-01-28 = 除夕");
                check(SubScreen.Calendar.ChineseHolidays.GetSolarTerm(new DateTime(2025, 12, 21)) == "冬至", "2025-12-21 = 冬至");
            }
            catch (Exception ex)
            {
                check(false, "农历模块异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 3. 图标字体 ---");
            try
            {
                check(Core.IconFont.FluentFamily == "Segoe Fluent Icons",
                    "界面图标字体 = Segoe Fluent Icons（实际 " + Core.IconFont.FluentFamily + "）");
                check(Core.IconFont.SymbolFamily == "Segoe UI Symbol",
                    "天气符号字体 = Segoe UI Symbol（实际 " + Core.IconFont.SymbolFamily + "）");
                check(!string.IsNullOrEmpty(Core.IconFont.TextFamily), "中文正文字体可用（" + Core.IconFont.TextFamily + "）");
            }
            catch (Exception ex)
            {
                check(false, "字体解析异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 4. 强调色 ---");
            try
            {
                Color accent = Core.AccentColor.GetSystemAccent();
                check(accent.R + accent.G + accent.B > 0, "读到系统强调色 " + Core.ColorUtil.ToHex(accent));
                Color fixedUp = Core.AccentColor.AdjustForTheme(Color.FromArgb(10, 10, 40), Core.AppTheme.Dark);
                check(Core.ColorUtil.Luminance(fixedUp) > Core.ColorUtil.Luminance(Color.FromArgb(10, 10, 40)),
                    "深色主题下过暗的强调色会被提亮（" + Core.ColorUtil.ToHex(fixedUp) + "）");
            }
            catch (Exception ex)
            {
                check(false, "强调色读取异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 5. 文字/图标不会被 GDI+ 整行丢弃（回归检查）---");
            try
            {
                using (var bmp = new Bitmap(240, 120, PixelFormat.Format32bppPArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);

                    // 故意给一个比行高矮得多的矩形
                    using (Font f = Core.IconFont.GetText(40f, FontStyle.Regular))
                    {
                        Core.Draw.Text(g, "测试ABC", f, Color.White, new RectangleF(0, 0, 240, 18), Core.Draw.LeftCenter);
                    }
                    using (Font f = Core.IconFont.GetIcon(Core.IconFontKind.Symbol, 46f))
                    {
                        Core.Draw.Icon(g, Core.Icons.Cloud, 46f, Color.White, new RectangleF(0, 60, 60, 60), Core.Draw.Center);
                    }

                    int lit = 0;
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            if (bmp.GetPixel(x, y).R > 100)
                            {
                                lit++;
                            }
                        }
                    }
                    check(lit > 100, "过矮矩形里文字与图标仍被绘制（亮像素 " + lit + " 个）");
                }
            }
            catch (Exception ex)
            {
                check(false, "绘制回归检查异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 6. 背景合成管线 ---");
            try
            {
                Core.AppSettings s = Core.AppSettings.Current;
                sb.AppendLine("      设置里背景类型 = " + s.BackgroundKind + "，文件 = " + s.BackgroundFileName);
                sb.AppendLine("      磁盘上的背景文件 = " + (Core.AppPaths.FindBackgroundFile() ?? "<无>"));

                if (s.BackgroundKind == Core.BackgroundKind.Image)
                {
                    string file = Core.AppPaths.FindBackgroundFile();
                    if (file != null)
                    {
                        using (var src = new SubScreen.Background.BackgroundSource())
                        {
                            src.Rebuild(new Size(320, 180), s);
                            check(!src.IsSolid, "背景被识别为静态图片（" + (src.Error ?? "无错误") + "）");
                            Bitmap frame = src.GetFrame();
                            check(frame != null && frame.Width == 320 && frame.Height == 180,
                                "合成帧尺寸 = " + (frame == null ? "<null>" : frame.Width + "x" + frame.Height));

                            // 纯色背景必须返回 null（调用方直接用 SolidColor 填充）
                            Core.AppSettings solid = s.Clone();
                            solid.BackgroundKind = Core.BackgroundKind.SolidColor;
                            using (var src2 = new SubScreen.Background.BackgroundSource())
                            {
                                src2.Rebuild(new Size(320, 180), solid);
                                check(src2.IsSolid && src2.GetFrame() == null, "纯色背景走纯色分支");
                            }
                        }
                    }
                    else
                    {
                        check(false, "设置里指定了背景图但磁盘上找不到");
                    }
                }
                else
                {
                    check(true, "当前是纯色背景，跳过图片合成检查");
                }
            }
            catch (Exception ex)
            {
                check(false, "背景合成异常：" + ex.GetType().Name + " " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 7. App.config 可被读取（内置配置节不得重复声明）---");
            try
            {
                // HttpClientHandler 默认会去读 <system.net> 配置，从而触发整个配置系统的解析。
                // 之前误在 <configSections> 里声明了内置节，这一步就会抛
                // "节是内置节，它不能由用户声明"，连带所有网络功能失效。
                using (var handler = new System.Net.Http.HttpClientHandler())
                {
                    check(handler != null, "创建 HttpClientHandler 成功（说明 App.config 合法）");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                }
                check(false, "App.config 有问题：" + inner.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 8. WinRT 异步层（SMTC 的命门）---");
            try
            {
                Media.WinRt.Initialize();
                check(true, "RoInitialize 成功");

                int err;
                IntPtr mgr = Media.WinRt.InvokeStaticsAsync(
                    "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager",
                    new Guid("2050C4EE-11A0-57DE-AED7-C97C70338245"),
                    6,
                    10000,
                    out err);

                // 这一条就是关键回归：如果异步状态是在 IAsyncOperation 上而不是
                // QI 出来的 IAsyncInfo 上读，get_Completed 会返回 null 被当成
                // AsyncStatus=0(Started)，这里就会稳定地"超时"。
                check(mgr != IntPtr.Zero,
                    "RequestAsync 完成（" + Media.WinRt.DescribeHResult(err) + "）");

                if (mgr != IntPtr.Zero)
                {
                    try
                    {
                        var getCurrent = Media.WinRt.GetMethod<Media.WinRt.GetObjectResultFn>(mgr, 6);
                        IntPtr session;
                        if (getCurrent(mgr, out session) >= 0 && session != IntPtr.Zero)
                        {
                            try
                            {
                                check(true, "当前媒体会话 = " + Media.WinRt.GetStringProperty(session, 6));
                            }
                            finally
                            {
                                Media.WinRt.Release(session);
                            }
                        }
                        else
                        {
                            check(true, "当前没有媒体会话（不算失败）");
                        }
                    }
                    finally
                    {
                        Media.WinRt.Release(mgr);
                    }
                }
            }
            catch (Exception ex)
            {
                check(false, "WinRT 异步层异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("--- 9. 系统音量（Core Audio / IAudioEndpointVolume）---");
            try
            {
                int vol;
                if (Core.SystemVolume.TryGetVolume(out vol))
                {
                    check(vol >= 0 && vol <= 100, "读到主音量 " + vol + "%");
                }
                else
                {
                    check(false, "读取主音量失败（没有默认播放设备？）");
                }

                bool muted;
                check(Core.SystemVolume.TryGetMute(out muted), "读到静音状态 = " + muted);
                check(Math.Abs(Core.SystemVolume.PercentToScalar(50) - 0.5f) < 0.001f, "PercentToScalar(50) = 0.5");
                check(Core.SystemVolume.PercentToScalar(-10) == 0f, "PercentToScalar 下界钳制");
                check(Core.SystemVolume.PercentToScalar(500) == 1f, "PercentToScalar 上界钳制");
                check(Core.SystemVolume.ScalarToPercent(0.37f) == 37, "ScalarToPercent(0.37) = 37");
            }
            catch (Exception ex)
            {
                check(false, "音量模块异常：" + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("================ 通过 " + pass + " 项，失败 " + fail + " 项 ================");

            try
            {
                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        private static void TryActivateExisting()
        {
            try
            {
                System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id == current.Id)
                    {
                        continue;
                    }

                    IntPtr hwnd = p.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        Core.NativeMethods.SetForegroundWindow(hwnd);
                    }
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        private static MainOptions ParseArgs(string[] args)
        {
            var options = new MainOptions();
            if (args == null)
            {
                return options;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null)
                {
                    continue;
                }

                string value = null;
                int eq = a.IndexOf('=');
                string key = a;
                if (a.StartsWith("--", StringComparison.Ordinal) && eq > 0)
                {
                    key = a.Substring(0, eq);
                    value = a.Substring(eq + 1);
                }

                switch (key.ToLowerInvariant())
                {
                    case "--capture":
                        options.CapturePath = value ?? Next(args, ref i);
                        break;

                    case "--demo":
                        options.Demo = true;
                        break;

                    case "--size":
                        string size = value ?? Next(args, ref i);
                        ParseSize(size, options);
                        break;

                    case "--selftest":
                        options.SelfTestPath = value ?? Next(args, ref i);
                        break;
                }
            }

            return options;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 < args.Length)
            {
                i++;
                return args[i];
            }
            return null;
        }

        private static void ParseSize(string text, MainOptions options)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string[] parts = text.ToLowerInvariant().Split('x');
            int w;
            int h;
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out w)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)
                && w > 0 && h > 0)
            {
                options.CaptureWidth = w;
                options.CaptureHeight = h;
            }
        }

        // ---------------- 未处理异常：写日志 + 提示，避免界面直接消失 ----------------

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportCrash(e != null ? e.Exception : null);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportCrash(e != null ? e.ExceptionObject as Exception : null);
        }

        private static void ReportCrash(Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            // 先把日志写出来。这里刻意不用 Core.AppPaths，
            // 因为崩溃有可能就发生在初始化那些目录的时候。
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                sb.AppendLine("CapturePath=" + (SubScreen.Main.StartupOptions != null ? SubScreen.Main.StartupOptions.CapturePath : null));
                sb.AppendLine(ex.ToString());
                sb.AppendLine();
                File.AppendAllText(Path.Combine(dir, "crash.log"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }

            try
            {
                Core.AppPaths.EnsureCreated();
                string path = Path.Combine(Core.AppPaths.CacheDir, "crash.log");
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                sb.AppendLine(ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }

            // 截图模式是给自动化核对用的，弹窗会把进程挂住
            bool silent = SubScreen.Main.StartupOptions != null && !string.IsNullOrEmpty(SubScreen.Main.StartupOptions.CapturePath);
            if (silent)
            {
                return;
            }

            try
            {
                MessageBox.Show(
                    "程序遇到一个未处理的错误：\r\n\r\n" + ex.Message +
                    "\r\n\r\n详细信息已写入 config\\cache\\crash.log",
                    "SubScreen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception)
            {
            }
        }
    }
}
