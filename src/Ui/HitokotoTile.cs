using System;
using System.Drawing;
using SubScreen.Core;
using SubScreen.Net;

namespace SubScreen.Ui
{
    /// <summary>右上角：一言。右上角带刷新按钮和更新时间，和天气卡片的样式一致。</summary>
    internal sealed class HitokotoTile : Tile
    {
        private string _text = "正在获取…";
        private string _attribution = string.Empty;
        private string _updatedText = string.Empty;
        private bool _isError;

        /// <summary>用户点了右上角刷新按钮。</summary>
        public event Action RefreshRequested;

        public void SetSentence(Hitokoto hitokoto)
        {
            if (hitokoto == null)
            {
                return;
            }

            _text = hitokoto.Text;
            _attribution = hitokoto.Attribution;
            _updatedText = "更新于 " + hitokoto.FetchedAt.ToString("HH:mm");
            _isError = false;
            MarkDirty();
        }

        public void SetError(string message)
        {
            _isError = true;
            _attribution = string.Empty;
            _updatedText = "更新于 " + DateTime.Now.ToString("HH:mm");
            _text = string.IsNullOrEmpty(message) ? "一言暂时取不到" : message;
            MarkDirty();
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            PaintCardBackground(g, theme, settings);

            RectangleF c = ContentBounds(settings);
            if (c.Height < 20f || c.Width < 20f)
            {
                return;
            }

            ClearHotspots();

            // ---------- 右上角：更新时间 + 刷新（与天气卡片一致） ----------
            float headerH = Math.Max(18f, c.Height * 0.135f);
            var header = new RectangleF(c.X, c.Y, c.Width, headerH);

            float refreshSize = headerH * 0.62f;
            float refreshBoxSize = Math.Min(headerH * 0.95f, c.Width * 0.06f);
            RectangleF refreshBox = new RectangleF(
                c.Right - refreshBoxSize, header.Y + (headerH - refreshBoxSize) / 2f,
                refreshBoxSize, refreshBoxSize);
            AddHotspot(refreshBox, delegate { RaiseRefresh(); });
            PaintIconButton(g, theme, refreshBox, Icons.Refresh, refreshSize);

            if (!string.IsNullOrEmpty(_updatedText))
            {
                float updSize = ClampFont(headerH * 0.52f);
                Font updFont = IconFont.GetText(updSize, FontStyle.Regular);
                float updWidth = refreshBoxSize + Metrics.Px(8f) + g.MeasureString(
                    _updatedText, updFont, int.MaxValue, StringFormat.GenericTypographic).Width;

                Draw.Text(g, _updatedText, updFont, theme.TextTertiary,
                    new RectangleF(c.Right - updWidth, header.Y,
                        updWidth - refreshBoxSize - Metrics.Px(8f), headerH),
                    Draw.RightCenter);
            }

            // ---------- 正文 ----------
            float bodyTop = header.Bottom + c.Height * 0.01f;

            // 左上角一个淡色的装饰引号
            float markSize = Math.Min(c.Height * 0.30f, c.Width * 0.09f);
            if (markSize > 12f)
            {
                Font markFont = IconFont.GetDisplay(markSize, FontStyle.Bold);
                Color markColor = ColorUtil.WithAlpha(theme.AccentOnCard, 60);
                Draw.Text(g, "\u201C", markFont, markColor,
                    new RectangleF(c.X, bodyTop - markSize * 0.22f, c.Width, markSize * 1.1f), Draw.Left);
            }

            float attrH = string.IsNullOrEmpty(_attribution) ? 0f : c.Height * 0.16f;

            var textRect = new RectangleF(
                c.X + c.Width * 0.03f,
                bodyTop + markSize * 0.72f,
                c.Width * 0.94f,
                Math.Max(10f, c.Bottom - attrH - (bodyTop + markSize * 0.72f)));

            float textSize = ClampFont(c.Height * 0.150f);
            Font textFont = IconFont.GetText(textSize, FontStyle.Regular);
            Color textColor = _isError ? theme.TextTertiary : theme.TextPrimary;
            Draw.Text(g, _text, textFont, textColor, textRect, Draw.WrapLeftCenter);

            if (attrH > 0f)
            {
                float size = ClampFont(attrH * 0.70f);
                Font font = IconFont.GetText(size, FontStyle.Regular);
                Draw.Text(g, _attribution, font, theme.TextSecondary,
                    new RectangleF(c.X, c.Bottom - attrH, c.Width, attrH), Draw.RightCenter);
            }
        }

        private void RaiseRefresh()
        {
            Action handler = RefreshRequested;
            if (handler != null)
            {
                handler();
            }
        }

        private static float ClampFont(float v)
        {
            if (v < 9f) return 9f;
            if (v > 120f) return 120f;
            return v;
        }
    }
}
