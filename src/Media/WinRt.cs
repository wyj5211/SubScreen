using System;
using System.Runtime.InteropServices;

namespace SubScreen.Media
{
    /// <summary>
    /// 极薄的 WinRT 互操作层。
    ///
    /// 背景：本机没有 Windows SDK 的合并版 Windows.winmd，也没有 mdmerge，
    /// 无法给 .NET Framework 工程加 WinRT 引用，也没有 NuGet 可用。
    /// 所以这里直接按 WinRT 的 ABI 约定调用 COM vtable。
    ///
    /// vtable 布局：
    ///   slot 0..2 = IUnknown      (QueryInterface / AddRef / Release)
    ///   slot 3..5 = IInspectable  (GetIids / GetRuntimeClassName / GetTrustLevel)
    ///   slot 6..  = 该接口自己的方法
    ///
    /// **极其重要的一点（踩过坑）**：
    /// WinRT 元数据里接口之间的 InterfaceImpl（继承关系）**不代表 ABI vtable 会把基接口的方法内联进来**。
    /// 每个接口在 ABI 上都有自己独立的 vtable，基接口要通过 QueryInterface 单独取。
    ///
    /// 具体例子：IAsyncOperation&lt;T&gt; 的 vtable 是
    ///   slot 6 put_Completed / slot 7 get_Completed / slot 8 GetResults
    /// 而 IAsyncInfo（get_Id / get_Status / get_ErrorCode / Cancel / Close）是**同一个对象上的另一个接口**，
    /// 必须先 QueryInterface(IID_IAsyncInfo) 拿到它的指针，在它上面 slot 7 才是 get_Status。
    ///
    /// 如果按"内联"的假设去读，就会在 IAsyncOperation 上把 get_Completed 的返回值
    /// （未注册回调时是 null）当成 AsyncStatus 读成 0 = Started，于是操作"永远不完成"。
    /// 这个坑是通过实际渲染 + 逐槽位验证才定位到的。
    /// </summary>
    internal static class WinRt
    {
        public const int RoInitMultithreaded = 1;

        private const int SlotQueryInterface = 0;
        private const int SlotRelease = 2;

        /// <summary>IAsyncInfo 的 IID，用来把异步操作的状态接口单独 QI 出来。</summary>
        public static readonly Guid IidAsyncInfo = new Guid("00000036-0000-0000-C000-000000000046");

        /// <summary>IInputStream 的 IID。</summary>
        public static readonly Guid IidInputStream = new Guid("905A0FE2-BC53-11DF-8C49-001E4FC686DA");

        /// <summary>IBuffer 的 IID。</summary>
        public static readonly Guid IidBuffer = new Guid("905A0FE0-BC53-11DF-8C49-001E4FC686DA");

        /// <summary>IRandomAccessStream 的 IID。</summary>
        public static readonly Guid IidRandomAccessStream = new Guid("905A0FE1-BC53-11DF-8C49-001E4FC686DA");

        /// <summary>IAsyncOperation&lt;T&gt;.GetResults —— 注意不是 13。</summary>
        internal const int SlotAsyncOperationGetResults = 8;

        /// <summary>IAsyncOperationWithProgress&lt;T,P&gt;.GetResults。</summary>
        internal const int SlotAsyncOperationWithProgressGetResults = 10;

        /// <summary>IAsyncInfo.get_Status（在 QI 出来的 IAsyncInfo 指针上）。</summary>
        internal const int SlotAsyncInfoGetStatus = 7;

        /// <summary>IAsyncInfo.get_ErrorCode（在 QI 出来的 IAsyncInfo 指针上）。</summary>
        internal const int SlotAsyncInfoGetErrorCode = 8;

        /// <summary>IAsyncInfo.Close（在 QI 出来的 IAsyncInfo 指针上）。</summary>
        internal const int SlotAsyncInfoClose = 10;


        // ---------------- combase.dll ----------------

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern void RoUninitialize();

