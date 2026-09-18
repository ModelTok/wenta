using System;

namespace Wenta
{
    /// <summary>Cross-section geometry primitives. Port of `wenta.core.geometry`.
    /// Area and hydraulic diameter are computed once at construction.</summary>
    public abstract class CrossSection
    {
        public readonly double Area;             // [m^2]
        public readonly double HydraulicDiameter; // D_h [m]
        public abstract string Describe();
        /// <summary>Wetted perimeter [m]: round π·D, rectangular 2·(W+H).</summary>
        public abstract double Perimeter { get; }

        protected CrossSection(double area, double hydraulicDiameter)
        {
            Area = area;
            HydraulicDiameter = hydraulicDiameter;
        }
    }

    /// <summary>Circular cross-section.</summary>
    public sealed class Round : CrossSection
    {
        public readonly double Diameter; // [m]

        public Round(double diameter)
            : base(Math.PI * (diameter / 2.0) * (diameter / 2.0), diameter)
        {
            if (diameter <= 0.0)
                throw new WentaException("diameter must be positive, got " + diameter);
            Diameter = diameter;
        }

        public override string Describe() { return "round D=" + (Diameter * 1000) + "mm"; }
        public override double Perimeter { get { return Math.PI * Diameter; } }
    }

    /// <summary>Rectangular cross-section.</summary>
    public sealed class Rectangular : CrossSection
    {
        public readonly double Width;  // [m]
        public readonly double Height; // [m]

        public Rectangular(double width, double height)
            : base(width * height, 2.0 * width * height / (width + height))
        {
            if (width <= 0.0 || height <= 0.0)
                throw new WentaException(
                    "width and height must be positive, got width=" + width
                    + ", height=" + height);
            Width = width;
            Height = height;
        }

        public override string Describe()
        {
            return "rect " + (Width * 1000) + "x" + (Height * 1000) + "mm";
        }
        public override double Perimeter { get { return 2.0 * (Width + Height); } }
    }

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

        /// <summary>Squared Euclidean distance to <paramref name="other"/>.</summary>
        public double DistanceSquaredTo(Point2 other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>Euclidean distance to <paramref name="other"/>.</summary>
        public double DistanceTo(Point2 other)
        {
            return Math.Sqrt(DistanceSquaredTo(other));
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

    public static class Geometry
    {
        /// <summary>ASHRAE equivalent round diameter for a rectangular duct:
        /// D_eq = 1.30 · (a·b)^0.625 / (a + b)^0.25 [m].</summary>
        public static double EquivalentRoundDiameter(double width, double height)
        {
            if (width <= 0.0 || height <= 0.0)
                throw new WentaException("width and height must be positive");
            return 1.30 * Math.Pow(width * height, 0.625) / Math.Pow(width + height, 0.25);
        }
    }
}
