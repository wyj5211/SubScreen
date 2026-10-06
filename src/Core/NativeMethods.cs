using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SubScreen.Core
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>取窗口 DPI，取不到时按系统 DPI，再取不到按 96。</summary>
        public static int GetWindowDpi(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi > 0)
                    {
                        return (int)dpi;
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi > 0)
                {
                    return (int)dpi;
                }
            }
            catch (Exception)
            {
            }

            return 96;
        }
    }
}
