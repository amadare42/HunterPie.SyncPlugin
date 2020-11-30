using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using HunterPie.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plugin.Sync.Util
{
    public interface ILoggerTarget
    {
        void Log(string message, LogLevel level);
    }

    public class ServerLoggerTarget : ILoggerTarget
    {
        private readonly string user;
        private HttpClient client = new HttpClient();

        public ServerLoggerTarget(string user)
        {
            this.user = user;
        }

        public async void Log(string message, LogLevel level)
        {
            var jObj = new JObject
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["level"] = level.ToString("G"),
                ["msg"] = message,
                ["text"] = $"{DateTime.Now:HH:mm:ss:fff} [{level:G}] {message}",
                ["user"] = this.user
            };
            var content = new StringContent(jObj.ToString(), Encoding.UTF8, "application/json");
            try
            {
                await this.client.PostAsync(ConfigService.Current.ServerUrl + "/logs/add", content);
            }
            catch (Exception)
            {
                // ignored
            }
        }
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
