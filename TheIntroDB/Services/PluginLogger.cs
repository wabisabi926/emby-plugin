using System;
using System.IO;
using System.Text;
using MediaBrowser.Model.Logging;

namespace TheIntroDB.Services
{
    public class PluginLogger : ILogger, IDisposable
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        public PluginLogger(string dataPath)
        {
            var logDir = Path.Combine(dataPath, "theintrodb");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "theintrodb.log");
            WriteLine("INF", "PluginLogger initialized");
        }

        public void Info(string message, params object[] paramList)
        {
            WriteLine("INF", message, paramList);
        }

        public void Warn(string message, params object[] paramList)
        {
            WriteLine("WRN", message, paramList);
        }

        public void Error(string message, params object[] paramList)
        {
            WriteLine("ERR", message, paramList);
        }

        public void Debug(string message, params object[] paramList)
        {
            WriteLine("DBG", message, paramList);
        }

        public void Fatal(string message, params object[] paramList)
        {
            WriteLine("FTL", message, paramList);
        }

        public void FatalException(string message, Exception exception, params object[] paramList)
        {
            WriteLine("FTL", message + ": " + exception, paramList);
        }

        public void ErrorException(string message, Exception exception, params object[] paramList)
        {
            WriteLine("ERR", message + ": " + exception, paramList);
        }

        public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent)
        {
            var level = SeverityToPrefix(severity);
            WriteLine(level, message);
            if (additionalContent != null && additionalContent.Length > 0)
            {
                lock (_lock)
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, additionalContent.ToString() + Environment.NewLine);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void Log(LogSeverity severity, string message, params object[] paramList)
        {
            WriteLine(SeverityToPrefix(severity), message, paramList);
        }

        public void Dispose()
        {
            WriteLine("INF", "PluginLogger disposed");
        }

#pragma warning disable CS0618
        public void Log(LogSeverity severity, ReadOnlyMemory<char> message)
        {
            WriteLine(SeverityToPrefix(severity), message.ToString());
        }

        public void Error(ReadOnlyMemory<char> message)
        {
            WriteLine("ERR", message.ToString());
        }

        public void Warn(ReadOnlyMemory<char> message)
        {
            WriteLine("WRN", message.ToString());
        }

        public void Info(ReadOnlyMemory<char> message)
        {
            WriteLine("INF", message.ToString());
        }

        public void Debug(ReadOnlyMemory<char> message)
        {
            WriteLine("DBG", message.ToString());
        }
#pragma warning restore CS0618

        private static string SeverityToPrefix(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Debug: return "DBG";
                case LogSeverity.Info: return "INF";
                case LogSeverity.Warn: return "WRN";
                case LogSeverity.Error: return "ERR";
                case LogSeverity.Fatal: return "FTL";
                default: return "???";
            }
        }

        private void WriteLine(string level, string message, params object[] paramList)
        {
            lock (_lock)
            {
                try
                {
                    var formatted = paramList != null && paramList.Length > 0
                        ? string.Format(message, paramList)
                        : message;
                    var line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                        DateTime.Now, level, formatted, Environment.NewLine);
                    File.AppendAllText(_logFilePath, line);
                }
                catch
                {
                }
            }
        }
    }
}
