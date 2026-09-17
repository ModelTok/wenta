using System;

namespace Wenta
{
    /// <summary>A thermal insulation material: name + thermal conductivity λ [W/(m·K)].</summary>
    public struct InsulationMaterial
    {
        public string Name;
        /// <summary>Thermal conductivity λ [W/(m·K)].</summary>
        public double Conductivity;

        public InsulationMaterial(string name, double conductivity)
        {
            Name = name;
            Conductivity = conductivity;
        }
    }

    /// <summary>Thermal insulation of ducts — calculation &amp; selection (izolacja
    /// termiczna). Port of `venti/src/insulation.rs` (issue #39 / #51):
    /// computing and choosing duct insulation thickness for two common
    /// criteria (per EN ISO 12241 / ASHRAE practice):
    ///
    /// 1. Condensation prevention — enough insulation that the outer surface
    ///    stays above the ambient dew point (cold supply air in a warm, humid
    ///    space is the classic case);
    /// 2. Heat-loss / heat-gain limit — enough insulation that the heat
    ///    transfer per metre stays under a target.
    ///
    /// The thermal model is a steady-state cylindrical resistance network per
    /// metre of duct (inner air film + cylindrical insulation + outer air
    /// film), solved by growing the thickness until the criterion is met:
    ///
    /// R_in  = 1 / (h_i · π · D)                      inner air film
    /// R_ins = ln(D_o / D) / (2π·λ)                   cylindrical insulation
    /// R_out = 1 / (h_e · π · D_o)                    outer air film
    /// q     = (T_air − T_amb) / (R_in + R_ins + R_out)     [W/m]
    /// T_s   = T_amb + q · R_out                       outer surface temp [°C]
    ///
    /// Condensation is avoided when T_s ≥ T_dew. All thicknesses are returned
    /// in metres (library SI convention); <see cref="SelectThickness"/> snaps
    /// to standard millimetre steps.</summary>
    public static class Insulation
    {
        /// <summary>Inner (air-side) heat-transfer coefficient default [W/m²K] —
        /// forced duct airflow.</summary>
        public const double DefaultInnerHtc = 10.0;

        /// <summary>Outer (ambient-side) heat-transfer coefficient default
        /// [W/m²K] — still indoor air.</summary>
        public const double DefaultOuterHtc = 8.0;

        /// <summary>Largest insulation thickness [m] the solvers will consider.</summary>
        public const double MaxThicknessM = 0.250;

        /// <summary>Common standard insulation thicknesses [mm] used for selection.</summary>
        public static readonly double[] StandardThicknessMm =
        {
            20.0, 30.0, 40.0, 50.0, 60.0, 80.0, 100.0, 120.0,
        };

        /// <summary>Typical duct insulants and their conductivities λ [W/(m·K)].</summary>
        public static readonly InsulationMaterial[] Materials =
        {
            new InsulationMaterial("mineral_wool", 0.035),
            new InsulationMaterial("pe_foam", 0.040),
            new InsulationMaterial("epdm_nbr", 0.038),
            new InsulationMaterial("pir", 0.024),
            new InsulationMaterial("pu_foam", 0.028),
        };

        /// <summary>Look up a material's conductivity by name (case-insensitive),
        /// e.g. "mineral_wool" -> 0.035. Returns false via <paramref name="conductivity"/>
        /// left at 0.0 when the material is not found.</summary>
        public static bool MaterialConductivity(string name, out double conductivity)
        {
            foreach (InsulationMaterial m in Materials)
            {
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    conductivity = m.Conductivity;
                    return true;
                }
            }
            conductivity = 0.0;
            return false;
        }

        /// <summary>Cylindrical insulation resistance per metre for outer diameter dO.</summary>
        private static double RIns(double cond, double d, double dO)
        {
            return Math.Log(dO / d) / (2.0 * Math.PI * cond);
        }

        /// <summary>Steady-state per-metre heat flow through a duct with
        /// insulation thickness t [m]. Returns q [W/m]; positive = outwards
        /// (hot duct), negative = inwards (cold duct).</summary>
        private static double HeatFlow(double airTempC, double ambientTempC, double cond,
            double dM, double tM, double innerHtc, double outerHtc)
        {
            double dO = dM + 2.0 * tM;
            double rIn = 1.0 / (innerHtc * Math.PI * dM);
            double rIns = RIns(cond, dM, dO);
            double rOut = 1.0 / (outerHtc * Math.PI * dO);
            return (airTempC - ambientTempC) / (rIn + rIns + rOut);
        }

        /// <summary>Outer surface temperature [°C] of the insulation for thickness t [m].</summary>
        private static double SurfaceTemp(double airTempC, double ambientTempC, double cond,
            double dM, double tM, double innerHtc, double outerHtc)
        {
            double q = HeatFlow(airTempC, ambientTempC, cond, dM, tM, innerHtc, outerHtc);
            double dO = dM + 2.0 * tM;
            double rOut = 1.0 / (outerHtc * Math.PI * dO);
            return ambientTempC + q * rOut;
        }

