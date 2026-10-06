using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SubScreen.Media
{
    /// <summary>一次播控采样结果。</summary>
    internal sealed class MediaSessionInfo
    {
        public bool HasSession;

        public string SourceAppId = string.Empty;
        public string Title = string.Empty;
        public string Artist = string.Empty;
        public string AlbumTitle = string.Empty;

        public bool IsPlaying;

        /// <summary>
        /// 非 null 表示封面变了。**所有权移交给接收方**，接收方负责 Dispose；
        /// 没变时为 null，接收方沿用上一次的封面。
        /// </summary>
        public Bitmap NewThumbnail;

        /// <summary>出错时的说明（用于界面上提示，没有会话不算错误）。</summary>
        public string Error = string.Empty;
    }

    /// <summary>
    /// 通过 Windows SMTC（Windows.Media.Control）读取并控制当前媒体会话。
    ///
    /// 所有 WinRT 调用都在自己创建的 MTA 线程上完成；指令通过队列送到同一线程执行，
    /// 因为会话对象是线程亲和的指针。
    /// </summary>
    internal sealed class SmtcService : IDisposable
    {
        // 需要的接口 IID，同样是从 winmd 元数据里读出来的
        private static readonly Guid IidSessionManagerStatics =
            new Guid("2050C4EE-11A0-57DE-AED7-C97C70338245");
        private static readonly Guid IidDataReaderFactory =
            new Guid("D7527847-57DA-4E15-914C-06806699A098");
        private static readonly Guid IidBufferByteAccess =
            new Guid("905A0FEF-BC53-11DF-8C49-001E4FC686DA");

        private const string SessionManagerClass =
            "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
        private const string DataReaderClass = "Windows.Storage.Streams.DataReader";

        private const int AwaitTimeoutMs = 3000;

        /// <summary>拿会话管理器失败后的重试间隔，避免每秒都去撞一次 3 秒的超时。</summary>
        private const int ManagerRetryMs = 30000;

        private readonly object _sync = new object();
        private readonly Queue<Action<IntPtr>> _commands = new Queue<Action<IntPtr>>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private SynchronizationContext _context;
        private Thread _thread;
        private volatile bool _running;
        private int _refreshMs = 1000;
        private int _nextManagerAttempt;

        /// <summary>最近一次不可恢复的错误（WinRT 不可用等），正常时为 null。</summary>
        public volatile string FatalError;

        /// <summary>在 UI 线程上回调。每次采样都会触发一次。</summary>
        public event Action<MediaSessionInfo> Updated;

        public void Start(SynchronizationContext uiContext, int refreshSeconds)
        {
            if (_running)
            {
                SetRefreshSeconds(refreshSeconds);
                return;
            }

            _context = uiContext;
            _refreshMs = Math.Max(200, refreshSeconds * 1000);
            _running = true;

            _thread = new Thread(WorkerLoop);
            _thread.IsBackground = true;
            _thread.Name = "SubScreen.SMTC";
            _thread.Start();
        }

        public void SetRefreshSeconds(int refreshSeconds)
        {
            _refreshMs = Math.Max(200, refreshSeconds * 1000);
            _wake.Set();
        }

        public void Stop()
        {
            _running = false;
            _wake.Set();
            Thread t = _thread;
            if (t != null && t.IsAlive)
            {
                t.Join(1500);
            }
            _thread = null;
        }

        public void TogglePlayPause()
        {
            Enqueue(delegate (IntPtr session)
            {
                // slot 20 = TryTogglePlayPauseAsync
                InvokeAsyncBool(session, 20);
            });
        }

        public void Next()
        {
            Enqueue(delegate (IntPtr session)
            {
                // slot 16 = TrySkipNextAsync
                InvokeAsyncBool(session, 16);
            });
        }

        public void Previous()
        {
            Enqueue(delegate (IntPtr session)
            {
                // slot 17 = TrySkipPreviousAsync
                InvokeAsyncBool(session, 17);
            });
        }

        /// <summary>跳转到 0..1 的位置。</summary>
        public void SeekFraction(double fraction)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            double f = fraction;

            Enqueue(delegate (IntPtr session)
            {
                try
                {
                    // 先拿时长（slot 8 = GetTimelineProperties，slot 7 = get_EndTime）
                    GetTimelinePropertiesFn getTimeline =
                        WinRt.GetMethod<GetTimelinePropertiesFn>(session, 8);
                    IntPtr timeline;
                    if (getTimeline(session, out timeline) < 0 || timeline == IntPtr.Zero)
                    {
                        return;
                    }

                    long endTicks = 0;
                    try
                    {
                        GetInt64PropertyFn getEnd = WinRt.GetMethod<GetInt64PropertyFn>(timeline, 7);
                        getEnd(timeline, out endTicks);
                    }
                    finally
                    {
                        WinRt.Release(timeline);
                    }

                    if (endTicks <= 0)
                    {
                        return;
                    }

                    long target = (long)(endTicks * f);
                    // slot 24 = TryChangePlaybackPositionAsync
                    TryChangePositionFn fn = WinRt.GetMethod<TryChangePositionFn>(session, 24);
                    IntPtr op;
                    if (fn(session, target, out op) >= 0 && op != IntPtr.Zero)
                    {
                        WinRt.WaitCompleted(op, AwaitTimeoutMs);
                        WinRt.Release(op);
                    }
                }
                catch (Exception)
                {
                }
            });
        }

        /// <summary>
        /// 同步采样一次当前媒体会话，只为 `--capture` 截图用（正常运行时走后台线程）。
        ///
        /// 必须丢到一个 MTA 线程上去做：WinRT 异步操作在 STA 公寓里需要消息泵才能推进，
        /// 而截图是在 UI 线程上同步跑的，直接调用会出现"标题拿得到、封面拿不到"这种半成功。
        /// 走 MTA 就和大程序运行时的后台线程一致了。
        /// </summary>
        internal static MediaSessionInfo CaptureOnce(int timeoutMs)
        {
            MediaSessionInfo result = null;

            var worker = new Thread(delegate ()
            {
                try
                {
                    result = CaptureOnWorker(timeoutMs);
                }
                catch (Exception ex)
                {
                    var failed = new MediaSessionInfo();
                    failed.Error = ex.Message;
                    result = failed;
                }
            });
            worker.IsBackground = true;
            worker.Name = "SubScreen.SMTC.Capture";
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            if (!worker.Join(timeoutMs + 5000))
            {
                var timedOut = new MediaSessionInfo();
                timedOut.Error = "采样超时";
                return timedOut;
            }

            return result ?? new MediaSessionInfo();
        }

        private static MediaSessionInfo CaptureOnWorker(int timeoutMs)
        {
            var info = new MediaSessionInfo();

            IntPtr factory = IntPtr.Zero;
            IntPtr op = IntPtr.Zero;
            IntPtr manager = IntPtr.Zero;
            IntPtr session = IntPtr.Zero;

            try
            {
                WinRt.Initialize();

                factory = WinRt.GetActivationFactory(SessionManagerClass, IidSessionManagerStatics);
                RequestAsyncFn request = WinRt.GetMethod<RequestAsyncFn>(factory, 6);
                if (request(factory, out op) < 0 || op == IntPtr.Zero)
                {
                    info.Error = "调用 SMTC RequestAsync 失败";
                    return info;
                }

                int errorCode;
                manager = WinRt.AwaitObject(op, timeoutMs, out errorCode);
                if (manager == IntPtr.Zero)
                {
                    info.Error = DescribeFailure(errorCode);
                    return info;
                }

                GetCurrentSessionFn getCurrent = WinRt.GetMethod<GetCurrentSessionFn>(manager, 6);
                if (getCurrent(manager, out session) < 0 || session == IntPtr.Zero)
                {
                    info.HasSession = false;
                    return info;
                }

                string lastThumbKey = null;
                info = Sample(session, ref lastThumbKey);
                return info;
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
                return info;
            }
            finally
            {
                if (session != IntPtr.Zero) WinRt.Release(session);
                if (manager != IntPtr.Zero) WinRt.Release(manager);
                if (op != IntPtr.Zero) WinRt.Release(op);
                if (factory != IntPtr.Zero) WinRt.Release(factory);
            }
        }

        private void Enqueue(Action<IntPtr> command)
        {
            lock (_sync)
            {
                _commands.Enqueue(command);
            }
            _wake.Set();
        }

        // ---------------- 工作线程 ----------------

        private void WorkerLoop()
        {
            IntPtr manager = IntPtr.Zero;
            IntPtr session = IntPtr.Zero;
            string lastThumbKey = null;

            try
            {
                WinRt.Initialize();
            }
            catch (Exception ex)
            {
                FatalError = "初始化 Windows 运行时失败：" + ex.Message;
                Publish(ErrorInfo(FatalError));
                return;
            }

            try
            {
                while (_running)
                {
                    // 1) 先处理待执行的播控指令
                    Action<IntPtr> command = null;
                    while (true)
                    {
                        lock (_sync)
                        {
                            if (_commands.Count == 0)
                            {
                                break;
                            }
                            command = _commands.Dequeue();
                        }

                        try
                        {
                            command(session);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // 2) 拿会话管理器（失败则退避一段时间再重试）
                    if (manager == IntPtr.Zero)
                    {
                        if (unchecked(Environment.TickCount - _nextManagerAttempt) < 0)
                        {
                            WaitForNextTick();
                            continue;
                        }

                        manager = TryGetManager();
                        if (manager == IntPtr.Zero)
                        {
                            _nextManagerAttempt = unchecked(Environment.TickCount + ManagerRetryMs);
                            Publish(ErrorInfo(FatalError ?? "无法连接 Windows 媒体会话管理器"));
                            WaitForNextTick();
                            continue;
                        }
                    }

                    // 3) 拿当前会话
                    IntPtr current = IntPtr.Zero;
                    try
                    {
                        GetCurrentSessionFn get = WinRt.GetMethod<GetCurrentSessionFn>(manager, 6);
                        IntPtr p;
                        if (get(manager, out p) >= 0)
                        {
                            current = p;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    if (current == IntPtr.Zero)
                    {
                        if (session != IntPtr.Zero)
                        {
                            WinRt.Release(session);
                            session = IntPtr.Zero;
                            lastThumbKey = null;
                        }

                        var none = new MediaSessionInfo();
                        none.HasSession = false;
                        Publish(none);
                        WaitForNextTick();
                        continue;
                    }

                    // 会话对象变了就换掉旧的
                    if (session != current)
                    {
                        if (session != IntPtr.Zero)
                        {
                            WinRt.Release(session);
                        }
                        session = current;
                        lastThumbKey = null;
                    }
                    else
                    {
                        WinRt.Release(current);
                    }

                    // 4) 采样
                    MediaSessionInfo info = Sample(session, ref lastThumbKey);
                    Publish(info);

                    WaitForNextTick();
                }
            }
            finally
            {
                if (session != IntPtr.Zero)
                {
                    WinRt.Release(session);
                }
                if (manager != IntPtr.Zero)
                {
                    WinRt.Release(manager);
                }
                WinRt.Uninitialize();
            }
        }

        private void WaitForNextTick()
        {
            _wake.WaitOne(_refreshMs);
        }

        private IntPtr TryGetManager()
        {
            try
            {
                IntPtr factory = WinRt.GetActivationFactory(SessionManagerClass, IidSessionManagerStatics);
                try
                {
                    // slot 6 = RequestAsync
                    RequestAsyncFn request = WinRt.GetMethod<RequestAsyncFn>(factory, 6);
                    IntPtr op;
                    if (request(factory, out op) < 0 || op == IntPtr.Zero)
                    {
                        FatalError = "调用 SMTC RequestAsync 失败";
                        return IntPtr.Zero;
                    }

                    try
                    {
                        int errorCode;
                        IntPtr mgr = WinRt.AwaitObject(op, AwaitTimeoutMs, out errorCode);
                        if (mgr == IntPtr.Zero)
                        {
                            FatalError = DescribeFailure(errorCode);
                            return IntPtr.Zero;
                        }
                        FatalError = null;
                        return mgr;
                    }
                    finally
                    {
                        WinRt.Release(op);
                    }
                }
                finally
                {
                    WinRt.Release(factory);
                }
            }
            catch (Exception ex)
            {
                FatalError = "无法访问 Windows 媒体会话：" + ex.Message;
                return IntPtr.Zero;
            }
        }

        private static MediaSessionInfo Sample(IntPtr session, ref string lastThumbKey)
        {
            var info = new MediaSessionInfo();
            info.HasSession = true;

            try
            {
                info.SourceAppId = WinRt.GetStringProperty(session, 6);

                // 播放状态：slot 9 = GetPlaybackInfo，slot 7 = get_PlaybackStatus
                GetPlaybackInfoFn getPlayback = WinRt.GetMethod<GetPlaybackInfoFn>(session, 9);
                IntPtr playback;
                if (getPlayback(session, out playback) >= 0 && playback != IntPtr.Zero)
                {
                    try
                    {
                        GetIntPropertyFn getStatus = WinRt.GetMethod<GetIntPropertyFn>(playback, 7);
                        int status;
                        if (getStatus(playback, out status) >= 0)
                        {
                            // 0 Closed, 1 Opened, 2 Changing, 3 Stopped, 4 Playing, 5 Paused
                            info.IsPlaying = status == 4;
                        }
                    }
                    finally
                    {
                        WinRt.Release(playback);
                    }
                }

                // 媒体信息：slot 7 = TryGetMediaPropertiesAsync
                TryGetMediaPropertiesFn getProps = WinRt.GetMethod<TryGetMediaPropertiesFn>(session, 7);
                IntPtr op;
                if (getProps(session, out op) >= 0 && op != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr props = WinRt.AwaitObject(op, AwaitTimeoutMs);
                        if (props != IntPtr.Zero)
                        {
                            try
                            {
                                info.Title = WinRt.GetStringProperty(props, 6);
                                info.Artist = WinRt.GetStringProperty(props, 9);
                                info.AlbumTitle = WinRt.GetStringProperty(props, 10);

                                string key = info.Title + "\u0001" + info.Artist + "\u0001" + info.AlbumTitle;
                                if (key != lastThumbKey)
                                {
                                    info.NewThumbnail = TryLoadThumbnail(props);
                                    // 只有真的拿到新封面才更新 key，否则下一轮还会重试
                                    if (info.NewThumbnail != null || string.IsNullOrEmpty(info.Title))
                                    {
                                        lastThumbKey = key;
                                    }
                                }
                            }
                            finally
                            {
                                WinRt.Release(props);
                            }
                        }
                    }
                    finally
                    {
                        WinRt.Release(op);
                    }
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }

            return info;
        }

        /// <summary>
        /// 读封面图。所有权移交给调用方。
        ///
        /// 这里刻意不调用流对象上的任何方法：
        /// OpenReadAsync 返回的是 IRandomAccessStreamWithContentType，它自己没有方法，
        /// 而 IRandomAccessStream 的方法（get_Size / GetInputStreamAt）到底在不在它的 vtable 里，
        /// 取决于 ABI 有没有把基接口内联进来 —— 这个不能靠猜。
        /// 所以直接 QI 出 IInputStream 交给 DataReader，靠 LoadAsync + UnconsumedBufferLength
        /// 拿实际长度，彻底绕开这个不确定性。
        /// </summary>
        private static Bitmap TryLoadThumbnail(IntPtr props)
        {
            const uint MaxBytes = 8 * 1024 * 1024;

            IntPtr streamRef = IntPtr.Zero;
            IntPtr op = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;
            IntPtr input = IntPtr.Zero;
            IntPtr factory = IntPtr.Zero;
            IntPtr reader = IntPtr.Zero;
            IntPtr loadOp = IntPtr.Zero;

            try
            {
                // slot 15 = IGlobalSystemMediaTransportControlsSessionMediaProperties.get_Thumbnail
                GetObjectPropertyFn getThumb = WinRt.GetMethod<GetObjectPropertyFn>(props, 15);
                if (getThumb(props, out streamRef) < 0 || streamRef == IntPtr.Zero)
                {
                    return null;
                }

                // slot 6 = IRandomAccessStreamReference.OpenReadAsync
                RequestAsyncFn openRead = WinRt.GetMethod<RequestAsyncFn>(streamRef, 6);
                if (openRead(streamRef, out op) < 0 || op == IntPtr.Zero)
                {
                    return null;
                }

                stream = WinRt.AwaitObject(op, AwaitTimeoutMs);
                if (stream == IntPtr.Zero)
                {
                    return null;
                }

                // 不调用流上的方法，直接 QI 出 IInputStream
                input = WinRt.QueryInterface(stream, WinRt.IidInputStream);
                if (input == IntPtr.Zero)
                {
                    return null;
                }

                factory = WinRt.GetActivationFactory(DataReaderClass, IidDataReaderFactory);
                // slot 6 = IDataReaderFactory.CreateDataReader
                CreateDataReaderFn create = WinRt.GetMethod<CreateDataReaderFn>(factory, 6);
                if (create(factory, input, out reader) < 0 || reader == IntPtr.Zero)
                {
                    return null;
                }

                // slot 29 = IDataReader.LoadAsync(count)
                LoadAsyncFn load = WinRt.GetMethod<LoadAsyncFn>(reader, 29);
                if (load(reader, MaxBytes, out loadOp) < 0 || loadOp == IntPtr.Zero)
                {
                    return null;
                }

                uint loaded = WinRt.AwaitUInt(loadOp, AwaitTimeoutMs);
                if (loaded == 0)
                {
                    return null;
                }

                // slot 6 = IDataReader.get_UnconsumedBufferLength，即实际读到的字节数
                GetUIntPropertyFn getUnconsumed = WinRt.GetMethod<GetUIntPropertyFn>(reader, 6);
                uint available;
                if (getUnconsumed(reader, out available) < 0 || available == 0)
                {
                    return null;
                }
                if (available > loaded)
                {
                    available = loaded;
                }

                // slot 15 = IDataReader.ReadBuffer(length)
                ReadBufferFn readBuffer = WinRt.GetMethod<ReadBufferFn>(reader, 15);
                IntPtr buffer;
                if (readBuffer(reader, available, out buffer) < 0 || buffer == IntPtr.Zero)
                {
                    return null;
                }

                byte[] bytes;
                try
                {
                    bytes = ReadBufferBytes(buffer);
                }
                finally
                {
                    WinRt.Release(buffer);
                }

                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                using (var ms = new MemoryStream(bytes))
                using (Image img = Image.FromStream(ms, true, true))
                {
                    return new Bitmap(img);
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (loadOp != IntPtr.Zero) WinRt.Release(loadOp);
                if (reader != IntPtr.Zero) WinRt.Release(reader);
                if (factory != IntPtr.Zero) WinRt.Release(factory);
                if (input != IntPtr.Zero) WinRt.Release(input);
                if (stream != IntPtr.Zero) WinRt.Release(stream);
                if (op != IntPtr.Zero) WinRt.Release(op);
                if (streamRef != IntPtr.Zero) WinRt.Release(streamRef);
            }
        }

        private static byte[] ReadBufferBytes(IntPtr buffer)
        {
            // slot 7 = get_Length
            GetUIntPropertyFn getLength = WinRt.GetMethod<GetUIntPropertyFn>(buffer, 7);
            uint length;
            if (getLength(buffer, out length) < 0 || length == 0)
            {
                return null;
            }

            IntPtr byteAccess = WinRt.QueryInterface(buffer, IidBufferByteAccess);
            if (byteAccess == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // IBufferByteAccess 是普通 COM 接口（非 IInspectable），Buffer 在 slot 3
                BufferFn getBytes = WinRt.GetMethod<BufferFn>(byteAccess, 3);
                IntPtr data;
                if (getBytes(byteAccess, out data) < 0 || data == IntPtr.Zero)
                {
                    return null;
                }

                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                return bytes;
            }
            finally
            {
                WinRt.Release(byteAccess);
            }
        }

        private void InvokeAsyncBool(IntPtr session, int slot)
        {
            if (session == IntPtr.Zero)
            {
                return;
            }

            try
            {
                RequestAsyncFn fn = WinRt.GetMethod<RequestAsyncFn>(session, slot);
                IntPtr op;
                if (fn(session, out op) >= 0 && op != IntPtr.Zero)
                {
                    try
                    {
                        WinRt.AwaitBool(op, AwaitTimeoutMs);
                    }
                    finally
                    {
                        WinRt.Release(op);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static MediaSessionInfo ErrorInfo(string message)
        {
            var info = new MediaSessionInfo();
            info.HasSession = false;
            info.Error = message ?? string.Empty;
            return info;
        }

        /// <summary>把等待失败的 errorCode 翻译成人能看懂的原因。</summary>
        private static string DescribeFailure(int errorCode)
        {
            if (errorCode == -1)
            {
                return string.Format(
                    "SMTC 请求超时：Windows 在 {0} 秒内没有响应媒体会话请求",
                    Math.Max(1, AwaitTimeoutMs / 1000));
            }
            if (errorCode == 0)
            {
                return "SMTC 会话管理器不可用";
            }
            if (errorCode == -2)
            {
                return "SMTC 异步操作失败，且系统没有给出具体错误码";
            }
            if (errorCode == unchecked((int)0x80070005))
            {
                return "Windows 拒绝访问媒体会话（0x80070005 拒绝访问）。" +
                       "未打包的桌面应用在部分系统版本上会被 SMTC 拒之门外。";
            }
            if (errorCode == unchecked((int)0x80070490))
            {
                return "系统里没有找到媒体会话组件（0x80070490 找不到元素）";
            }
            return string.Format("SMTC 会话管理器请求失败（HRESULT=0x{0:X8}）", errorCode);
        }

        private void Publish(MediaSessionInfo info)
        {
            Action<MediaSessionInfo> handler = Updated;
            if (handler == null)
            {
                return;
            }

            SynchronizationContext ctx = _context;
            if (ctx != null)
            {
                ctx.Post(delegate { handler(info); }, null);
            }
            else
            {
                handler(info);
            }
        }

        public void Dispose()
        {
            Stop();
            if (_wake != null)
            {
                _wake.Close();
            }
        }

        // ---------------- 委托签名（slot 见调用处注释） ----------------

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RequestAsyncFn(IntPtr self, out IntPtr operation);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentSessionFn(IntPtr self, out IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetPlaybackInfoFn(IntPtr self, out IntPtr info);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTimelinePropertiesFn(IntPtr self, out IntPtr timeline);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int TryGetMediaPropertiesFn(IntPtr self, out IntPtr operation);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int TryChangePositionFn(IntPtr self, long positionTicks, out IntPtr operation);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetIntPropertyFn(IntPtr self, out int value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInt64PropertyFn(IntPtr self, out long value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetUIntPropertyFn(IntPtr self, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetUInt64PropertyFn(IntPtr self, out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetObjectPropertyFn(IntPtr self, out IntPtr value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInputStreamAtFn(IntPtr self, ulong position, out IntPtr stream);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateDataReaderFn(IntPtr self, IntPtr inputStream, out IntPtr reader);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int LoadAsyncFn(IntPtr self, uint count, out IntPtr operation);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadBufferFn(IntPtr self, uint length, out IntPtr buffer);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int BufferFn(IntPtr self, out IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CloseFn(IntPtr self);
    }
}
