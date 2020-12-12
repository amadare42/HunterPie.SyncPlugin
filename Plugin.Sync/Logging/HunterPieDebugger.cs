using System;
using HunterPie.Logger;

namespace Plugin.Sync.Logging
{
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
                    // using Log instead of Debug to be able to show debug messages even when ShowDebugMessages is disabled
                    Debugger.Log("[TRACE] " + message);
                    break;
                
                case LogLevel.Debug:
                    // using Log instead of Debug to be able to show debug messages even when ShowDebugMessages is disabled
                    Debugger.Log("[DEBUG] " + message);
                    break;
                
                case LogLevel.Info:
                    Debugger.Log(message);
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }
}