        /// <summary>Smallest insulation thickness [m] so the outer surface stays
        /// at or above the dew point (condensation prevention).
        ///
        /// Example: cold supply air (8 °C) in a 24 °C room at 60% RH (dew point
        /// ≈ 15.8 °C), mineral wool λ=0.035, 200 mm duct, indoor film
        /// coefficients: RequiredThicknessCondensation(8.0, 15.8, 24.0, 0.035,
        /// 0.2, 10.0, 8.0) returns a value t with 0 &lt; t &lt; 0.1.</summary>
        public static double RequiredThicknessCondensation(double airTempC, double dewPointC,
            double ambientTempC, double conductivity, double ductOuterDiameterM,
            double innerHtc, double outerHtc)
        {
            Validate(ductOuterDiameterM, conductivity, innerHtc, outerHtc);
            // Already condensation-safe at t = 0?
            if (SurfaceTemp(airTempC, ambientTempC, conductivity, ductOuterDiameterM, 0.0,
                innerHtc, outerHtc) >= dewPointC)
            {
                return 0.0;
            }
            // Grow thickness until the surface warms above the dew point.
            const double step = 0.001; // 1 mm
            double t = 0.0;
            while (t <= MaxThicknessM)
            {
                t += step;
                if (SurfaceTemp(airTempC, ambientTempC, conductivity, ductOuterDiameterM, t,
                    innerHtc, outerHtc) >= dewPointC)
                {
                    return t;
                }
            }
            throw new WentaException("dew point cannot be reached within the maximum considered thickness");
        }

        /// <summary>Smallest insulation thickness [m] so the per-metre heat
        /// transfer magnitude is at most targetWPerM (heat-loss / heat-gain
        /// limit).
        ///
        /// Example: RequiredThicknessHeatLoss(60.0, 20.0, 10.0, 0.035, 0.2,
        /// 10.0, 8.0) returns a value t &gt; 0.</summary>
        public static double RequiredThicknessHeatLoss(double airTempC, double ambientTempC,
            double targetWPerM, double conductivity, double ductOuterDiameterM,
            double innerHtc, double outerHtc)
        {
            Validate(ductOuterDiameterM, conductivity, innerHtc, outerHtc);
            if (targetWPerM <= 0.0)
                throw new WentaException("target_w_per_m must be positive");
            double atZero = Math.Abs(HeatFlow(airTempC, ambientTempC, conductivity,
                ductOuterDiameterM, 0.0, innerHtc, outerHtc));
            if (atZero <= targetWPerM)
                return 0.0;
            const double step = 0.001;
            double t = 0.0;
            while (t <= MaxThicknessM)
            {
                t += step;
                double q = Math.Abs(HeatFlow(airTempC, ambientTempC, conductivity,
                    ductOuterDiameterM, t, innerHtc, outerHtc));
                if (q <= targetWPerM)
                    return t;
            }
            throw new WentaException("heat-loss target cannot be met within the maximum considered thickness");
        }

        /// <summary>Per-metre heat transfer [W/m] through insulation of
        /// thickness thicknessM [m] (positive = outwards). Useful to report
        /// the resulting loss after selection.</summary>
        public static double HeatLossWithInsulation(double airTempC, double ambientTempC,
            double conductivity, double ductOuterDiameterM, double thicknessM,
            double innerHtc, double outerHtc)
        {
            Validate(ductOuterDiameterM, conductivity, innerHtc, outerHtc);
            if (thicknessM < 0.0)
                throw new WentaException("thickness_m must be non-negative");
            return HeatFlow(airTempC, ambientTempC, conductivity, ductOuterDiameterM,
                thicknessM, innerHtc, outerHtc);
        }

        /// <summary>Round a required thickness [m] up to the smallest standard
        /// step (<see cref="StandardThicknessMm"/>). Throws when the
        /// requirement exceeds the largest standard step.
        ///
        /// Example: SelectThickness(0.045) returns 0.05 (45 mm -> 50 mm step).</summary>
        public static double SelectThickness(double requiredM)
        {
            if (requiredM < 0.0)
                throw new WentaException("required_m must be non-negative");
            double requiredMm = requiredM * 1000.0;
            double max = StandardThicknessMm[StandardThicknessMm.Length - 1];
            if (requiredMm > max)
            {
                throw new WentaException(string.Format(
                    "required thickness {0:F0} mm exceeds the largest standard step {1:F0} mm",
                    requiredMm, max));
            }
            foreach (double s in StandardThicknessMm)
            {
                if (s >= requiredMm)
                    return s / 1000.0;
            }
            throw new WentaException("largest standard step covers the required range");
        }

        private static void Validate(double dM, double cond, double innerHtc, double outerHtc)
        {
            if (dM <= 0.0)
                throw new WentaException("duct_outer_diameter_m must be positive");
            if (cond <= 0.0)
                throw new WentaException("conductivity must be positive");
            if (innerHtc <= 0.0 || outerHtc <= 0.0)
                throw new WentaException("heat-transfer coefficients must be positive");
        }
    }
}
