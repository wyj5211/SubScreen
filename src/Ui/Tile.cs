using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SubScreen.Core;

namespace SubScreen.Ui
{
    /// <summary>
    /// 界面尺寸换算。Scale = 1.0 表示 1920x1080 @ 96 DPI 下的基准尺寸，
    /// 其它分辨率和 DPI 按比例放大缩小，这样一套数值能适配各种屏幕。
    /// </summary>
    internal sealed class UiMetrics
    {
        public readonly float Scale;

        public UiMetrics(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (scale > 3.0f) scale = 3.0f;
            Scale = scale;
        }

        public float Px(float value)
        {
            return value * Scale;
        }

        public int I(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public float Font(float value)
        {
            return Math.Max(6f, value * Scale);
        }
    }

    /// <summary>卡片内的可点击区域。</summary>
    internal sealed class Hotspot
    {
        public RectangleF Rect;
        public Action<PointF> Action;
        public Cursor Cursor = Cursors.Hand;
    }

    /// <summary>
    /// 磁贴卡片基类。
    ///
    /// 每张卡片把自己的内容渲染进一张和自身等大的 32bppPArgb 位图并缓存，
    /// 只有内容变化时才重绘。整屏合成时只是几次位图拷贝，
    /// 所以时钟每秒跳动、进度条每秒推进都不会引起全屏重绘。
    /// </summary>
    internal abstract class Tile : IDisposable
    {
        public RectangleF Bounds;

        protected Theme CurrentTheme;
        protected AppSettings CurrentSettings;
        protected UiMetrics Metrics = new UiMetrics(1f);

        private readonly List<Hotspot> _hotspots = new List<Hotspot>();
        private Bitmap _cache;
        private bool _dirty = true;

        /// <summary>鼠标当前位置（屏幕坐标），用于 hover 效果。</summary>
        protected PointF MousePosition = new PointF(-1000f, -1000f);

        public void MarkDirty()
        {
            _dirty = true;
        }

        public bool IsDirty
        {
            get { return _dirty; }
        }

        /// <summary>命中测试：返回被命中的热点序号，-1 表示没有。</summary>
        public int HitTestHotspot(PointF p)
        {
            for (int i = 0; i < _hotspots.Count; i++)
            {
                if (_hotspots[i].Rect.Contains(p))
                {
                    return i;
                }
            }
            return -1;
        }

        public virtual Cursor CursorAt(PointF p)
        {
            int i = HitTestHotspot(p);
            return i >= 0 ? _hotspots[i].Cursor : Cursors.Default;
        }

        public bool Contains(PointF p)
        {
            return Bounds.Contains(p);
        }

        public void SetMousePosition(PointF p)
        {
            MousePosition = p;
        }

        /// <summary>
        /// 在某个热点上松开，触发它的动作。返回是否消费掉该事件。
        /// 需要"按下拖动"交互的卡片（比如音量条）可以覆盖它。
        /// </summary>
        public virtual bool MouseUp(PointF p)
        {
            int i = HitTestHotspot(p);
            if (i < 0)
            {
                return false;
            }

            Hotspot h = _hotspots[i];
            if (h.Action != null)
            {
                h.Action(p);
            }
            return true;
        }

        /// <summary>
        /// 鼠标左键按下。返回 true 表示这张卡片要接管后续的移动/松开事件（拖拽）。
        /// </summary>
        public virtual bool MouseDown(PointF p)
        {
            return false;
        }

        /// <summary>拖拽过程中的移动。返回 true 表示内容变了、需要重绘。</summary>
        public virtual bool MouseMove(PointF p)
        {
            return false;
        }

