using System;

namespace Wenta
{
    /// <summary>A selectable sizing standard (dimension set) for duct sizing.</summary>
    public enum Standard
    {
        /// <summary>EN 1505:2001 (rectangular) + EN 1506:2007 (round).
        /// Uses the existing <see cref="StandardSizes"/> tables verbatim, so the
        /// canonical EN behaviour is preserved.</summary>
        En1505_1506,

        /// <summary>ASHRAE / SMACNA nominal sizes (inch-based series, converted to mm).</summary>
        AsHrae,

        /// <summary>DIN 24155 series (Renard R10/R20 preferred numbers).</summary>
        Din
    }

    /// <summary>Configurable STANDARDS (size tables) for duct sizing. Port of
    /// `venti/src/standards.rs`.
    ///
    /// Different projects and regions size ductwork to different preferred
    /// standard dimension sets. This class exposes the three most common ones
    /// so the sizing code can choose between EN 1505/1506, ASHRAE/SMACNA, and
    /// DIN without changing the sizing machinery itself.
    ///
    /// All dimension tables are in millimetres.
    ///
    /// The EN tables are reused verbatim from <see cref="StandardSizes"/>
    /// (itself a port of `venti::data::standard_sizes`); the ASHRAE/DIN tables
    /// are declared inline here, matching the Rust `const` arrays.</summary>
    public static class Standards
    {
        /// <summary>ASHRAE round ducts — customary nominal diameters in inches
        /// converted to millimetres and rounded to whole mm: 4..40 in one-inch
        /// steps.</summary>
        public static readonly int[] AshraeRoundDuctSizes =
        {
            102, 127, 152, 178, 203, 229, 254, 305, 356, 406, 457, 508, 559, 610, 660, 711, 762, 813, 864,
            914, 965, 1016,
        };

        /// <summary>ASHRAE / SMACNA rectangular ducts — customary nominal
        /// width×height in inches converted to mm and rounded (`w × h`,
        /// `w &gt;= h`).</summary>
        public static readonly int[][] AshraeRectangularDuctSizes =
        {
            new[] {152, 102}, new[] {203, 102}, new[] {203, 152}, new[] {254, 102}, new[] {254, 152},
            new[] {305, 102}, new[] {305, 152}, new[] {305, 203}, new[] {356, 152}, new[] {356, 203},
            new[] {356, 254}, new[] {406, 152}, new[] {406, 203}, new[] {406, 254}, new[] {406, 305},
            new[] {457, 203}, new[] {457, 254}, new[] {457, 305}, new[] {508, 203}, new[] {508, 254},
            new[] {508, 305}, new[] {508, 356}, new[] {559, 254}, new[] {559, 305}, new[] {610, 203},
            new[] {610, 254}, new[] {610, 305}, new[] {610, 356}, new[] {610, 406}, new[] {660, 305},
            new[] {711, 305}, new[] {762, 305}, new[] {762, 356}, new[] {914, 356}, new[] {914, 406},
            new[] {1067, 406}, new[] {1219, 406}, new[] {1219, 457}, new[] {1372, 457}, new[] {1524, 508},
            new[] {1676, 559}, new[] {1829, 610},
        };

        /// <summary>DIN 24155 round ducts — nominal diameter in mm from the
        /// Renard R10 series.</summary>
        public static readonly int[] DinRoundDuctSizes =
        {
            63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250,
        };

