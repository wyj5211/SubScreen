using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SubScreen.Core;

namespace SubScreen
{
    /// <summary>
    /// 设置窗体。所有设置项保存到 exe 同级 config\settings.ini。
    ///
    /// 编辑的是一份草稿（AppSettings.Clone），只有点"确定"才会写回并落盘，
    /// 所以"取消"天然不会留下任何副作用（背景文件替换也推迟到确定时才做）。
    /// </summary>
    public partial class Settings : Form
    {
        private AppSettings _target;
        private AppSettings _draft;

        /// <summary>用户新选的背景图源路径；确定时才复制进 config\background。</summary>
        private string _pendingImagePath;
        private bool _clearBackground;

        // 背景
        private ComboBox _bgKind;
        private Button _bgColor;
        private TextBox _bgFile;
        private Button _bgBrowse;
        private Button _bgClear;
        private TrackBar _bgBrightness;
        private TrackBar _bgBlur;
        private ComboBox _bgFit;
        private TrackBar _bgDim;

        // 外观
        private ComboBox _theme;
        private TrackBar _cardOpacity;
        private ComboBox _accentMode;
        private Button _accentColor;
        private TrackBar _cornerRadius;
        private TrackBar _columnRatio;
        private TrackBar _tileGap;
        private TrackBar _tilePadding;
        private TrackBar _screenMargin;

        // 天气
        private TextBox _weatherHost;
        private TextBox _weatherKey;
        private TextBox _weatherCity;
        private Label _weatherLocationId;
        private NumericUpDown _weatherRefresh;
        private ComboBox _weatherUnit;

        // 内容
        private NumericUpDown _hitokotoRefresh;
        private TextBox _hitokotoCategories;
        private NumericUpDown _mediaRefresh;
        private ComboBox _weekStart;
        private CheckBox _showLunar;
        private CheckBox _showSolarTerm;
        private CheckBox _showHoliday;

        // 常规
        private ComboBox _screenChoice;
        private ComboBox _screenDevice;
        private CheckBox _coverTaskbar;
        private CheckBox _autoStart;
        private CheckBox _clockSeconds;
        private CheckBox _clock24;

        private Label _hint;
        private bool _ready;

        /// <summary>
        /// 无参构造：Visual Studio 设计器要求窗体有公开的无参构造，并且会在设计进程里真的执行它。
        /// 所以这里除了 InitializeComponent() 之外什么都不做 —— 文件 IO、读屏幕列表、
        /// 代码建控件树这些一律推迟到 <see cref="Prepare"/>，设计时直接跳过。
        /// </summary>
        public Settings()
        {
            InitializeComponent();

            if (IsDesignTime)
            {
                Text = "SubScreen 设置（界面由 Settings.cs 代码构建，设计器里不显示控件）";
                ClientSize = new Size(560, 120);
                return;
            }

            Prepare(AppSettings.Current);
        }

        // AppSettings 是 internal，所以这个重载只能是 internal
        internal Settings(AppSettings settings)
        {
            InitializeComponent();

            if (IsDesignTime)
            {
                return;
            }

            Prepare(settings ?? AppSettings.Current);
        }

        /// <summary>
        /// LicenseManager.UsageMode 在设计器进程里是 Designtime，正常运行时时 Runtime。
        /// 这是判断"当前是否在设计器里"最可靠的方式（比 DesignMode 属性早可用）。
        /// </summary>
        private static bool IsDesignTime
        {
            get { return LicenseManager.UsageMode == LicenseUsageMode.Designtime; }
        }

        private void Prepare(AppSettings settings)
        {
            _target = settings;
            _draft = _target.Clone();
            BuildUi();
            LoadValues();
            _ready = true;
        }

        // ================================================================
        // 界面构建
        // ================================================================

