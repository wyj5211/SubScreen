using System;
using System.Drawing;
using System.Windows.Forms;
using SubScreen.Core;

namespace SubScreen.Ui
{
    /// <summary>
    /// 布局宿主：负责按屏幕尺寸和设置算出每张卡片的位置，并把绘制/鼠标事件分发下去。
    ///
    /// 布局：左右两列各占一半。
    ///   左列（自上而下）：时钟 / 天气 / 音乐播控 / 左下角工具条（设置·刷新·退出）
    ///   右列（自上而下）：一言 / 日历（占满剩余高度）
    /// </summary>
    internal sealed class Dashboard : IDisposable
    {
        public readonly ClockTile Clock = new ClockTile();
        public readonly WeatherTile Weather = new WeatherTile();
        public readonly MediaTile Media = new MediaTile();
        public readonly HitokotoTile Hitokoto = new HitokotoTile();
        public readonly CalendarTile Calendar = new CalendarTile();
        public readonly ToolbarTile Toolbar = new ToolbarTile();

        private readonly Tile[] _tiles;

        private UiMetrics _metrics = new UiMetrics(1f);
        private Tile _hoverTile;
        private int _hoverHotspot = -1;

        public Dashboard()
        {
            _tiles = new Tile[] { Clock, Weather, Media, Hitokoto, Calendar, Toolbar };
        }

        public Tile[] Tiles
        {
            get { return _tiles; }
        }

        public UiMetrics Metrics
        {
            get { return _metrics; }
        }

        /// <summary>重新计算布局。dpiScale = 当前窗口 DPI / 96。</summary>
        public void Layout(Size client, AppSettings settings, float dpiScale)
        {
            // 分辨率缩放：以 1920x1080 为基准，这样换屏幕后字号比例不变
            float resScale = Math.Min(client.Width / 1920f, client.Height / 1080f);
            if (resScale <= 0f)
            {
                resScale = 1f;
            }
            _metrics = new UiMetrics(dpiScale * resScale);

            float margin = _metrics.Px(settings.ScreenMargin);
            float gap = _metrics.Px(settings.TileGap);
            float availW = Math.Max(200f, client.Width - margin * 2f);
            float availH = Math.Max(200f, client.Height - margin * 2f);

            int ratio = settings.ColumnRatio;
            if (ratio < 20) ratio = 20;
            if (ratio > 80) ratio = 80;

            float leftW = (float)Math.Round(availW * (ratio / 100f) - gap / 2f);
            float rightW = availW - leftW - gap;
            float rightX = margin + leftW + gap;
            if (rightW < 80f)
            {
                rightW = Math.Max(80f, availW - leftW);
                rightX = margin + leftW + gap;
            }

            // ---- 左列：时钟 / 天气 / 播控 / 工具条 ----
            float toolbarH = _metrics.Px(46f);
            float flexible = Math.Max(120f, availH - gap * 3f - toolbarH);
            float h1 = (float)Math.Round(flexible * 0.26f);
            float h2 = (float)Math.Round(flexible * 0.365f);
            float h3 = flexible - h1 - h2;

            float y = margin;
            Clock.Bounds = new RectangleF(margin, y, leftW, h1);
            y += h1 + gap;
            Weather.Bounds = new RectangleF(margin, y, leftW, h2);
            y += h2 + gap;
            Media.Bounds = new RectangleF(margin, y, leftW, h3);
            y += h3 + gap;

            // 工具条只占它自己内容那么宽，不铺满整列
            float toolbarW = Math.Min(leftW, ToolbarTile.MeasureWidth(_metrics, toolbarH));
            Toolbar.Bounds = new RectangleF(margin, y, toolbarW, toolbarH);

            // ---- 右列：一言 / 日历 ----
            float rightFlexible = Math.Max(120f, availH - gap);
            float r1 = (float)Math.Round(rightFlexible * 0.24f);
            float r2 = rightFlexible - r1;

            Hitokoto.Bounds = new RectangleF(rightX, margin, rightW, r1);
            Calendar.Bounds = new RectangleF(rightX, margin + r1 + gap, rightW, r2);

            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].MarkDirty();
            }
        }

        public void Blit(Graphics g, Theme theme, AppSettings settings)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].Blit(g, theme, settings, _metrics);
            }
        }

        public Tile HitTest(PointF p)
        {
            // 后画的在上层，倒序命中
            for (int i = _tiles.Length - 1; i >= 0; i--)
            {
                if (_tiles[i].Contains(p))
                {
                    return _tiles[i];
                }
            }
            return null;
        }

        /// <summary>更新鼠标位置；返回 true 表示 hover 状态变了，需要重绘。</summary>
        public bool SetMouse(PointF p)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].SetMousePosition(p);
            }

            Tile tile = HitTest(p);
            int hotspot = tile != null ? tile.HitTestHotspot(p) : -1;

            if (tile == _hoverTile && hotspot == _hoverHotspot)
            {
                return false;
            }

            if (_hoverTile != null)
            {
                _hoverTile.MarkDirty();
            }
            if (tile != null)
            {
                tile.MarkDirty();
            }

            _hoverTile = tile;
            _hoverHotspot = hotspot;
            return true;
        }

        public Cursor CursorAt(PointF p)
        {
            Tile tile = HitTest(p);
            return tile != null ? tile.CursorAt(p) : Cursors.Default;
        }

        public bool MouseUp(PointF p)
        {
            Tile tile = HitTest(p);
            return tile != null && tile.MouseUp(p);
        }

        /// <summary>左键按下。返回接管拖拽的卡片，没有则返回 null。</summary>
        public Tile MouseDown(PointF p)
        {
            Tile tile = HitTest(p);
            if (tile != null && tile.MouseDown(p))
            {
                return tile;
            }
            return null;
        }

        /// <summary>主题或设置变化后，所有卡片都需要重画。</summary>
        public void MarkAllDirty()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].MarkDirty();
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].Dispose();
            }
        }
    }
}
