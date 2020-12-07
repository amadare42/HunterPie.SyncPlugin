using System;
using Plugin.Sync.Util;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests
{
    public class TestOutputLogger : ILoggerTarget
    {
        private readonly ITestOutputHelper output;

        public TestOutputLogger(ITestOutputHelper output)
        {
            this.output = output;
        }

        public void Log(string message, LogLevel level)
        {
            var value = $"{DateTime.Now:HH:mm:ss:ffff} [{level:G}] {message}";
            Console.WriteLine(value);
            this.output.WriteLine(value);
        }
    }
}