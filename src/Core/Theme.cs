using System;
using System.Drawing;

namespace SubScreen.Core
{
    /// <summary>
    /// 主题调色板。深色/浅色两套，强调色参与计算，卡片背景带用户设定的不透明度。
    /// </summary>
    internal sealed class Theme
    {
        public bool IsDark;

        /// <summary>强调色（已做对比度修正）。</summary>
        public Color Accent;

        /// <summary>主文字色。</summary>
        public Color TextPrimary;
        /// <summary>次要文字（标签、说明）。</summary>
        public Color TextSecondary;
        /// <summary>更弱的文字（辅助信息）。</summary>
        public Color TextTertiary;

        /// <summary>卡片背景（含 CardOpacity 决定的 alpha）。</summary>
        public Color CardBack;
        /// <summary>卡片描边（很淡）。</summary>
        public Color CardBorder;

        /// <summary>当前背景图上的遮罩色（含 alpha）。</summary>
        public Color Scrim;

        /// <summary>强调色之上应该放的文字颜色。</summary>
        public Color OnAccent;

        public static Theme Create(AppSettings s)
        {
            var t = new Theme();
            bool dark = s.Theme == AppTheme.Dark;
            t.IsDark = dark;
            t.Accent = AccentColor.Resolve(s);

            if (dark)
            {
                t.TextPrimary = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
                t.TextSecondary = Color.FromArgb(255, 0xC8, 0xC8, 0xC8);
                t.TextTertiary = Color.FromArgb(255, 0x8C, 0x8C, 0x8C);
            }
            else
            {
                // 浅色模式下原来的两档灰太浅（尤其卡片带透明度、背景图透出来时看不清），
                // 这里压深：次要 #3C3C3C ≈ 10.7:1，辅助 #5F5F5F ≈ 7.0:1（对白底），
                // 三档之间仍保留明确的层次。
                t.TextPrimary = Color.FromArgb(255, 0x1A, 0x1A, 0x1A);
                t.TextSecondary = Color.FromArgb(255, 0x3C, 0x3C, 0x3C);
                t.TextTertiary = Color.FromArgb(255, 0x5F, 0x5F, 0x5F);
            }

            int cardAlpha = (int)Math.Round(255.0 * Math.Min(100, Math.Max(0, s.CardOpacity)) / 100.0);
            Color cardBase = dark ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            t.CardBack = Color.FromArgb(cardAlpha, cardBase.R, cardBase.G, cardBase.B);

            t.CardBorder = dark
                ? Color.FromArgb(Math.Min(255, cardAlpha / 3 + 12), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(Math.Min(255, cardAlpha / 3 + 12), 0x00, 0x00, 0x00);

            // 遮罩：深色主题用黑，浅色主题用白
            int dimAlpha = (int)Math.Round(255.0 * Math.Min(100, Math.Max(0, s.BackgroundDim)) / 100.0);
            t.Scrim = dark
                ? Color.FromArgb(dimAlpha, 0x00, 0x00, 0x00)
                : Color.FromArgb(dimAlpha, 0xFF, 0xFF, 0xFF);

            t.OnAccent = ColorUtil.Luminance(t.Accent) > 0.6
                ? Color.FromArgb(255, 0x1A, 0x1A, 0x1A)
                : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

            return t;
        }

        /// <summary>在卡片上用强调色画文字/描边时的颜色（保证可读）。</summary>
        public Color AccentOnCard
        {
            get
            {
                double lum = ColorUtil.Luminance(Accent);
                if (IsDark && lum < 0.5)
                {
                    return ColorUtil.Shade(Accent, 0.3);
                }
                if (!IsDark && lum > 0.6)
                {
                    return ColorUtil.Shade(Accent, -0.25);
                }
                return Accent;
            }
        }
    }
}
