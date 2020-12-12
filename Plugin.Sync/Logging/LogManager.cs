using System.IO;
using System.Runtime.CompilerServices;

namespace Plugin.Sync.Logging
{
    public static class LogManager
    {
        public static IClassLogger CreateLogger(string name) => new ClassLogger(name);

        public static IClassLogger GetCurrentClassLogger([CallerFilePath]string callerFilePath = null)
        {
            var callerTypeName = Path.GetFileNameWithoutExtension(callerFilePath);
            return new ClassLogger(callerTypeName);
        }
        
    }
}