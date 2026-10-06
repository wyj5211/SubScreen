using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SubScreen.Core;
using SubScreen.Media;

namespace SubScreen.Ui
{
    /// <summary>
    /// 左下角：音乐播控（Windows SMTC）。
    /// 左边封面，右边标题/艺术家、上一曲·播放暂停·下一曲按钮，
    /// 最下面是系统音量控制（喇叭图标 + 音量条 + 数字）。
    /// </summary>
    internal sealed class MediaTile : Tile
    {
        private MediaSessionInfo _info;
        private Bitmap _cover;
        private SmtcService _service;

        private int _volumePercent;
        private bool _muted;
        private bool _volumeKnown;
        private bool _dragging;

        /// <summary>音量条位置，绘制时记录，供拖拽换算用。</summary>
        private RectangleF _volumeBar = RectangleF.Empty;
        private RectangleF _speakerBox = RectangleF.Empty;

        public void AttachService(SmtcService service)
        {
            _service = service;
        }

        public void Update(MediaSessionInfo info)
        {
            if (info == null)
            {
                return;
            }

            _info = info;

            // 封面所有权移交给本卡片
            if (info.NewThumbnail != null)
            {
                if (_cover != null)
                {
                    _cover.Dispose();
                }
                _cover = info.NewThumbnail;
                info.NewThumbnail = null;
            }

            MarkDirty();
        }

        /// <summary>
        /// 从系统读回音量（可能被键盘或别的程序改过）。
        /// 返回 true 表示显示内容变了、需要重绘。
        /// </summary>
        public bool RefreshVolume()
        {
            // 正在拖动时不要把系统的中间值抢回来，否则会跟手打架
            if (_dragging)
            {
                return false;
            }

            int volume;
            if (!SystemVolume.TryGetVolume(out volume))
            {
                return false;
            }

            bool muted = _muted;
            bool haveMute = SystemVolume.TryGetMute(out muted);

            bool changed = !_volumeKnown
                           || volume != _volumePercent
                           || (haveMute && muted != _muted);

            _volumeKnown = true;
            _volumePercent = volume;
            if (haveMute)
            {
                _muted = muted;
            }

            if (changed)
            {
                MarkDirty();
            }
            return changed;
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            PaintCardBackground(g, theme, settings);

            RectangleF c = ContentBounds(settings);
            if (c.Height < 24f || c.Width < 24f)
            {
                return;
            }

            ClearHotspots();

            bool hasSession = _info != null && _info.HasSession;
            if (!hasSession)
            {
                PaintPlaceholder(g, theme, settings, c);
                return;
            }

            // ---------- 布局 ----------
            float coverSize = Math.Min(c.Height, c.Width * 0.34f);
            coverSize = Math.Max(24f, coverSize);
            var cover = new RectangleF(c.X, c.Y + (c.Height - coverSize) / 2f, coverSize, coverSize);

            float gap = Metrics.Px(16f);
            float rightX = cover.Right + gap;
            float rightW = c.Right - rightX;
            float rh = c.Height;

            if (rightW < 60f)
            {
                PaintCover(g, theme, settings, cover);
                return;
            }

            PaintCover(g, theme, settings, cover);

            var right = new RectangleF(rightX, c.Y, rightW, rh);

            // ---------- 标题 / 艺术家 ----------
            float titleH = rh * 0.19f;
            float artistH = rh * 0.14f;

            string title = string.IsNullOrEmpty(_info.Title) ? "未知曲目" : _info.Title;
            Font titleFont = IconFont.GetText(ClampFont(titleH * 0.72f), FontStyle.Regular);
            Draw.Text(g, title, titleFont, theme.TextPrimary,
                new RectangleF(right.X, right.Y, right.Width, titleH), Draw.LeftCenter);

            string sub = _info.Artist;
            if (!string.IsNullOrEmpty(_info.AlbumTitle))
            {
                sub = string.IsNullOrEmpty(sub) ? _info.AlbumTitle : sub + " · " + _info.AlbumTitle;
            }
            if (string.IsNullOrEmpty(sub))
            {
                sub = AppName(_info.SourceAppId);
            }

            Font artistFont = IconFont.GetText(ClampFont(artistH * 0.80f), FontStyle.Regular);
            Draw.Text(g, sub, artistFont, theme.TextSecondary,
                new RectangleF(right.X, right.Y + titleH, right.Width, artistH), Draw.LeftCenter);

            // ---------- 按钮 ----------
            float btn = Math.Min(rh * 0.20f, rightW * 0.115f);
            btn = Math.Max(18f, btn);
            float playBtn = btn * 1.2f;
            float btnGap = Metrics.Px(14f);
            float totalW = btn * 2f + playBtn + btnGap * 2f;

            float bandTop = right.Y + titleH + artistH + rh * 0.035f;
            float bandH = Math.Max(playBtn, rh * 0.5f);
            float btnCenterY = bandTop + bandH / 2f;

            float bx = right.X + Math.Max(0f, (right.Width - totalW) / 2f);

            var prevBox = new RectangleF(bx, btnCenterY - btn / 2f, btn, btn);
            bx += btn + btnGap;
            var playBox = new RectangleF(bx, btnCenterY - playBtn / 2f, playBtn, playBtn);
            bx += playBtn + btnGap;
            var nextBox = new RectangleF(bx, btnCenterY - btn / 2f, btn, btn);

            AddHotspot(prevBox, delegate { Send(delegate (SmtcService s) { s.Previous(); }); });
            AddHotspot(playBox, delegate { Send(delegate (SmtcService s) { s.TogglePlayPause(); }); });
            AddHotspot(nextBox, delegate { Send(delegate (SmtcService s) { s.Next(); }); });

            PaintIconButton(g, theme, prevBox, Icons.Prev, btn * 0.56f);
            PaintAccentIconButton(g, theme, playBox, _info.IsPlaying ? Icons.Pause : Icons.Play, playBtn * 0.48f);
            PaintIconButton(g, theme, nextBox, Icons.Next, btn * 0.56f);

            // ---------- 音量 ----------
            float volH = Math.Max(14f, rh * 0.155f);
            // 贴住卡片底部，避免按钮和它之间留一大片空白
            float volRowTop = Math.Max(bandTop + bandH + Metrics.Px(4f), c.Bottom - volH);

            PaintVolume(g, theme, right, volRowTop, Math.Min(volH, c.Bottom - volRowTop));
        }

