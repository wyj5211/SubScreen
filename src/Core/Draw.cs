using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SubScreen.Core
{
    /// <summary>绘图小工具。所有卡片都在一个 32bppPArgb 内存表面上绘制，所以文字统一用 AntiAliasGridFit。</summary>
    internal static class Draw
    {
        public static readonly StringFormat Left = MakeFormat(StringAlignment.Near, StringAlignment.Near);
        public static readonly StringFormat LeftCenter = MakeFormat(StringAlignment.Near, StringAlignment.Center);
        public static readonly StringFormat Center = MakeFormat(StringAlignment.Center, StringAlignment.Center);
        public static readonly StringFormat Right = MakeFormat(StringAlignment.Far, StringAlignment.Near);
        public static readonly StringFormat RightCenter = MakeFormat(StringAlignment.Far, StringAlignment.Center);
        public static readonly StringFormat CenterTop = MakeFormat(StringAlignment.Center, StringAlignment.Near);
        public static readonly StringFormat LeftBottom = MakeFormat(StringAlignment.Near, StringAlignment.Far);
        public static readonly StringFormat RightBottom = MakeFormat(StringAlignment.Far, StringAlignment.Far);

        /// <summary>会自动换行的格式（一言卡片用）。</summary>
        public static readonly StringFormat WrapLeft = MakeWrapFormat(StringAlignment.Near, StringAlignment.Near);
        public static readonly StringFormat WrapCenter = MakeWrapFormat(StringAlignment.Center, StringAlignment.Center);
        public static readonly StringFormat WrapLeftCenter = MakeWrapFormat(StringAlignment.Near, StringAlignment.Center);
        public static readonly StringFormat WrapRightBottom = MakeWrapFormat(StringAlignment.Far, StringAlignment.Far);

        private static StringFormat MakeFormat(StringAlignment align, StringAlignment line)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = align;
            f.LineAlignment = line;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            return f;
        }

        private static StringFormat MakeWrapFormat(StringAlignment align, StringAlignment line)
        {
            var f = new StringFormat(StringFormat.GenericDefault);
            f.Alignment = align;
            f.LineAlignment = line;
            f.Trimming = StringTrimming.EllipsisWord;
            return f;
        }

        public static void SetTextQuality(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        /// <summary>生成圆角矩形路径。radius &lt;= 0 时退化为直角矩形。</summary>
        public static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0.5f)
            {
                path.AddRectangle(r);
                path.CloseFigure();
                return path;
            }

            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 1f)
            {
                path.AddRectangle(r);
                path.CloseFigure();
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            if (color.A == 0 || r.Width <= 0f || r.Height <= 0f)
            {
                return;
            }

            using (GraphicsPath path = RoundedPath(r, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedBorder(Graphics g, Color color, RectangleF r, float radius, float width)
        {
            if (color.A == 0 || width <= 0f || r.Width <= 0f || r.Height <= 0f)
            {
                return;
            }

            RectangleF rr = r;
            rr.Inflate(-width / 2f, -width / 2f);
            using (GraphicsPath path = RoundedPath(rr, Math.Max(0f, radius - width / 2f)))
            using (var pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawPath(pen, path);
            }
        }

        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
            {
                return SizeF.Empty;
            }
            return g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF box, StringFormat format)
        {
            if (string.IsNullOrEmpty(text) || color.A == 0 || box.Width <= 0f || box.Height <= 0f)
            {
                return;
            }

            // GDI+ 在"矩形高度放不下整行"且开了省略号裁剪时，会把这一行**整个丢掉**
            // （不是裁掉一部分，是干脆不画）。表现就是文字神秘消失。
            // 这里把矩形撑到字体实际行高，保持原来的垂直对齐方式。
            if (format != null && format.Trimming != StringTrimming.None)
            {
                float lineHeight = font.GetHeight(g) + 1f;
                if (box.Height < lineHeight)
                {
                    float extra = lineHeight - box.Height;
                    float top = box.Y;
                    if (format.LineAlignment == StringAlignment.Center)
                    {
                        top -= extra / 2f;
                    }
                    else if (format.LineAlignment == StringAlignment.Far)
                    {
                        top -= extra;
                    }
                    box = new RectangleF(box.X, top, box.Width, lineHeight);
                }
            }

            using (var brush = new SolidBrush(color))
            {
                g.DrawString(text, font, brush, box, format);
            }
        }

        public static void TextAt(Graphics g, string text, Font font, Color color, float x, float y)
        {
            Text(g, text, font, color, new RectangleF(x, y, 10000f, 1000f), Left);
        }

        /// <summary>把图标字形画在一个矩形里（默认居中）。</summary>
        public static void Icon(Graphics g, IconSpec icon, float pixelSize, Color color, RectangleF box, StringFormat format)
        {
            if (icon.IsEmpty || color.A == 0)
            {
                return;
            }

            Font font = IconFont.GetIcon(icon.Font, pixelSize);
            string s = icon.Code.ToString();

            // 和 Draw.Text 同样的坑：矩形高度小于行高时 GDI+ 会整行丢弃。
            // 图标字号常常接近框高，不处理的话大图标会凭空消失。
            RectangleF target = new RectangleF(box.X, box.Y - pixelSize * 0.04f, box.Width, box.Height);
            if (format != null && format.Trimming != StringTrimming.None)
            {
                float lineHeight = font.GetHeight(g) + 1f;
                if (target.Height < lineHeight)
                {
                    float extra = lineHeight - target.Height;
                    float top = target.Y;
                    if (format.LineAlignment == StringAlignment.Center)
                    {
                        top -= extra / 2f;
                    }
                    else if (format.LineAlignment == StringAlignment.Far)
                    {
                        top -= extra;
                    }
                    target = new RectangleF(target.X, top, target.Width, lineHeight);
                }
            }

            using (var brush = new SolidBrush(color))
            {
                g.DrawString(s, font, brush, target, format);
            }
        }

        public static void Icon(Graphics g, IconSpec icon, float pixelSize, Color color, RectangleF box)
        {
            Icon(g, icon, pixelSize, color, box, Center);
        }

        public static void Icon(Graphics g, IconSpec icon, float pixelSize, Color color, float x, float y, float w, float h)
        {
            Icon(g, icon, pixelSize, color, new RectangleF(x, y, w, h), Center);
        }

        /// <summary>画一条水平分隔线。</summary>
        public static void Separator(Graphics g, Color color, float x, float y, float width)
        {
            using (var pen = new Pen(color, 1f))
            {
                g.DrawLine(pen, x, y, x + width, y);
            }
        }

        /// <summary>进度条：底槽 + 已完成部分，圆角。</summary>
        public static void ProgressBar(Graphics g, RectangleF r, float progress, Color track, Color fill, float radius)
        {
            FillRounded(g, track, r, radius);
            if (progress <= 0f)
            {
                return;
            }
            if (progress > 1f)
            {
                progress = 1f;
            }

            var filled = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * progress), r.Height);
            FillRounded(g, fill, filled, radius);
        }
    }
}
