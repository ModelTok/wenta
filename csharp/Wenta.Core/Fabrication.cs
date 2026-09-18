using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Duct fabrication breakout — lengths, surface area, weight, cutting
    /// schedule. Port of `venti/src/fabrication.rs` (the "CADvent feature", issue #30).
    ///
    /// Computes the fabrication quantities a duct shop needs to cut and order
    /// sheet metal for a run of ductwork:
    /// <list type="bullet">
    /// <item><see cref="DuctSurfaceAreaM2"/> — the wetted surface area of a duct run
    /// (perimeter x length), the basis for estimating sheet-metal area.</item>
    /// <item><see cref="DuctWeightKg"/> — the weight of that sheet-metal area given a
    /// steel gauge (thickness) and density (steel ~ 7850 kg/m^3).</item>
    /// <item><see cref="FabricationBreakout"/> — a straight-vs-fittings length
    /// summary for a fabricated run.</item>
    /// <item><see cref="CuttingSchedule"/> — accumulate per-component lengths across
    /// many runs and return a sorted, de-duplicated list (component, total length).</item>
    /// </list>
    ///
    /// All quantities are SI: length m, area m^2, weight kg, density kg/m^3.</summary>
    public static class Fabrication
    {
        /// <summary>Default density of duct steel [kg/m^3] (mild / galvanized sheet steel).</summary>
        public const double SteelDensityKgM3 = 7850.0;

        /// <summary>Wetted surface area of a duct run with the given cross-section and
        /// length [m^2] — the metal area to be fabricated. This is the perimeter of the
        /// cross-section multiplied by the run length:
        /// round: A = pi * D * L; rectangular: A = 2 * (W + H) * L.</summary>
        public static double DuctSurfaceAreaM2(CrossSection crossSection, double lengthM)
        {
            if (lengthM < 0.0)
                throw new WentaException("length must be non-negative");
            return crossSection.Perimeter * lengthM;
        }

        /// <summary>Weight [kg] of fabricated sheet metal for a given surface area,
        /// steel gauge (thickness) and density: weight = surfaceAreaM2 * gaugeM * density.
        /// `gaugeMm` is required (a thickness of zero contributes no weight); when
        /// `densityKgM3` is null it defaults to <see cref="SteelDensityKgM3"/> (7850 kg/m^3).</summary>
        public static double DuctWeightKg(double surfaceAreaM2, double? gaugeMm, double? densityKgM3)
        {
            if (surfaceAreaM2 < 0.0)
                throw new WentaException("surface area must be non-negative");

            if (gaugeMm == null)
                throw new WentaException("gauge (steel thickness) is required to compute weight");
            double gauge = gaugeMm.Value;
            if (gauge <= 0.0)
                throw new WentaException("gauge must be positive");
            double gaugeM = gauge / 1000.0;

            double density = densityKgM3 ?? SteelDensityKgM3;
            if (density <= 0.0)
                throw new WentaException("density must be positive");

            return surfaceAreaM2 * gaugeM * density;
        }

        /// <summary>A fabrication length breakout for a run: straight duct vs. fittings.</summary>
        public sealed class FabricationBreakout
        {
            /// <summary>Total straight (non-fitting) duct length [m].</summary>
            public readonly double StraightM;

            /// <summary>Total equivalent fitting length [m].</summary>
            public readonly double FittingsM;

            /// <summary>Build a breakout from straight and fitting lengths, validating
            /// that both are non-negative.</summary>
            public FabricationBreakout(double straight, double fittings)
            {
                if (straight < 0.0 || fittings < 0.0)
                    throw new WentaException("straight and fittings lengths must be non-negative");
                StraightM = straight;
                FittingsM = fittings;
            }

            /// <summary>Total fabricated duct length [m] = straight + fittings.</summary>
            public double TotalM()
            {
                return StraightM + FittingsM;
            }
        }

        /// <summary>Accumulate per-component duct lengths across many runs.
        /// Takes a list of (component, lengthM) pairs and returns a sorted (by
        /// component name, ordinal), de-duplicated list of (component, totalLengthM)
        /// pairs, summing the lengths of each component wherever it repeats.</summary>
        public static List<KeyValuePair<string, double>> CuttingSchedule(
            IEnumerable<KeyValuePair<string, double>> ducts)
        {
            var totals = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> duct in ducts)
            {
                double existing;
                totals.TryGetValue(duct.Key, out existing); // 0.0 when absent
                totals[duct.Key] = existing + duct.Value;
            }

            // Ordinal key order (the BTreeMap order of the Rust original).
            var keys = new List<string>(totals.Keys);
            keys.Sort(string.CompareOrdinal);

            var result = new List<KeyValuePair<string, double>>(keys.Count);
            foreach (string k in keys)
                result.Add(new KeyValuePair<string, double>(k, totals[k]));
            return result;
        }
    }
}
