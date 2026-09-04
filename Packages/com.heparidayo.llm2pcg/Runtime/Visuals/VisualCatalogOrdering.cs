using System;
using System.Collections.Generic;

namespace Llm2Pcg.Visuals
{
    public static class VisualCatalogOrdering
    {
        public static List<string> SortedPaths(IEnumerable<string> paths)
        {
            List<string> result = new List<string>();
            if (paths != null)
                foreach (string path in paths)
                    if (!string.IsNullOrWhiteSpace(path)) result.Add(path.Replace('\\', '/'));
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
