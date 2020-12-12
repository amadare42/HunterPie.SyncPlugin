namespace Plugin.Sync.Logging
{
    public interface IClassLogger : ILoggerTarget
    {
        bool IsEnabled(LogLevel level);
        void Log(string message);
    }
}