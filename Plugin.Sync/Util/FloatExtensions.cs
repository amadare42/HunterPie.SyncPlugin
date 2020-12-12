using System;

namespace Plugin.Sync.Util
{
    public static class FloatExtensions
    {
        public static bool EqualsDelta(this float a, float b, float delta)
        {
            return Math.Abs(a - b) < delta;
        }
    }
}