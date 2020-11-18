using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.Sync
{
    public static class LinqExtensions
    {
        public static TVal MaxOrDefault<TObj, TVal>(this IEnumerable<TObj> collection, Func<TObj, TVal> predicate)
        {
            return collection.Select(predicate).DefaultIfEmpty().Max();
        }
    }
}