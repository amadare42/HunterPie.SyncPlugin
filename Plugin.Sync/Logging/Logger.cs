using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Plugin.Sync.Logging
{
    public static class Logger
    {
        public static string Prefix = "[Sync Plugin]";

        public static LogLevel LogLevel = LogLevel.Info;

        public static List<ILoggerTarget> Targets = new List<ILoggerTarget>
        {
            new HunterPieDebugger()
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEnabled(LogLevel level) => LogLevel <= level;

        public static void Log(string message, LogLevel level = LogLevel.Info) => Write($"{Prefix} {message}", level);
        public static void Info(string message) => Write($"{Prefix} {message}", LogLevel.Info);
        public static void Error(string message) => Write($"{Prefix} {message}", LogLevel.Error);
        public static void Warn(string message) => Write($"{Prefix} {message}", LogLevel.Warn);
        public static void Debug(string message) => Write($"{Prefix} {message}", LogLevel.Debug);
        public static void Trace(string message) => Write($"{Prefix} {message}", LogLevel.Trace);


        private static void Write(string message, LogLevel level)
        {
            if (level < LogLevel) return;
            foreach (var target in Targets)
            {
                target.Log(message, level);
            }
        }
    }
}
