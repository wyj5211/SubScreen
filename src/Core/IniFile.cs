using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace SubScreen.Core
{
    /// <summary>
    /// 极简 INI 读写。保留节与键的书写顺序，注释行在保存时丢弃（设置文件由程序生成，不需要保留注释）。
    /// </summary>
    internal sealed class IniFile
    {
        private readonly List<string> _sectionOrder = new List<string>();
        private readonly Dictionary<string, List<string>> _keyOrder =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _values =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private const string GlobalSection = "";

        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            if (!File.Exists(path))
            {
                return ini;
            }

            string current = GlobalSection;
            ini.EnsureSection(current);

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception)
            {
                return ini;
            }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                {
                    continue;
                }

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end > 1)
                    {
                        current = line.Substring(1, end - 1).Trim();
                        ini.EnsureSection(current);
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                ini.Set(current, key, value);
            }

            return ini;
        }

        private void EnsureSection(string section)
        {
            if (_values.ContainsKey(section))
            {
                return;
            }
            _values[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _keyOrder[section] = new List<string>();
            _sectionOrder.Add(section);
        }

        public void Set(string section, string key, string value)
        {
            if (section == null)
            {
                section = GlobalSection;
            }
            EnsureSection(section);

            Dictionary<string, string> bag = _values[section];
            if (!bag.ContainsKey(key))
            {
                _keyOrder[section].Add(key);
            }
            bag[key] = value ?? string.Empty;
        }

        public string Get(string section, string key, string fallback)
        {
            if (section == null)
            {
                section = GlobalSection;
            }

            Dictionary<string, string> bag;
            if (_values.TryGetValue(section, out bag))
            {
                string v;
                if (bag.TryGetValue(key, out v))
                {
                    return v;
                }
            }
            return fallback;
        }

        public int GetInt(string section, string key, int fallback, int min, int max)
        {
            string raw = Get(section, key, null);
            int v;
            if (raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                return Clamp(v, min, max);
            }
            return fallback;
        }

        public double GetDouble(string section, string key, double fallback, double min, double max)
        {
            string raw = Get(section, key, null);
            double v;
            if (raw != null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return Math.Min(max, Math.Max(min, v));
            }
            return fallback;
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            string raw = Get(section, key, null);
            if (raw == null)
            {
                return fallback;
            }

            raw = raw.Trim();
            if (raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (raw.Equals("0", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("no", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return fallback;
        }

        public Color GetColor(string section, string key, Color fallback)
        {
            string raw = Get(section, key, null);
            Color parsed;
            if (!string.IsNullOrEmpty(raw) && ColorUtil.TryParseHex(raw, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public T GetEnum<T>(string section, string key, T fallback) where T : struct
        {
            string raw = Get(section, key, null);
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }

            try
            {
                // 忽略大小写匹配枚举名
                foreach (string name in Enum.GetNames(typeof(T)))
                {
                    if (string.Equals(name, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return (T)Enum.Parse(typeof(T), name);
                    }
                }
            }
            catch (Exception)
            {
            }
            return fallback;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; SubScreen 配置文件（程序生成，手动修改后重启生效）");

            foreach (string section in _sectionOrder)
            {
                bool isEmptyGlobal = section.Length == 0;
                if (isEmptyGlobal && _keyOrder[section].Count == 0)
                {
                    continue;
                }

                if (!isEmptyGlobal)
                {
                    sb.Append('[').Append(section).Append(']').AppendLine();
                }

                foreach (string key in _keyOrder[section])
                {
                    sb.Append(key).Append('=').Append(_values[section][key]).AppendLine();
                }

                if (!isEmptyGlobal)
                {
                    sb.AppendLine();
                }
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 先写临时文件再替换，避免写一半崩掉把配置弄坏
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }
    }

    internal static class ColorUtil
    {
        /// <summary>解析 #RRGGBB 或 #AARRGGBB。</summary>
        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
            }
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
            }

            uint v;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
            {
                return false;
            }

            if (s.Length <= 6)
            {
                color = Color.FromArgb(255, (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
                return true;
            }

            color = Color.FromArgb((int)((v >> 24) & 0xFF), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
            return true;
        }

        public static string ToHex(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        /// <summary>按比例调整明度。amount 为 -1..1，负值变暗，正值变亮。</summary>
        public static Color Shade(Color c, double amount)
        {
            if (amount < 0)
            {
                double f = 1.0 + amount;
                return Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
            }
            return Color.FromArgb(
                c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            if (alpha < 0)
            {
                alpha = 0;
            }
            if (alpha > 255)
            {
                alpha = 255;
            }
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>把前景色按 alpha 混合到背景色上，得到不透明结果。</summary>
        public static Color Blend(Color fore, Color back, double alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 1) alpha = 1;
            return Color.FromArgb(
                255,
                (int)(fore.R * alpha + back.R * (1 - alpha)),
                (int)(fore.G * alpha + back.G * (1 - alpha)),
                (int)(fore.B * alpha + back.B * (1 - alpha)));
        }

        /// <summary>感知亮度，用于决定在该颜色上放深色还是浅色文字。</summary>
        public static double Luminance(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }
    }
}
