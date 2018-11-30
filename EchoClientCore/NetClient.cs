using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using socket.core.Client;
using System.Threading;

namespace EchoClientCore
{
    /// <summary>
    /// 网络客户端管理类
    /// </summary>
    public class NetClients
    {
        private INetClient[] netClients;
        private Random random;
        /// <summary>
        /// 构造网络客户端管理器对象
        /// </summary>
        /// <param name="protocol">协议，填udp或tcp</param>
        /// <param name="clientcount">连接数</param>
        /// <param name="threadsperclient">每个连接的线程数</param>
        /// <param name="buffersize">每个连接的接收缓冲区大小</param>
        public NetClients(string protocol, int clientcount, int threadsperclient, int buffersize)
        {
            if (clientcount < 1 || threadsperclient < 1)
            {
                Logger.Instance.LogFatal($"连接数: {clientcount} 或线程数: {threadsperclient} 非法");
                return;
            }
            netClients = new INetClient[clientcount];
            for (int i = 0; i < clientcount; i++)
            {
                netClients[i] = NetClientFactory.GetNetClient(protocol, buffersize, threadsperclient);
                netClients[i].OnSend += OnSendInternal;
                netClients[i].OnReceive += OnReceiveInternal;
            }
            random = new Random(Environment.TickCount);
        }
        /// <summary>
        /// 接收回调
        /// </summary>
        public event Action<byte[], int, int> OnReceive;
        /// <summary>
        /// 发送回调
        /// </summary>
        public event Action<int> OnSend;
        /// <summary>
        /// 连接远端目标
        /// </summary>
        /// <param name="ip">IPv4、IPv6地址</param>
        /// <param name="port">端口号</param>
        /// <returns></returns>
        public bool Init(string ip, int port)
        {
            if (netClients.Length == 0)
            {
                Logger.Instance.LogFatal("Connect: 没有可用的客户端连接");
                return false;
            }
            for (int i = 0; i < netClients.Length; i++)
            {
                if (!netClients[i].Connect(ip, port))
                {
                    Logger.Instance.LogFatal($"客户端 {i}/{netClients.Length} 连接远端目标 {ip}:{port} 失败");
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// 发送数据(负载均衡)
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool Send(byte[] data, int offset, int length)
        {
            if (netClients.Length == 0)
            {
                Logger.Instance.LogFatal("Send: 没有可用的客户端连接");
                return false;
            }
            else if (netClients.Length == 1)
            {
                return netClients[0].Send(data, offset, length);
            }
            else
            {
                return netClients[BitConverter.ToInt32(data) % netClients.Length].Send(data, offset, length);
            }
        }
        private void OnReceiveInternal(byte[] data, int offset, int length)
        {
            OnReceive(data, offset, length);
        }

        private void OnSendInternal(int sendbytes)
        {
            OnSend(sendbytes);
        }
    }

    /// <summary>
    /// 协议无关的网络客户端接口
    /// </summary>
    interface INetClient
    {
        bool Connect(string ip, int port);
        bool Send(byte[] data, int offset, int length);
        event Action<byte[], int, int> OnReceive;
        event Action<int> OnSend;
    }

    /// <summary>
    /// 生成网络客户端的简易工厂
    /// </summary>
    internal class NetClientFactory
    {
        public static INetClient GetNetClient(string protocol, int buffersize, int threads)
        {
            if (buffersize < 16)
            {
                Logger.Instance.LogFatal($"生成NetClient缓冲区过小: {buffersize}");
                return null;
            }
            if (protocol.ToLower() == "udp")
            {
                Logger.Instance.LogTrace($"生成了新的UdpNetClient");
                return new NetUdpClient(buffersize, threads);
            }
            else if (protocol.ToLower() == "tcp")
            {
                Logger.Instance.LogTrace($"生成了新的TcpNetClient");
                return new NetTcpClient(buffersize, threads);
            }
            else
            {
                Logger.Instance.LogFatal($"生成NetClient协议参数错误: {protocol}");
                return null;
            }
        }
    }

    /// <summary>
    /// UDP协议网络客户端实现
    /// </summary>
    internal class NetUdpClient : INetClient
    {
        private UdpClients udpClient;
        public NetUdpClient(int buffersize, int threads)
        {
            udpClient = new UdpClients(buffersize);
            udpClient.OnSend += OnSendInternal;
            udpClient.OnReceive += OnReceiveInternal;
        }

        public event Action<byte[], int, int> OnReceive;
        public event Action<int> OnSend;

        public bool Connect(string ip, int port)
        {
            udpClient.Start(ip, port);
            return true;
        }

        public bool Send(byte[] data, int offset, int length)
        {
            udpClient.Send(data, offset, length);
            return true;
        }

        private void OnReceiveInternal(byte[] data, int offset, int length)
        {
            OnReceive(data, offset, length);
        }

        private void OnSendInternal(int sendbytes)
        {
            OnSend(sendbytes);
        }
    }

    /// <summary>
    /// TCP协议网络客户端实现
    /// </summary>
    internal class NetTcpClient : INetClient
    {
        private TcpPushClient tcpClient;
        private bool connResult;
        private ManualResetEvent mre;
        public NetTcpClient(int buffersize, int threads)
        {
            connResult = false;
            mre = new ManualResetEvent(false);
            tcpClient = new TcpPushClient(buffersize);
            tcpClient.OnConnect += OnConnectInternal;
            tcpClient.OnSend += OnSendInternal;
            tcpClient.OnReceive += OnReceiveInternal;
        }

        public event Action<byte[], int, int> OnReceive;
        public event Action<int> OnSend;

        public bool Connect(string ip, int port)
        {
            tcpClient.Connect(ip, port);
            mre.WaitOne(5000);
            mre.Reset();
            return connResult;
        }

        public bool Send(byte[] data, int offset, int length)
        {
            tcpClient.Send(data, offset, length);
            return true;
        }

        private void OnReceiveInternal(byte[] data)
        {
            OnReceive(data, 0, data.Length);
        }

        private void OnSendInternal(int sendbytes)
        {
            OnSend(sendbytes);
        }

        private void OnConnectInternal(bool result)
        {
            connResult = result;
            mre.Set();
        }
    }
}
