using System;

namespace Wenta
{
    /// <summary>The display/engineering unit system for a project. Port of
    /// `venti::settings::Units` — renamed to <c>UnitSystem</c> here because
    /// the static unit-converter class <see cref="Wenta.Units"/> already
    /// occupies the `Units` name in this namespace.
    ///
    /// <list type="bullet">
    /// <item><description><see cref="Si"/> — metric (mm, m, m/s, Pa).</description></item>
    /// <item><description><see cref="Ip"/> — Imperial / customary (in, ft, fpm, in w.c.).</description></item>
    /// </list></summary>
    public enum UnitSystem
    {
        /// <summary>SI / metric units.</summary>
        Si,

        /// <summary>Imperial (inch-pound) customary units.</summary>
        Ip
    }

    /// <summary>Project-scoped defaults for sizing and pressure-drop. Port of
    /// `venti::settings::ProjectSettings` (the dependency-free core; the
    /// `cli`-feature JSON (de)serialization helpers are out of scope for this
    /// bare-csc build).
    ///
    /// These are the values a new project starts from and that every sizing
    /// and pressure-drop call uses unless overridden.
    ///
    /// A freshly constructed instance starts from sensible, valid defaults:
    /// <see cref="Standard"/> = <see cref="Wenta.Standard.En1505_1506"/>,
    /// <see cref="DefaultDiameterMm"/> = 200.0,
    /// <see cref="AbsoluteRoughnessM"/> = 0.0001,
    /// <see cref="TargetVelocityMs"/> = 4.0,
    /// <see cref="TargetPaPerM"/> = 1.0,
    /// <see cref="NoiseSpace"/> = "office",
    /// <see cref="UnitSystem"/> = <see cref="Wenta.UnitSystem.Si"/>.</summary>
    public class ProjectSettings
    {
        /// <summary>The sizing standard (dimension set) to use.</summary>
        public Standard Standard;

        /// <summary>Default round-duct diameter used when no section is given, in mm.</summary>
        public double DefaultDiameterMm;

        /// <summary>Default absolute roughness of the duct material, in metres.</summary>
        public double AbsoluteRoughnessM;

        /// <summary>Default target duct velocity, in m/s.</summary>
        public double TargetVelocityMs;

        /// <summary>Default target specific pressure drop, in Pa/m.</summary>
        public double TargetPaPerM;

        /// <summary>Acoustic design space (e.g. "office", "conference", "lobby").</summary>
        public string NoiseSpace;

        /// <summary>The unit system to display results in.</summary>
        public UnitSystem UnitSystem;

        /// <summary>Construct a new project's settings from the same sensible,
        /// valid defaults as Rust's `ProjectSettings::default()`.</summary>
        public ProjectSettings()
        {
            Standard = Standard.En1505_1506;
            DefaultDiameterMm = 200.0;
            AbsoluteRoughnessM = 0.0001;
            TargetVelocityMs = 4.0;
            TargetPaPerM = 1.0;
            NoiseSpace = "office";
            UnitSystem = UnitSystem.Si;
        }

        /// <summary>Validate that every numeric default is physically
        /// meaningful. Throws <see cref="WentaException"/> describing the
        /// first invalid field found, checked in the same order as the Rust
        /// port: diameter, roughness, velocity, then pressure rate. The
        /// <see cref="Wenta.Units"/> value is valid by construction (the enum
        /// only has <see cref="Wenta.Units.Si"/> and
        /// <see cref="Wenta.Units.Ip"/>, so there is no invalid state).</summary>
        public void Validate()
        {
            if (DefaultDiameterMm <= 0.0)
                throw new WentaException("default_diameter_mm must be > 0");
            if (AbsoluteRoughnessM <= 0.0)
                throw new WentaException("absolute_roughness_m must be > 0");
            if (TargetVelocityMs <= 0.0)
                throw new WentaException("target_velocity_ms must be > 0");
            if (TargetPaPerM <= 0.0)
                throw new WentaException("target_pa_per_m must be > 0");
        }
    }
}
