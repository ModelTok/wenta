using System;

namespace Wenta
{
    /// <summary>Sheet-metal developments (rozwinięcia) — unfold duct and fitting
    /// geometries to flat patterns. Port of `venti/src/development.rs`.
    ///
    /// These routines estimate the <b>flat-pattern size</b> (developed width,
    /// length and surface area) needed to fabricate a duct, elbow or reducer out
    /// of flat sheet metal. All values are SI (metres, radians / degrees).
    ///
    /// <para><b>Heuristic note.</b> Everything in this module is a design
    /// approximation for estimating flat-pattern sizes (area / length), not
    /// an exact sheet-metal unfolding. Real fabrication must account for seam
    /// and edge allowances (pleating, standing seams, lock-forming, welding
    /// margins), material thickness, and the exact segmented construction of
    /// an elbow. Use the returned dimensions as starting estimates for
    /// quoting, weight, and logistics — not as cut-sheet-ready geometry.</para></summary>
    public static class Development
    {
        /// <summary>Validate that a dimensional argument is finite and positive.</summary>
        private static void RequirePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new WentaException(name + " must be finite, got " + value);
            if (value <= 0.0)
                throw new WentaException(name + " must be positive, got " + value);
        }

        /// <summary>Unfolds a straight round duct into a flat rectangle.
        ///
        /// The flat pattern is a rectangle whose <i>width</i> is the duct circumference
        /// (`π·d`) and whose <i>length</i> is the duct length, so the developed area is
        /// `π·d·l` — the lateral surface of the cylinder.</summary>
        public static FlatPiece RoundDuctDevelopment(double ductDiameterM, double lengthM)
        {
            RequirePositive(ductDiameterM, "duct_diameter_m");
            RequirePositive(lengthM, "length_m");
            double width = Math.PI * ductDiameterM;
            return new FlatPiece(width, lengthM);
        }

        /// <summary>Unfolds a segmented round elbow into a flat strip.
        ///
        /// The elbow centreline sweeps through <paramref name="angleDeg"/> around a bend
        /// radius <paramref name="radiusM"/>, so the developed <i>length</i> along the
        /// centreline is the arc length `angle_rad·radius`. The developed <i>width</i> is
        /// the duct circumference `π·d`. The area is `width · arc_length`, independent of
        /// the number of segments used (segment count only affects how the curved strip
        /// is subdivided, not its total size).</summary>
        public static FlatPiece RoundElbowDevelopment(double radiusM, double diameterM,
            double angleDeg, uint segments)
        {
            RequirePositive(radiusM, "radius_m");
            RequirePositive(diameterM, "diameter_m");
            RequirePositive(angleDeg, "angle_deg");
            if (segments == 0)
                throw new WentaException("segments must be at least 1");

            double angleRad = angleDeg * Math.PI / 180.0;
            double width = Math.PI * diameterM;
            double arcLength = angleRad * radiusM;
            return new FlatPiece(width, arcLength);
        }

        /// <summary>Unfolds a conical reducer (frustum) into a flat pattern using a
        /// trapezoid approximation.
        ///
        /// The flat pattern is approximated as a trapezoid (or, for a straight duct,
        /// a rectangle) whose <i>width</i> is the average circumference `π·(d1+d2)/2` and
        /// whose <i>length</i> is the slant height `√(length² + ((d2−d1)/2)²)`. This is a
        /// close estimate of the developed surface of a conical frustum for shallow
        /// tapers, but not an exact annular-sector unfolding.
        ///
        /// See the class-level note: this is an estimate for sizing, not a
        /// cut-sheet layout.</summary>
        public static FlatPiece ReducerConeDevelopment(double dSmallM, double dLargeM,
            double lengthM)
        {
            RequirePositive(dSmallM, "d_small_m");
            RequirePositive(dLargeM, "d_large_m");
            RequirePositive(lengthM, "length_m");
            double halfDelta = Math.Abs(dLargeM - dSmallM) / 2.0;
            double slant = Math.Sqrt(lengthM * lengthM + halfDelta * halfDelta);
            double width = Math.PI * (dSmallM + dLargeM) / 2.0;
            return new FlatPiece(width, slant);
        }
    }

    /// <summary>A rectangular flat-pattern cut for a duct or fitting development.
    ///
    /// <see cref="WidthM"/> and <see cref="LengthM"/> describe the bounding rectangle
    /// of the unfolded sheet; <see cref="AreaM2"/> is the developed surface area of the
    /// flat pattern.</summary>
    public struct FlatPiece
    {
        /// <summary>Developed width of the flat pattern (metres).</summary>
        public readonly double WidthM;
        /// <summary>Developed length of the flat pattern (metres).</summary>
        public readonly double LengthM;
        /// <summary>Developed surface area of the flat pattern (square metres).</summary>
        public readonly double AreaM2;

        /// <summary>Build a piece from its width and length, computing the area.</summary>
        public FlatPiece(double widthM, double lengthM)
        {
            WidthM = widthM;
            LengthM = lengthM;
            AreaM2 = widthM * lengthM;
        }
    }
}
