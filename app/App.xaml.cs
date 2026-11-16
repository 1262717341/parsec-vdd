using System;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media;

namespace ParsecVDisplay
{
    public partial class App : Application
    {
        const string ID = "QpHOX8IBUHBznGtqk9xm1";
        public const string NAME = "ParsecVDisplay";
        public const string TITLE = "Parsec 虚拟显示器";
        public const string VERSION = "0.46.9";

        public static bool Silent { get; private set; }

        static App()
        {
            var displays = Display.GetAllDisplays();
            return;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length >= 2 && e.Args[0] == "-custom")
            {
                var modes = Display.ParseModes(e.Args[1]);
                ParsecVDD.SetCustomDisplayModes(modes);

                Shutdown();
                return;
            }

            Silent = e.Args.Contains("-silent");

            var signal = new EventWaitHandle(false,
                EventResetMode.AutoReset, ID, out var isOwned);

            if (!isOwned)
            {
                signal.Set();
                Shutdown();
                return;
            }

            Log.Write(Silent ? "程序启动（开机自启，静默模式）" : "程序启动");

            var status = ParsecVDD.QueryStatus();

            // 开机自启时驱动/服务可能还没就绪，静默模式下先等待一段时间，避免直接退出
            if (status != Device.Status.OK && Silent)
            {
                for (int i = 0; i < 40 && status != Device.Status.OK; i++)
                {
                    Thread.Sleep(500);
                    status = ParsecVDD.QueryStatus();
                }
            }

            if (status != Device.Status.OK)
            {
                Log.Write($"驱动状态异常: {status}");

                if (!Silent)
                {
                    if (status == Device.Status.RESTART_REQUIRED)
                    {
                        MessageBox.Show("需要重启电脑才能完成驱动安装。",
                            TITLE, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (status == Device.Status.DISABLED || status == Device.Status.DISABLED_SERVICE)
                    {
                        MessageBox.Show($"{ParsecVDD.ADAPTER} 已被禁用，请在设备管理器中启用它。",
                            TITLE, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else if (status == Device.Status.NOT_INSTALLED)
                    {
                        MessageBox.Show("请先安装驱动程序。",
                            TITLE, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"驱动状态异常，请检查后重试。当前状态：{Device.StatusText(status)}。",
                            TITLE, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                Shutdown();
                return;
            }

            if (ParsecVDD.Init() == false)
            {
                Log.Write("获取设备句柄失败");

                if (!Silent)
                {
                    MessageBox.Show("无法获取设备句柄，请重新检查驱动是否安装正确。",
                        TITLE, MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                Shutdown();
                return;
            }

            // 自检并修复开机启动项
            Config.EnsureStartupEntry();

            Task.Run(() =>
            {
                while (signal.WaitOne())
                {
                    Tray.ShowApp();
                }
            });

            base.OnStartup(e);

            // Disable GPU to prevent flickering when adding display
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ParsecVDD.Uninit();
            base.OnExit(e);
        }
    }
}