        [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        // ---------------- 委托签名 ----------------

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetStatusFn(IntPtr self, out int status);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetErrorCodeFn(IntPtr self, out int hr);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CloseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetObjectResultFn(IntPtr self, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetBoolResultFn(IntPtr self, out byte result);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetUIntResultFn(IntPtr self, out uint result);

        // ---------------- 生命周期 ----------------

        public static void Initialize()
        {
            int hr = RoInitialize(RoInitMultithreaded);

            // 0x80010106 = RPC_E_CHANGED_MODE：本线程已经被初始化为 STA（例如 WinForms 主线程）。
            // 这种情况下按 STA 再初始化一次即可，返回 S_FALSE 表示"本来就是"，同样算成功。
            if (hr == unchecked((int)0x80010106))
            {
                hr = RoInitialize(0 /* RO_INIT_SINGLETHREADED */);
            }

            if (hr < 0)
            {
                throw new InvalidOperationException("RoInitialize 失败，HRESULT=0x" + hr.ToString("X8"));
            }
        }

        public static void Uninitialize()
        {
            try
            {
                RoUninitialize();
            }
            catch (Exception)
            {
            }
        }

        // ---------------- vtable 调用 ----------------

        public static T GetMethod<T>(IntPtr pUnk, int slot) where T : class
        {
            if (pUnk == IntPtr.Zero)
            {
                throw new ArgumentNullException("pUnk");
            }

            IntPtr vtable = Marshal.ReadIntPtr(pUnk);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        public static int Release(IntPtr pUnk)
        {
            if (pUnk == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                ReleaseFn fn = GetMethod<ReleaseFn>(pUnk, SlotRelease);
                return fn(pUnk);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static IntPtr QueryInterface(IntPtr pUnk, Guid iid)
        {
            if (pUnk == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            QueryInterfaceFn fn = GetMethod<QueryInterfaceFn>(pUnk, SlotQueryInterface);
            IntPtr result;
            int hr = fn(pUnk, ref iid, out result);
            if (hr < 0)
            {
                return IntPtr.Zero;
            }
            return result;
        }

        public static void CloseObject(IntPtr pUnk)
        {
            if (pUnk == IntPtr.Zero)
            {
                return;
            }

            // Close 是 IAsyncInfo 的方法，必须先 QI 出 IAsyncInfo 才能调（它不在 IAsyncOperation 的 vtable 里）
            IntPtr asyncInfo = QueryInterface(pUnk, IidAsyncInfo);
            if (asyncInfo == IntPtr.Zero)
            {
                return;
            }

            try
            {
                CloseFn fn = GetMethod<CloseFn>(asyncInfo, SlotAsyncInfoClose);
                fn(asyncInfo);
            }
            catch (Exception)
            {
            }
            finally
            {
                Release(asyncInfo);
            }
        }

        // ---------------- HSTRING ----------------

        public static IntPtr CreateString(string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }

            IntPtr h;
            int hr = WindowsCreateString(value, value.Length, out h);
            if (hr < 0)
            {
                throw new InvalidOperationException("WindowsCreateString 失败，HRESULT=0x" + hr.ToString("X8"));
            }
            return h;
        }

        public static void DeleteString(IntPtr h)
        {
            if (h != IntPtr.Zero)
            {
                WindowsDeleteString(h);
            }
        }

        public static string ReadString(IntPtr h)
        {
            if (h == IntPtr.Zero)
            {
                return string.Empty;
            }

            uint len;
            IntPtr buf = WindowsGetStringRawBuffer(h, out len);
            if (buf == IntPtr.Zero || len == 0)
            {
                return string.Empty;
            }
            return Marshal.PtrToStringUni(buf, (int)len);
        }

        /// <summary>读一个返回 HSTRING 的属性（slot 指向 get_XXX），并释放 HSTRING。</summary>
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int GetStringFn(IntPtr self, out IntPtr value);

        internal static string GetStringProperty(IntPtr pUnk, int slot)
        {
            if (pUnk == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                GetStringFn fn = GetMethod<GetStringFn>(pUnk, slot);
                IntPtr h;
                int hr = fn(pUnk, out h);
                if (hr < 0 || h == IntPtr.Zero)
                {
                    return string.Empty;
                }

                try
                {
                    return ReadString(h);
                }
                finally
                {
                    DeleteString(h);
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // ---------------- 激活 ----------------

        /// <summary>按接口 IID 拿激活工厂（静态方法所在的对象）。</summary>
        public static IntPtr GetActivationFactory(string className, Guid iid)
        {
            IntPtr classId = CreateString(className);
            try
            {
                IntPtr factory;
                int hr = RoGetActivationFactory(classId, ref iid, out factory);
                if (hr < 0)
                {
                    throw new InvalidOperationException(
                        "无法激活 WinRT 类 " + className + "，HRESULT=0x" + hr.ToString("X8"));
                }
                return factory;
            }
            finally
            {
                DeleteString(classId);
            }
        }

        // ---------------- 异步操作 ----------------

        public delegate int GetInt64ResultFn(IntPtr self, out long result);

        /// <summary>
        /// 取 IAsyncOperation&lt;T&gt; 的结果。
        /// GetResults 在 slot 8（不是 13！13 是 IAsyncOperationWithProgress 之后的错误位）。
        /// </summary>
        internal static IntPtr AwaitObject(IntPtr operation, int timeoutMs)
        {
            int errorCode;
            return AwaitObject(operation, timeoutMs, out errorCode);
        }

        internal static IntPtr AwaitObject(IntPtr operation, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            if (!WaitCompleted(operation, timeoutMs, out errorCode))
            {
                return IntPtr.Zero;
            }

            GetObjectResultFn get = GetMethod<GetObjectResultFn>(operation, SlotAsyncOperationGetResults);
            IntPtr result;
            int hr = get(operation, out result);
            if (hr < 0)
            {
                errorCode = hr;
                return IntPtr.Zero;
            }
            return result;
        }

        internal static bool AwaitBool(IntPtr operation, int timeoutMs)
        {
            int errorCode;
            return AwaitBool(operation, timeoutMs, out errorCode);
        }

        internal static bool AwaitBool(IntPtr operation, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            if (!WaitCompleted(operation, timeoutMs, out errorCode))
            {
                return false;
            }

            GetBoolResultFn get = GetMethod<GetBoolResultFn>(operation, SlotAsyncOperationGetResults);
            byte result;
            int hr = get(operation, out result);
            if (hr < 0)
            {
                errorCode = hr;
                return false;
            }
            return result != 0;
        }

        internal static uint AwaitUInt(IntPtr operation, int timeoutMs)
        {
            int errorCode;
            return AwaitUInt(operation, timeoutMs, out errorCode);
        }

        internal static uint AwaitUInt(IntPtr operation, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            if (!WaitCompleted(operation, timeoutMs, out errorCode))
            {
                return 0;
            }

            GetUIntResultFn get = GetMethod<GetUIntResultFn>(operation, SlotAsyncOperationGetResults);
            uint result;
            int hr = get(operation, out result);
            if (hr < 0)
            {
                errorCode = hr;
                return 0;
            }
            return result;
        }

        /// <summary>IAsyncOperationWithProgress&lt;T,P&gt; 版本（GetResults 在 slot 10）。</summary>
        internal static IntPtr AwaitObjectWithProgress(IntPtr operation, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            if (!WaitCompleted(operation, timeoutMs, out errorCode))
            {
                return IntPtr.Zero;
            }

            GetObjectResultFn get = GetMethod<GetObjectResultFn>(operation, SlotAsyncOperationWithProgressGetResults);
            IntPtr result;
            int hr = get(operation, out result);
            if (hr < 0)
            {
                errorCode = hr;
                return IntPtr.Zero;
            }
            return result;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int NoArgAsyncFn(IntPtr self, out IntPtr operation);

        /// <summary>
        /// 调用某个 WinRT 类 statics 接口上的无参异步方法并等结果。
        /// 供自检使用，同时也是"异步状态接口有没有取对"的最直接的回归探针：
        /// 如果状态是在 IAsyncOperation 上而不是 QI 出来的 IAsyncInfo 上读，
        /// 这里会稳定地超时。
        /// </summary>
        internal static IntPtr InvokeStaticsAsync(
            string className, Guid staticsIid, int methodSlot, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            IntPtr factory = IntPtr.Zero;
            IntPtr op = IntPtr.Zero;
            try
            {
                factory = GetActivationFactory(className, staticsIid);
                NoArgAsyncFn fn = GetMethod<NoArgAsyncFn>(factory, methodSlot);
                if (fn(factory, out op) < 0 || op == IntPtr.Zero)
                {
                    errorCode = -3;
                    return IntPtr.Zero;
                }
                return AwaitObject(op, timeoutMs, out errorCode);
            }
            finally
            {
                if (op != IntPtr.Zero)
                {
                    Release(op);
                }
                if (factory != IntPtr.Zero)
                {
                    Release(factory);
                }
            }
        }

        internal static bool WaitCompleted(IntPtr operation, int timeoutMs)
        {
            int errorCode;
            return WaitCompleted(operation, timeoutMs, out errorCode);
        }

        /// <summary>
        /// 轮询等待异步操作结束。
        ///
        /// 这里的关键：状态查询必须走 **QueryInterface 出来的 IAsyncInfo 指针**。
        /// 直接拿 IAsyncOperation 指针去调 slot 7 会打到 get_Completed 上，
        /// 未注册回调时它返回 null，被当成 AsyncStatus 读就是 0（Started），
        /// 于是操作看上去"永远不完成"——这个坑就是这么来的。
        ///
        /// errorCode：-1 = 超时，-2 = 操作报错但拿不到 HRESULT，其它为 WinRT 返回的 HRESULT。
        /// </summary>
        internal static bool WaitCompleted(IntPtr operation, int timeoutMs, out int errorCode)
        {
            errorCode = 0;
            if (operation == IntPtr.Zero)
            {
                errorCode = -1;
                return false;
            }

            IntPtr asyncInfo = QueryInterface(operation, IidAsyncInfo);
            if (asyncInfo == IntPtr.Zero)
            {
                errorCode = -2;
                return false;
            }

            try
            {
                GetStatusFn getStatus = GetMethod<GetStatusFn>(asyncInfo, SlotAsyncInfoGetStatus);
                var start = Environment.TickCount;

                while (true)
                {
                    int status;
                    int hr = getStatus(asyncInfo, out status);
                    if (hr < 0)
                    {
                        errorCode = hr;
                        return false;
                    }

                    if (status == 1)
                    {
                        return true;
                    }

                    if (status == 2 || status == 3)
                    {
                        // Canceled / Error
                        GetErrorCodeFn getError = GetMethod<GetErrorCodeFn>(asyncInfo, SlotAsyncInfoGetErrorCode);
                        int code;
                        if (getError(asyncInfo, out code) < 0)
                        {
                            code = 0;
                        }
                        errorCode = code != 0 ? code : (status == 2 ? unchecked((int)0x800704C7) : -2);
                        return false;
                    }

                    if (timeoutMs > 0 && unchecked(Environment.TickCount - start) > timeoutMs)
                    {
                        errorCode = -1;
                        return false;
                    }

                    System.Threading.Thread.Sleep(8);
                }
            }
            finally
            {
                Release(asyncInfo);
            }
        }

        /// <summary>把 HRESULT 格式化成便于排查的文本。</summary>
        public static string DescribeHResult(int hr)
        {
            if (hr == -1)
            {
                return "超时";
            }
            if (hr == -2)
            {
                return "操作失败但未提供 HRESULT";
            }
            return "HRESULT=0x" + hr.ToString("X8");
        }
    }
}
