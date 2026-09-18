using System;

namespace Wenta
{
    /// <summary>A single point on a vendor fan curve.
    /// <see cref="FlowM3s"/> is the volumetric flow through the fan [m^3/s] and
    /// <see cref="StaticPressurePa"/> is the static pressure the fan develops
    /// at that flow [Pa].</summary>
    public struct FanPoint
    {
        public readonly double FlowM3s;
        public readonly double StaticPressurePa;

        public FanPoint(double flowM3s, double staticPressurePa)
        {
            FlowM3s = flowM3s;
            StaticPressurePa = staticPressurePa;
        }
    }

    /// <summary>Fan selection: vendor fan curves (wentylatory) and duty-point
    /// picking. Port of `venti/src/fan.rs`.
    ///
    /// A ductwork system is designed for a target duty point: a design flow
    /// (m^3/s) and the static pressure the fan must develop at that flow (Pa)
    /// to push air through the sized network. A fan is modelled by its
    /// static-pressure curve — the static pressure the fan delivers as a
    /// function of flow, tabulated as a polyline of points copied from a
    /// vendor catalogue. Selection is the classic "fan curve vs. system
    /// curve" procedure, inverted: instead of plotting the fan curve against
    /// the system curve, we ask which fan, at the design flow, still
    /// delivers at least the required static pressure (a non-negative
    /// pressure margin).
    ///
    /// All quantities are SI: flow in m^3/s, static pressure in Pa, shaft
    /// power in W.</summary>
    public sealed class FanCurve
    {
        public readonly string Name;
        public readonly FanPoint[] Points;

        /// <summary>Build a fan curve from its name and tabulated points.
        /// Throws when fewer than 2 points are given (a curve needs at
        /// least a segment), when flows are not strictly increasing (equal
        /// or decreasing flow makes the flow -&gt; pressure mapping
        /// ambiguous), or when any value is non-finite (NaN / infinity
        /// breaks interpolation).</summary>
        public FanCurve(string name, FanPoint[] points)
        {
            if (points == null || points.Length < 2)
                throw new WentaException("fan curve '" + name + "' needs at least 2 points, got "
                    + (points == null ? 0 : points.Length));
            for (int i = 0; i < points.Length - 1; i++)
            {
                FanPoint a = points[i];
                FanPoint b = points[i + 1];
                if (!IsFinite(a.FlowM3s) || !IsFinite(a.StaticPressurePa)
                    || !IsFinite(b.FlowM3s) || !IsFinite(b.StaticPressurePa))
                    throw new WentaException("fan curve '" + name + "' contains a non-finite point");
                if (a.FlowM3s >= b.FlowM3s)
                    throw new WentaException("fan curve '" + name + "' flows must be strictly increasing ("
                        + a.FlowM3s + " m3/s then " + b.FlowM3s + " m3/s)");
            }
            Name = name;
            Points = points;
        }

        /// <summary>Static pressure [Pa] delivered by the fan at
        /// <paramref name="flowM3s"/> [m^3/s].
        ///
        /// The vendor curve is interpolated piecewise-linearly between the
        /// two points bracketing the requested flow. Throws when
        /// <paramref name="flowM3s"/> lies outside the tabulated curve range
        /// (the catalogue data is not defined beyond its measured points),
        /// or when it is not finite.</summary>
        public double StaticPressureAt(double flowM3s)
        {
            if (!IsFinite(flowM3s))
                throw new WentaException("fan '" + Name + "': flow " + flowM3s + " m3/s is not finite");
            FanPoint first = Points[0];
            FanPoint last = Points[Points.Length - 1];
            if (flowM3s < first.FlowM3s || flowM3s > last.FlowM3s)
                throw new WentaException("fan '" + Name + "': flow " + flowM3s + " m3/s outside curve range ["
                    + first.FlowM3s + " m3/s ... " + last.FlowM3s + " m3/s]");
            // `flowM3s` is finite and inside [first, last], so exactly one
            // segment brackets it (including the knot values on its boundaries).
            for (int i = 0; i < Points.Length - 1; i++)
            {
                FanPoint a = Points[i];
                FanPoint b = Points[i + 1];
                if (flowM3s >= a.FlowM3s && flowM3s <= b.FlowM3s)
                {
                    double t = (flowM3s - a.FlowM3s) / (b.FlowM3s - a.FlowM3s);
                    return a.StaticPressurePa + t * (b.StaticPressurePa - a.StaticPressurePa);
                }
            }
            // Defensive: unreachable for finite in-range flows on a validated curve.
            throw new WentaException("fan '" + Name + "': no segment brackets flow " + flowM3s + " m3/s");
        }

