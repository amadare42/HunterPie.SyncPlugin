﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using HunterPie.Logger;
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

        public static void Apply(Config config)
        {
            Logger.LogLevel = config.LogLevel;
            // for debug
            var dumpLogsPath = Path.Combine(Path.GetDirectoryName(typeof(ConfigService).Assembly.Location), "dumpLogs");
            Logger.Info("!");
            if (File.Exists(dumpLogsPath))
            {
                var name = File.ReadAllText(dumpLogsPath);
                Logger.Targets.Add(new ServerLoggerTarget(name));
                Logger.Info($"Using server logging as '{name}'");
            }
            Logger.Log($"Using server {Current.ServerUrl}; logs level is {Current.LogLevel:G}; [Version: {typeof(ConfigService).Assembly.GetName().Version}]");
        }
    }
}
