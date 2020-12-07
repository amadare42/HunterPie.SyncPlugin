using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Plugin.Sync.Util;

namespace Plugin.Sync
{
    public class Config
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Info;
        public string ServerUrl { get; set; } = "https://amadare-mhw-sync.herokuapp.com/dev";
    }

    public static class ConfigService
    {
        public static Config Current;

        public static string TraceName;

        public static void Load()
        {
            var settingsPath = Path.Combine(Path.GetDirectoryName(typeof(ConfigService).Assembly.Location), "config.json");
            try
            {
                if (File.Exists(settingsPath))
                {
                    var text = File.ReadAllText(settingsPath);
                    Current = JsonConvert.DeserializeObject<Config>(text);
                }
                else
                {
                    Current = new Config();
                }
                File.WriteAllText(settingsPath, JsonConvert.SerializeObject(Current, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    Converters = new JsonConverter[] { new StringEnumConverter() }
                }));
            }
            catch (Exception ex)
            {
                Current = new Config();
                Logger.Error($"Error on loading config. Using default one. {ex}");
                // don't write default config to not override user changes in json and don't crash app if saving is failing
            }
            Apply(Current);
        }
        
        public static string GetWsUrl()
        {
            var serverUrl = Current.ServerUrl;
            if (!serverUrl.StartsWith("http"))
            {
                throw new Exception($"Cannot parse server url: '{serverUrl}'");
            }
            var sb = new StringBuilder("ws");
            
            // removing 'http' part: [http]s://example.com/
            sb.Append(serverUrl, 4, serverUrl.Length - 4);
            // adding '/' if missing
            if (!serverUrl.EndsWith("/")) sb.Append("/");
            sb.Append("connect");
            
            // result: https://example.com -> wss://example.com/connect
            return sb.ToString();
        }

        public static void Apply(Config config)
        {
            Logger.LogLevel = config.LogLevel;
            // for debug
            var dumpLogsPath = Path.Combine(Path.GetDirectoryName(typeof(ConfigService).Assembly.Location), "dumpLogs");
            if (File.Exists(dumpLogsPath))
            {
                var cfgString = File.ReadAllText(dumpLogsPath);
                var parts = cfgString.Split('|');
                var name = parts[0];
                var room = parts.Length > 1 ? parts[1] : "";
                
                Logger.Targets.Add(new ServerLoggerTarget(name, room));
                Logger.Info($"Using server logging as '{cfgString}' (room: '{room}')");
                TraceName = cfgString;
            }
            Logger.Log($"Using server {Current.ServerUrl}; logs level is {Current.LogLevel:G}; [Version: {typeof(ConfigService).Assembly.GetName().Version}]");
        }
    }
}
