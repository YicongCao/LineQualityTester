using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;

namespace EchoClientCore
{
    /// <summary>
    /// 根据Microsoft.Extensions.Logging包制作的简易单例日志类
    /// </summary>
    public sealed class Logger
    {
        private static readonly Lazy<Logger> lazy = new Lazy<Logger>(() => new Logger());
        private static readonly Lazy<ILogger> logger = new Lazy<ILogger>(() => new ServiceCollection()
                           .AddLogging()
                           .BuildServiceProvider()
                           .GetService<ILoggerFactory>()
                           .AddConsole(LogLevel.Information)
                           .AddDebug(LogLevel.Trace)
                           .CreateLogger(nameof(Logger)));
        private int eventId = 0;

        public static Logger Instance
        {
            get
            {
                return lazy.Value;
            }
        }
        private static ILogger LoggerInternal
        {
            get
            {
                return logger.Value;
            }
        }

        public Logger()
        {

        }

        public void LogTrace(string format, params object[] paramList)
        {
            LoggerInternal.LogTrace(eventId++, "[{0}] {1}", DateTime.Now, string.Format(format, paramList));
        }
        public void LogInfo(string format, params object[] paramList)
        {
            LoggerInternal.LogInformation(eventId++, "[{0}] {1}", DateTime.Now, string.Format(format, paramList));
        }
        public void LogWarn(string format, params object[] paramList)
        {
            LoggerInternal.LogWarning(eventId++, "[{0}] {1}", DateTime.Now, string.Format(format, paramList));
        }
        public void LogError(string format, params object[] paramList)
        {
            LoggerInternal.LogError(eventId++, "[{0}] {1}", DateTime.Now, string.Format(format, paramList));
        }
        public void LogFatal(string format, params object[] paramList)
        {
            LoggerInternal.LogCritical(eventId++, "[{0}] {1}", DateTime.Now, string.Format(format, paramList));
        }
    }
}
