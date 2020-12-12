namespace Plugin.Sync.Logging
{
    public class ClassLogger : IClassLogger
    {
        private readonly string prefix;

        public ClassLogger(string prefix)
        {
            this.prefix = prefix;
        }

        public bool IsEnabled(LogLevel level)
        {
            return Logger.IsEnabled(level);
        }

        public void Log(string message)
        {
            Log(message, LogLevel.Info);
        }

        public void Log(string message, LogLevel level)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                message = $"[{this.prefix}] {message}";
            }

            Logger.Log(message, level);
        }
    }
}