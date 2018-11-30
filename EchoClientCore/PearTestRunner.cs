using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
namespace EchoClientCore
{
    public class PearTestRunner
    {
        private IProtocolWrapper protocolWrapper;
        private NetClients netClients;
        private MetricManager metricManager;
        private Stopwatch stopWatch;
        private bool stopMark;
        private bool logFinalMark;  // 是否输出过最终报表
        private Timer timerForIntervalRelay;
        private Timer timerForLogging;
        private Mutex mutex;
        private ConcurrentQueue<int> sendQueue;
        public PearTestRunner()
        {
            protocolWrapper = null;
            netClients = null;
            metricManager = null;
            stopWatch = null;
            stopMark = false;
            logFinalMark = false;
            timerForLogging = null;
            timerForIntervalRelay = null;
            mutex = new Mutex();
            sendQueue = new ConcurrentQueue<int>();
        }
        public bool Init(string configFileName)
        {
            if (!ConfigManager.Instance.Init(configFileName))
            {
                Logger.Instance.LogError("读取配置文件失败");
                return false;
            }
            Logger.Instance.LogTrace("配置文件读取成功");
            protocolWrapper = ProtocolWrapperFactory.GetProtocolWrapper("");
            metricManager = new MetricManager();
            if (!metricManager.Init(ConfigManager.Instance.SendPoolSize))
            {
                Logger.Instance.LogError("性能计数器初始化失败");
                return false;
            }
            Logger.Instance.LogTrace("性能计数器初始化成功");
            netClients = new NetClients(ConfigManager.Instance.Protocol, ConfigManager.Instance.ClientCount,
                                        ConfigManager.Instance.ThreadsPerClient, ConfigManager.Instance.BufferSize);
            netClients.OnSend += this.OnSend;
            netClients.OnSend += metricManager.OnSend;
            netClients.OnReceive += this.OnReceive;
            netClients.OnReceive += metricManager.OnReceive;
            if (!netClients.Init(ConfigManager.Instance.IP, ConfigManager.Instance.Port))
            {
                Logger.Instance.LogError("客户端连接初始化失败");
                return false;
            }
            Logger.Instance.LogTrace("客户端连接初始化成功");
            stopWatch = new Stopwatch();

            Console.WriteLine($"压测目标: [{ConfigManager.Instance.Protocol.ToUpper()}] {ConfigManager.Instance.Target.ToString()}");
            Console.WriteLine($"客户端连接数: {ConfigManager.Instance.ClientCount}, 发送池大小: {ConfigManager.Instance.SendPoolSize}");
            if (ConfigManager.Instance.ControlMode == "count")
            {
                Console.WriteLine($"预计发包数量: {ConfigManager.Instance.ControlParam}");
            }
            else if (ConfigManager.Instance.ControlMode == "time")
            {
                Console.WriteLine($"预计发包时间: {ConfigManager.Instance.ControlParam / 1000}s");
            }
            else if (ConfigManager.Instance.ControlMode == "manual")
            {
                Console.WriteLine("手动停止发包(按任意键退出程序)");
            }
            else
            {
                Console.WriteLine($"未知控制模式: {ConfigManager.Instance.ControlMode}");
            }
            if (ConfigManager.Instance.DataProtocol == "raw")
            {
                Console.WriteLine($"数据协议: 普通回显服务");
            }
            else if (ConfigManager.Instance.DataProtocol == "fakes5")
            {
                Console.WriteLine($"数据协议: 经典LSP协议/回显, 真实目标: {ConfigManager.Instance.TargetAcc}");
            }
            else if (ConfigManager.Instance.DataProtocol == "fakes5echo")
            {
                Console.WriteLine($"数据协议: 经典LspUdping, 真实目标: 海外S5节点");
            }
            else
            {
                Console.WriteLine($"数据协议: 未知, 不可思议的事情发生了");
            }
            if (ConfigManager.Instance.RelayMode == "interval")
            {
                Console.WriteLine($"发包模式: 连续发送, 间隔时间: {ConfigManager.Instance.RelayInterval}ms");
            }
            else if (ConfigManager.Instance.RelayMode == "onsend")
            {
                Console.WriteLine($"发包模式: 固定速度发包, 发包速度: {ConfigManager.Instance.RelayMaxPps}pps");
            }
            else if (ConfigManager.Instance.RelayMode == "onrecv")
            {
                Console.WriteLine($"发包模式: 接力发送, 热身包量: {ConfigManager.Instance.WarmupCount}, 热身包间隔: {ConfigManager.Instance.WarmupInterval}ms");
            }
            else
            {
                Console.WriteLine($"未知发包模式: {ConfigManager.Instance.RelayMode}");
            }
            Console.WriteLine("");

            Logger.Instance.LogInfo("压测程序初始化成功\r\n");
            return true;
        }
        public bool Start()
        {
            // 结束控制：time、count、manual
            // 接力控制：interval、onsend、onrecv
            // 打点控制：time、count

            stopWatch.Start();

            Func<int> sendthread = () =>
            {
                int dummy = 0;
                while (!stopMark)
                {
                    if (ConfigManager.Instance.RelayMode == "onsend")
                    {
                        while (metricManager.GetSpeedMeter(MetricType.Realtime).GetSpeed() > ConfigManager.Instance.RelayMaxPps)
                        {
                            Thread.Sleep(ConfigManager.Instance.RelayPpsDelay);
                        }
                    }
                    if (sendQueue.TryDequeue(out dummy) && !stopMark)
                    {
                        SendOnce();
                    }
                }
                return 0;
            };

            if (ConfigManager.Instance.ClientCount > 1)
            {
                Task.Run(() => { sendthread(); });
                Task.Run(() => { sendthread(); });
                Task.Run(() => { sendthread(); });
                Task.Run(() => { sendthread(); });
            }

            if (ConfigManager.Instance.RelayMode == "interval")
            {
                timerForIntervalRelay = new Timer(RelayInterval, null, 1000, ConfigManager.Instance.RelayInterval);
            }
            else if (ConfigManager.Instance.RelayMode == "onsend")
            {
                if (ConfigManager.Instance.ClientCount > 1)
                {
                    for (int i = 0; i < ConfigManager.Instance.RelayMaxPps; i++)
                    {
                        sendQueue.Enqueue(233);
                    }
                }
                else
                {
                    Task.Run(() =>
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            SendOnce();
                        }
                    });
                }
            }
            else if (ConfigManager.Instance.RelayMode == "onrecv")
            {
                Task.Run(() =>
                {
                    for (int i = 0; i < ConfigManager.Instance.WarmupCount && !stopMark; i++)
                    {
                        if (ConfigManager.Instance.ClientCount > 1)
                            sendQueue.Enqueue(233);
                        else
                            SendOnce();
                        Thread.Sleep(ConfigManager.Instance.WarmupInterval);
                    }
                });
            }
            else
            {
                Logger.Instance.LogError("无效的发包接力参数");
                return false;
            }

