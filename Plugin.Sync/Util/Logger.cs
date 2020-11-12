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
                case LogLevel.Trace:
                    Debugger.Debug(message);
                    break;

                case LogLevel.Debug:
                    Debugger.Debug(message);
                    break;

                case LogLevel.Warn:
                    Debugger.Warn(message);
                    break;

                case LogLevel.Error:
                    Debugger.Error(message);
                    break;

                case LogLevel.Info:
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

        public static bool PluginDebug = true;

        public static ILoggerTarget Target = new HunterPieDebugger();

        public static void Log(string message) => Target.Log($"[{PluginName}] {message}", LogLevel.Info);

        public static void Error(string message) => Target.Log($"[ERROR] [{PluginName}] {message}", LogLevel.Error);

        public static void Debug(string message) => Target.Log($"[Debug] [{PluginName}] {message}", PluginDebug ? LogLevel.Info : LogLevel.Debug);

        public static void Trace(string message) => Target.Log($"[Trace] [{PluginName}] {message}", PluginDebug ? LogLevel.Info : LogLevel.Trace);
    }
}