        /// <summary>True when <paramref name="v"/> is neither NaN nor infinite.</summary>
        internal static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }

    /// <summary>Duty-point power and fan-selection helpers.
    /// Port of the free functions in `venti/src/fan.rs`.</summary>
    public static class Fan
    {
        /// <summary>Shaft power [W] the fan must develop for a duty point
        /// (fan power input to the fluid, sometimes called "air power"
        /// before motor/impeller efficiencies are folded in).
        /// Closed form: P = Q * p / eta, with Q = flow [m^3/s],
        /// p = static pressure [Pa] and eta = the total (impeller x motor)
        /// efficiency in (0, 1].
        /// Throws when <paramref name="efficiency"/> is outside (0, 1],
        /// when flow or pressure are negative, or when any input is
        /// non-finite.</summary>
        public static double Power(double flowM3s, double pressurePa, double efficiency)
        {
            if (!FanCurve.IsFinite(flowM3s) || !FanCurve.IsFinite(pressurePa)
                || !FanCurve.IsFinite(efficiency))
                throw new WentaException("fan_power: flow, pressure and efficiency must be finite");
            if (flowM3s < 0.0)
                throw new WentaException("fan_power: flow must be >= 0 m3/s, got " + flowM3s);
            if (pressurePa < 0.0)
                throw new WentaException("fan_power: pressure must be >= 0 Pa, got " + pressurePa);
            if (efficiency <= 0.0 || efficiency > 1.0)
                throw new WentaException("fan_power: efficiency must be in (0, 1], got " + efficiency);
            return flowM3s * pressurePa / efficiency;
        }

        /// <summary>Pressure margin [Pa] of <paramref name="fan"/> at the
        /// design flow: the static pressure the fan's curve delivers at
        /// <paramref name="designFlowM3s"/> minus <paramref name="requiredStaticPa"/>.
        ///
        /// A positive margin means the fan beats the requirement at the duty
        /// point; zero means it delivers exactly the required pressure; a
        /// negative margin means it falls short. Returns null when
        /// <paramref name="designFlowM3s"/> is outside the fan's tabulated
        /// curve range — the margin is undefined there, so that fan cannot
        /// be compared at this duty point.
        /// Throws for a negative or non-finite <paramref name="designFlowM3s"/>
        /// or <paramref name="requiredStaticPa"/>.</summary>
        public static double? Margin(FanCurve fan, double designFlowM3s, double requiredStaticPa)
        {
            if (!FanCurve.IsFinite(designFlowM3s) || designFlowM3s < 0.0)
                throw new WentaException("margin_pa: design flow must be a finite value >= 0 m3/s, got "
                    + designFlowM3s);
            if (!FanCurve.IsFinite(requiredStaticPa) || requiredStaticPa < 0.0)
                throw new WentaException("margin_pa: required pressure must be a finite value >= 0 Pa, got "
                    + requiredStaticPa);
            // Outside the tabulated range the curve is undefined -> no margin.
            if (designFlowM3s < fan.Points[0].FlowM3s
                || designFlowM3s > fan.Points[fan.Points.Length - 1].FlowM3s)
                return null;
            return fan.StaticPressureAt(designFlowM3s) - requiredStaticPa;
        }

        /// <summary>Pick the first fan from <paramref name="curves"/> that
        /// delivers the required static pressure at the design flow.
        ///
        /// For each fan in catalogue order the curve static pressure at
        /// <paramref name="designFlowM3s"/> is compared with
        /// <paramref name="requiredStaticPa"/>; the index of the first fan
        /// whose margin (curve pressure - required) is &gt;= 0 is returned.
        /// Fans whose curve does not cover the design flow (margin
        /// undefined) are skipped, as are fans that fall short. Null means
        /// no fan in the catalogue meets the duty point.
        /// Throws for a negative or non-finite <paramref name="designFlowM3s"/>
        /// or <paramref name="requiredStaticPa"/> (raised on the first fan
        /// compared; an empty catalogue never evaluates the arguments and
        /// simply returns null).</summary>
        public static int? PickFan(FanCurve[] curves, double designFlowM3s, double requiredStaticPa)
        {
            for (int i = 0; i < curves.Length; i++)
            {
                double? margin = Margin(curves[i], designFlowM3s, requiredStaticPa);
                if (margin.HasValue && margin.Value >= 0.0)
                    return i;
            }
            return null;
        }
    }
}