            // 性能打点线程
            if (ConfigManager.Instance.LogMode == "time")
            {
                timerForLogging = new Timer((object x) =>
                {
                    if (stopMark)
                    {
                        timerForLogging.Dispose();
                    }
                    else
                    {
                        Console.WriteLine(metricManager.GetMetricMessage(MetricType.Period));
                        metricManager.ResetPeriodMetricData();
                    }
                }
                , null, ConfigManager.Instance.LogParam, ConfigManager.Instance.LogParam);
            }
            return true;
        }
        public bool StopManually()
        {
            stopMark = true;
            Finish();
            return true;
        }
        private void OnReceive(byte[] data, int offset, int length)
        {
            CheckStopMark();
            if (ConfigManager.Instance.RelayMode == "onrecv" && !stopMark)
            {
                if (ConfigManager.Instance.ClientCount > 1)
                    sendQueue.Enqueue(233);
                else
                    SendOnce();
            }
        }
        private void OnSend(int sendbytes)
        {
            CheckStopMark();
            CheckLogPeriodByCount();
            if (ConfigManager.Instance.RelayMode == "onsend" && !stopMark)
            {
                if (ConfigManager.Instance.ClientCount > 1)
                {
                    sendQueue.Enqueue(233);
                }
                else
                {
                    while (metricManager.GetSpeedMeter(MetricType.Realtime).GetSpeed() > ConfigManager.Instance.RelayMaxPps)
                    {
                        Thread.Sleep(ConfigManager.Instance.RelayPpsDelay);
                    }
                    SendOnce();
                }
            }
        }
        private void Finish()
        {
            if (!stopMark)
            {
                Logger.Instance.LogError("未在结束后再调用Finish()");
                return;
            }
            if (!logFinalMark)
            {
                logFinalMark = true;
                int waitms = (int)metricManager.GetLatencyMeter(MetricType.Total).GetLatency() * 5;
                waitms = (waitms > 5000) ? 5000 : waitms;
                waitms = (waitms < 1000) ? 1000 : waitms;
                Logger.Instance.LogWarn("已停止发送, 等待回包中, 预计需要 {0:f2}s", (Math.Round(waitms / 1000d, 2)));
                Thread.Sleep(waitms);  // 等待最后的回包
                Console.WriteLine("\r\n测试完成, 下面是最终结果: ");
                Console.WriteLine(metricManager.GetMetricMessage(MetricType.Total));
                Console.WriteLine();
                Logger.Instance.LogInfo("测试程序已停止");
            }
        }
        private void RelayInterval(object stateInfo)
        {
            CheckStopMark();
            if (stopMark)
            {
                timerForIntervalRelay.Dispose();
            }
            else
            {
                if (ConfigManager.Instance.ClientCount > 1)
                    sendQueue.Enqueue(233);
                else
                    SendOnce();
            }
        }
        private void CheckStopMark()
        {
            mutex.WaitOne();
            if (ConfigManager.Instance.ControlMode == "count" &&
                metricManager.GetLossMeter(MetricType.Total).GetLoss().Item1 >= ConfigManager.Instance.ControlParam)
            {
                stopMark = true;
            }
            if (ConfigManager.Instance.ControlMode == "time" &&
                stopWatch.ElapsedMilliseconds >= ConfigManager.Instance.ControlParam * 1000)
            {
                stopMark = true;
            }
            if (stopMark)
            {
                Finish();
            }
            mutex.ReleaseMutex();
        }
        private void CheckLogPeriodByCount()
        {
            mutex.WaitOne();
            if (ConfigManager.Instance.LogMode == "count" &&
                metricManager.GetLossMeter(MetricType.Total).GetLoss().Item1 % ConfigManager.Instance.LogParam == 0)
            {
                Console.WriteLine(metricManager.GetMetricMessage(MetricType.Period));
                metricManager.ResetPeriodMetricData();
            }
            mutex.ReleaseMutex();
        }
        private void SendOnce()
        {
            if (metricManager.AcquireEchoDataArg(out byte[] data, out int offset, out int length, out int echoint))
            {
                netClients.Send(data, offset, length);
                metricManager.StartEchoWatch(echoint);
            }
            else
            {
                StopManually();
            }
        }
    }
}
