using System;
using HunterPie.Logger;

namespace Plugin.Sync.Util
{
    public interface ILoggerTarget
    {
        void Log(string message, LogLevel level);
    }

    public enum LogLevel
    {
        Trace,
        Debug,
        Warn,
        Info,
        Error
    }

    public class HunterPieDebugger : ILoggerTarget
    {
        public void Log(string message, LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warn:
                    Debugger.Warn(message);
                    break;

                case LogLevel.Error:
                    Debugger.Error(message);
                    break;

                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    // using Log instead of Debug to be able to show debug messages even when ShowDebugMessages is disabled
                    Debugger.Log(message);
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }

    public class ConsoleTarget : ILoggerTarget
    {
        public void Log(string message, LogLevel level) => Console.WriteLine($"[{level:G}] {message}");
    }

    public static class Logger
    {
        public static string PluginName = "Sync Plugin";

        public static LogLevel LogLevel = LogLevel.Info;

        public static ILoggerTarget Target = new HunterPieDebugger();

        public static void Log(string message) => Write($"[{PluginName}] {message}", LogLevel.Info);
        public static void Error(string message) => Write($"[{PluginName}] {message}", LogLevel.Error);

        public static void Debug(string message) => Write($"[{PluginName}] {message}", LogLevel.Debug);
        public static void Trace(string message) => Write($"[Trace] [{PluginName}] {message}", LogLevel.Trace);


        private static void Write(string message, LogLevel level)
        {
            if (level < LogLevel) return;
            Target.Log(message, level);
        }
    }
}
