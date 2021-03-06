﻿using System.Collections.Generic;
using HunterPie.Core;
using Plugin.Sync.Logging;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests.Manual
{
    public class BaseTests
    {
        protected readonly ITestOutputHelper TestOutput;

        public BaseTests(ITestOutputHelper testOutput)
        {
            this.TestOutput = testOutput;
            Logger.Targets = new List<ILoggerTarget>
            {
                new TestOutputLogger(testOutput)
            };
            Logger.Prefix = "";
            Logger.LogLevel = LogLevel.Trace;
            ConfigService.Current = new Config
            {
                ServerUrl = "http://localhost:5001"
            };
            UserSettings.PlayerConfig = new UserSettings.Config.Rootobject {Overlay = {GameScanDelay = 150}};
        }
        
    }
}