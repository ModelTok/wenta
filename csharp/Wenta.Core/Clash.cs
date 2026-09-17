using System;
using System.Collections.Generic;
using System.Text;

namespace Wenta
{
    /// <summary>A point in the plane. Port of the `(f64, f64)` pair alias `P`
    /// used throughout `venti/src/clash.rs`.</summary>
    public struct Point2
    {
        public readonly double X;
        public readonly double Y;

        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>A straight duct centreline segment, as consumed by clash
    /// detection. Mirrors the subset of `venti::topology::Segment` that
    /// `clash.rs` actually uses (component id, 2D start/end, diameter).</summary>
    public class DuctSegment
    {
        public readonly string ComponentId;
        public readonly Point2 Start;
        public readonly Point2 End;
        public readonly double Diameter;

        public DuctSegment(string componentId, Point2 start, Point2 end, double diameter)
        {
            ComponentId = componentId;
            Start = start;
            End = end;
            Diameter = diameter;
        }
    }

    /// <summary>A detected clash between two duct segments.
    ///
    /// <see cref="A"/> and <see cref="B"/> are the component ids of the two
    /// segments, ordered lexicographically (ordinal) so every pair is
    /// reported exactly once. <see cref="DistanceM"/> is the exact minimum
    /// distance between the two 2D centreline segments [m].</summary>
    public class Clash
    {
        public readonly string A;
        public readonly string B;
        public readonly double DistanceM;

        public Clash(string a, string b, double distanceM)
        {
            A = a;
            B = b;
            DistanceM = distanceM;
        }
    }

    /// <summary>Clash detection — find overlapping / near-intersecting duct
    /// segments. Port of `venti/src/clash.rs`.
    ///
    /// Given a set of <see cref="DuctSegment"/>s, <see cref="FindClashes"/>
    /// reports every distinct pair whose minimum 2D centreline distance is
    /// smaller than the sum of the two radii plus a user-supplied clearance
    /// margin.</summary>
    public static class ClashDetection
    {
        // Matches Rust's `f64::EPSILON` (machine epsilon for f64), NOT
        // System.Double.Epsilon (the smallest representable positive double).
        private const double Epsilon = 2.2204460492503131e-16;

