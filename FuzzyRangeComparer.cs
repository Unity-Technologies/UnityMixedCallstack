using System.Collections.Generic;

namespace UnityMixedCallstack
{
    struct Range
    {
        public ulong Start;
        public ulong End;
        public string Name;
        public string File;

        public override string ToString()
        {
            return $"{File}::{Name} -- IP Range: {Start:X16} -> {End:X16}";
        }
    }
    class FuzzyRangeComparer : IComparer<Range>
    {
        public int Compare(Range x, Range y)
        {
            if (x.Name == null && y.Start <= x.Start && y.End >= x.Start)
            {
                return 0;
            }

            if (y.Name == null && x.Start <= y.Start && x.End >= y.Start)
            {
                return 0;
            }

            return x.Start.CompareTo(y.Start);
        }
    }
}