        private void PaintVolume(Graphics g, Theme theme, RectangleF right, float top, float height)
        {
            float iconW = height;
            float numW = height * 1.05f;
            float gap = Metrics.Px(10f);

            _speakerBox = new RectangleF(right.X, top, iconW, height);

            float barH = Math.Max(4f, height * 0.26f);
            float barX = _speakerBox.Right + gap;
            float barW = Math.Max(10f, right.Right - numW - gap - barX);
            _volumeBar = new RectangleF(barX, top + (height - barH) / 2f, barW, barH);

            // 喇叭图标：静音时换成带叉的。音量还没读到时按普通喇叭画，别误报成静音。
            bool silent = _muted || (_volumeKnown && _volumePercent <= 0);
            bool speakerHover = IsHovered(_speakerBox);
            if (speakerHover)
            {
                Draw.FillRounded(g, ColorUtil.WithAlpha(theme.TextPrimary, 26), _speakerBox,
                    _speakerBox.Height / 2f);
            }

            Color speakerColor = speakerHover ? theme.TextPrimary : theme.TextSecondary;
            Draw.Icon(g, silent ? Icons.SpeakerMute : Icons.Speaker, height * 0.68f,
                silent ? theme.AccentOnCard : speakerColor, _speakerBox);

            // 点击喇叭切换静音
            AddHotspot(_speakerBox, delegate { ToggleMute(); });

            // 音量条：遵循强调色
            float ratio = _volumeKnown ? _volumePercent / 100f : 0f;
            // 轨道用稍深一点的灰，浅色背景上也能看清
            Color track = ColorUtil.WithAlpha(theme.TextPrimary, 64);
            Draw.ProgressBar(g, _volumeBar, ratio, track, theme.AccentOnCard, barH / 2f);

            // 拖动时在条上留一个圆形手柄，手感更明确
            if (_dragging)
            {
                float knob = barH * 2.1f;
                float kx = _volumeBar.X + _volumeBar.Width * Math.Min(1f, Math.Max(0f, ratio));
                kx = Math.Min(_volumeBar.Right, Math.Max(_volumeBar.X, kx));
                var knobRect = new RectangleF(kx - knob / 2f, _volumeBar.Y + _volumeBar.Height / 2f - knob / 2f,
                    knob, knob);
                using (var brush = new SolidBrush(theme.AccentOnCard))
                {
                    g.FillEllipse(brush, knobRect);
                }
            }

            // 右侧只显示数字
            string text = _volumeKnown ? _volumePercent.ToString() : "--";
            Font font = IconFont.GetText(ClampFont(height * 0.70f), FontStyle.Regular);
            Draw.Text(g, text, font, theme.TextSecondary,
                new RectangleF(right.Right - numW, top, numW, height), Draw.RightCenter);
        }

        private void ToggleMute()
        {
            bool target = !_muted;
            if (SystemVolume.TrySetMute(target))
            {
                _muted = target;
                _volumeKnown = true;
                MarkDirty();
            }
        }

        /// <summary>把横坐标换算成 0..100 的音量并写入系统。</summary>
        private void SetVolumeFromX(float x)
        {
            if (_volumeBar.Width <= 0f)
            {
                return;
            }

            double fraction = (x - _volumeBar.X) / _volumeBar.Width;
            int percent = (int)Math.Round(fraction * 100.0);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            // 调音量时顺手取消静音，符合直觉
            if (_muted)
            {
                if (SystemVolume.TrySetMute(false))
                {
                    _muted = false;
                }
            }

            if (_volumeKnown && percent == _volumePercent)
            {
                return;
            }

            if (SystemVolume.TrySetVolume(percent))
            {
                _volumePercent = percent;
                _volumeKnown = true;
                MarkDirty();
            }
        }

