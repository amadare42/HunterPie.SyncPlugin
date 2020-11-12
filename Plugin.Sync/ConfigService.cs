using System;
using System.IO;
using Newtonsoft.Json;
using Plugin.Sync.Server;
using Plugin.Sync.Util;

namespace Plugin.Sync
{
    public class Config
    {
        public bool ShowDebugLogs { get; set; } = true;
        public string ServerUrl { get; set; } = "http://localhost:5001";
    }

    public static class ConfigService
    {
        public static Config Current;

        public static void LoadAndApply()
        {
            Load();
            Apply(Current);
        }

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
                    File.WriteAllText(settingsPath, JsonConvert.SerializeObject(Current));
                }
            }
            catch (Exception ex)
            {
                Current = new Config();
                Logger.Error($"Error on loading config. Using default one. {ex}");
            }
        }

        public static void Apply(Config config)
        {
            SyncServerClient.BaseUrl = config.ServerUrl;
            Logger.PluginDebug = config.ShowDebugLogs;
            Logger.Log($"Using server {Current.ServerUrl}; debug logs are {(Current.ShowDebugLogs ? "ON" : "OFF")}");
        }
    }
}
