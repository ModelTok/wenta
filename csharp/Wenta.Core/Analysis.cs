using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Per-branch analysis row shown in the dockable panel.
    /// One entry per duct component (<see cref="RigidDuct"/> or
    /// <see cref="FlexDuct"/>) in the network.</summary>
    public sealed class BranchInfo
    {
        /// <summary>The component id of the duct in the network.</summary>
        public string ComponentId;
        /// <summary>Component kind: "RigidDuct" or "FlexDuct".</summary>
        public string Kind;
        /// <summary>Volumetric flow carried by the branch [m^3/s].</summary>
        public double FlowM3s;
        /// <summary>Mean duct air velocity [m/s].</summary>
        public double VelocityMs;
        /// <summary>Total pressure drop across the branch [Pa].</summary>
        public double PressureDropPa;
        /// <summary>Airflow-regenerated noise level [dB re 1e-12 W], null when
        /// it cannot be evaluated (e.g. zero velocity).</summary>
        public double? RegeneratedNoiseDb;
        /// <summary>Damper loss coefficient (zeta) required to balance the
        /// branch against the critical path, null when not meaningful
        /// (e.g. zero velocity).</summary>
        public double? BalancingZeta;
    }

    /// <summary>The analysis report: the critical-path pressure drop plus one
    /// branch per duct.</summary>
    public sealed class AnalysisSummary
    {
        /// <summary>Total pressure drop along the critical path [Pa].</summary>
        public double CriticalDpPa;
        /// <summary>One <see cref="BranchInfo"/> per duct component.</summary>
        public List<BranchInfo> Branches = new List<BranchInfo>();
        /// <summary>The number of duct branches (equal to Branches.Count).</summary>
        public int NBranches { get { return Branches.Count; } }
    }

    /// <summary>Per-branch analysis report: combine the pressure-drop network
    /// solution with the sound (regenerated noise) and balancing (damper zeta)
    /// modules into a single dockable-panel report per branch. Port of
    /// `venti/src/analysis.rs`.
    ///
    /// For every duct branch (RigidDuct / FlexDuct) in a solved network this
    /// reports its flow, velocity, pressure drop, regenerated noise level and
    /// the damper loss coefficient (zeta) needed to balance it against the
    /// critical path.</summary>
    public static class Analysis
    {
        /// <summary>Analyze a network and produce a per-branch sound +
        /// balancing report.
        ///
        /// This method SOLVES <paramref name="network"/> in place: it calls
        /// <see cref="Network.Solve"/>, which mutates every port's flowrate,
        /// velocity and pressure drop. That differs from the Rust `analyze`
        /// (which clones the network) and from <see cref="Bom.Build"/>,
        /// <see cref="Results.ExtractResults"/> and
        /// <see cref="Marking.AssignBranchMarks"/>, which are read-only over a
        /// network the caller has already solved. Solving is idempotent
        /// (<see cref="Solver.PropagateFlowrates"/> resets every node's flow
        /// first), so calling this on an already-solved network reproduces
        /// the first solve; it just cannot leave the network untouched.
        ///
        /// For every RigidDuct/FlexDuct branch:
        /// * FlowM3s — the inlet-port flowrate after the solve.
        /// * VelocityMs — flow / cross-section area.
        /// * PressureDropPa — the total drop across the component's ports.
        /// * RegeneratedNoiseDb — the regenerated-noise correlation using the
        ///   duct's hydraulic diameter (RigidDuct / rectangular) or diameter
        ///   (FlexDuct / round); null when the inputs are not physical
        ///   (e.g. zero velocity) rather than throwing.
        /// * BalancingZeta — <see cref="Balancing.BalancingZeta"/> against the
        ///   critical-path drop, using the component's own pressure drop as
        ///   the available-pressure proxy (see below).
        ///
        /// <para><b>Balancing "available" pressure proxy.</b> A branch's
        /// available pressure is not directly solvable from the scalar
        /// critical-path DP alone; for this dockable-panel report the
        /// component's own pressure drop is used as a proxy for the pressure
        /// available at that branch. This is a deliberate approximation.
        /// Branches whose own drop is close to the critical drop see
        /// zeta ~= 0 (damper fully open), while branches that drop much less
        /// than the critical path are treated as over-supplied and get a
        /// positive zeta to eat the surplus.</para>
        ///
        /// Throws <see cref="WentaException"/> propagated from solving the
        /// network (e.g. a cyclic graph).</summary>
        public static AnalysisSummary Analyze(Network network, Fluid fluid)
        {
            double criticalDpPa = network.Solve(fluid);

            var branches = new List<BranchInfo>();

            foreach (var kv in network.Components)
            {
                string cid = kv.Key;
                Component c = kv.Value;

                double area, diameter;
                string kind;

                RigidDuct rigid = c as RigidDuct;
                FlexDuct flex = c as FlexDuct;
                if (rigid != null)
                {
                    area = rigid.CrossSection.Area;
                    diameter = rigid.CrossSection.HydraulicDiameter;
                    kind = "RigidDuct";
                }
                else if (flex != null)
                {
                    area = flex.Area;
                    diameter = flex.Diameter;
                    kind = "FlexDuct";
                }
                else
                {
                    // Only ducts are reported as branches.
                    continue;
                }

                // Inlet flow from the component's inlet port (0 when unset).
                double flowM3s = c.InletFlowrate() ?? 0.0;

                double velocityMs = flowM3s / area;

                // Total pressure drop across all ports.
                double pressureDropPa = c.TotalPressureDrop();

                // Regenerated noise: the correlation rejects velocity <= 0,
                // diameter <= 0 and density <= 0, so report null there instead
                // (NaN inputs pass through to the correlation, as before).
                double? regeneratedNoiseDb =
                    !(velocityMs <= 0.0) && !(diameter <= 0.0) && !(fluid.Density <= 0.0)
                        ? (double?)Sound.RegeneratedNoiseRound(velocityMs, diameter, fluid.Density)
                        : null;

                // Balancing zeta: use the component's own drop as the
                // available-pressure proxy (documented above). Null when the
                // velocity is not meaningful.
                double? balancingZeta = velocityMs > 0.0
                    ? (double?)Balancing.BalancingZeta(criticalDpPa, pressureDropPa, velocityMs, fluid.Density)
                    : null;

                branches.Add(new BranchInfo
                {
                    ComponentId = cid,
                    Kind = kind,
                    FlowM3s = flowM3s,
                    VelocityMs = velocityMs,
                    PressureDropPa = pressureDropPa,
                    RegeneratedNoiseDb = regeneratedNoiseDb,
                    BalancingZeta = balancingZeta,
                });
            }

            return new AnalysisSummary
            {
                CriticalDpPa = criticalDpPa,
                Branches = branches,
            };
        }
    }
}
