using System;
using System.ServiceProcess;

namespace ParsecVDisplay.Svc
{
    static class Program
    {
        static void Main(string[] args)
        {
            // 调试用：ParsecVDisplaySvc.exe -test <期望数量> <运行秒数>
            if (args.Length > 0 && args[0] == "-test")
            {
                int desired = args.Length > 1 ? int.Parse(args[1]) : 0;
                int seconds = args.Length > 2 ? int.Parse(args[2]) : 20;

                Keeper.TestRun(desired, seconds);
                return;
            }

            ServiceBase.Run(new KeeperService());
        }
    }
}
