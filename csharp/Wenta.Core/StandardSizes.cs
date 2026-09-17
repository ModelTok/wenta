using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Standard EN duct sizes (mm). Port of `wenta.data.standard_sizes`.</summary>
    public static class StandardSizes
    {
        /// <summary>EN 1505:2001 rectangular ducts (width × height, mm).</summary>
        public static readonly int[][] RectangularDuctSizes =
        {
            new[] {100, 200}, new[] {150, 200}, new[] {200, 200}, new[] {100, 250}, new[] {150, 250},
            new[] {200, 250}, new[] {250, 250}, new[] {100, 300}, new[] {150, 300}, new[] {200, 300},
            new[] {250, 300}, new[] {300, 300}, new[] {100, 400}, new[] {150, 400}, new[] {200, 400},
            new[] {250, 400}, new[] {300, 400}, new[] {400, 400}, new[] {150, 500}, new[] {200, 500},
            new[] {250, 500}, new[] {300, 500}, new[] {400, 500}, new[] {500, 500}, new[] {150, 600},
            new[] {200, 600}, new[] {250, 600}, new[] {300, 600}, new[] {400, 600}, new[] {500, 600},
            new[] {600, 600}, new[] {200, 800}, new[] {250, 800}, new[] {300, 800}, new[] {400, 800},
            new[] {500, 800}, new[] {600, 800}, new[] {800, 800}, new[] {250, 1000}, new[] {300, 1000},
            new[] {400, 1000}, new[] {500, 1000}, new[] {600, 1000}, new[] {800, 1000}, new[] {1000, 1000},
            new[] {300, 1200}, new[] {400, 1200}, new[] {500, 1200}, new[] {600, 1200}, new[] {800, 1200},
            new[] {1000, 1200}, new[] {1200, 1200}, new[] {400, 1400}, new[] {500, 1400}, new[] {600, 1400},
            new[] {800, 1400}, new[] {1000, 1400}, new[] {1200, 1400}, new[] {400, 1600}, new[] {500, 1600},
            new[] {600, 1600}, new[] {800, 1600}, new[] {1000, 1600}, new[] {1200, 1600}, new[] {500, 1800},
            new[] {600, 1800}, new[] {800, 1800}, new[] {1000, 1800}, new[] {1200, 1800}, new[] {500, 2000},
            new[] {600, 2000}, new[] {800, 2000}, new[] {1000, 2000}, new[] {1200, 2000},
        };

        /// <summary>EN 1506:2007 round ducts (nominal diameter, mm).</summary>
        public static readonly int[] RoundDuctSizes =
        {
            63, 80, 100, 125, 150, 160, 200, 250, 300, 315, 355, 400,
            450, 500, 560, 630, 710, 800, 900, 1000, 1120, 1250,
        };

        private static Round[] _roundSections;
        private static Rectangular[] _rectSections;

        public static Round[] RoundSections()
        {
            if (_roundSections == null)
            {
                var list = new List<Round>(RoundDuctSizes.Length);
                foreach (int d in RoundDuctSizes)
                    list.Add(new Round(d / 1000.0));
                _roundSections = list.ToArray();
            }
            return _roundSections;
        }

        public static Rectangular[] RectangularSections()
        {
            if (_rectSections == null)
            {
                var list = new List<Rectangular>(RectangularDuctSizes.Length);
                foreach (int[] wh in RectangularDuctSizes)
                    list.Add(new Rectangular(wh[0] / 1000.0, wh[1] / 1000.0));
                _rectSections = list.ToArray();
            }
            return _rectSections;
        }

        /// <summary>Nearest EN 1506 nominal diameter [mm]. With roundUp=true
        /// (default) picks the smallest standard size ≥ diameter_mm; otherwise
        /// the closest standard size in either direction (ties favour the
        /// higher size — this method's long-standing, parity-tested
        /// behaviour).</summary>
        public static int NearestRoundSize(double diameterMm, bool roundUp = true)
        {
            return NearestInTable(RoundDuctSizes, diameterMm, roundUp, tieFavorsLower: false);
        }

        /// <summary>Shared "nearest value in a small sorted table" scan used by
        /// both <see cref="NearestRoundSize"/> and
        /// <see cref="Standards.NearestRoundSizeFor"/>. <paramref name="tieFavorsLower"/>
        /// lets each caller keep its own (already-tested) tie-breaking rule.</summary>
        internal static int NearestInTable(int[] sizes, double value, bool roundUp, bool tieFavorsLower)
        {
            int first = sizes[0];
            int last = sizes[sizes.Length - 1];
            if (value <= first) return first;
            if (value >= last) return last;

            int idx = Array.FindIndex(sizes, s => s >= value);
            int hi = sizes[idx];
            if (roundUp || hi == value) return hi;

            int lo = sizes[idx - 1];
            double dHi = hi - value;
            double dLo = value - lo;
            return tieFavorsLower ? (dHi < dLo ? hi : lo) : (dLo < dHi ? lo : hi);
        }
    }
}
