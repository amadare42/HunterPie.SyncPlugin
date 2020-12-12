namespace Plugin.Sync.Logging
{
    public static class LoggingExtensions
    {
        public static void Info(this ILoggerTarget log, string message) => log.Log(message, LogLevel.Info);
        public static void Error(this ILoggerTarget log, string message) => log.Log(message, LogLevel.Error);
        public static void Warn(this ILoggerTarget log, string message) => log.Log(message, LogLevel.Warn);
        public static void Debug(this ILoggerTarget log, string message) => log.Log(message, LogLevel.Debug);
        public static void Trace(this ILoggerTarget log, string message) => log.Log(message, LogLevel.Trace);
    }
}