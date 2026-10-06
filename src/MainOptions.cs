using System;

namespace SubScreen
{
    /// <summary>
    /// 启动参数（由 Program 在构造窗体前设置）。
    ///
    /// 单独放一个文件：WinForms 设计器只认"文件里的第一个类"，
    /// 把非窗体类型放在 Main.cs 里会导致 Main.cs 无法用设计器打开。
    /// </summary>
    internal sealed class MainOptions
    {
        /// <summary>非空时进入截图模式：渲染一张 PNG 后退出，用于离线核对界面。</summary>
        public string CapturePath;

        /// <summary>截图模式下填充演示数据（天气/一言/播控都用样例）。</summary>
        public bool Demo;

        public int CaptureWidth = 2160;
        public int CaptureHeight = 1080;

        /// <summary>非空时跑离线自检（INI 往返、农历、图标字体、APNG 探测等），把报告写到该路径。</summary>
        public string SelfTestPath;
    }
}
