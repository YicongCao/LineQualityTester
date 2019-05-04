using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Linq;
using System.ComponentModel;

namespace EchoClientCore
{
    public enum MetricType
    {
        Realtime = 0,
        Period = 1,
        Total = 2,
        TotalLog = 3,
    }
    /// <summary>
    /// 性能计数管理类
    /// </summary>
    class MetricManager
    {
        // 发送池相关
        private ConcurrentQueue<int> echoIndexPool;
        private List<int> occupiedIndexPool;
        private byte[] echoDataPool;
        private Stopwatch[] echoWatchPool;
        private int poolUnitSize;
        private int poolCapacity;
        // 性能计数器
        private const int REALTIME = 0;
        private const int PERIOD = 1;
        private const int TOTAL = 2;
        private SpeedMeter[] speedMeters;
        private LatencyMeter[] latencyMeters;
        private LossMeter[] lossMeters;
        private Mutex mutex;
        private int lastSendBytes;

        public MetricManager()
        {
            echoIndexPool = null;
            occupiedIndexPool = null;
            echoDataPool = null;
            echoWatchPool = null;
            poolUnitSize = 0;
            poolCapacity = 0;
            speedMeters = new SpeedMeter[3];
            latencyMeters = new LatencyMeter[3];
            lossMeters = new LossMeter[3];
            mutex = new Mutex();
            lastSendBytes = 0;
        }
        public bool Init(int sendpoolsize)
        {
            // 初始化发送池数据
            poolCapacity = sendpoolsize;
            poolUnitSize = BitConverter.GetBytes(int.MaxValue).Length; // 4 bytes
            echoIndexPool = new ConcurrentQueue<int>();
            occupiedIndexPool = new List<int>();
            echoDataPool = new byte[poolCapacity * poolUnitSize];
            echoWatchPool = new Stopwatch[poolCapacity];
            int digits = poolCapacity.ToString().Length;
            for (int i = 0; i < poolCapacity; i++)
            {
                echoIndexPool.Enqueue(i);
                echoWatchPool[i] = new Stopwatch();
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, echoDataPool, i * poolUnitSize, poolUnitSize);
            }
            // 初始化计数器
            speedMeters[REALTIME] = new SpeedMeter();
            speedMeters[PERIOD] = new SpeedMeter(long.MaxValue);
            speedMeters[TOTAL] = new SpeedMeter(long.MaxValue);
            latencyMeters[REALTIME] = new LatencyMeter();
            latencyMeters[PERIOD] = new LatencyMeter(long.MaxValue);
            latencyMeters[TOTAL] = new LatencyMeter(long.MaxValue);
            lossMeters[REALTIME] = new LossMeter();
            lossMeters[PERIOD] = new LossMeter(long.MaxValue);
            lossMeters[TOTAL] = new LossMeter(long.MaxValue);
            // 检查秒表精度
            if (!Stopwatch.IsHighResolution)
            {
                Logger.Instance.LogWarn("未采用高精度计时");
            }
            return true;
        }

