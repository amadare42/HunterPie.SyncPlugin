using System;

namespace Plugin.Sync.Logging
{
    public class ConsoleTarget : ILoggerTarget
    {
        public void Log(string message, LogLevel level)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Warn => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };
            Console.WriteLine($"{DateTime.Now:HH:mm:ss:ffff} [{level:G}] {message}");
            Console.ForegroundColor = prevColor;
        }
    }
}