        private bool InVolumeHitArea(PointF p)
        {
            if (_volumeBar.IsEmpty)
            {
                return false;
            }

            RectangleF hit = _volumeBar;
            hit.Inflate(Metrics.Px(6f), Metrics.Px(10f));
            return hit.Contains(p);
        }

        public override bool MouseDown(PointF p)
        {
            if (InVolumeHitArea(p))
            {
                _dragging = true;
                SetVolumeFromX(p.X);
                MarkDirty();
                return true;
            }
            return false;
        }

        public override bool MouseMove(PointF p)
        {
            if (!_dragging)
            {
                return false;
            }
            SetVolumeFromX(p.X);
            return true;
        }

        public override bool MouseUp(PointF p)
        {
            if (_dragging)
            {
                _dragging = false;
                // 松手时再对齐一次系统状态
                RefreshVolumeAfterDrag();
                MarkDirty();
                return true;
            }
            return base.MouseUp(p);
        }

        private void RefreshVolumeAfterDrag()
        {
            int volume;
            if (SystemVolume.TryGetVolume(out volume))
            {
                _volumePercent = volume;
                _volumeKnown = true;
            }
            bool muted;
            if (SystemVolume.TryGetMute(out muted))
            {
                _muted = muted;
            }
        }

        public override Cursor CursorAt(PointF p)
        {
            if (InVolumeHitArea(p) || (!_speakerBox.IsEmpty && _speakerBox.Contains(p)))
            {
                return Cursors.Hand;
            }
            return base.CursorAt(p);
        }

        private void Send(Action<SmtcService> action)
        {
            SmtcService s = _service;
            if (s != null && action != null)
            {
                action(s);
            }
        }

        private void PaintCover(Graphics g, Theme theme, AppSettings settings, RectangleF cover)
        {
            float radius = Metrics.Px(settings.CornerRadius * 0.7f);

            if (_cover != null)
            {
                // 等比裁切填满方框，避免变形
                float scale = Math.Max(cover.Width / _cover.Width, cover.Height / _cover.Height);
                float dw = _cover.Width * scale;
                float dh = _cover.Height * scale;
                var dest = new RectangleF(
                    cover.X + (cover.Width - dw) / 2f,
                    cover.Y + (cover.Height - dh) / 2f,
                    dw, dh);

                using (GraphicsPath path = Draw.RoundedPath(cover, radius))
                {
                    GraphicsState state = g.Save();
                    g.SetClip(path);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_cover, dest);
                    g.Restore(state);
                }
            }
            else
            {
                Color bg = ColorUtil.WithAlpha(theme.TextPrimary, 22);
                Draw.FillRounded(g, bg, cover, radius);
                Draw.Icon(g, Icons.Music, cover.Height * 0.42f, theme.TextTertiary, cover);
            }

            Draw.DrawRoundedBorder(g, theme.CardBorder, cover, radius, 1f);
        }

        private void PaintPlaceholder(Graphics g, Theme theme, AppSettings settings, RectangleF c)
        {
            float coverSize = Math.Min(c.Height, c.Width * 0.34f);
            coverSize = Math.Max(24f, coverSize);
            var cover = new RectangleF(c.X, c.Y + (c.Height - coverSize) / 2f, coverSize, coverSize);

            float radius = Metrics.Px(settings.CornerRadius * 0.7f);
            Draw.FillRounded(g, ColorUtil.WithAlpha(theme.TextPrimary, 18), cover, radius);
            Draw.Icon(g, Icons.Music, cover.Height * 0.42f, theme.TextTertiary, cover);

            float rightX = cover.Right + Metrics.Px(16f);
            float rightW = c.Right - rightX;
            if (rightW < 60f)
            {
                return;
            }

            string title = "没有正在播放的媒体";
            string sub;

            if (_info != null && !string.IsNullOrEmpty(_info.Error))
            {
                title = "媒体播控不可用";
                sub = _info.Error;
            }
            else
            {
                sub = "打开任意播放器开始播放后会自动显示";
            }

            Font titleFont = IconFont.GetText(ClampFont(c.Height * 0.135f), FontStyle.Regular);
            Draw.Text(g, title, titleFont, theme.TextSecondary,
                new RectangleF(rightX, c.Y, rightW, c.Height * 0.5f), Draw.LeftBottom);

            Font subFont = IconFont.GetText(ClampFont(c.Height * 0.088f), FontStyle.Regular);
            Draw.Text(g, sub, subFont, theme.TextTertiary,
                new RectangleF(rightX, c.Y + c.Height * 0.5f, rightW, c.Height * 0.4f), Draw.WrapLeft);
        }

        private static string AppName(string appUserModelId)
        {
            if (string.IsNullOrEmpty(appUserModelId))
            {
                return string.Empty;
            }

            int bang = appUserModelId.IndexOf('!');
            if (bang > 0)
            {
                return appUserModelId.Substring(0, bang);
            }
            return appUserModelId;
        }

        private static float ClampFont(float v)
        {
            if (v < 8f) return 8f;
            if (v > 200f) return 200f;
            return v;
        }

        public override void Dispose()
        {
            if (_cover != null)
            {
                _cover.Dispose();
                _cover = null;
            }
            base.Dispose();
        }
    }
}
