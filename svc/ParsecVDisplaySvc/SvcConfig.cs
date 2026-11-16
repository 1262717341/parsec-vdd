using System;
using Microsoft.Win32;

namespace ParsecVDisplay.Svc
{
    /// <summary>
    /// 服务端的配置读取：服务以 SYSTEM 身份运行，读不到用户 HKCU，
    /// 所以统一读写 HKLM\SOFTWARE\ParsecVDisplay（安装时已给普通用户写权限）。
    /// </summary>
    internal static class SvcConfig
    {
        const string KEY = @"SOFTWARE\ParsecVDisplay";

        public static int GetDisplayCount()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(KEY))
                {
                    var value = key?.GetValue("DisplayCount");

                    if (value != null)
                        return Convert.ToInt32(value);
                }
            }
            catch (Exception ex)
            {
                SvcLog.Write($"读取显示数量失败：{ex.Message}");
            }

            return 1;
        }

        public static void SetDisplayCount(int count)
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(KEY))
                    key?.SetValue("DisplayCount", count, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                SvcLog.Write($"写入显示数量失败：{ex.Message}");
            }
        }
    }
}
