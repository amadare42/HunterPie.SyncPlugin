using System.Collections.Generic;

namespace Plugin.Sync.Util
{
    public static class ListComparator
    {
        public static bool AreEqual<T>(this IList<T> a, IList<T> b)
        {
            if (a == null || b == null)
            {
                return ReferenceEquals(a, b);
            }
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }

            return true;
        }
    }
}
