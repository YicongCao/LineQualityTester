using System;
using System.Text;
using System.Threading;

namespace EchoClientCore
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            do
            {
                if (args.Length < 1)
                {
                    Logger.Instance.LogError("请指定配置文件");
                    break;
                }
                PearTestRunner pear = new PearTestRunner();
                if (!pear.Init(args[0]))
                {
                    Logger.Instance.LogError("压测程序初始化失败");
                    break;
                }
                if (!pear.Start())
                {
                    Logger.Instance.LogError("压测程序无法开始测试");
                    break;
                }
                bool unsupervised = ConfigManager.Instance.Unsupervised;
                if (unsupervised)
                {
                    Thread.Sleep(ConfigManager.Instance.SelfKillTime);
                }
                else
                {
                    Console.ReadKey(true);
                }
                pear.StopManually();
                Logger.Instance.LogInfo("再次按任意键退出");
                if (!unsupervised)
                {
                    Console.ReadKey(true);
                }
                return;
            }
            while (false);
            Logger.Instance.LogInfo("按任意键退出");
            Console.ReadKey(true);
        }
    }
}
