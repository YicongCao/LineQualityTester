using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace EchoClientCore
{
    public sealed class ConfigManager
    {
        private static readonly Lazy<ConfigManager> lazy = new Lazy<ConfigManager>(() => new ConfigManager());
        public static ConfigManager Instance
        {
            get
            {
                return lazy.Value;
            }
        }

        private string protocol, controlmode, relaymode, logmode, ip;
        private int clientcount, threadsperclient, controlparam, port;
        private int relayinterval, relaymaxpps, relayppsdelay, warmupcount, warmupinterval;
        private int sendpoolsize, logparam, buffersize;
        private IPEndPoint target;
        // for classic lsp (fake s5)
        private string dataprotocol;
        private string ipacc;
        private int portacc;
        private IPEndPoint targetacc;
        public ConfigManager()
        {
            protocol = controlmode = relaymode = logmode = ip = "invalid";
            port = 0;
            clientcount = 1;
            threadsperclient = 6;
            controlparam = 100000;
            relayinterval = 10;
            relaymaxpps = 1000;
            relayppsdelay = 10;
            warmupcount = 1500;
            warmupinterval = 2;
            sendpoolsize = 10000;
            logparam = 10000;
            buffersize = 2048;
            target = null;
            targetacc = null;
            ipacc = "invalid";
            dataprotocol = "raw";
            portacc = 0;
        }
        /// <summary>
        /// 初始化配置文件管理器
        /// </summary>
        /// <param name="ConfigFileName">配置文件名</param>
        /// <returns></returns>
        public bool Init(string ConfigFileName)
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + ConfigFileName;
            if (File.Exists(path))
            {
                // 读取文件内容
                StreamReader reader = new StreamReader(path);
                string xmlcontent = reader.ReadToEnd();
                reader.Close();

                // 解析属性值
                var doc = XDocument.Parse(xmlcontent);
                protocol = ReadConfigToString(doc, "general", "protocol", "");
                clientcount = ReadConfigToInt(doc, "general", "clientcount", 1);
                threadsperclient = ReadConfigToInt(doc, "general", "threadsperclient", 8);
                buffersize = ReadConfigToInt(doc, "general", "buffersize", 2048);
                string ipinternal = ReadConfigToString(doc, "target", "ip", "127.0.0.1");
                int portinternal = ReadConfigToInt(doc, "target", "port", 7777);

                IPAddress ipaddr;
                if (!IPAddress.TryParse(ipinternal, out ipaddr))
                {
                    IPAddress[] iplist = Dns.GetHostAddresses(ipinternal);
                    if (iplist != null && iplist.Length > 0)
                    {
                        ipaddr = iplist[0];
                    }
                    else
                    {
                        Logger.Instance.LogError($"无法解析名称: {ipinternal}");
                        return false;
                    }
                }
                ipinternal = ipaddr.ToString();
                if (portinternal <= 0 || portinternal > 65536)
                {
                    Logger.Instance.LogError($"无效端口号: {portinternal}");
                    return false;
                }

                this.ip = ipinternal;
                this.port = portinternal;
                target = new IPEndPoint(IPAddress.Parse(ipinternal), portinternal);
                controlmode = ReadConfigToString(doc, "control", "mode", "manual");
                controlparam = ReadConfigToInt(doc, "control", "param", 10000);
                relaymode = ReadConfigToString(doc, "relay", "mode", "interval");
                relayinterval = ReadConfigToInt(doc, "relay", "interval", 200);
                relaymaxpps = ReadConfigToInt(doc, "relay", "maxpps", 1000);
                relayppsdelay = ReadConfigToInt(doc, "relay", "ppsdelay", 5);
                warmupcount = ReadConfigToInt(doc, "relay", "warmupcount", 500);
                warmupinterval = ReadConfigToInt(doc, "relay", "warmupinterval", 1);
                sendpoolsize = ReadConfigToInt(doc, "debug", "sendpoolsize", 8);
                logmode = ReadConfigToString(doc, "log", "mode", "time");
                logparam = ReadConfigToInt(doc, "log", "param", 5000);
                dataprotocol = ReadConfigToString(doc, "dataprotocol", "mode", "raw");
                string ipacc = ReadConfigToString(doc, "dataprotocol", "param1", "127.0.0.1");
                int portacc = ReadConfigToInt(doc, "dataprotocol", "param2", 7777);

                if (!IPAddress.TryParse(ipacc, out ipaddr))
                {
                    IPAddress[] iplist = Dns.GetHostAddresses(ipacc);
                    if (iplist != null && iplist.Length > 0)
                    {
                        ipaddr = iplist[0];
                    }
                    else
                    {
                        Logger.Instance.LogError($"无法解析名称: {ipacc}");
                        return false;
                    }
                }
                ipacc = ipaddr.ToString();
                if (portacc <= 0 || portacc > 65536)
                {
                    Logger.Instance.LogError($"无效端口号: {portacc}");
                    return false;
                }

                this.ipacc = ipacc;
                this.portacc = portacc;
                targetacc = new IPEndPoint(IPAddress.Parse(ipacc), portacc);

                // 有效性检查
                if (protocol != "udp" && protocol != "tcp")
                {
                    Logger.Instance.LogError("不支持的协议");
                    return false;
                }
                if (clientcount <= 0 || threadsperclient <= 0)
                {
                    Logger.Instance.LogError("客户端数/线程数不能小于0");
                    return false;
                }
                if (controlmode != "count" && controlmode != "time" && controlmode != "manual")
                {
                    Logger.Instance.LogError("发包控制模式设置错误");
                    return false;
                }
                if (relaymode != "interval" && relaymode != "onsend" && relaymode != "onrecv")
                {
                    Logger.Instance.LogError("接力控制模式设置错误");
                    return false;
                }
                if (logmode != "count" && logmode != "time")
                {
                    Logger.Instance.LogError("Log打点控制模式设置错误");
                    return false;
                }
                if (controlparam <= 0)
                {
                    Logger.Instance.LogError("发包控制参数设置错误");
                    return false;
                }
                if (relayinterval < 0 || relayppsdelay < 0 || warmupinterval < 0 || warmupcount <= 0 || relaymaxpps <= 0)
                {
                    Logger.Instance.LogError("接力控制参数设置错误");
                    return false;
                }
                if (sendpoolsize <= 0)
                {
                    Logger.Instance.LogError("发送池大小参数设置错误");
                    return false;
                }
                if (logparam <= 0)
                {
                    Logger.Instance.LogError("Log打点参数设置错误");
                    return false;
                }
                if (protocol == "tcp" && dataprotocol == "fakes5")
                {
                    Logger.Instance.LogError("本程序暂不支持经典LSP数据协议的TCP模式");
                    return false;
                }
            }
            else
            {
                Logger.Instance.LogError("配置文件不存在");
                return false;
            }
            Logger.Instance.LogInfo($"已加载配置文件: {ConfigFileName}\r\n");
            return true;
        }
        private string ReadConfigToString(XDocument doc, string elementName, string attributeName, string defaultVal = "")
        {
            //doc.Descendants("relay").First().Attribute("maxpps").Value
            string ret = defaultVal;
            do
            {
                var elements = doc.Descendants(elementName);
                if (elements == null || elements.Count() <= 0)
                {
                    Logger.Instance.LogWarn($"元素 {elementName} 不存在, 使用默认值 {defaultVal}");
                    break;
                }
                else if (elements.Count() > 1)
                {
                    Logger.Instance.LogWarn($"元素 {elementName} 存在多个, 使用默认值 {defaultVal}");
                    break;
                }
                var attribute = elements.First().Attribute(attributeName);
                if (attribute == null)
                {
                    Logger.Instance.LogTrace($"属性 {elementName}->{elementName} 不存在, 使用默认值 {defaultVal}");
                    break;
                }
                ret = attribute.Value.Trim().ToLower();
            }
            while (false);
            return ret;
        }
        private int ReadConfigToInt(XDocument doc, string elementName, string attributeName, int defaultVal = -1)
        {
            int ret = defaultVal;
            do
            {
                string val = ReadConfigToString(doc, elementName, attributeName);
                if (val == null)
                {
                    break;
                }
                if (!int.TryParse(val, out ret))
                {
                    Logger.Instance.LogTrace($"属性 {elementName}->{attributeName}: [{val}] 无法转换成整数, 使用默认值 {defaultVal}");
                    ret = defaultVal;
                    break;
                }
            }
            while (false);
            return ret;
        }
        /// <summary>
        /// TCP or UDP
        /// </summary>
        public string Protocol { get => protocol; }
        /// <summary>
        /// 发包控制：time or count
        /// </summary>
        public string ControlMode { get => controlmode; }
        /// <summary>
        /// 接力模式：interval, onsend or onrecv
        /// </summary>
        public string RelayMode { get => relaymode; }
        /// <summary>
        /// 日志打点模式：time or count
        /// </summary>
        public string LogMode { get => logmode; }
        /// <summary>
        /// 模拟客户端（使用socket）的数量
        /// </summary>
        public int ClientCount { get => clientcount; }
        /// <summary>
        /// 每个客户端配备发送线程的数量
        /// </summary>
        public int ThreadsPerClient { get => threadsperclient; }
        /// <summary>
        /// 发包控制参数：时间(秒)或包数
        /// </summary>
        public int ControlParam { get => controlparam; }
        /// <summary>
        /// 间隔时间(毫秒)
        /// </summary>
        public int RelayInterval { get => relayinterval; }
        /// <summary>
        /// 最大发包速度(pps)
        /// </summary>
        public int RelayMaxPps { get => relaymaxpps; }
        /// <summary>
        /// 控制发包速度的间隔(毫秒)
        /// </summary>
        public int RelayPpsDelay { get => relayppsdelay; }
        /// <summary>
        /// 热身数量(决定最大pps)
        /// </summary>
        public int WarmupCount { get => warmupcount; }
        /// <summary>
        /// 热身间隔(毫秒)
        /// </summary>
        public int WarmupInterval { get => warmupinterval; }
        /// <summary>
        /// 发送池大小
        /// </summary>
        public int SendPoolSize { get => sendpoolsize; }
        /// <summary>
        /// 日志打点参数：时间(秒)或包数
        /// </summary>
        public int LogParam { get => logparam; }
        /// <summary>
        /// 压测目标
        /// </summary>
        public IPEndPoint Target { get => target; }
        /// <summary>
        /// socket的接收缓冲区大小(字节)
        /// </summary>
        public int BufferSize { get => buffersize; }
        /// <summary>
        /// IP
        /// </summary>
        /// <value>The ip.</value>
        public string IP { get => ip; }
        /// <summary>
        /// 端口
        /// </summary>
        /// <value>The port.</value>
        public int Port { get => port; }
        /// <summary>
        /// 数据协议：raw表示纯回显服务，无自定协议
        /// </summary>
        /// <value>The data protocol.</value>
        public string DataProtocol { get => dataprotocol; }
        /// <summary>
        /// 代理IP
        /// </summary>
        /// <value>The IPA cc.</value>
        public string IPAcc { get => ipacc; }
        /// <summary>
        /// 代理端口
        /// </summary>
        /// <value>The port acc.</value>
        public int PortAcc { get => portacc; }
        /// <summary>
        /// 代理目标
        /// </summary>
        /// <value>The target acc.</value>
        public IPEndPoint TargetAcc { get => targetacc; }
    }
}
