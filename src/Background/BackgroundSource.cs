using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SubScreen.Core;

namespace SubScreen.Background
{
    /// <summary>
    /// 背景来源：把 config/background/bg.* 做成"屏幕尺寸、已应用亮度/模糊/适应模式"的一张位图。
    ///
    /// 只支持纯色和静态图片。动图（GIF/APNG）支持已按要求移除。
    /// 合成结果会缓存，只有设置变化或窗口尺寸变化时才重算。
    /// </summary>
    internal sealed class BackgroundSource : IDisposable
    {
        private AppSettings _settings = new AppSettings();
        private Size _target = new Size(1920, 1080);

        private Bitmap _static;
        private Bitmap _composed;

        public bool IsSolid { get; private set; }
        public Color SolidColor { get; private set; }

        /// <summary>载入失败时的原因，用于在界面上提示。</summary>
        public string Error { get; private set; }

        public BackgroundSource()
        {
            IsSolid = true;
            SolidColor = Color.Black;
        }

        /// <summary>
        /// 按当前设置重建。可在后台线程调用，但调用方需要保证不与 GetFrame 并发。
        /// </summary>
        public void Rebuild(Size target, AppSettings settings)
        {
            _settings = settings;
            _target = new Size(Math.Max(1, target.Width), Math.Max(1, target.Height));
            Error = null;

            Release();
            IsSolid = true;
            SolidColor = settings.BackgroundColor;

            if (settings.BackgroundKind != BackgroundKind.Image)
            {
                return;
            }

            string path = ResolveBackgroundPath(settings);
            if (path == null)
            {
                Error = "找不到背景图片文件";
                return;
            }

            try
            {
                Bitmap img = LoadStatic(path);
                if (img == null)
                {
                    Error = "无法解码背景图片（格式不支持或文件损坏）";
                    return;
                }

                _static = DownscaleIfNeeded(img, _target);
                if (!ReferenceEquals(_static, img))
                {
                    img.Dispose();
                }

                _composed = Compose(_static);
                IsSolid = false;
            }
            catch (Exception ex)
            {
                Error = "载入背景失败：" + ex.Message;
                Release();
                IsSolid = true;
                SolidColor = settings.BackgroundColor;
            }
        }

        private static string ResolveBackgroundPath(AppSettings settings)
        {
            // 优先信任设置里记录的文件名，找不到再扫描目录
            if (!string.IsNullOrEmpty(settings.BackgroundFileName))
            {
                string p = Path.Combine(AppPaths.BackgroundDir, settings.BackgroundFileName);
                if (File.Exists(p))
                {
                    return p;
                }
            }
            return AppPaths.FindBackgroundFile();
        }

