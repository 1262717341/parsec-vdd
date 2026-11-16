using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Win32;

namespace ParsecVDisplay
{
    internal static class Config
    {
        // 服务（SYSTEM 身份）开机时要读配置，所以主配置放 HKLM（安装时已给普通用户写权限），
        // HKLM 写不进去时退回 HKCU。
        static string REG_PATH_HKLM => $"HKEY_LOCAL_MACHINE\\SOFTWARE\\{App.NAME}";
        static string REG_PATH_HKCU => $"HKEY_CURRENT_USER\\SOFTWARE\\{App.NAME}";
        const string REG_STARTUP_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string REG_STARTUP_APPROVED = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        const string KEY_RUN_ON_STARTUP = "RunOnStartup";
        const string KEY_FALLBACK_DISPLAY = "FallbackDisplay";
        const string KEY_KEEP_SCREEN_ON = "KeepScreenOn";
        const string KEY_DISPLAY_COUNT = "DisplayCount";

        #region 基础读写

        /// <summary>先读 HKLM（服务也读这里），没有再退回 HKCU（老版本留下的配置）。</summary>
        static object ReadValue(string key)
        {
            try
            {
                var value = Registry.GetValue(REG_PATH_HKLM, key, null);
                if (value != null) return value;
            }
            catch { }

            try
            {
                return Registry.GetValue(REG_PATH_HKCU, key, null);
            }
            catch
            {
                return null;
            }
        }

        static void WriteValue(string key, object value, RegistryValueKind kind)
        {
            try
            {
                Registry.SetValue(REG_PATH_HKLM, key, value, kind);
                return;
            }
            catch { }

            try
            {
                Registry.SetValue(REG_PATH_HKCU, key, value, kind);
            }
            catch (Exception ex)
            {
                Log.Write($"写入注册表失败: {key}", ex);
            }
        }

        public static int GetInt(string key, int @default = 0)
        {
            try
            {
                var value = ReadValue(key);
                return value == null ? @default : Convert.ToInt32(value);
            }
            catch
            {
                return @default;
            }
        }

        public static void SetInt(string key, int value)
        {
            WriteValue(key, value, RegistryValueKind.DWord);
        }

        public static string GetString(string key)
        {
            return ReadValue(key) as string;
        }

        public static void SetString(string key, string value)
        {
            WriteValue(key, value ?? string.Empty, RegistryValueKind.String);
        }

        public static void DeleteValue(string key)
        {
            // HKLM 通常只有读权限，删不掉是正常的，不当作错误记录
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\{App.NAME}", true))
                {
                    k?.DeleteValue(key, false);
                }
            }
            catch { }

            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{App.NAME}", true))
                {
                    k?.DeleteValue(key, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"删除注册表值失败: {key}", ex);
            }
        }

        #endregion

        #region 显示器数量与分辨率记忆

        public static int DisplayCount
        {
            get => GetInt(KEY_DISPLAY_COUNT);
            set => SetInt(KEY_DISPLAY_COUNT, value);
        }

        /// <summary>
        /// 保存当前所有 Parsec 虚拟显示器的数量、分辨率、刷新率与方向，
        /// 下次开机启动时按此记录恢复。
        /// </summary>
        public static void SaveDisplays(IList<Display> displays)
        {
            int count = displays?.Count ?? 0;

            DisplayCount = count;

            for (int i = 0; i < count; i++)
            {
                var display = displays[i];
                var mode = display?.CurrentMode;

                if (mode == null) continue;

                SetString(DisplayKey(i), string.Join(",",
                    mode.Width, mode.Height, mode.Hz, (int)display.CurrentOrientation));
            }

            // 清掉多余的旧记录
            for (int i = count; i < 16; i++)
            {
                if (GetString(DisplayKey(i)) == null) break;
                DeleteValue(DisplayKey(i));
            }
        }

        public static bool TryGetSavedDisplay(int index,
            out int width, out int height, out int hz, out Display.Orientation orientation)
        {
            width = 0;
            height = 0;
            hz = 0;
            orientation = Display.Orientation.Angle0;

            var raw = GetString(DisplayKey(index));
            if (string.IsNullOrEmpty(raw)) return false;

            var tokens = raw.Split(',');
            if (tokens.Length < 3) return false;

            if (!int.TryParse(tokens[0], out width) ||
                !int.TryParse(tokens[1], out height) ||
                !int.TryParse(tokens[2], out hz))
                return false;

            if (tokens.Length >= 4 && int.TryParse(tokens[3], out var o) &&
                o >= (int)Display.Orientation.Angle0 && o <= (int)Display.Orientation.Angle270)
                orientation = (Display.Orientation)o;

            return width > 0 && height > 0;
        }

