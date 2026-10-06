using System;
using System.Drawing;
using SubScreen.Core;

namespace SubScreen.Ui
{
    /// <summary>
    /// 左下角的全局工具条：设置 / 立即刷新 / 最小化 / 关闭。
    /// 它不是一张卡片，画成一个悬浮胶囊，宽度只包住按钮。
    /// </summary>
    internal sealed class ToolbarTile : Tile
    {
        public event Action SettingsRequested;
        public event Action RefreshRequested;
        public event Action MinimizeRequested;
        public event Action ExitRequested;

        /// <summary>按钮个数（Dashboard 用它算工具条宽度）。</summary>
        public const int ButtonCount = 4;

        /// <summary>
        /// 工具条刚好包住按钮所需的宽度。
        /// 必须和 PaintTile 里的算法一致，否则右边会多出一段空白。
        /// </summary>
        public static float MeasureWidth(UiMetrics metrics, float height)
        {
            float pad = metrics.Px(6f);
            float btn = height - pad * 2f;
            float gap = metrics.Px(4f);
            return pad * 2f + btn * ButtonCount + gap * (ButtonCount - 1);
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            ClearHotspots();

            float h = Bounds.Height;
            if (h < 12f || Bounds.Width < 12f)
            {
                return;
            }

            float radius = h / 2f;
            Draw.FillRounded(g, theme.CardBack, Bounds, radius);
            Draw.DrawRoundedBorder(g, theme.CardBorder, Bounds, radius, 1f);

            float pad = Metrics.Px(6f);
            float btn = h - pad * 2f;
            float gap = Metrics.Px(4f);
            float x = Bounds.X + pad;

            var gearBox = new RectangleF(x, Bounds.Y + pad, btn, btn);
            x += btn + gap;
            var refreshBox = new RectangleF(x, Bounds.Y + pad, btn, btn);
            x += btn + gap;
            var minimizeBox = new RectangleF(x, Bounds.Y + pad, btn, btn);
            x += btn + gap;
            var closeBox = new RectangleF(x, Bounds.Y + pad, btn, btn);

            AddHotspot(gearBox, delegate { Raise(SettingsRequested); });
            AddHotspot(refreshBox, delegate { Raise(RefreshRequested); });
            AddHotspot(minimizeBox, delegate { Raise(MinimizeRequested); });
            AddHotspot(closeBox, delegate { Raise(ExitRequested); });

            PaintIconButton(g, theme, gearBox, Icons.Gear, btn * 0.52f);
            PaintIconButton(g, theme, refreshBox, Icons.Refresh, btn * 0.52f);
            PaintIconButton(g, theme, minimizeBox, Icons.Minimize, btn * 0.52f);
            PaintIconButton(g, theme, closeBox, Icons.Close, btn * 0.50f);
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }
    }
}
