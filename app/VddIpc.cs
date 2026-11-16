using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ParsecVDisplay
{
    /// <summary>
    /// 与常驻服务（ParsecVDisplaySvc）通信的极简客户端：本机回环 TCP，一问一答。
    /// 服务统一持有驱动句柄，界面只发命令（ADD / REMOVE / SET / COUNT）。
    /// </summary>
    internal static class VddIpc
    {
        const int PORT = 47523;
        const int TIMEOUT_MS = 2000;

        public static bool IsAvailable()
        {
            return Send("COUNT") != null;
        }

        /// <summary>发送一行命令，返回服务端回复；服务不存在或超时返回 null。</summary>
        public static string Send(string command)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.ConnectAsync(IPAddress.Loopback, PORT);

                    if (!connect.Wait(TIMEOUT_MS))
                        return null;

                    client.ReceiveTimeout = TIMEOUT_MS;

                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.AutoFlush = true;
                        writer.WriteLine(command);

                        return reader.ReadLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"连接后台服务失败（{command}）：{ex.Message}");
                return null;
            }
        }
    }
}