        public bool AcquireEchoDataArg(out byte[] buffer, out int offset, out int length, out int echoint)
        {
            mutex.WaitOne();
            buffer = null;
            offset = -1;
            length = -1;
            echoint = -1;
            int echoIndex = -1;
            if (ConfigManager.Instance.DataProtocol == "fakes5echo")
            {
                echoIndex = 97;
                if (occupiedIndexPool.IndexOf(echoIndex) != -1)
                {
                    Logger.Instance.LogError("已经有正在进行的97号请求");
                    mutex.ReleaseMutex();
                    return false;
                }
            }
            else
            {
                if (!echoIndexPool.TryDequeue(out echoIndex))
                {
                    Logger.Instance.LogError("发送池枯竭");
                    mutex.ReleaseMutex();
                    return false;
                }
            }
            occupiedIndexPool.Add(echoIndex);
            mutex.ReleaseMutex();
            buffer = new byte[poolUnitSize];
            offset = 0;
            length = poolUnitSize;
            Buffer.BlockCopy(echoDataPool, echoIndex * poolUnitSize, buffer, 0, poolUnitSize);

            if (BitConverter.ToInt32(buffer) != echoIndex)
            {
                Logger.Instance.LogError($"数据校验出错: {BitConverter.ToInt32(buffer)} != {echoIndex}");
                return false;
            }

            // Logger.Instance.LogInfo($"[{echoIndex}] sent");
            echoint = echoIndex;

            ProtocolWrapperFactory.GetProtocolWrapper("default").PackProtocol(ref buffer, ref offset, ref length);

            return true;
        }
        public void StartEchoWatch(int echoindex)
        {
            if (echoindex < 0 || echoindex >= echoWatchPool.Length)
            {
                Logger.Instance.LogWarn($"启动秒表下标越界{echoindex}");
            }
            echoWatchPool[echoindex].Reset();
            echoWatchPool[echoindex].Restart();
        }
        public List<int> GetTimeoutEchoDataArgs()
        {
            return occupiedIndexPool;
        }
        public void ResetPeriodMetricData()
        {
            speedMeters[PERIOD].Reset();
            latencyMeters[PERIOD].Reset();
            lossMeters[PERIOD].Reset();
        }
        public string GetMetricMessage(MetricType metricType)
        {
            string prefix = "";
            int index = -1;
            if (metricType == MetricType.Realtime)
            {
                prefix = "实时";
                index = REALTIME;
            }
            if (metricType == MetricType.Period)
            {
                prefix = "阶段";
                index = PERIOD;
            }
            if (metricType == MetricType.Total)
            {
                prefix = "总计";
                index = TOTAL;
            }
            if (metricType == MetricType.TotalLog)
            {
                prefix = "日志";
                index = TOTAL;

                return string.Format("{0}, {1}, {2}pps, {3}ms, {4}, {5}lost, {6}sent, {7}Mbps",
                                 prefix,
                                 DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                 speedMeters[index].GetSpeed(),
                                 latencyMeters[index].GetLatency(),
                                 lossMeters[index].GetLossString(),
                                 lossMeters[index].GetLoss().Item1 - lossMeters[index].GetLoss().Item2,
                                 lossMeters[index].GetLoss().Item1,
                                 (double)speedMeters[index].GetSpeed() * lastSendBytes * 8d / 1000d / 1000d);
            }
            return string.Format("[{5}] 速度: {0}pps, 延迟: {1:f2}ms, 丢包: {2} ({3}/{4}), 带宽: {6:f2}Mbps",
                                 speedMeters[index].GetSpeed(),
                                 latencyMeters[index].GetLatency(),
                                 lossMeters[index].GetLossString(),
                                 lossMeters[index].GetLoss().Item1 - lossMeters[index].GetLoss().Item2,
                                 lossMeters[index].GetLoss().Item1,
                                 prefix,
                                 (double)speedMeters[index].GetSpeed() * lastSendBytes * 8d / 1000d / 1000d);
        }
        public SpeedMeter GetSpeedMeter(MetricType metricType)
        {
            return speedMeters[(int)metricType];
        }
        public LatencyMeter GetLatencyMeter(MetricType metricType)
        {
            return latencyMeters[(int)metricType];
        }
        public LossMeter GetLossMeter(MetricType metricType)
        {
            return lossMeters[(int)metricType];
        }
        public void OnReceive(byte[] data, int offset, int length)
        {
            long latency = 0;

            ProtocolWrapperFactory.GetProtocolWrapper("default").UnpackProtocol(ref data, ref offset, ref length);

            for (int i = offset; i < length + offset; i += poolUnitSize)
            {
                if (RecycleEchoDataArg(data, i, poolUnitSize, out latency))
                {
                    // for debug
                    // Logger.Instance.LogInfo($"[{BitConverter.ToInt32(data, i)}]: {latency}ms");
                    latencyMeters.ToList().ForEach(x => x.IncreaseLatency(latency));
                }
                lossMeters.ToList().ForEach(x => x.IncreaseRecv());
            }

        }
        public void OnSend(int sendbytes)
        {
            speedMeters.ToList().ForEach(x => x.IncreaseCount());
            lossMeters.ToList().ForEach(x => x.IncreaseSend());
            if (lastSendBytes != sendbytes)
                lastSendBytes = sendbytes;
        }
        private bool RecycleEchoDataArg(byte[] buffer, int offset, int length, out long latency)
        {
            int echoIndex = -1;
            latency = -1;

            if (length % poolUnitSize != 0)
            {
                Logger.Instance.LogWarn($"收到非法数据: {echoIndex} length = {length}");
                return false;
            }

            for (int i = offset; i < length + offset; i += poolUnitSize)
            {
                echoIndex = BitConverter.ToInt32(buffer, i);
                if (echoIndex < 0 || echoIndex >= poolCapacity)
                {
                    Logger.Instance.LogWarn($"回收下标越界: {echoIndex}");
                    continue;
                }
                latency = echoWatchPool[echoIndex].ElapsedMilliseconds;
                if (latency > 2500)
                {
                    Logger.Instance.LogWarn($"异常超大延迟: [{echoIndex}] -> {latency}ms");
                    continue;
                }
                if (!echoWatchPool[echoIndex].IsRunning)
                {
                    Logger.Instance.LogWarn($"{echoIndex} 号秒表未在运行(回显数据可能出错)");
                    continue;
                }
                mutex.WaitOne();
                echoWatchPool[echoIndex].Stop();
                echoWatchPool[echoIndex].Reset();
                occupiedIndexPool.Remove(echoIndex);
                echoIndexPool.Enqueue(echoIndex);
                mutex.ReleaseMutex();
            }
            return true;
        }
    }
    /// <summary>
    /// 速度计
    /// </summary>
    class SpeedMeter
    {
        private long count;
        private Stopwatch watch;
        private long interval;
        private object obj;
        /// <summary>
        /// 生成一个速度计
        /// </summary>
        /// <param name="intervalParam">统计区间(毫秒)</param>
        public SpeedMeter(long intervalParam = 1000)
        {
            count = 0;
            interval = intervalParam;
            obj = 3;
            watch = new Stopwatch();
            Start();
        }
        /// <summary>
        /// 开始计速
        /// </summary>
        private void Start()
        {
            watch.Reset();
            watch.Start();
        }
        /// <summary>
        /// 增加次数
        /// </summary>
        public void IncreaseCount()
        {
            lock (obj)
            {
                if (watch.ElapsedMilliseconds > interval)
                {
                    Reset();
                }
            }
            Interlocked.Increment(ref count);
        }
        /// <summary>
        /// 增加指定数量
        /// </summary>
        /// <param name="toadd">Toadd.</param>
        public void IncreaseCount(int toadd)
        {
            lock (obj)
            {
                if (watch.ElapsedMilliseconds > interval)
                {
                    Reset();
                }
            }
            Interlocked.Add(ref count, toadd);
        }
        /// <summary>
        /// 获取速度
        /// </summary>
        /// <returns></returns>
        public long GetSpeed()
        {
            long pps = (long)(Math.Round((double)count / (double)(watch.ElapsedMilliseconds) * 1000d)); // packets per second
            pps = (pps > 0) ? pps : 0;
            return pps;
        }
        /// <summary>
        /// 获取带宽
        /// </summary>
        /// <returns>The speed float.</returns>
        public double GetSpeedFloat()
        {
            double speed = (double)count / (double)(watch.ElapsedMilliseconds) * 8d / 1000d; // Mbps
            speed = (speed > 0) ? Math.Round(speed, 2) : 0;
            return speed;
        }
        /// <summary>
        /// 重置计数器
        /// </summary>
        public void Reset()
        {
            lock (obj)
            {
                count = 0;
                watch.Restart();
            }
        }
    }
    /// <summary>
    /// 延迟计
    /// </summary>
    class LatencyMeter
    {
        private long count;
        private long sum;
        private Stopwatch watch;
        private long interval;
        private object obj;
        public LatencyMeter(long intervalParam = 1000)
        {
            count = 0;
            sum = 0;
            interval = intervalParam;
            obj = 4;
            watch = new Stopwatch();
            Start();
        }
        /// <summary>
        /// 开始统计
        /// </summary>
        private void Start()
        {
            watch.Reset();
            watch.Start();
        }
        /// <summary>
        /// 增加计量
        /// </summary>
        public void IncreaseLatency(long latency)
        {
            lock (obj)
            {
                if (watch.ElapsedMilliseconds > interval)
                {
                    Reset();
                }
            }
            Interlocked.Increment(ref count);
            Interlocked.Add(ref sum, latency);
        }
        /// <summary>
        /// 获取平均延迟
        /// </summary>
        public double GetLatency()
        {
            if (count == 0)
                return 0;
            double latency = (double)(Math.Round((double)Interlocked.Read(ref sum) / (double)Interlocked.Read(ref count), 2));
            latency = (latency > 0) ? latency : 0;
            return latency;
        }
        /// <summary>
        /// 重置计数器
        /// </summary>
        public void Reset()
        {
            lock (obj)
            {
                count = 0;
                sum = 0;
                watch.Restart();
            }
        }
    }
    /// <summary>
    /// 丢包计
    /// </summary>
    class LossMeter
    {
        private long send;
        private long recv;
        private Stopwatch watch;
        private long interval;
        private object obj;
        public LossMeter(long intervalParam = 1000)
        {
            send = 0;
            recv = 0;
            interval = intervalParam;
            obj = 4;
            watch = new Stopwatch();
            Start();
        }
        /// <summary>
        /// 开始统计
        /// </summary>
        private void Start()
        {
            watch.Reset();
            watch.Start();
        }
        /// <summary>
        /// 标记发包
        /// </summary>
        public void IncreaseSend()
        {
            lock (obj)
            {
                if (watch.ElapsedMilliseconds > interval)
                {
                    Reset();
                }
            }
            Interlocked.Increment(ref send);
        }
        /// <summary>
        /// 标记收包
        /// </summary>
        public void IncreaseRecv()
        {
            lock (obj)
            {
                if (watch.ElapsedMilliseconds > interval)
                {
                    Reset();
                }
            }
            Interlocked.Increment(ref recv);
        }
        /// <summary>
        /// 获取丢包率
        /// </summary>
        public Tuple<long, long> GetLoss()
        {
            return Tuple.Create(Interlocked.Read(ref send), Interlocked.Read(ref recv));
        }
        public string GetLossString()
        {
            return string.Format("{0:f2}%", System.Math.Round(100d * (double)(send - recv) / (double)send, 2));
        }
        /// <summary>
        /// 重置计数器
        /// </summary>
        public void Reset()
        {
            lock (obj)
            {
                send = 0;
                recv = 0;
                watch.Restart();
            }
        }
    }
}
