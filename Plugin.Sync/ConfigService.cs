using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Plugin.Sync.Logging;

namespace Plugin.Sync
{
    public class Config
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Info;
        public string ServerUrl { get; set; } = "https://amadare-mhw-sync.herokuapp.com/";

        public ServerLoggingConfig ServerLogging { get; set; } = null;
    }

    public class ServerLoggingConfig
    {
        public bool Enable { get; set; }
        public string Name { get; set; }
        public string Room { get; set; }
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
                    // hack to don't show ServerLogging unless specified
                    NullValueHandling = NullValueHandling.Ignore,
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

            if (config.ServerLogging != null && config.ServerLogging.Enable)
            {
                var name = string.IsNullOrEmpty(config.ServerLogging.Name)
                    ? "user-" + Guid.NewGuid().ToString().Substring(0, 4)
                    : config.ServerLogging.Name;
                name = name.Substring(0, Math.Min(name.Length, 10));
                TraceName = name;

                var room = string.IsNullOrEmpty(config.ServerLogging.Room) ? "" : config.ServerLogging.Room;
                room = room.Substring(0, Math.Min(room.Length, 10));

                var existingLogger = (ServerLoggerTarget) Logger.Targets.FirstOrDefault(l => l.GetType() == typeof(ServerLoggerTarget));
                if (existingLogger != null)
                {
                    Logger.Log("Server logger changed");
                    var _ = existingLogger.Close();
                }
                Logger.Targets.Add(new ServerLoggerTarget(name, room));
                Logger.Info($"Using server logging as '{name}' (room: '{room}')");
            }
            
            Logger.Log($"Using server {Current.ServerUrl}; logs level is {Current.LogLevel:G}; [Version: {typeof(ConfigService).Assembly.GetName().Version}]");
        }
    }
}
