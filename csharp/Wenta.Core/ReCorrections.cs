using System;

namespace Wenta
{
    /// <summary>Reynolds &amp; size corrections for fitting loss coefficients (zeta).
    /// Port of `venti/src/re.rs`.
    ///
    /// Published fitting data (ASHRAE Duct Fitting Database, Miller, Idelchik) is
    /// Reynolds- and size-dependent — a constant zeta is only valid near one
    /// (Re, duct-size) test point, and round elbows in particular lose slightly
    /// less as Re grows. These helpers apply documented, conservative
    /// multiplicative corrections to a base zeta so catalogued constants degrade
    /// gracefully out of their test range.
    ///
    /// Source note: the exact coefficients of the licensed ASHRAE DB are not
    /// reproduced here; the exponents are mild, physically-motivated values in the
    /// direction the DB/SMACNA data trends (smaller zeta at higher Re, slightly
    /// higher zeta for very small ducts), each clamped so the correction stays
    /// within a few tens of percent.</summary>
    public static class ReCorrections
    {
        /// <summary>Reynolds number of the reference test point used by catalogue constants.</summary>
        public const double ReRef = 50000.0;

        /// <summary>Nominal duct size [m] of the reference test point (200 mm).</summary>
        public const double DRefM = 0.200;

        /// <summary>Clamp <paramref name="v"/> to [lo, hi] (NaN passes through).</summary>
        internal static double Clamp(double v, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, v));
        }

        private static void RequirePositive(double value, string name)
        {
            if (value <= 0.0)
                throw new WentaException(name + " must be positive");
        }

        /// <summary>Multiplicative Reynolds correction:
        /// zeta(Re)/zeta_ref = (Re_ref / Re)^k, clamped to [0.75, 1.5].
        ///
        /// k &gt; 0 makes higher-Re flow slightly less lossy (the round-elbow trend);
        /// k = 0 gives no correction. Throws if <paramref name="reynoldsNumber"/> is
        /// non-positive.</summary>
        public static double ReCorrection(double reynoldsNumber, double exponent)
        {
            RequirePositive(reynoldsNumber, "reynolds_number");
            return Clamp(Math.Pow(ReRef / reynoldsNumber, exponent), 0.75, 1.5);
        }

        /// <summary>Multiplicative duct-size correction:
        /// zeta(D)/zeta_ref = (D_ref / D)^s, clamped to [0.9, 1.3].
        ///
        /// s &gt; 0 makes small ducts slightly more lossy (the size-effect trend in
        /// the fitting data). Throws if <paramref name="ductDiameterM"/> is
        /// non-positive.</summary>
        public static double SizeCorrection(double ductDiameterM, double exponent)
        {
            RequirePositive(ductDiameterM, "duct_diameter_m");
            return Clamp(Math.Pow(DRefM / ductDiameterM, exponent), 0.9, 1.3);
        }

        /// <summary>Apply both the Reynolds and size corrections to a base zeta.
        ///
        /// Convenience for any fitting whose catalogue value was measured at
        /// (<see cref="ReRef"/>, <see cref="DRefM"/>). <paramref name="fluid"/>
        /// defaults to <see cref="Fluid.StandardAir"/>.</summary>
        public static double CorrectedZeta(double baseZeta, double velocity, double ductDiameterM,
            double reExponent, double sizeExponent, Fluid fluid = null)
        {
            fluid = fluid ?? Fluid.StandardAir();
            double re = Friction.Reynolds(velocity, ductDiameterM, fluid.KinematicViscosity);
            double factor = ReCorrection(re, reExponent) * SizeCorrection(ductDiameterM, sizeExponent);
            return baseZeta * factor;
        }
    }
}
