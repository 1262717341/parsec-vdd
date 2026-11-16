using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace ParsecVDisplay.Svc
{
    public class KeeperService : ServiceBase
    {
        Keeper _keeper;

        public KeeperService()
        {
            ServiceName = "ParsecVDisplaySvc";
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _keeper = new Keeper();
            _keeper.Start();

            // 关键：SCM 只有在 OnStart 返回后才把服务标记为“正在运行”，
            // 而 Sunshine 等串流服务依赖本服务，所以这里等显示器真正就绪再返回，
            // 就能保证“虚拟显示器先于串流软件存在”。
            if (_keeper.WaitUntilReady(20000))
                SvcLog.Write("服务已就绪：虚拟显示器已先于串流软件创建完成");
        }

        protected override void OnStop()
        {
            _keeper?.Stop();
            _keeper = null;
        }

        protected override void OnShutdown()
        {
            _keeper?.Stop();
            base.OnShutdown();
        }
    }

    /// <summary>
    /// 常驻守护：开机即以 SYSTEM 身份打开驱动句柄，按配置补齐显示器数量，
    /// 并每 80ms 发送一次 Update（驱动的存活心跳），让虚拟显示器在
    /// 登录/串流之前就存在，并且不再被反复插拔（设备 ID、显示器编号因此保持稳定）。
    /// 界面通过本机回环端口 <see cref="PORT"/> 下发 ADD / REMOVE / SET 命令。
    /// </summary>
    internal class Keeper
    {
        const string ADAPTER_GUID = "{00b41627-04c4-429e-a26e-0265cf50c8fa}";
        const string MONITOR_CLASS_GUID = "{4d36e96e-e325-11ce-bfc1-08002be10318}";
        const string MONITOR_HWID = "MONITOR\\PSCCDD0";

        public const int PORT = 47523;

        const uint IOCTL_ADD = 0x0022e004;
        const uint IOCTL_REMOVE = 0x0022a008;
        const uint IOCTL_UPDATE = 0x0022a00c;
        const uint IOCTL_VERSION = 0x0022e010;

        const int MAX_DISPLAYS = 8;

        readonly object _sync = new object();

        IntPtr _handle = IntPtr.Zero;
        int _count;
        int _desired;
        volatile bool _stop;

        Thread _worker;
        Thread _server;

        bool HandleValid
        {
            get { return _handle != IntPtr.Zero && _handle != (IntPtr)(-1); }
        }

        public void Start()
        {
            _stop = false;
            _desired = SvcConfig.GetDisplayCount();

            if (_desired < 0) _desired = 0;
            if (_desired > MAX_DISPLAYS) _desired = MAX_DISPLAYS;

            SvcLog.Write($"---------- 服务启动，期望虚拟显示器数量：{_desired} ----------");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "vdd-keeper" };
            _worker.Start();

            _server = new Thread(ServerLoop) { IsBackground = true, Name = "vdd-api" };
            _server.Start();
        }

        public void Stop()
        {
            _stop = true;

            try { _worker?.Join(2000); } catch { }
            try { _server?.Join(2000); } catch { }

            lock (_sync)
            {
                if (HandleValid)
                {
                    Device.CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }

            SvcLog.Write("服务已停止（虚拟显示器随之移除）");
        }

        public static void TestRun(int desired, int seconds)
        {
            var keeper = new Keeper();

            keeper._stop = false;
            keeper._desired = desired;
            SvcLog.Write($"[测试模式] 期望数量 {desired}，运行 {seconds} 秒");

            keeper._worker = new Thread(keeper.WorkerLoop) { IsBackground = true };
            keeper._worker.Start();
            keeper._server = new Thread(keeper.ServerLoop) { IsBackground = true };
            keeper._server.Start();

            Thread.Sleep(seconds * 1000);
            keeper.Stop();
        }

        void WorkerLoop()
        {
            bool loggedConnect = false;
            var clock = Stopwatch.StartNew();
            long lastCheck = -100000;

            while (!_stop)
            {
                try
                {
                    lock (_sync)
                    {
                        if (!HandleValid && Device.OpenHandle(ADAPTER_GUID, out var handle))
                        {
                            _handle = handle;
                            _count = 0;

                            if (!loggedConnect)
                            {
                                SvcLog.Write("已连接虚拟显示器驱动");
                                loggedConnect = true;
                            }
                        }

                        if (HandleValid)
                        {
                            // 每 3 秒核对一次“Windows 实际看到的显示器数量”，有偏差就纠正
                            if (clock.ElapsedMilliseconds - lastCheck >= 3000)
                            {
                                ReconcileLocked();
                                lastCheck = clock.ElapsedMilliseconds;
                            }

                            IoControl(_handle, IOCTL_UPDATE, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SvcLog.Write("守护循环异常：" + ex.Message);

                    lock (_sync)
                    {
                        if (HandleValid)
                            Device.CloseHandle(_handle);

                        _handle = IntPtr.Zero;
                        _count = 0;
                    }

                    Thread.Sleep(2000);
                }

                Thread.Sleep(80);
            }
        }

        /// <summary>
        /// 以“Windows 实际枚举到的显示器数量”为准做校正：不足就补，多了就减。
        /// 等待期间持续心跳，避免驱动把显示器当成失联而拔掉。
        /// </summary>
        void ReconcileLocked()
        {
            int present = Device.CountPresentDevices(MONITOR_CLASS_GUID, MONITOR_HWID);
            _count = present;

            int tries = 0;

            while (present < _desired && tries < MAX_DISPLAYS)
            {
                var index = IoControl(_handle, IOCTL_ADD, null);

                if (index < 0)
                {
                    SvcLog.Write("添加虚拟显示器失败（驱动返回错误）");
                    break;
                }

                PingFor(1600);

                present = Device.CountPresentDevices(MONITOR_CLASS_GUID, MONITOR_HWID);
                _count = present;
                tries++;

                SvcLog.Write($"已添加虚拟显示器（驱动索引 {index}），Windows 可见 {present}/{_desired} 台");
            }

            if (present >= _desired && tries > 0 && _desired > 0)
                SvcLog.Write($"虚拟显示器已就绪（{present} 台），串流软件可以连接了");

            while (present > _desired)
            {
                IoControl(_handle, IOCTL_REMOVE, IndexBytes(present - 1));

                PingFor(1000);

                present = Device.CountPresentDevices(MONITOR_CLASS_GUID, MONITOR_HWID);
                _count = present;

                SvcLog.Write($"已移除多余的虚拟显示器，剩余 {present} 台");
            }
        }

        /// <summary>在等待 PnP 生效期间持续发送心跳。</summary>
        void PingFor(int milliseconds)
        {
            var end = Environment.TickCount + milliseconds;

            while (!_stop && Environment.TickCount < end)
            {
                IoControl(_handle, IOCTL_UPDATE, null);
                Thread.Sleep(80);
            }
        }

        /// <summary>
        /// 等到 Windows 实际枚举到的虚拟显示器数量达到期望值（或超时）。
        /// 开机时由 OnStart 调用，用来“卡住”依赖它的串流服务。
        /// </summary>
        public bool WaitUntilReady(int timeoutMs)
        {
            if (_desired <= 0) return true;

            var end = Environment.TickCount + timeoutMs;

            while (!_stop && Environment.TickCount < end)
            {
                int present = Device.CountPresentDevices(MONITOR_CLASS_GUID, MONITOR_HWID);

                if (present >= _desired)
                {
                    SvcLog.Write($"等待完成：Windows 已枚举到 {present} 台虚拟显示器");
                    return true;
                }

                Thread.Sleep(300);
            }

            SvcLog.Write($"等待虚拟显示器就绪超时（{timeoutMs} ms），服务继续运行");
            return false;
        }

        void ServerLoop()
        {
            TcpListener listener;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, PORT);
                listener.Start();
                SvcLog.Write($"界面命令端口已监听 127.0.0.1:{PORT}");
            }
            catch (Exception ex)
            {
                SvcLog.Write("无法监听界面命令端口：" + ex.Message);
                return;
            }

            while (!_stop)
            {
                try
                {
                    using (var client = listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 3000;

                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            writer.AutoFlush = true;

                            var line = reader.ReadLine();

                            if (line != null)
                                writer.WriteLine(HandleCommand(line.Trim()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_stop)
                    {
                        SvcLog.Write("界面命令端口异常：" + ex.Message);
                        Thread.Sleep(300);
                    }
                }
            }

            try { listener.Stop(); } catch { }
        }

        string HandleCommand(string command)
        {
            try
            {
                SvcLog.Write("收到界面命令：" + command);

                lock (_sync)
                {
                    if (command.Equals("COUNT", StringComparison.OrdinalIgnoreCase))
                        return "OK " + _count;

                    if (!HandleValid)
                        return "ERR 驱动未就绪";

                    if (command.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase))
                    {
                        var minor = IoControl(_handle, IOCTL_VERSION, null);
                        return "OK 0." + minor;
                    }

                    if (command.StartsWith("ADD", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_count >= MAX_DISPLAYS)
                            return "ERR 已达到最大数量";

                        var index = IoControl(_handle, IOCTL_ADD, null);
                        if (index < 0)
                            return "ERR 添加失败";

                        IoControl(_handle, IOCTL_UPDATE, null);
                        _count++;
                        _desired = _count;
                        SvcConfig.SetDisplayCount(_count);
                        SvcLog.Write($"界面请求添加显示器，当前 {_count} 台");

                        return "OK " + index;
                    }

                    if (command.StartsWith("REMOVE", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = command.Split(' ');
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var idx))
                            return "ERR 参数错误";

                        IoControl(_handle, IOCTL_REMOVE, IndexBytes(idx));
                        IoControl(_handle, IOCTL_UPDATE, null);

                        if (_count > 0) _count--;
                        _desired = _count;
                        SvcConfig.SetDisplayCount(_count);
                        SvcLog.Write($"界面请求移除显示器 {idx}，当前 {_count} 台");

                        return "OK";
                    }

                    if (command.StartsWith("SET", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = command.Split(' ');
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var n))
                            return "ERR 参数错误";

                        if (n < 0) n = 0;
                        if (n > MAX_DISPLAYS) n = MAX_DISPLAYS;

                        _desired = n;
                        ReconcileLocked();
                        SvcConfig.SetDisplayCount(_count);

                        return "OK " + _count;
                    }

                    return "ERR 未知命令";
                }
            }
            catch (Exception ex)
            {
                SvcLog.Write("处理界面命令失败：" + ex.Message);
                return "ERR " + ex.Message;
            }
        }

        static byte[] IndexBytes(int index)
        {
            var data = BitConverter.GetBytes((ushort)unchecked(index & 0xFFFF));
            Array.Reverse(data);

            return data;
        }

        #region 驱动 IOCTL

        [StructLayout(LayoutKind.Sequential)]
        struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint code,
            byte[] inBuffer, int inSize, out int outBuffer, int outSize,
            ref OVERLAPPED overlapped, IntPtr completionRoutine);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern IntPtr CreateEventA(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr handle, ref OVERLAPPED overlapped,
            out uint transferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        static int IoControl(IntPtr handle, uint code, byte[] data)
        {
            var inBuffer = new byte[32];

            if (data != null)
                Array.Copy(data, inBuffer, Math.Min(data.Length, inBuffer.Length));

            var overlapped = new OVERLAPPED();
            overlapped.hEvent = CreateEventA(IntPtr.Zero, false, false, null);

            int result = 0;

            DeviceIoControl(handle, code, inBuffer, inBuffer.Length,
                out result, sizeof(int), ref overlapped, IntPtr.Zero);

            GetOverlappedResult(handle, ref overlapped, out _, true);

            if (overlapped.hEvent != IntPtr.Zero)
                CloseHandle(overlapped.hEvent);

            return result;
        }

        #endregion
    }
}
