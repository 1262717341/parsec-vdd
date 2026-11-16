using System;
using System.IO;
using System.Text;

namespace ParsecVDisplay.Svc
{
    internal static class SvcLog
    {
        static readonly object Sync = new object();

        public static string LogPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ParsecVDisplay");

                return Path.Combine(dir, "svc-log.txt");
            }
        }

        public static void Write(string message)
        {
            lock (Sync)
            {
                try
                {
                    var path = LogPath;
                    var dir = Path.GetDirectoryName(path);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var file = new FileInfo(path);

                    if (file.Exists && file.Length > 2 * 1024 * 1024)
                    {
                        try { File.Delete(path + ".1"); } catch { }
                        try { File.Move(path, path + ".1"); } catch { }
                    }

                    File.AppendAllText(path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}\r\n",
                        new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }
    }
}
