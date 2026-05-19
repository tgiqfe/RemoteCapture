using System;
using System.IO;
using System.Threading;

namespace RemoteCapture.Lib
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static bool _isEnabled = true;

        static Logger()
        {
            // Set log file path to the executable directory
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = Path.Combine(exeDir, $"RemoteCapture_{timestamp}.log");

            // Write initial log entry
            WriteLog("Logger initialized");
        }

        public static void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
        }

        public static void WriteLog(string message, string category = "INFO")
        {
            if (!_isEnabled)
                return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var logEntry = $"[{timestamp}] [{category}] [Thread-{threadId}] {message}";

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }

                // Also write to Debug output
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public static void Debug(string message)
        {
            WriteLog(message, "DEBUG");
        }

        public static void Info(string message)
        {
            WriteLog(message, "INFO");
        }

        public static void Warning(string message)
        {
            WriteLog(message, "WARN");
        }

        public static void Error(string message)
        {
            WriteLog(message, "ERROR");
        }

        public static void Error(string message, Exception ex)
        {
            WriteLog($"{message} - Exception: {ex.Message}\nStackTrace: {ex.StackTrace}", "ERROR");
        }

        public static string GetLogFilePath()
        {
            return _logFilePath;
        }
    }
}
