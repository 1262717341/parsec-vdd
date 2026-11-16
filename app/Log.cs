using System;
using System.IO;
using System.Text;

namespace ParsecVDisplay
{
    /// <summary>
    /// 轻量日志，写入 %LOCALAPPDATA%\ParsecVDisplay\log.txt，方便排查开机启动失败等问题。
    /// </summary>
    internal static class Log
    {
        static readonly object Sync = new object();

        public static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    App.NAME);

                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "log.txt");
            }
        }

        public static void Write(string message)
        {
            Write(message, null);
        }

        public static void Write(string message, Exception ex)
        {
            try
            {
                var line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                line.Append("  ");
                line.Append(message);
                if (ex != null)
                {
                    line.Append(" | ");
                    line.Append(ex.GetType().Name);
                    line.Append(": ");
                    line.Append(ex.Message);
                }
                line.Append("\r\n");

                lock (Sync)
                {
                    var path = FilePath;

                    // 超过 1MB 就重新开始，避免无限增长
                    try
                    {
                        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                            File.Delete(path);
                    }
                    catch { }

                    File.AppendAllText(path, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志本身不能影响主流程
            }
        }
    }
}