        /// <summary>Distance between two points.</summary>
        private static double PointPoint(Point2 a, Point2 b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance from point `p` to segment `a`..`b`.</summary>
        private static double PointSegment(Point2 p, Point2 a, Point2 b)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double len2 = abx * abx + aby * aby;
            // Degenerate segment: distance to a single point.
            if (len2 == 0.0)
                return PointPoint(p, a);

            double apx = p.X - a.X;
            double apy = p.Y - a.Y;
            double t = Clamp((apx * abx + apy * aby) / len2, 0.0, 1.0);
            Point2 closest = new Point2(a.X + t * abx, a.Y + t * aby);
            return PointPoint(p, closest);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>Exact minimum distance between two 2D line segments
        /// `p0`..`p1` and `q0`..`q1`.
        ///
        /// Handles the general (skew-like) case, parallel / collinear and
        /// degenerate (zero-length) segments via the closest-feet / endpoint
        /// fall-backs.</summary>
        internal static double SegmentDistance(Point2 p0, Point2 p1, Point2 q0, Point2 q1)
        {
            double ux = p1.X - p0.X, uy = p1.Y - p0.Y;
            double vx = q1.X - q0.X, vy = q1.Y - q0.Y;
            double wx = p0.X - q0.X, wy = p0.Y - q0.Y;

            double a = ux * ux + uy * uy;
            double b = ux * vx + uy * vy;
            double c = vx * vx + vy * vy;
            double d = ux * wx + uy * wy;
            double e = vx * wx + vy * wy;

            double denom = a * c - b * b;

            // Parallel (or any degenerate) segments — reduce to a sequence of
            // point-segment distances, which is exact and covers collinear
            // overlap.
            if (denom <= Epsilon)
            {
                double best = double.PositiveInfinity;
                best = Math.Min(best, PointSegment(p0, q0, q1));
                best = Math.Min(best, PointSegment(p1, q0, q1));
                best = Math.Min(best, PointSegment(q0, p0, p1));
                best = Math.Min(best, PointSegment(q1, p0, p1));
                return best;
            }

            // Non-parallel: solve for the closest points on the two (infinite)
            // lines, clamping to the segment ranges.
            double sC = (b * e - c * d) / denom;
            double tC = (a * e - b * d) / denom;
            double s = Clamp(sC, 0.0, 1.0);
            double t = Clamp(tC, 0.0, 1.0);

            Point2 p = new Point2(p0.X + s * ux, p0.Y + s * uy);
            Point2 q = new Point2(q0.X + t * vx, q0.Y + t * vy);

            // If the un-clamped optimum lay inside both ranges this is exact;
            // otherwise fall back to the boundary closest-feet which is still
            // exact for segment-to-segment distance.
            if (sC >= 0.0 && sC <= 1.0 && tC >= 0.0 && tC <= 1.0)
                return PointPoint(p, q);

            double fallback = double.PositiveInfinity;
            fallback = Math.Min(fallback, PointSegment(p0, q0, q1));
            fallback = Math.Min(fallback, PointSegment(p1, q0, q1));
            fallback = Math.Min(fallback, PointSegment(q0, p0, p1));
            fallback = Math.Min(fallback, PointSegment(q1, p0, p1));
            return fallback;
        }

        /// <summary>Find all duct-segment clashes.
        ///
        /// For every distinct, unordered pair of <see cref="DuctSegment"/>s the
        /// exact minimum distance between the two 2D centreline segments is
        /// computed; a pair is reported as a <see cref="Clash"/> when
        /// `distance &lt; (d_a + d_b) / 2 + clearanceM`, where `d_a`/`d_b` are
        /// the segment diameters and `clearanceM` a required gap between duct
        /// surfaces. The pair is ordered (`a` &lt;= `b`, ordinal) so each
        /// clash is reported once.</summary>
        /// <exception cref="WentaException">`clearanceM` &lt; 0.</exception>
        public static List<Clash> FindClashes(IList<DuctSegment> segments, double clearanceM)
        {
            if (clearanceM < 0.0)
                throw new WentaException("clearance_m must be >= 0");

            List<Clash> clashes = new List<Clash>();
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    DuctSegment a = segments[i];
                    DuctSegment b = segments[j];
                    double dist = SegmentDistance(a.Start, a.End, b.Start, b.End);
                    double threshold = (a.Diameter + b.Diameter) / 2.0 + clearanceM;
                    if (dist < threshold)
                    {
                        string nameA, nameB;
                        if (string.CompareOrdinal(a.ComponentId, b.ComponentId) <= 0)
                        {
                            nameA = a.ComponentId;
                            nameB = b.ComponentId;
                        }
                        else
                        {
                            nameA = b.ComponentId;
                            nameB = a.ComponentId;
                        }
                        clashes.Add(new Clash(nameA, nameB, dist));
                    }
                }
            }
            return clashes;
        }

        /// <summary>Number of clashes in a clash list.</summary>
        public static int ClashCount(IList<Clash> clashes)
        {
            return clashes.Count;
        }

        /// <summary>Render clashes as CSV with an `a,b,distance_m` header.
        /// Distances are trimmed to at most 6 decimals (e.g. `0.0` renders as
        /// `0`), matching Rust's default `f64` `Display` formatting for the
        /// values this module produces.</summary>
        public static string ClashesAsCsv(IList<Clash> clashes)
        {
            StringBuilder sb = new StringBuilder("a,b,distance_m\n");
            for (int i = 0; i < clashes.Count; i++)
            {
                Clash c = clashes[i];
                sb.Append(c.A).Append(',').Append(c.B).Append(',')
                  .Append(FmtNum(c.DistanceM)).Append('\n');
            }
            return sb.ToString();
        }

        private static string FmtNum(double v)
        {
            return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