        static string DisplayKey(int index) => $"Display{index}";

        #endregion

        #region 备用显示器 / 屏幕常亮

        public static bool FallbackDisplay
        {
            get => GetInt(KEY_FALLBACK_DISPLAY) != 0;
            set => SetInt(KEY_FALLBACK_DISPLAY, value ? 1 : 0);
        }

        public static bool KeepScreenOn
        {
            get
            {
                bool enable = GetInt(KEY_KEEP_SCREEN_ON) != 0;
                Helper.StayAwake(enable);
                return enable;
            }
            set
            {
                SetInt(KEY_KEEP_SCREEN_ON, value ? 1 : 0);
                Helper.StayAwake(value);
            }
        }

        #endregion

        #region 开机启动

        /// <summary>
        /// 开机启动开关。勾选后写入 HKCU 的 Run 项，并清掉系统“已禁用”标记；
        /// 程序每次启动都会自检并修复，避免被杀软 / 任务管理器禁用之后失效。
        /// </summary>
        public static bool RunOnStartup
        {
            get
            {
                try
                {
                    bool exists;

                    using (var key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_PATH, false))
                    {
                        exists = key?.GetValue(App.NAME) != null;
                    }

                    // Run 项存在，但被任务管理器/杀软标记为“已禁用”时，等同于没开
                    return exists && !IsStartupDisabled();
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                SetInt(KEY_RUN_ON_STARTUP, value ? 1 : 0);
                ApplyStartupEntry(value);
            }
        }

        /// <summary>
        /// 读取 StartupApproved\Run 里的启用状态：首字节 bit0 为 1 表示“已禁用”。
        /// （任务管理器禁用启动项、部分安全软件的启动项拦截都会写这个值）
        /// </summary>
        static bool IsStartupDisabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_APPROVED, false))
                {
                    var blob = key?.GetValue(App.NAME) as byte[];

                    return blob != null && blob.Length > 0 && (blob[0] & 1) == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 把启动项在系统里标记为“启用”，否则即便 Run 项存在，开机也不会启动。
        /// </summary>
        static void SetStartupApproved(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_STARTUP_APPROVED))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        // 02 00 00 00 + 8 字节时间戳（0 表示不限） = 已启用
                        var blob = new byte[12];
                        blob[0] = 0x02;

                        var current = key.GetValue(App.NAME) as byte[];

                        // 已经是启用状态就不动它，避免无谓写入
                        if (current != null && current.Length > 0 && (current[0] & 1) == 0)
                            return;

                        key.SetValue(App.NAME, blob, RegistryValueKind.Binary);
                    }
                    else
                    {
                        key.DeleteValue(App.NAME, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("写入启动项启用状态失败", ex);
            }
        }

        /// <summary>
        /// 程序启动时调用：只要曾经勾选过开机启动，就重新写入 Run 项，
        /// 即使被清理掉也能自动恢复。
        /// </summary>
        public static void EnsureStartupEntry()
        {
            try
            {
                bool wanted = GetInt(KEY_RUN_ON_STARTUP) != 0 || RunOnStartup;

                // 老安装包只写了 Run 项、没有记忆开关，这里补齐开关状态
                if (wanted && GetInt(KEY_RUN_ON_STARTUP) == 0)
                    SetInt(KEY_RUN_ON_STARTUP, 1);

                if (wanted)
                {
                    if (IsStartupDisabled())
                        Log.Write("检测到启动项被系统/安全软件标记为“已禁用”，正在恢复为启用状态");

                    ApplyStartupEntry(true);
                }
            }
            catch (Exception ex)
            {
                Log.Write("自检开机启动失败", ex);
            }
        }

        static void ApplyStartupEntry(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_STARTUP_PATH))
                {
                    if (key == null)
                    {
                        Log.Write("无法打开开机启动注册表项");
                        return;
                    }

                    if (enable)
                    {
                        var exePath = Assembly.GetExecutingAssembly().Location;
                        if (string.IsNullOrEmpty(exePath))
                            exePath = System.IO.Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory, App.NAME + ".exe");

                        key.SetValue(App.NAME, $"\"{exePath}\" -silent", RegistryValueKind.String);
                        Log.Write($"已写入开机启动: \"{exePath}\" -silent");
                    }
                    else
                    {
                        key.DeleteValue(App.NAME, false);
                        Log.Write("已取消开机启动");
                    }
                }

                // 关键一步：同步系统“启动项已启用”标记，否则 Run 项写了也会被开机跳过
                SetStartupApproved(enable);
            }
            catch (Exception ex)
            {
                Log.Write("写入开机启动失败", ex);
            }
        }

        #endregion
    }
}
