using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;

namespace EchoClientCore
{
    /// <summary>
    /// 协议封装接口
    /// </summary>
    public interface IProtocolWrapper
    {
        bool PackProtocol(ref byte[] data, ref int offset, ref int length);
        bool UnpackProtocol(ref byte[] data, ref int offset, ref int length);
    }

    /// <summary>
    /// 简易协议封装工厂类
    /// </summary>
    public class ProtocolWrapperFactory
    {
        private static IProtocolWrapper protocolWrapper = null;
        private static Mutex mutex = new Mutex();
        public static IProtocolWrapper GetProtocolWrapper(string param)
        {
            mutex.WaitOne();
            if (protocolWrapper == null)
            {
                protocolWrapper = new RawProtocolWrapper();
            }
            mutex.ReleaseMutex();
            return protocolWrapper;
        }
    }

    /// <summary>
    /// 无自订协议
    /// </summary>
    public class RawProtocolWrapper : IProtocolWrapper
    {
        public bool PackProtocol(ref byte[] data, ref int offset, ref int length)
        {
            // 无协议，只做基本检查
            if (offset < 0 || offset >= data.Length)
            {
                Logger.Instance.LogFatal($"协议封包 offset 参数错误: {offset}");
                return false;
            }
            if (length <= 0 || length > data.Length)
            {
                Logger.Instance.LogFatal($"协议封包 length 参数错误: {offset}");
                return false;
            }
            return true;
        }

        public bool UnpackProtocol(ref byte[] data, ref int offset, ref int length)
        {
            // 无协议，只做基本检查
            if (offset < 0 || offset >= data.Length)
            {
                Logger.Instance.LogFatal($"协议解包 offset 参数错误: {offset}");
                return false;
            }
            if (length <= 0 || length > data.Length)
            {
                Logger.Instance.LogFatal($"协议解包 length 参数错误: {offset}");
                return false;
            }
            return true;
        }
    }
}