        private void BuildUi()
        {
            SuspendLayout();

            Text = "SubScreen 设置";
            Font = new Font(IconFont.TextFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(760, 660);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SystemColors.Window;

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;

            tabs.TabPages.Add(BuildBackgroundTab());
            tabs.TabPages.Add(BuildAppearanceTab());
            tabs.TabPages.Add(BuildWeatherTab());
            tabs.TabPages.Add(BuildContentTab());
            tabs.TabPages.Add(BuildGeneralTab());

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 58;
            bottom.Padding = new Padding(14, 10, 14, 10);

            _hint = new Label();
            _hint.AutoSize = true;
            _hint.ForeColor = SystemColors.GrayText;
            _hint.Location = new Point(16, 20);
            _hint.Text = "设置保存在 config\\settings.ini，点“确定”后写入";

            // 按钮一律用系统样式（FlatStyle.System 交给 Windows 绘制），不做任何外观定制
            var reset = MakeDialogButton("恢复默认");
            reset.Click += delegate { ResetToDefaults(); };

            var ok = MakeDialogButton("确定");
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate { Commit(); };

            var cancel = MakeDialogButton("取消");
            cancel.DialogResult = DialogResult.Cancel;

            // 右下角对齐
            bottom.Resize += delegate
            {
                int right = bottom.ClientSize.Width - 14;
                cancel.Location = new Point(right - cancel.Width, 14);
                ok.Location = new Point(cancel.Left - ok.Width - 8, 14);
                reset.Location = new Point(ok.Left - reset.Width - 24, 14);
            };

            bottom.Controls.Add(_hint);
            bottom.Controls.Add(reset);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(bottom);

            AcceptButton = ok;
            CancelButton = cancel;

            ResumeLayout(false);
        }

        /// <summary>
        /// 对话框按钮：交给 Windows 绘制（FlatStyle.System），不设置任何颜色/边框，
        /// 大小按文字自动伸缩，只保证一个统一的最小宽度。
        /// </summary>
        private static Button MakeDialogButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.System;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(84, 28);
            b.Padding = new Padding(6, 0, 6, 0);
            return b;
        }

        /// <summary>文档链接：用系统链接样式（LinkLabel 默认配色 + 手型光标）。</summary>
        private static LinkLabel MakeDocLink(string text, string url)
        {
            var link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.Cursor = Cursors.Hand;
            // 明确指定整段文字都是链接区域，不依赖默认值
            link.LinkArea = new LinkArea(0, text.Length);
            link.LinkClicked += delegate { OpenUrl(url); };
            return link;
        }

        /// <summary>
        /// 用默认浏览器打开网址。
        ///
        /// 就这么简单，**不要再加任何"加固"**：
        ///   - 不要挪到后台线程去（实测从后台线程启动浏览器会失败）
        ///   - 不要 ProcessStartInfo + WaitForExit 去"验证"是否真的起来了
        ///     （浏览器"把网址交给已有实例后自己退出"是正常行为，按退出码判定会误判成失败，
        ///      然后弹框、并且再也不打开浏览器）
        ///   - 不要串 explorer.exe 兜底
        /// 这些都是我加过又回退的，加一次坏一次。
        /// </summary>
        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception ex)
            {
                // 不静默失败：打不开就把地址显示出来，可以手动复制
                MessageBox.Show(
                    "无法打开浏览器：" + Environment.NewLine + ex.Message
                    + Environment.NewLine + Environment.NewLine
                    + "请手动复制访问：" + Environment.NewLine + url,
                    "SubScreen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// 设置页的表格：Dock 用 Top + AutoSize，让它自己长到内容高度，
        /// 由 TabPage 负责滚动。之前用 Dock=Fill + AutoScroll，
        /// 内容高度接近视口时行位置会被算错，出现莫名的空白行。
        /// </summary>
        private static TableLayoutPanel NewTable()
        {
            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.Padding = new Padding(16, 14, 16, 14);
            t.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            return t;
        }

        private static TabPage NewPage(string title)
        {
            var page = new TabPage(title);
            page.AutoScroll = true;
            page.UseVisualStyleBackColor = true;
            return page;
        }

        private static int AddRow(TableLayoutPanel t, string label, Control control)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(3, 9, 3, 3);

            control.Margin = new Padding(3, 5, 3, 5);
            // LinkLabel 不能同时 AutoSize + 左右拉伸：会被拉到整格宽度（实测 546px），
            // 而链接区域只有文字那么宽（348px），右边那一截点了没反应。
            control.Anchor = (control is LinkLabel)
                ? AnchorStyles.Left
                : AnchorStyles.Left | AnchorStyles.Right;

            t.Controls.Add(l, 0, row);
            t.Controls.Add(control, 1, row);
            return row;
        }

        /// <summary>
        /// 小节标题：加粗 + 系统蓝色。
        ///
        /// 字体必须基于窗体自己的字体（微软雅黑 UI）来加粗。
        /// Label 在加入父容器之前，Font 拿到的是 Control.DefaultFont（Microsoft Sans Serif），
        /// 那个字体没有中文字形，渲染时会回退成宋体 —— 标题显示成宋体就是这个原因。
        /// </summary>
        private void AddHeader(TableLayoutPanel t, string text)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font(Font, FontStyle.Bold);
            l.ForeColor = SystemColors.HotTrack;
            l.Margin = new Padding(3, 14, 3, 2);

            t.Controls.Add(l, 0, row);
            t.SetColumnSpan(l, 2);
        }