        /// <summary>把卡片画到目标 Graphics 上（屏幕坐标）。</summary>
        public void Blit(Graphics g, Theme theme, AppSettings settings, UiMetrics metrics)
        {
            CurrentTheme = theme;
            CurrentSettings = settings;
            Metrics = metrics;

            int w = (int)Math.Round(Bounds.Width);
            int h = (int)Math.Round(Bounds.Height);
            if (w < 4 || h < 4)
            {
                return;
            }

            if (_cache == null || _cache.Width != w || _cache.Height != h)
            {
                if (_cache != null)
                {
                    _cache.Dispose();
                }
                _cache = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                _dirty = true;
            }

            if (_dirty)
            {
                using (Graphics cg = Graphics.FromImage(_cache))
                {
                    cg.Clear(Color.Transparent);
                    cg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    cg.SmoothingMode = SmoothingMode.AntiAlias;
                    cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    cg.CompositingQuality = CompositingQuality.HighQuality;

                    GraphicsState state = cg.Save();
                    cg.TranslateTransform(-Bounds.X, -Bounds.Y);
                    _hotspots.Clear();
                    try
                    {
                        PaintTile(cg, theme, settings);
                    }
                    catch (Exception)
                    {
                        // 单张卡片画挂了不能拖垮整个界面
                    }
                    cg.Restore(state);
                }
                _dirty = false;
            }

            g.DrawImageUnscaled(_cache, (int)Math.Round(Bounds.X), (int)Math.Round(Bounds.Y));
        }

        /// <summary>子类实现：在屏幕坐标系下绘制卡片内容。</summary>
        protected abstract void PaintTile(Graphics g, Theme theme, AppSettings settings);

        // ---------------- 子类用的辅助 ----------------

        protected void ClearHotspots()
        {
            _hotspots.Clear();
        }

        protected void AddHotspot(RectangleF rect, Action<PointF> action)
        {
            var h = new Hotspot();
            h.Rect = rect;
            h.Action = action;
            _hotspots.Add(h);
        }

        protected void AddHotspot(RectangleF rect, Cursor cursor, Action<PointF> action)
        {
            var h = new Hotspot();
            h.Rect = rect;
            h.Action = action;
            h.Cursor = cursor;
            _hotspots.Add(h);
        }

        /// <summary>画卡片底 + 描边。所有卡片都先调这个。</summary>
        protected void PaintCardBackground(Graphics g, Theme theme, AppSettings settings)
        {
            float radius = Metrics.Px(settings.CornerRadius);
            Draw.FillRounded(g, theme.CardBack, Bounds, radius);
            Draw.DrawRoundedBorder(g, theme.CardBorder, Bounds, radius, 1f);
        }

        protected RectangleF ContentBounds(AppSettings settings)
        {
            float pad = Metrics.Px(settings.TilePadding);
            return new RectangleF(
                Bounds.X + pad, Bounds.Y + pad,
                Math.Max(1f, Bounds.Width - pad * 2f),
                Math.Max(1f, Bounds.Height - pad * 2f));
        }

        protected bool IsHovered(RectangleF r)
        {
            return r.Contains(MousePosition);
        }

        /// <summary>
        /// 画一个图标按钮。hover 时补一层淡背景，让点击目标看得出来。
        /// </summary>
        protected void PaintIconButton(Graphics g, Theme theme, RectangleF box, IconSpec icon, float iconSize)
        {
            bool hover = IsHovered(box);
            if (hover)
            {
                Draw.FillRounded(g, ColorUtil.WithAlpha(theme.TextPrimary, 26), box, box.Height * 0.5f);
            }

            Color c = hover ? theme.TextPrimary : theme.TextSecondary;
            Draw.Icon(g, icon, iconSize, c, box);
        }

        /// <summary>
        /// 画一个实心圆底的图标按钮（播放/暂停那种）。
        /// </summary>
        protected void PaintAccentIconButton(Graphics g, Theme theme, RectangleF box, IconSpec icon, float iconSize)
        {
            bool hover = IsHovered(box);
            Color bg = hover ? ColorUtil.Shade(theme.Accent, theme.IsDark ? 0.15 : -0.1) : theme.Accent;
            Draw.FillRounded(g, bg, box, box.Height * 0.5f);
            Draw.Icon(g, icon, iconSize, theme.OnAccent, box);
        }

        public virtual void Dispose()
        {
            if (_cache != null)
            {
                _cache.Dispose();
                _cache = null;
            }
        }
    }
}
