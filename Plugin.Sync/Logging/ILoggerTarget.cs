namespace Plugin.Sync.Logging
{
    public interface ILoggerTarget
    {
        void Log(string message, LogLevel level);
    }
}