        private static Control MakeSlider(int min, int max, int value, out TrackBar barOut, out Label labelOut)
        {
            var host = new TableLayoutPanel();
            host.ColumnCount = 2;
            host.RowCount = 1;
            host.Height = 32;
            host.Margin = new Padding(0);
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54f));

            // 注意：lambda 只能捕获局部变量，不能捕获 out 参数，
            // 所以这里先用局部变量建好，最后再赋给 out。
            var bar = new TrackBar();
            bar.Minimum = min;
            bar.Maximum = max;
            bar.TickStyle = TickStyle.None;
            bar.AutoSize = false;
            bar.Height = 30;
            bar.Dock = DockStyle.Fill;
            bar.Value = Math.Min(max, Math.Max(min, value));

            var label = new Label();
            label.Text = bar.Value.ToString(CultureInfo.InvariantCulture);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleRight;

            bar.ValueChanged += delegate
            {
                label.Text = bar.Value.ToString(CultureInfo.InvariantCulture);
            };

            host.Controls.Add(bar, 0, 0);
            host.Controls.Add(label, 1, 0);

            barOut = bar;
            labelOut = label;
            return host;
        }

        private static ComboBox MakeCombo()
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = 260;
            return c;
        }

        private sealed class ComboItem
        {
            public string Text;
            public object Value;
            public override string ToString() { return Text; }
        }

        private static void Fill(ComboBox combo, params object[] pairs)
        {
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                var item = new ComboItem();
                item.Text = Convert.ToString(pairs[i]);
                item.Value = pairs[i + 1];
                combo.Items.Add(item);
            }
        }

        private static void SelectValue(ComboBox combo, object value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var item = combo.Items[i] as ComboItem;
                if (item == null)
                {
                    continue;
                }
                if (item.Value is Enum && value is Enum)
                {
                    if (Convert.ToInt32(item.Value) == Convert.ToInt32(value))
                    {
                        combo.SelectedIndex = i;
                        return;
                    }
                }
                else if (Equals(item.Value, value))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static object SelectedValue(ComboBox combo)
        {
            var item = combo.SelectedItem as ComboItem;
            return item != null ? item.Value : null;
        }

        private static Button MakeColorButton(Color color)
        {
            var b = new Button();
            b.Width = 120;
            b.Height = 28;
            b.FlatStyle = FlatStyle.Flat;
            b.Text = ColorUtil.ToHex(color);
            b.BackColor = color;
            b.ForeColor = ColorUtil.Luminance(color) > 0.55 ? Color.Black : Color.White;
            b.Tag = color;
            return b;
        }

        private static void SetColorButton(Button b, Color color)
        {
            b.Tag = color;
            b.Text = ColorUtil.ToHex(color);
            b.BackColor = color;
            b.ForeColor = ColorUtil.Luminance(color) > 0.55 ? Color.Black : Color.White;
        }

        private static void WireColorButton(Button b, Action<Color> onPicked)
        {
            b.Click += delegate
            {
                Color current = b.Tag is Color ? (Color)b.Tag : Color.White;
                using (var dlg = new ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.Color = current;
                    if (dlg.ShowDialog(b.FindForm()) == DialogResult.OK)
                    {
                        SetColorButton(b, dlg.Color);
                        if (onPicked != null)
                        {
                            onPicked(dlg.Color);
                        }
                    }
                }
            };
        }

        // ---------------- 各个选项卡 ----------------

        private TabPage BuildBackgroundTab()
        {
            var page = NewPage("背景");
            var t = NewTable();

            AddHeader(t, "背景来源");

            _bgKind = MakeCombo();
            Fill(_bgKind, "纯色", BackgroundKind.SolidColor, "图片", BackgroundKind.Image);
            AddRow(t, "背景类型", _bgKind);

            _bgColor = MakeColorButton(_draft.BackgroundColor);
            WireColorButton(_bgColor, delegate (Color c) { _draft.BackgroundColor = c; });
            AddRow(t, "纯色颜色", _bgColor);

            var fileHost = new TableLayoutPanel();
            fileHost.ColumnCount = 3;
            fileHost.RowCount = 1;
            fileHost.Height = 32;
            fileHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            fileHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            fileHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));

            _bgFile = new TextBox();
            _bgFile.ReadOnly = true;
            _bgFile.Dock = DockStyle.Fill;

            _bgBrowse = new Button();
            _bgBrowse.Text = "浏览…";
            _bgBrowse.Dock = DockStyle.Fill;
            _bgBrowse.Click += OnBrowseBackground;

            _bgClear = new Button();
            _bgClear.Text = "清除";
            _bgClear.Dock = DockStyle.Fill;
            _bgClear.Click += delegate
            {
                _pendingImagePath = null;
                _clearBackground = true;
                _bgFile.Text = string.Empty;
                SelectValue(_bgKind, BackgroundKind.SolidColor);
            };

            fileHost.Controls.Add(_bgFile, 0, 0);
            fileHost.Controls.Add(_bgBrowse, 1, 0);
            fileHost.Controls.Add(_bgClear, 2, 0);
            AddRow(t, "背景图片", fileHost);

            AddHeader(t, "背景效果");

            Control host;
            host = MakeSlider(-100, 100, _draft.BackgroundBrightness, out _bgBrightness, out _);
            AddRow(t, "亮度", host);

            host = MakeSlider(0, 100, _draft.BackgroundBlur, out _bgBlur, out _);
            AddRow(t, "模糊度", host);

            _bgFit = MakeCombo();
            Fill(_bgFit,
                "填充（裁切铺满）", ImageFitMode.Fill,
                "适应（完整可见）", ImageFitMode.Fit,
                "拉伸（不保持比例）", ImageFitMode.Stretch,
                "平铺", ImageFitMode.Tile,
                "居中（原始大小）", ImageFitMode.Center,
                "缩放（只放大）", ImageFitMode.Zoom);
            AddRow(t, "图片适应模式", _bgFit);

            host = MakeSlider(0, 100, _draft.BackgroundDim, out _bgDim, out _);
            AddRow(t, "遮罩", host);

            var note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(600, 0);
            note.ForeColor = SystemColors.GrayText;
            note.Text = "只支持静态图片（jpg / png / bmp / tif 等）。\r\n" +
                        "新背景会在点“确定”后复制到 config\\background\\bg.扩展名，" +
                        "并删除该目录下其它 bg.* 残留（切换背景不会累积文件）。";
            AddRow(t, string.Empty, note);

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildAppearanceTab()
        {
            var page = NewPage("外观");
            var t = NewTable();

            AddHeader(t, "主题与强调色");

            _theme = MakeCombo();
            Fill(_theme, "深色模式", AppTheme.Dark, "浅色模式", AppTheme.Light);
            AddRow(t, "主题", _theme);

            _accentMode = MakeCombo();
            Fill(_accentMode, "跟随 Windows 强调色", AccentMode.System, "自定义", AccentMode.Custom);
            AddRow(t, "强调色来源", _accentMode);

            _accentColor = MakeColorButton(_draft.CustomAccent);
            WireColorButton(_accentColor, delegate (Color c) { _draft.CustomAccent = c; });
            AddRow(t, "自定义强调色", _accentColor);

            Control host = MakeSlider(0, 100, _draft.CardOpacity, out _cardOpacity, out _);
            AddRow(t, "卡片不透明度", host);

            AddHeader(t, "布局");

            host = MakeSlider(0, 40, _draft.CornerRadius, out _cornerRadius, out _);
            AddRow(t, "卡片圆角", host);

            host = MakeSlider(20, 80, _draft.ColumnRatio, out _columnRatio, out _);
            AddRow(t, "左右分栏比例（左列 %）", host);

            host = MakeSlider(0, 60, _draft.TileGap, out _tileGap, out _);
            AddRow(t, "卡片间距", host);

            host = MakeSlider(0, 80, _draft.TilePadding, out _tilePadding, out _);
            AddRow(t, "卡片内边距", host);

            host = MakeSlider(0, 120, _draft.ScreenMargin, out _screenMargin, out _);
            AddRow(t, "屏幕四周留白", host);

            var note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(600, 0);
            note.ForeColor = SystemColors.GrayText;
            note.Text = "所有尺寸以 1920×1080 为基准自动缩放，换分辨率不用重新调。" +
                        "卡片本身不做模糊，以保证性能。";
            AddRow(t, string.Empty, note);

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildWeatherTab()
        {
            var page = NewPage("天气");
            var t = NewTable();

            AddHeader(t, "和风天气（新版 API Host）");

            _weatherHost = new TextBox();
            _weatherHost.Width = 300;
            AddRow(t, "API Host", _weatherHost);

            _weatherKey = new TextBox();
            _weatherKey.Width = 300;
            AddRow(t, "API Key", _weatherKey);

            _weatherCity = new TextBox();
            _weatherCity.Width = 200;
            _weatherCity.TextChanged += delegate
            {
                // 城市改名后原来的 LocationID 就失效了，清掉让它重新解析
                _draft.WeatherLocationId = string.Empty;
                _weatherLocationId.Text = "（将重新解析）";
            };
            AddRow(t, "城市 / 地区", _weatherCity);

            _weatherLocationId = new Label();
            _weatherLocationId.AutoSize = true;
            _weatherLocationId.ForeColor = SystemColors.GrayText;
            AddRow(t, "已解析 LocationID", _weatherLocationId);

            _weatherRefresh = new NumericUpDown();
            _weatherRefresh.Minimum = 1;
            _weatherRefresh.Maximum = 1440;
            _weatherRefresh.Width = 90;
            AddRow(t, "刷新间隔（分钟）", _weatherRefresh);

            _weatherUnit = MakeCombo();
            Fill(_weatherUnit, "摄氏 °C", true, "华氏 °F", false);
            AddRow(t, "温度单位", _weatherUnit);

            var note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(600, 0);
            note.ForeColor = SystemColors.GrayText;
            note.Text =
                "新版接口要在控制台拿到「API Host」（形如 abcdefg.re.qweatherapi.com）和 API Key。\r\n" +
                "城市可以直接填名称（北京、海淀区），程序会自动通过 GeoAPI 解析成 LocationID 并缓存；\r\n" +
                "也可以直接填 9 位数字 LocationID（如 101010100），此时不会调用 GeoAPI。\r\n" +
                "提示：API Key 以明文保存在 config\\settings.ini 中。";
            AddRow(t, string.Empty, note);

            AddRow(t, "官方文档", MakeDocLink(
                "和风天气开发服务 · API 文档（dev.qweather.com/docs/api）",
                "https://dev.qweather.com/docs/api/"));

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildContentTab()
        {
            var page = NewPage("内容");
            var t = NewTable();

            AddHeader(t, "一言");

            _hitokotoRefresh = new NumericUpDown();
            _hitokotoRefresh.Minimum = 1;
            _hitokotoRefresh.Maximum = 1440;
            _hitokotoRefresh.Width = 90;
            AddRow(t, "刷新间隔（分钟）", _hitokotoRefresh);

            _hitokotoCategories = new TextBox();
            _hitokotoCategories.Width = 220;
            AddRow(t, "分类（留空=不限）", _hitokotoCategories);

            var hitoNote = new Label();
            hitoNote.AutoSize = true;
            hitoNote.MaximumSize = new Size(600, 0);
            hitoNote.ForeColor = SystemColors.GrayText;
            hitoNote.Text = "参照一言官方API文档填写参数，例如?c=a&&c=c。留空表示不限定分类，由接口随机返回。";
            AddRow(t, string.Empty, hitoNote);

            AddRow(t, "官方文档", MakeDocLink(
                "一言开发者中心 · 句子接口文档（developer.hitokoto.cn/sentence）",
                "https://developer.hitokoto.cn/sentence/"));

            AddHeader(t, "音乐播控（Windows SMTC）");

            _mediaRefresh = new NumericUpDown();
            _mediaRefresh.Minimum = 1;
            _mediaRefresh.Maximum = 60;
            _mediaRefresh.Width = 90;
            AddRow(t, "刷新间隔（秒）", _mediaRefresh);

            var note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(600, 0);
            note.ForeColor = SystemColors.GrayText;
            note.Text = "通过Windows SMTC接口读取和控制正在播放的音乐，需要在音乐播放器中开启SMTC功能。";
            AddRow(t, string.Empty, note);

            AddHeader(t, "日历");

            _weekStart = MakeCombo();
            Fill(_weekStart, "星期一", true, "星期日", false);
            AddRow(t, "一周起始日", _weekStart);

            _showLunar = new CheckBox();
            _showLunar.Text = "显示农历日";
            _showLunar.AutoSize = true;
            AddRow(t, "农历", _showLunar);

            _showSolarTerm = new CheckBox();
            _showSolarTerm.Text = "显示二十四节气";
            _showSolarTerm.AutoSize = true;
            AddRow(t, "节气", _showSolarTerm);

            _showHoliday = new CheckBox();
            _showHoliday.Text = "标注法定节假日（颜色跟随强调色）";
            _showHoliday.AutoSize = true;
            AddRow(t, "节日", _showHoliday);

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildGeneralTab()
        {
            var page = NewPage("常规");
            var t = NewTable();

            AddHeader(t, "显示位置");

            _screenChoice = MakeCombo();
            Fill(_screenChoice,
                "自动（选面积最大的副屏）", ScreenChoice.Auto,
                "主屏", ScreenChoice.Primary,
                "指定屏幕", ScreenChoice.Specific);
            AddRow(t, "目标屏幕", _screenChoice);

            _screenDevice = MakeCombo();
            foreach (Screen s in Screen.AllScreens)
            {
                _screenDevice.Items.Add(new ComboItem
                {
                    Text = s.DeviceName + (s.Primary ? "（主屏）" : "（副屏）") + "  " + s.Bounds.Width + "×" + s.Bounds.Height,
                    Value = s.DeviceName
                });
            }
            AddRow(t, "指定哪一块", _screenDevice);

            _coverTaskbar = new CheckBox();
            _coverTaskbar.Text = "铺满整屏（覆盖副屏任务栏）";
            _coverTaskbar.AutoSize = true;
            AddRow(t, "任务栏", _coverTaskbar);

            AddHeader(t, "启动与时钟");

            _autoStart = new CheckBox();
            _autoStart.Text = "开机自动启动";
            _autoStart.AutoSize = true;
            AddRow(t, "开机自启", _autoStart);

            _clockSeconds = new CheckBox();
            _clockSeconds.Text = "显示秒";
            _clockSeconds.AutoSize = true;
            AddRow(t, "时钟", _clockSeconds);

            _clock24 = new CheckBox();
            _clock24.Text = "24 小时制";
            _clock24.AutoSize = true;
            AddRow(t, "制式", _clock24);

            page.Controls.Add(t);
            return page;
        }

        // ================================================================
        // 取值 / 回填
        // ================================================================

        private void LoadValues()
        {
            SelectValue(_bgKind, _draft.BackgroundKind);
            SetColorButton(_bgColor, _draft.BackgroundColor);
            _bgFile.Text = _draft.BackgroundFileName;
            _bgBrightness.Value = Clamp(_draft.BackgroundBrightness, -100, 100);
            _bgBlur.Value = Clamp(_draft.BackgroundBlur, 0, 100);
            SelectValue(_bgFit, _draft.BackgroundFit);
            _bgDim.Value = Clamp(_draft.BackgroundDim, 0, 100);

            SelectValue(_theme, _draft.Theme);
            _cardOpacity.Value = Clamp(_draft.CardOpacity, 0, 100);
            SelectValue(_accentMode, _draft.AccentMode);
            SetColorButton(_accentColor, _draft.CustomAccent);
            _cornerRadius.Value = Clamp(_draft.CornerRadius, 0, 40);
            _columnRatio.Value = Clamp(_draft.ColumnRatio, 20, 80);
            _tileGap.Value = Clamp(_draft.TileGap, 0, 60);
            _tilePadding.Value = Clamp(_draft.TilePadding, 0, 80);
            _screenMargin.Value = Clamp(_draft.ScreenMargin, 0, 120);

            _weatherHost.Text = _draft.WeatherHost;
            _weatherKey.Text = _draft.WeatherKey;
            _weatherCity.Text = _draft.WeatherCity;
            _weatherLocationId.Text = string.IsNullOrEmpty(_draft.WeatherLocationId)
                ? "（尚未解析）"
                : _draft.WeatherLocationId;
            _weatherRefresh.Value = Clamp(_draft.WeatherRefreshMinutes, 1, 1440);
            SelectValue(_weatherUnit, _draft.WeatherUseCelsius);

            _hitokotoRefresh.Value = Clamp(_draft.HitokotoRefreshMinutes, 1, 1440);
            _hitokotoCategories.Text = _draft.HitokotoCategories;
            _mediaRefresh.Value = Clamp(_draft.MediaRefreshSeconds, 1, 60);
            SelectValue(_weekStart, _draft.WeekStartsMonday);
            _showLunar.Checked = _draft.ShowLunar;
            _showSolarTerm.Checked = _draft.ShowSolarTerm;
            _showHoliday.Checked = _draft.ShowHoliday;

            SelectValue(_screenChoice, _draft.ScreenChoice);
            if (!string.IsNullOrEmpty(_draft.ScreenDevice))
            {
                SelectValue(_screenDevice, _draft.ScreenDevice);
            }
            else if (_screenDevice.Items.Count > 0)
            {
                _screenDevice.SelectedIndex = 0;
            }
            _coverTaskbar.Checked = _draft.CoverTaskbar;
            _autoStart.Checked = _draft.AutoStart;
            _clockSeconds.Checked = _draft.ClockShowSeconds;
            _clock24.Checked = _draft.ClockUse24Hour;
        }

        private void ResetToDefaults()
        {
            if (MessageBox.Show(this, "把所有设置恢复成默认值？", "SubScreen",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            var fresh = new AppSettings();
            _draft.CopyFrom(fresh);
            _pendingImagePath = null;
            _clearBackground = true;
            _bgFile.Text = string.Empty;
            LoadValues();
        }

        private void Commit()
        {
            // 设计器里 _draft 是 null（Prepare 没跑），防止误触发
            if (!_ready || _draft == null)
            {
                return;
            }

            // 1) 从控件读回草稿
            _draft.BackgroundKind = (BackgroundKind)SelectedValue(_bgKind);
            _draft.BackgroundFit = (ImageFitMode)SelectedValue(_bgFit);
            _draft.BackgroundBrightness = _bgBrightness.Value;
            _draft.BackgroundBlur = _bgBlur.Value;
            _draft.BackgroundDim = _bgDim.Value;

            _draft.Theme = (AppTheme)SelectedValue(_theme);
            _draft.AccentMode = (AccentMode)SelectedValue(_accentMode);
            _draft.CardOpacity = _cardOpacity.Value;
            _draft.CornerRadius = _cornerRadius.Value;
            _draft.ColumnRatio = _columnRatio.Value;
            _draft.TileGap = _tileGap.Value;
            _draft.TilePadding = _tilePadding.Value;
            _draft.ScreenMargin = _screenMargin.Value;

            _draft.WeatherHost = _weatherHost.Text.Trim();
            _draft.WeatherKey = _weatherKey.Text.Trim();
            _draft.WeatherCity = _weatherCity.Text.Trim();
            _draft.WeatherRefreshMinutes = (int)_weatherRefresh.Value;
            _draft.WeatherUseCelsius = (bool)SelectedValue(_weatherUnit);

            _draft.HitokotoRefreshMinutes = (int)_hitokotoRefresh.Value;
            _draft.HitokotoCategories = _hitokotoCategories.Text.Trim();
            _draft.MediaRefreshSeconds = (int)_mediaRefresh.Value;
            _draft.WeekStartsMonday = (bool)SelectedValue(_weekStart);
            _draft.ShowLunar = _showLunar.Checked;
            _draft.ShowSolarTerm = _showSolarTerm.Checked;
            _draft.ShowHoliday = _showHoliday.Checked;

            _draft.ScreenChoice = (ScreenChoice)SelectedValue(_screenChoice);
            if (_screenDevice.SelectedItem is ComboItem)
            {
                _draft.ScreenDevice = Convert.ToString(((ComboItem)_screenDevice.SelectedItem).Value);
            }
            _draft.CoverTaskbar = _coverTaskbar.Checked;
            _draft.AutoStart = _autoStart.Checked;
            _draft.ClockShowSeconds = _clockSeconds.Checked;
            _draft.ClockUse24Hour = _clock24.Checked;

            // 2) 背景文件的实际落地
            try
            {
                if (!string.IsNullOrEmpty(_pendingImagePath))
                {
                    string installed = AppPaths.InstallBackground(_pendingImagePath);
                    _draft.BackgroundFileName = Path.GetFileName(installed);
                    _draft.BackgroundKind = BackgroundKind.Image;
                }
                else if (_clearBackground)
                {
                    AppPaths.RemoveAllBackgrounds();
                    _draft.BackgroundFileName = string.Empty;
                    _draft.BackgroundKind = BackgroundKind.SolidColor;
                }
                else if (_draft.BackgroundKind == BackgroundKind.Image
                         && string.IsNullOrEmpty(_draft.BackgroundFileName))
                {
                    string existing = AppPaths.FindBackgroundFile();
                    if (existing != null)
                    {
                        _draft.BackgroundFileName = Path.GetFileName(existing);
                    }
                    else
                    {
                        _draft.BackgroundKind = BackgroundKind.SolidColor;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "处理背景文件失败：" + ex.Message, "SubScreen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 3) 写回共享实例并落盘
            _target.CopyFrom(_draft);
            try
            {
                _target.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：" + ex.Message, "SubScreen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            ApplyAutoStart(_target.AutoStart);
        }

        private void OnBrowseBackground(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择背景图片（静态图片，如 jpg / png / bmp / tif）";
                dlg.Filter =
                    "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.ico|" +
                    "所有文件|*.*";
                dlg.CheckFileExists = true;

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _pendingImagePath = dlg.FileName;
                _clearBackground = false;
                _bgFile.Text = Path.GetFileName(dlg.FileName) + "（点确定后复制到 config\\background）";
                SelectValue(_bgKind, BackgroundKind.Image);

                string ext = Path.GetExtension(dlg.FileName);
                bool animatedFile = ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                                    || ext.Equals(".apng", StringComparison.OrdinalIgnoreCase);
                _hint.Text = animatedFile
                    ? "该文件是动图，程序只支持静态图片，只会显示第一帧"
                    : "已选择静态图片：" + Path.GetFileName(dlg.FileName);
            }
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // ================================================================
        // 开机自启
        // ================================================================

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SubScreen";

        private static void ApplyAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        key.SetValue(RunValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                        {
                            key.DeleteValue(RunValueName, false);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
