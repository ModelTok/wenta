using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Sound / acoustics for ductwork: airflow-regenerated noise, the
    /// room equation that converts duct sound power into a space
    /// sound-pressure level, and NC (Noise Criterion) compliance checks.
    /// Port of `venti/src/sound.rs`.
    ///
    /// Mirrors the "calculation of sound" feature of CADvent / duct-design
    /// practice: a duct carrying air generates noise (regenerated noise) that
    /// depends strongly on velocity and on duct size; that sound power is then
    /// attenuated by the duct path and finally radiates into the served room,
    /// where it must stay below the space's NC target.</summary>
    public static class Sound
    {
        /// <summary>Reference air density used to normalise the density term
        /// of the regenerated-noise correlation (kg/m^3, standard air at
        /// 20 C).</summary>
        private static readonly double RhoZero = Fluid.StandardAir().Density;

        /// <summary>Calibration offset for the regenerated-noise correlation
        /// (dB re 1e-12 W).</summary>
        private const double RegenC = 10.0;

        /// <summary>Typical NC (Noise Criterion) targets by space type, dB.
        /// Parallel to <see cref="Sizing.NoiseLimitsMs"/>, which caps
        /// *velocity* to keep a space quiet; this table caps the resulting
        /// *level* directly.</summary>
        public static readonly Dictionary<string, double> NoiseLimitsNc =
            new Dictionary<string, double>
        {
            { "studio", 25.0 },     // recording / broadcast
            { "bedroom", 25.0 },
            { "office", 35.0 },
            { "classroom", 35.0 },
            { "retail", 40.0 },
            { "industrial", 60.0 },
        };

        /// <summary>Regenerated (airflow) sound-power level of a straight
        /// round duct, in dB re 1e-12 W.
        ///
        /// Reference formula: turbulent airflow in a duct radiates acoustic
        /// power that scales with the aerodynamic power of the flow —
        /// `W ~ rho*v^6` (Lighthill's v^6 law for subsonic aerodynamic sound)
        /// — and falls as the duct grows, so that for a *fixed* velocity a
        /// larger duct generates less noise (the same turbulent energy is
        /// spread over a larger, thinner boundary layer whose wall-pressure
        /// fluctuations couple less efficiently to the acoustic field).
        /// Working in dB against the 1e-12 W reference:
        ///
        /// `Lw = C + 10*log10(rho/rho0) + 60*log10(v) - 20*log10(d)`
        ///
        /// with `v` = mean duct velocity [m/s], `d` = duct internal diameter
        /// [m], `rho` = air density [kg/m^3] (defaults to standard air),
        /// `rho0` = 1.204 kg/m^3, and `C` = 10 dB (calibration offset putting
        /// the level on the usual regenerated-noise range). Hence regenerated
        /// noise grows ~ `v^6` and falls ~ `1/d^2`.</summary>
        /// <param name="velocity">Mean duct air velocity [m/s], must be &gt; 0.</param>
        /// <param name="diameter">Duct internal diameter [m], must be &gt; 0.</param>
        /// <param name="density">Optional air density [kg/m^3], must be &gt; 0.
        /// Defaults to standard air.</param>
        public static double RegeneratedNoiseRound(double velocity, double diameter,
            double? density = null)
        {
            if (velocity <= 0.0)
                throw new WentaException("velocity must be positive, got " + velocity);
            if (diameter <= 0.0)
                throw new WentaException("diameter must be positive, got " + diameter);
            double rho = density ?? RhoZero;
            if (rho <= 0.0)
                throw new WentaException("density must be positive, got " + rho);

            return RegenC + 10.0 * Math.Log10(rho / RhoZero) + 60.0 * Math.Log10(velocity)
                - 20.0 * Math.Log10(diameter);
        }

        /// <summary>Convert a duct sound-*power* level [dB re 1e-12 W] into a
        /// reverberant sound-*pressure* level [dB re 20 uPa] inside the
        /// served room.
        ///
        /// Reference formula: under the diffuse-field assumption, the
        /// reverberant sound pressure level produced by a sound power source
        /// in a room is given by the classical "room equation" (ISO 3740
        /// family / acoustic room theory):
        ///
        /// `Lp = Lw + 10*log10( 4*(1 - alpha) / (alpha*S) )`
        ///
        /// where `Lw` = source sound power level [dB], `S` = total room
        /// surface area [m^2], and `alpha` = average Sabine absorption
        /// coefficient of the room (an open, absorptive room attenuates the
        /// level, a small, reverberant room raises it). This is the
        /// transducer that turns duct regenerated noise into the level a
        /// person actually hears.</summary>
        /// <param name="soundPowerDb">Source sound power level [dB re 1e-12 W].</param>
        /// <param name="roomSurfaceArea">Total internal room surface area
        /// [m^2], &gt; 0.</param>
        /// <param name="absorptionCoefficient">Average absorption
        /// coefficient, in (0, 1).</param>
        public static double DuctPressureLevel(double soundPowerDb, double roomSurfaceArea,
            double absorptionCoefficient)
        {
            if (roomSurfaceArea <= 0.0)
                throw new WentaException(
                    "room_surface_area must be positive, got " + roomSurfaceArea);
            if (!(absorptionCoefficient > 0.0 && absorptionCoefficient < 1.0))
                throw new WentaException(
                    "absorption_coefficient must be in (0, 1), got " + absorptionCoefficient);

            double roomTerm = 4.0 * (1.0 - absorptionCoefficient)
                / (absorptionCoefficient * roomSurfaceArea);
            return soundPowerDb + 10.0 * Math.Log10(roomTerm);
        }

        private static double NcLimit(string spaceType)
        {
            double limit;
            if (!NoiseLimitsNc.TryGetValue(spaceType, out limit))
                throw new WentaException(
                    "unknown space_type '" + spaceType
                    + "'; expected one of studio|bedroom|office|classroom|retail|industrial");
            return limit;
        }

        /// <summary>Check a computed sound level against the NC target for
        /// <paramref name="spaceType"/>.
        ///
        /// Returns <c>true</c> when <paramref name="levelDb"/> is at or below
        /// the space's NC limit, reusing the <see cref="NoiseLimitsNc"/>
        /// mapping (parallel to <see cref="Sizing.NoiseLimitsMs"/>).</summary>
        public static bool NcOk(string spaceType, double levelDb)
        {
            return NcOkTarget(NcLimit(spaceType), levelDb);
        }

        /// <summary>Check a computed sound level against an explicit numeric
        /// NC target.
        ///
        /// Returns <c>true</c> when <paramref name="levelDb"/> is at or below
        /// <paramref name="ncTarget"/>. Unlike <see cref="NcOk"/> this needs
        /// no space-type lookup, so it cannot fail.</summary>
        public static bool NcOkTarget(double ncTarget, double levelDb)
        {
            return levelDb <= ncTarget + 1e-9;
        }
    }
}
