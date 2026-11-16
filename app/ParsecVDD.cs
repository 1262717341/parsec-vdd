using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ParsecVDisplay
{
    internal static class ParsecVDD
    {
        public const string DISPLAY_ID = "PSCCDD0";
        public const string DISPLAY_NAME = "ParsecVDA";

        public const string ADAPTER = "Parsec Virtual Display Adapter";
        public const string ADAPTER_GUID = "{00b41627-04c4-429e-a26e-0265cf50c8fa}";

        public const string HARDWARE_ID = @"Root\Parsec\VDA";
        public const string CLASS_GUID = "{4d36e968-e325-11ce-bfc1-08002be10318}";

        static IntPtr VddHandle;
        static Task UpdateTask;
        static Task RestoreTask;
        static CancellationTokenSource Cancellation;

        // 恢复上次设置期间不要覆盖已保存的记录
        static volatile bool Restoring;

        // 开机后 Windows 会再应用一遍自己的显示器记忆，这段时间内不记录系统触发的变化
        static DateTime QuietUntil = DateTime.MinValue;

        /// <summary>
        /// true 表示虚拟显示器由常驻服务托管：本进程不持有驱动句柄，
        /// 添加/移除都通过命名管道交给服务执行，避免两个句柄抢同一批显示器。
        /// </summary>
        public static bool ServiceManaged { get; private set; }

        // actually 16 devices could be created per adapter
        // so just use a half to avoid plugging lag
        public static int MAX_DISPLAYS => 8;

        public static int DisplayCount { get; private set; } = 0;

        public delegate void DisplayChangedCallback(List<Display> displays, bool noMonitors);
        public static event DisplayChangedCallback DisplayChanged;

        public static bool Init()
        {
            ServiceManaged = VddIpc.IsAvailable();

            if (ServiceManaged)
            {
                Log.Write("检测到常驻服务，虚拟显示器由服务托管");
            }
            else if (!Device.OpenHandle(ADAPTER_GUID, out VddHandle))
            {
                return false;
            }

            Cancellation = new CancellationTokenSource();

            // 服务在持续心跳，本进程不必再 ping 驱动
            if (!ServiceManaged)
                UpdateTask = Task.Run(() => UpdateRoutine(Cancellation.Token), Cancellation.Token);

            Restoring = true;
            QuietUntil = DateTime.UtcNow.AddSeconds(45);
            RestoreTask = Task.Run(() => RestoreRoutine(Cancellation.Token), Cancellation.Token);

            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
            SystemEvents.SessionEnding += SessionEnding;

            return true;
        }

        public static void Uninit()
        {
            // 退出前保存当前显示器布局与分辨率，供下次开机恢复
            SaveCurrentDisplays(true);

            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            SystemEvents.SessionEnding -= SessionEnding;

            Cancellation?.Cancel();

            try
            {
                UpdateTask?.Wait(1000);
                RestoreTask?.Wait(1000);
            }
            catch { }

            // 服务托管的显示器由服务维持，这里不能去关服务的句柄
            if (!ServiceManaged)
                Device.CloseHandle(VddHandle);
        }

        static async void UpdateRoutine(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                long start = sw.ElapsedMilliseconds;

                Core.Update(VddHandle);

                if (token.IsCancellationRequested)
                    break;

                if ((sw.ElapsedMilliseconds - start) < 100)
                {
                    await Task.Delay(80);
                }
            }
        }

        /// <summary>
        /// 开机启动时按上次保存的记录恢复：虚拟显示器数量 + 每个显示器的分辨率/刷新率/方向。
        /// </summary>
        static async void RestoreRoutine(CancellationToken token)
        {
            try
            {
                int savedCount = Config.DisplayCount;

                if (savedCount <= 0)
                {
                    Restoring = false;
                    return;
                }

                if (savedCount > MAX_DISPLAYS) savedCount = MAX_DISPLAYS;

                Log.Write($"准备恢复上次的虚拟显示器设置，数量：{savedCount}");

                for (int i = 0; i < savedCount; i++)
                {
                    if (Config.TryGetSavedDisplay(i, out var w, out var h, out var hz, out var ori))
                        Log.Write($"  记录中的显示器 {i + 1}：{w}x{h}@{hz}Hz，方向 {(int)ori}");
                }

                // 1. 等驱动就绪（开机时服务可能还没起来）
                var timer = Stopwatch.StartNew();
                while (!token.IsCancellationRequested && timer.ElapsedMilliseconds < 20000)
                {
                    if (QueryStatus() == Device.Status.OK) break;
                    await Task.Delay(1000, token);
                }

                // 2. 补齐虚拟显示器数量
                timer.Restart();
                while (!token.IsCancellationRequested && timer.ElapsedMilliseconds < 20000)
                {
                    if (GetParsecDisplays().Count >= savedCount) break;
                    AddDisplay();
                    await Task.Delay(1500, token);
                }

                // 3. 等显示器真正出现，再逐个应用上次的分辨率
                //    Windows 登录后还会自己应用一次“每台显示器的记忆设置”，可能把刚设好的分辨率改回默认，
                //    所以这里多盯几轮：被改回去就重新应用一次。
                timer.Restart();
                bool done = false;
                int rounds = 0;

                while (!token.IsCancellationRequested && timer.ElapsedMilliseconds < 30000 && rounds < 4)
                {
                    var displays = GetParsecDisplays();
                    if (displays.Count >= savedCount && displays.TrueForAll(d => d.Active))
                    {
                        done = ApplySavedModes(displays, savedCount);

                        if (done)
                        {
                            await Task.Delay(3000, token);

                            if (ModesMatchSaved(displays, savedCount))
                                break;

                            Log.Write("检测到分辨率被系统改回，重新应用上次的设置");
                            rounds++;
                            continue;
                        }
                    }

                    await Task.Delay(1000, token);
                }

                Log.Write(done ? "已恢复上次的虚拟显示器设置" : "部分虚拟显示器设置未能恢复");
            }
            catch (Exception ex)
            {
                Log.Write("恢复虚拟显示器设置失败", ex);
            }
            finally
            {
                Restoring = false;
            }
        }

        /// <summary>当前显示器的分辨率是否与保存的记录一致（不做任何修改）。</summary>
        static bool ModesMatchSaved(List<Display> displays, int savedCount)
        {
            bool match = true;

            for (int i = 0; i < displays.Count && i < savedCount; i++)
            {
                var display = displays[i];

                if (!Config.TryGetSavedDisplay(i, out var width, out var height, out var hz, out var orientation))
                    continue;

                if (display.CurrentMode == null)
                {
                    match = false;
                    continue;
                }

                if (display.CurrentMode.Width != width ||
                    display.CurrentMode.Height != height ||
                    (hz > 0 && display.CurrentMode.Hz != hz) ||
                    display.CurrentOrientation != orientation)
                    match = false;
            }

            return match;
        }

        /// <summary>
        /// 按保存的记录给显示器设置分辨率/刷新率/方向，全部成功返回 true。
        /// </summary>
        static bool ApplySavedModes(List<Display> displays, int savedCount)
        {
            bool allDone = true;

            for (int i = 0; i < displays.Count && i < savedCount; i++)
            {
                var display = displays[i];

                if (!Config.TryGetSavedDisplay(i, out var width, out var height, out var hz, out var orientation))
                    continue;

                if (!display.Active || display.CurrentMode == null)
                {
                    allDone = false;
                    continue;
                }

                bool sizeOk = display.CurrentMode.Width == width && display.CurrentMode.Height == height;
                bool hzOk = hz <= 0 || display.CurrentMode.Hz == hz;
                bool orientationOk = display.CurrentOrientation == orientation;

                if (sizeOk && hzOk && orientationOk)
                    continue;

                // 保存的分辨率已经不被驱动支持时，保持当前设置
                if (!IsResolutionSupported(display, width, height))
                {
                    Log.Write($"显示器 {i + 1}: 保存的分辨率 {width}x{height} 已不被支持，跳过");
                    continue;
                }

                bool ok = display.ChangeMode(width, height, hz > 0 ? hz : (int?)null, orientation);

                Log.Write($"显示器 {i + 1}: 恢复 {width}x{height}@{hz}Hz，方向 {(int)orientation}，结果：{(ok ? "成功" : "失败")}");

                if (!ok) allDone = false;
            }

            return allDone;
        }

        static bool IsResolutionSupported(Display display, int width, int height)
        {
            if (display.CurrentMode != null &&
                display.CurrentMode.Width == width &&
                display.CurrentMode.Height == height)
                return true;

            if (display.SupportedResolutions == null || display.SupportedResolutions.Count == 0)
                return true;

            return display.SupportedResolutions.Any(r => r.Width == width && r.Height == height);
        }

        public static List<Display> GetParsecDisplays()
        {
            var displays = Display.GetAllDisplays();

            return displays.FindAll(d => d.DisplayName != null &&
                d.DisplayName.Equals(DISPLAY_ID, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 保存当前所有虚拟显示器的数量与分辨率，供下次启动恢复。
        /// </summary>
        public static void SaveCurrentDisplays(bool force = false)
        {
            if (Restoring) return;

            // 开机后的一小段时间里 Windows 会自己把“每台显示器的记忆设置”再应用一遍，
            // 这类系统触发的变化不应该覆盖用户记录，所以安静期内只允许用户操作强制写入
            if (!force && DateTime.UtcNow < QuietUntil) return;

            try
            {
                var displays = GetParsecDisplays();

                // 服务托管时，插拔过渡瞬间可能枚举不到显示器：
                // 先问服务的真实数量，避免把记录误写成 0（否则下次开机就不恢复了）
                if (displays.Count == 0 && ServiceManaged)
                {
                    var reply = VddIpc.Send("COUNT");

                    if (reply != null && reply.StartsWith("OK") && reply.Substring(2).Trim() != "0")
                        return;
                }

                Config.SaveDisplays(displays);
            }
            catch (Exception ex)
            {
                Log.Write("保存虚拟显示器设置失败", ex);
            }
        }

        public static void Invalidate()
        {
            DisplaySettingsChanged(null, EventArgs.Empty);
        }

        static void DisplaySettingsChanged(object sender, EventArgs e)
        {
            var displays = Display.GetAllDisplays();
            bool noMonitors = displays.Count == 0;

            displays = displays.FindAll(d => d.DisplayName
                .Equals(DISPLAY_ID, StringComparison.OrdinalIgnoreCase));

            DisplayCount = displays.Count;
            noMonitors = DisplayCount == 0 && noMonitors;

            DisplayChanged?.Invoke(displays, noMonitors);

            // 显示器状态变化时同步记录，避免异常退出后丢失设置
            SaveCurrentDisplays();
        }

        static void SessionEnding(object sender, SessionEndingEventArgs e)
        {
            SaveCurrentDisplays(true);
        }

        public static Device.Status QueryStatus()
        {
            return Device.QueryStatus(CLASS_GUID, HARDWARE_ID);
        }

        public static string QueryVersion()
        {
            if (ServiceManaged)
            {
                var reply = VddIpc.Send("VERSION");

                if (reply != null && reply.StartsWith("OK"))
                    return reply.Substring(2).Trim();

                return "未知";
            }

            Core.Update(VddHandle);
            Core.Version(VddHandle, out int minor);

            return $"0.{minor}";
        }

        public static int AddDisplay()
        {
            if (ServiceManaged)
            {
                var reply = VddIpc.Send("ADD");
                Log.Write($"通过服务添加虚拟显示器：{reply}");

                if (reply != null && reply.StartsWith("OK"))
                    return int.TryParse(reply.Substring(2).Trim(), out var svcIndex) ? svcIndex : 0;

                return -1;
            }

            Core.Add(VddHandle, out int index);
            Core.Update(VddHandle);

            return index;
        }

        public static void RemoveDisplay(int index)
        {
            bool viaService = false;

            if (ServiceManaged)
            {
                var reply = VddIpc.Send("REMOVE " + index);

                if (reply != null && reply.StartsWith("OK"))
                {
                    viaService = true;
                }
                else
                {
                    Log.Write($"服务移除显示器 {index} 失败，改为本地处理：{reply}");
                }
            }

            if (!viaService)
            {
                Core.Remove(VddHandle, index);
                Core.Update(VddHandle);
            }

            SaveCurrentDisplays(true);
        }

        public static void RemoveLastDisplay()
        {
            if (DisplayCount > 0)
            {
                int index = DisplayCount - 1;
                RemoveDisplay(index);
            }
        }

        static unsafe class Core
        {
            public const uint IOCTL_ADD = 0x0022e004;
            public const uint IOCTL_REMOVE = 0x0022a008;
            public const uint IOCTL_UPDATE = 0x0022a00c;
            public const uint IOCTL_VERSION = 0x0022e010;

            static int IoControl(IntPtr handle, uint code, byte[] data)
            {
                var InBuffer = new byte[32];
                var Overlapped = new Native.OVERLAPPED();
                int OutBuffer = 0;

                if (data != null)
                    Array.Copy(data, InBuffer, Math.Min(data.Length, InBuffer.Length));

                fixed (byte* input = InBuffer)
                {
                    Overlapped.hEvent = Native.CreateEventA(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    Native.DeviceIoControl(handle, code, input, InBuffer.Length, &OutBuffer, sizeof(int), IntPtr.Zero, ref Overlapped);
                    Native.GetOverlappedResult(handle, ref Overlapped, out var NumberOfBytesTransferred, true);

                    if (Overlapped.hEvent != IntPtr.Zero)
                        Native.CloseHandle(Overlapped.hEvent);
                }

                return OutBuffer;
            }

            public static void Version(IntPtr handle, out int minor)
            {
                // Remove() takes only 2 bytes for index
                //   so this 4 bytes return could be a combination ((major << 16) | minor)
                //   it should be clear when Parsec VDD comes to v1.0
                minor = IoControl(handle, IOCTL_VERSION, null);
            }

            public static void Update(IntPtr handle)
            {
                IoControl(handle, IOCTL_UPDATE, null);
            }

            public static void Add(IntPtr handle, out int index)
            {
                index = IoControl(handle, IOCTL_ADD, null);
            }

            public static void Remove(IntPtr handle, int index)
            {
                // 16-bit BE index
                var indexData = BitConverter.GetBytes((ushort)unchecked(index & 0xFFFF));
                Array.Reverse(indexData);

                IoControl(handle, IOCTL_REMOVE, indexData);
            }
        }

        public static IList<Display.Mode> GetCustomDisplayModes()
        {
            var list = new List<Display.Mode>();

            using (var vdd = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Parsec\\vdd", RegistryKeyPermissionCheck.ReadSubTree))
            {
                if (vdd != null)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        using (var index = vdd.OpenSubKey($"{i}", RegistryKeyPermissionCheck.ReadSubTree))
                        {
                            if (index != null)
                            {
                                var width = index.GetValue("width");
                                var height = index.GetValue("height");
                                var hz = index.GetValue("hz");

                                if (width != null && height != null && hz != null)
                                {
                                    list.Add(new Display.Mode
                                    {
                                        Width = Convert.ToUInt16(width),
                                        Height = Convert.ToUInt16(height),
                                        Hz = Convert.ToUInt16(hz),
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return list;
        }

        // Requires admin perm
        public static void SetCustomDisplayModes(List<Display.Mode> modes)
        {
            using (var vdd = Registry.LocalMachine.CreateSubKey("SOFTWARE\\Parsec\\vdd", RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (vdd != null)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        using (var index = vdd.CreateSubKey($"{i}", RegistryKeyPermissionCheck.ReadWriteSubTree))
                        {
                            if (i >= modes.Count && index != null)
                            {
                                index.Dispose();
                                vdd.DeleteSubKey($"{i}");
                            }
                            else if (index != null)
                            {
                                index.SetValue("width", modes[i].Width, RegistryValueKind.DWord);
                                index.SetValue("height", modes[i].Height, RegistryValueKind.DWord);
                                index.SetValue("hz", modes[i].Hz, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
            }
        }

        static unsafe class Native
        {
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                IntPtr device, uint code,
                void* lpInBuffer, int nInBufferSize,
                void* lpOutBuffer, int nOutBufferSize,
                IntPtr lpBytesReturned,
                ref OVERLAPPED lpOverlapped
            );

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetOverlappedResult(
                IntPtr handle,
                ref OVERLAPPED lpOverlapped,
                out uint lpNumberOfBytesTransferred,
                [MarshalAs(UnmanagedType.Bool)] bool bWait
            );

            [StructLayout(LayoutKind.Sequential)]
            public struct OVERLAPPED
            {
                public IntPtr Internal;
                public IntPtr InternalHigh;
                public IntPtr Pointer;
                public IntPtr hEvent;
            }

            [DllImport("kernel32.dll")]
            public static extern IntPtr CreateEventA(IntPtr a, IntPtr b, IntPtr c, IntPtr d);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);
        }
    }
}
