using System;
using System.IO;

namespace SubScreen.Core
{
    /// <summary>
    /// 应用的目录布局。所有可写数据都放在 exe 同级的 config 目录下。
    /// </summary>
    internal static class AppPaths
    {
        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string ConfigDir
        {
            get { return Path.Combine(BaseDir, "config"); }
        }

        public static string BackgroundDir
        {
            get { return Path.Combine(ConfigDir, "background"); }
        }

        public static string FontsDir
        {
            get { return Path.Combine(ConfigDir, "fonts"); }
        }

        public static string CacheDir
        {
            get { return Path.Combine(ConfigDir, "cache"); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(ConfigDir, "settings.ini"); }
        }

        /// <summary>背景文件的固定主名（不含扩展名）。</summary>
        public const string BackgroundBaseName = "bg";

        public static void EnsureCreated()
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(BackgroundDir);
            Directory.CreateDirectory(FontsDir);
            Directory.CreateDirectory(CacheDir);
        }

        /// <summary>返回当前实际存在的 bg.* 文件完整路径；不存在返回 null。</summary>
        public static string FindBackgroundFile()
        {
            try
            {
                if (!Directory.Exists(BackgroundDir))
                {
                    return null;
                }

                string[] files = Directory.GetFiles(BackgroundDir, BackgroundBaseName + ".*");
                if (files.Length == 0)
                {
                    return null;
                }

                // 多个残留时取修改时间最新的
                string best = files[0];
                DateTime bestTime = File.GetLastWriteTimeUtc(best);
                for (int i = 1; i < files.Length; i++)
                {
                    DateTime t = File.GetLastWriteTimeUtc(files[i]);
                    if (t > bestTime)
                    {
                        bestTime = t;
                        best = files[i];
                    }
                }
                return best;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 把源图片复制成 config/background/bg.&lt;ext&gt;，并删除其它 bg.* 残留。
        /// 返回新文件的完整路径。
        /// </summary>
        public static string InstallBackground(string sourcePath)
        {
            EnsureCreated();
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".img";
            }

            string target = Path.Combine(BackgroundDir, BackgroundBaseName + ext.ToLowerInvariant());
            bool sameFile = string.Equals(
                Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);

            if (!sameFile)
            {
                // 先删掉同名旧文件，避免 Copy 因占用失败
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Copy(sourcePath, target, true);
            }

            CleanupBackgrounds(Path.GetFileName(target));
            return target;
        }

        /// <summary>删除 config/background 下除 keepFileName 之外的所有 bg.* 文件。</summary>
        public static void CleanupBackgrounds(string keepFileName)
        {
            try
            {
                if (!Directory.Exists(BackgroundDir))
                {
                    return;
                }

                foreach (string f in Directory.GetFiles(BackgroundDir, BackgroundBaseName + ".*"))
                {
                    if (!string.Equals(Path.GetFileName(f), keepFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(f);
                        }
                        catch (Exception)
                        {
                            // 被占用就留着，下次启动再清理
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>删除当前背景文件（用于切换到纯色背景）。</summary>
        public static void RemoveAllBackgrounds()
        {
            CleanupBackgrounds(null);
        }
    }
}