        private static Bitmap LoadStatic(string path)
        {
            // 用 FileStream 打开，避免 Image.FromFile 长期占用文件句柄
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs, true, true))
            {
                return new Bitmap(img);
            }
        }

        private static Bitmap DownscaleIfNeeded(Bitmap src, Size target)
        {
            long pixels = (long)src.Width * src.Height;
            long limit = (long)target.Width * target.Height;
            if (pixels <= limit)
            {
                return src;
            }

            double scale = Math.Sqrt((double)limit / pixels);
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));

            var dst = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                Draw.SetTextQuality(g);
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return dst;
        }

        /// <summary>取合成好的背景图。纯色背景返回 null，调用方直接用 SolidColor 填充。</summary>
        public Bitmap GetFrame()
        {
            return IsSolid ? null : _composed;
        }

        private Bitmap Compose(Bitmap src)
        {
            var dst = new Bitmap(_target.Width, _target.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                Draw.SetTextQuality(g);
                g.Clear(_settings.BackgroundColor);

                using (var attrs = new ImageAttributes())
                {
                    if (_settings.BackgroundBrightness != 0)
                    {
                        attrs.SetColorMatrix(BrightnessMatrix(_settings.BackgroundBrightness));
                    }
                    DrawFitted(g, src, _target, _settings.BackgroundFit, attrs);
                }
            }

            int radius = BlurRadius(_settings.BackgroundBlur);
            if (radius > 0)
            {
                BoxBlur(dst, radius);
            }

            return dst;
        }

        private static void DrawFitted(Graphics g, Image src, Size target, ImageFitMode mode, ImageAttributes attrs)
        {
            int tw = target.Width;
            int th = target.Height;
            int sw = src.Width;
            int sh = src.Height;
            if (sw <= 0 || sh <= 0)
            {
                return;
            }

            switch (mode)
            {
                case ImageFitMode.Stretch:
                    g.DrawImage(src, new Rectangle(0, 0, tw, th), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    break;

                case ImageFitMode.Fill:
                case ImageFitMode.Zoom:
                    {
                        double sx = (double)tw / sw;
                        double sy = (double)th / sh;
                        double s = Math.Max(sx, sy);
                        if (mode == ImageFitMode.Zoom && s < 1.0)
                        {
                            s = 1.0;
                        }
                        int dw = (int)Math.Ceiling(sw * s);
                        int dh = (int)Math.Ceiling(sh * s);
                        g.DrawImage(src, new Rectangle((tw - dw) / 2, (th - dh) / 2, dw, dh),
                            0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    }
                    break;

                case ImageFitMode.Fit:
                    {
                        double s = Math.Min((double)tw / sw, (double)th / sh);
                        int dw = Math.Max(1, (int)Math.Round(sw * s));
                        int dh = Math.Max(1, (int)Math.Round(sh * s));
                        g.DrawImage(src, new Rectangle((tw - dw) / 2, (th - dh) / 2, dw, dh),
                            0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    }
                    break;

                case ImageFitMode.Center:
                    g.DrawImage(src, new Rectangle((tw - sw) / 2, (th - sh) / 2, sw, sh),
                        0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                    break;

                case ImageFitMode.Tile:
                    for (int y = 0; y < th; y += sh)
                    {
                        for (int x = 0; x < tw; x += sw)
                        {
                            g.DrawImage(src, new Rectangle(x, y, sw, sh), 0, 0, sw, sh, GraphicsUnit.Pixel, attrs);
                        }
                    }
                    break;
            }
        }

        private static ColorMatrix BrightnessMatrix(int brightness)
        {
            double a = Math.Min(100, Math.Max(-100, brightness)) / 100.0;
            float scale;
            float offset;
            if (a < 0)
            {
                scale = (float)(1.0 + a);
                offset = 0f;
            }
            else
            {
                // 向白色插值，避免高亮时直接截断成一片白
                scale = (float)(1.0 - a);
                offset = (float)a;
            }

            return new ColorMatrix(new[]
            {
                new[] { scale, 0f, 0f, 0f, 0f },
                new[] { 0f, scale, 0f, 0f, 0f },
                new[] { 0f, 0f, scale, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { offset, offset, offset, 0f, 1f }
            });
        }

        /// <summary>把 0..100 的模糊度映射到像素半径。</summary>
        public static int BlurRadius(int blur)
        {
            if (blur <= 0)
            {
                return 0;
            }
            if (blur > 100)
            {
                blur = 100;
            }
            // 1..40 px，非线性，低档位更细腻
            return Math.Max(1, (int)Math.Round(Math.Pow(blur / 100.0, 1.35) * 40.0));
        }

        /// <summary>
        /// 两趟滑动窗口均值模糊，近似高斯。O(w*h)，对 2160x1080 大约几十毫秒。
        /// 背景在合成后 alpha 恒为 255，所以在 premultiplied 空间直接模糊是正确的。
        /// </summary>
        public static void BoxBlur(Bitmap bmp, int radius)
        {
            if (radius < 1 || bmp.Width < 2 || bmp.Height < 2)
            {
                return;
            }

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);

                int w = bmp.Width;
                int h = bmp.Height;
                int stride = data.Stride;
                int bytes = stride * h;

                var buf = new byte[bytes];
                var tmp = new byte[bytes];
                Marshal.Copy(data.Scan0, buf, 0, bytes);

                for (int pass = 0; pass < 2; pass++)
                {
                    BlurHorizontal(buf, tmp, w, h, stride, radius);
                    BlurVertical(tmp, buf, w, h, stride, radius);
                }

                Marshal.Copy(buf, 0, data.Scan0, bytes);
            }
            finally
            {
                if (data != null)
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        private static void BlurHorizontal(byte[] src, byte[] dst, int w, int h, int stride, int radius)
        {
            int div = radius * 2 + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int sb = 0, sg = 0, sr = 0, sa = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int xx = k < 0 ? 0 : (k >= w ? w - 1 : k);
                    int i = row + xx * 4;
                    sb += src[i];
                    sg += src[i + 1];
                    sr += src[i + 2];
                    sa += src[i + 3];
                }

                for (int x = 0; x < w; x++)
                {
                    int o = row + x * 4;
                    dst[o] = (byte)(sb / div);
                    dst[o + 1] = (byte)(sg / div);
                    dst[o + 2] = (byte)(sr / div);
                    dst[o + 3] = (byte)(sa / div);

                    int xOut = x - radius;
                    int xIn = x + radius + 1;
                    if (xOut < 0) xOut = 0;
                    if (xIn >= w) xIn = w - 1;

                    int io = row + xOut * 4;
                    int ino = row + xIn * 4;
                    sb += src[ino] - src[io];
                    sg += src[ino + 1] - src[io + 1];
                    sr += src[ino + 2] - src[io + 2];
                    sa += src[ino + 3] - src[io + 3];
                }
            }
        }

        private static void BlurVertical(byte[] src, byte[] dst, int w, int h, int stride, int radius)
        {
            int div = radius * 2 + 1;
            for (int x = 0; x < w; x++)
            {
                int col = x * 4;
                int sb = 0, sg = 0, sr = 0, sa = 0;

                for (int k = -radius; k <= radius; k++)
                {
                    int yy = k < 0 ? 0 : (k >= h ? h - 1 : k);
                    int i = col + yy * stride;
                    sb += src[i];
                    sg += src[i + 1];
                    sr += src[i + 2];
                    sa += src[i + 3];
                }

                for (int y = 0; y < h; y++)
                {
                    int o = col + y * stride;
                    dst[o] = (byte)(sb / div);
                    dst[o + 1] = (byte)(sg / div);
                    dst[o + 2] = (byte)(sr / div);
                    dst[o + 3] = (byte)(sa / div);

                    int yOut = y - radius;
                    int yIn = y + radius + 1;
                    if (yOut < 0) yOut = 0;
                    if (yIn >= h) yIn = h - 1;

                    int io = col + yOut * stride;
                    int ino = col + yIn * stride;
                    sb += src[ino] - src[io];
                    sg += src[ino + 1] - src[io + 1];
                    sr += src[ino + 2] - src[io + 2];
                    sa += src[ino + 3] - src[io + 3];
                }
            }
        }

        private void Release()
        {
            if (_composed != null)
            {
                _composed.Dispose();
                _composed = null;
            }
            if (_static != null)
            {
                _static.Dispose();
                _static = null;
            }
        }

        public void Dispose()
        {
            Release();
        }
    }
}