        /// <summary>DIN 24155 rectangular ducts — `w × h` in mm where both
        /// sides come from the Renard R10/R20 preferred-number series and
        /// `w &gt;= h`.</summary>
        public static readonly int[][] DinRectangularDuctSizes =
        {
            new[] {100, 100},
            new[] {125, 100}, new[] {125, 125},
            new[] {160, 100}, new[] {160, 125}, new[] {160, 160},
            new[] {200, 100}, new[] {200, 125}, new[] {200, 160}, new[] {200, 200},
            new[] {250, 100}, new[] {250, 125}, new[] {250, 160}, new[] {250, 200}, new[] {250, 250},
            new[] {315, 100}, new[] {315, 125}, new[] {315, 160}, new[] {315, 200}, new[] {315, 250}, new[] {315, 315},
            new[] {400, 100}, new[] {400, 125}, new[] {400, 160}, new[] {400, 200}, new[] {400, 250}, new[] {400, 315}, new[] {400, 400},
            new[] {500, 100}, new[] {500, 125}, new[] {500, 160}, new[] {500, 200}, new[] {500, 250}, new[] {500, 315}, new[] {500, 400}, new[] {500, 500},
            new[] {630, 100}, new[] {630, 125}, new[] {630, 160}, new[] {630, 200}, new[] {630, 250}, new[] {630, 315}, new[] {630, 400}, new[] {630, 500}, new[] {630, 630},
            new[] {800, 100}, new[] {800, 125}, new[] {800, 160}, new[] {800, 200}, new[] {800, 250}, new[] {800, 315}, new[] {800, 400}, new[] {800, 500}, new[] {800, 630}, new[] {800, 800},
            new[] {1000, 100}, new[] {1000, 125}, new[] {1000, 160}, new[] {1000, 200}, new[] {1000, 250}, new[] {1000, 315}, new[] {1000, 400}, new[] {1000, 500}, new[] {1000, 630}, new[] {1000, 800}, new[] {1000, 1000},
            new[] {1250, 100}, new[] {1250, 125}, new[] {1250, 160}, new[] {1250, 200}, new[] {1250, 250}, new[] {1250, 315}, new[] {1250, 400}, new[] {1250, 500}, new[] {1250, 630}, new[] {1250, 800}, new[] {1250, 1000}, new[] {1250, 1250},
            new[] {1600, 100}, new[] {1600, 125}, new[] {1600, 160}, new[] {1600, 200}, new[] {1600, 250}, new[] {1600, 315}, new[] {1600, 400}, new[] {1600, 500}, new[] {1600, 630}, new[] {1600, 800}, new[] {1600, 1000}, new[] {1600, 1250}, new[] {1600, 1600},
            new[] {2000, 100}, new[] {2000, 125}, new[] {2000, 160}, new[] {2000, 200}, new[] {2000, 250}, new[] {2000, 315}, new[] {2000, 400}, new[] {2000, 500}, new[] {2000, 630}, new[] {2000, 800}, new[] {2000, 1000}, new[] {2000, 1250}, new[] {2000, 1600}, new[] {2000, 2000},
        };

        /// <summary>The standard round duct sizes for a given <see cref="Standard"/>, in mm.</summary>
        public static int[] StandardRoundSizes(Standard standard)
        {
            switch (standard)
            {
                case Standard.En1505_1506: return StandardSizes.RoundDuctSizes;
                case Standard.AsHrae: return AshraeRoundDuctSizes;
                case Standard.Din: return DinRoundDuctSizes;
                default: throw new WentaException("unknown standard");
            }
        }

        /// <summary>The standard rectangular duct sizes for a given
        /// <see cref="Standard"/>, as `[width, height]` pairs in mm.</summary>
        public static int[][] StandardRectangularSizes(Standard standard)
        {
            switch (standard)
            {
                case Standard.En1505_1506: return StandardSizes.RectangularDuctSizes;
                case Standard.AsHrae: return AshraeRectangularDuctSizes;
                case Standard.Din: return DinRectangularDuctSizes;
                default: throw new WentaException("unknown standard");
            }
        }

        /// <summary>Return the nearest standard round diameter [mm] for the
        /// given <see cref="Standard"/>.
        ///
        /// With `roundUp = true` (default), picks the smallest standard size
        /// that is &gt;= `diameterMm`; otherwise picks the closest standard
        /// size in either direction (ties favour the lower size). Values below
        /// the smallest / above the largest standard size are clamped to the
        /// respective end of the table.</summary>
        public static int NearestRoundSizeFor(Standard standard, double diameterMm, bool roundUp = true)
        {
            return StandardSizes.NearestInTable(StandardRoundSizes(standard), diameterMm, roundUp,
                tieFavorsLower: true);
        }
    }
}
