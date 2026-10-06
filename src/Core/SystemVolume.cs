using System;
using System.Runtime.InteropServices;

namespace SubScreen.Core
{
    /// <summary>
    /// 系统主音量控制。
    ///
    /// 走 Windows Core Audio 的 IAudioEndpointVolume（经典 COM，不是 WinRT），
    /// 所以可以直接用 ComImport 声明接口，不需要任何第三方库。
    ///
    /// 只会从 UI 线程调用（媒体卡片），所以不做跨线程缓存 ——
    /// 每次调用现取现放，避免把 STA 上创建的接口指针带到别的线程去。
    /// </summary>
    internal static class SystemVolume
    {
        private const int EDataFlowRender = 0;      // eRender
        private const int ERoleMultimedia = 1;      // eMultimedia

        private static readonly Guid IidAudioEndpointVolume =
            new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr client);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);

            [PreserveSig]
            int OpenPropertyStore(int access, out IntPtr store);

            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

            [PreserveSig]
            int GetState(out int state);
        }

        /// <summary>方法顺序必须和 Windows SDK 的 IAudioEndpointVolume 声明一致。</summary>
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig]
            int RegisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int UnregisterControlChangeNotify(IntPtr notify);

            [PreserveSig]
            int GetChannelCount(out int count);

            [PreserveSig]
            int SetMasterVolumeLevel(float level, IntPtr context);

            [PreserveSig]
            int SetMasterVolumeLevelScalar(float level, IntPtr context);

            [PreserveSig]
            int GetMasterVolumeLevel(out float level);

            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);

            [PreserveSig]
            int SetChannelVolumeLevel(int channel, float level, IntPtr context);

            [PreserveSig]
            int SetChannelVolumeLevelScalar(int channel, float level, IntPtr context);

            [PreserveSig]
            int GetChannelVolumeLevel(int channel, out float level);

            [PreserveSig]
            int GetChannelVolumeLevelScalar(int channel, out float level);

            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);

            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

            [PreserveSig]
            int GetVolumeStepInfo(out int step, out int stepCount);

            [PreserveSig]
            int VolumeStepUp(IntPtr context);

            [PreserveSig]
            int VolumeStepDown(IntPtr context);

            [PreserveSig]
            int QueryHardwareSupport(out int mask);

            [PreserveSig]
            int GetVolumeRange(out float min, out float max, out float increment);
        }

        /// <summary>取默认播放设备的主音量接口。失败返回 null（例如没有音频设备）。</summary>
        private static IAudioEndpointVolume Acquire()
        {
            object enumeratorObject = null;
            IMMDevice device = null;
            try
            {
                enumeratorObject = new MMDeviceEnumerator();
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;

                if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device) < 0
                    || device == null)
                {
                    return null;
                }

                Guid iid = IidAudioEndpointVolume;
                object volumeObject;
                if (device.Activate(ref iid, 0 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out volumeObject) < 0
                    || volumeObject == null)
                {
                    return null;
                }

                return volumeObject as IAudioEndpointVolume;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // device / enumerator 由 RCW 托管，这里不额外 Release，
                // 让 GC 回收即可（每秒一次调用，压力可以忽略）。
                GC.KeepAlive(enumeratorObject);
            }
        }

        /// <summary>读取主音量，0..100。失败返回 false。</summary>
        public static bool TryGetVolume(out int percent)
        {
            percent = 0;
            try
            {
                IAudioEndpointVolume volume = Acquire();
                if (volume == null)
                {
                    return false;
                }

                float scalar;
                if (volume.GetMasterVolumeLevelScalar(out scalar) < 0)
                {
                    return false;
                }

                percent = ScalarToPercent(scalar);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>设置主音量，0..100。</summary>
        public static bool TrySetVolume(int percent)
        {
            try
            {
                IAudioEndpointVolume volume = Acquire();
                if (volume == null)
                {
                    return false;
                }

                return volume.SetMasterVolumeLevelScalar(PercentToScalar(percent), IntPtr.Zero) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryGetMute(out bool muted)
        {
            muted = false;
            try
            {
                IAudioEndpointVolume volume = Acquire();
                if (volume == null)
                {
                    return false;
                }

                bool value;
                if (volume.GetMute(out value) < 0)
                {
                    return false;
                }

                muted = value;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TrySetMute(bool muted)
        {
            try
            {
                IAudioEndpointVolume volume = Acquire();
                if (volume == null)
                {
                    return false;
                }

                return volume.SetMute(muted, IntPtr.Zero) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static int ScalarToPercent(float scalar)
        {
            int v = (int)Math.Round(scalar * 100.0);
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }

        public static float PercentToScalar(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return percent / 100f;
        }
    }
}
