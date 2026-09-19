using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>Options for <see cref="ReFit.Apply"/>. Mirrors the subset of
    /// <see cref="BatchSizingRequest"/> that makes sense when the ducts, their
    /// flows and their shapes already exist in a network: the network supplies
    /// the flow, the shape, the length and the roughness, so only the sizing
    /// criterion and the snapping standard are caller-chosen.</summary>
    public sealed class ReFitOptions
    {
        /// <summary>Sizing criterion. <see cref="SizingMethod.Velocity"/> and
        /// <see cref="SizingMethod.EqualFriction"/> are supported directly;
        /// <see cref="SizingMethod.PressureDropBudget"/> is applied per duct as
        /// a budget of <see cref="TargetPaPerM"/> × the duct's own length (i.e.
        /// the same criterion as EqualFriction).
        /// <see cref="SizingMethod.NoiseLimit"/> and
        /// <see cref="SizingMethod.AspectRatio"/> are rejected: this class has
        /// no space-type / aspect-ratio input, and AspectRatio would also change
        /// a duct's shape. Use <see cref="BatchSizing"/> for those.</summary>
        public SizingMethod Method = SizingMethod.Velocity;
        /// <summary>Velocity method: maximum duct velocity [m/s].</summary>
        public double TargetVelocity = 4.0;
        /// <summary>EqualFriction / PressureDropBudget: maximum linear pressure
        /// drop [Pa/m].</summary>
        public double TargetPaPerM = 1.0;
        /// <summary>Size table the new sections are snapped to when
        /// <see cref="SnapToStandard"/> is set.</summary>
        public Standard Standard = Standard.En1505_1506;
        /// <summary>Snap each computed section up to the nearest size in
        /// <see cref="Standard"/>. The <see cref="Sizing"/> methods already pick
        /// from the EN tables, so this only bites for the ASHRAE / DIN
        /// standards (and it is what makes re-fitting idempotent).</summary>
        public bool SnapToStandard = true;
        /// <summary>Air properties; null means <see cref="Fluid.StandardAir"/>.</summary>
        public Fluid Fluid;
    }

    /// <summary>What <see cref="ReFit.Apply"/> did to one duct
    /// (<see cref="RigidDuct"/> or <see cref="FlexDuct"/>).</summary>
    public sealed class ReFitChange
    {
        /// <summary>The duct's component id in the network.</summary>
        public string ComponentId;
        /// <summary>Diameter before the re-fit [m]. For a rectangular duct this
        /// is the hydraulic diameter (as in <see cref="Marking"/>).</summary>
        public double OldDiameterM;
        /// <summary>Diameter after the re-fit [m] (hydraulic diameter for a
        /// rectangular duct).</summary>
        public double NewDiameterM;
        /// <summary>Mean velocity before the re-fit [m/s].</summary>
        public double OldVelocityMs;
        /// <summary>Mean velocity after the re-fit [m/s].</summary>
        public double NewVelocityMs;
        /// <summary>False when the re-sized (and snapped) section is the size
        /// the duct already had.</summary>
        public bool Changed;
    }

    /// <summary>Outcome of <see cref="ReFit.Apply"/>: the re-sized network, one
    /// <see cref="ReFitChange"/> per duct, and the critical-path pressure drop
    /// before and after.</summary>
    public sealed class ReFitResult
    {
        /// <summary>A NEW network with the same ids, connections and non-duct
        /// components, and re-sized ducts. Already solved.</summary>
        public Network Network;
        /// <summary>One entry per duct, in the input network's component order.</summary>
        public List<ReFitChange> Changes = new List<ReFitChange>();
        /// <summary>Critical-path pressure drop of the input network [Pa].</summary>
        public double OldCriticalDpPa;
        /// <summary>Critical-path pressure drop of <see cref="Network"/> [Pa].</summary>
        public double NewCriticalDpPa;

        /// <summary>CSV header, matching <see cref="ToCsv"/>.</summary>
        public static readonly string[] Fields =
        {
            "component_id",
            "old_diameter_m",
            "new_diameter_m",
            "old_velocity_ms",
            "new_velocity_ms",
            "changed",
        };

        /// <summary>The <see cref="Changes"/> table as CSV with a header row;
        /// invariant culture, no trailing newline. The critical-path values are
        /// not part of the table — read them from
        /// <see cref="OldCriticalDpPa"/> / <see cref="NewCriticalDpPa"/>.</summary>
        public string ToCsv()
        {
            var lines = new List<string> { string.Join(",", Fields) };
            if (Changes == null) return lines[0];
            foreach (ReFitChange c in Changes)
            {
                lines.Add(string.Join(",", new[]
                {
                    ReFit.Quote(c.ComponentId),
                    c.OldDiameterM.ToString(CultureInfo.InvariantCulture),
                    c.NewDiameterM.ToString(CultureInfo.InvariantCulture),
                    c.OldVelocityMs.ToString(CultureInfo.InvariantCulture),
                    c.NewVelocityMs.ToString(CultureInfo.InvariantCulture),
                    c.Changed ? "true" : "false",
                }));
            }
            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>Balancing advice for one terminal: how much pressure its branch
    /// actually drops, how much the critical branch drops, and the damper the
    /// difference implies.</summary>
    public sealed class BalancingHint
    {
        /// <summary>Component id of the terminal.</summary>
        public string TerminalId;
        /// <summary>Pressure dropped along this terminal's own branch,
        /// source → terminal [Pa].</summary>
        public double AvailableDpPa;
        /// <summary>Pressure the system must deliver: the critical (largest)
        /// branch drop [Pa]; the same for every terminal.</summary>
        public double RequiredDpPa;
        /// <summary>RequiredDpPa − AvailableDpPa, floored at 0 [Pa]: what this
        /// terminal's balancing damper has to absorb.</summary>
        public double SurplusDpPa;
        /// <summary>Damper loss coefficient producing
        /// <see cref="SurplusDpPa"/>; 0 on the critical branch.</summary>
        public double DamperZeta;
        /// <summary>Damper opening [%] for <see cref="DamperZeta"/>; 100 on the
        /// critical branch.</summary>
        public double DamperOpenPercent;
    }

    /// <summary>Auto-resize / re-fit on edit, plus balancing hints (issue #27).
    ///
    /// <para><b>Re-fit.</b> <see cref="Apply"/> takes a network whose flows have
    /// changed (a terminal was retargeted, a branch added) and re-sizes every
    /// duct to the flow it now carries, using one of the <see cref="Sizing"/>
    /// criteria and snapping to a <see cref="Standard"/>'s size table. Ducts are
    /// immutable (<see cref="RigidDuct.CrossSection"/> and
    /// <see cref="FlexDuct.Diameter"/> are readonly), so the result is a NEW
    /// network: same ids, same connections, same non-duct components rebuilt
    /// from their constructor arguments, re-sized ducts.</para>
    ///
    /// <para><b>Balancing hints.</b> <see cref="Hints"/> reports, per terminal,
    /// the pressure its branch drops against the critical branch and the damper
    /// setting that eats the difference (<see cref="Balancing"/>).</para></summary>
    public static class ReFit
    {
        // ---- re-fit ---------------------------------------------------------

        /// <summary>Re-size every duct in <paramref name="network"/> for the
        /// flow it carries and return the re-sized copy.
        ///
        /// <para>This method SOLVES <paramref name="network"/> in place (like
        /// <see cref="Analysis.Analyze"/>, and unlike <see cref="Bom.Build"/> /
        /// <see cref="Results.ExtractResults"/>): the duct flows are what the
        /// re-fit sizes from, and requiring the caller to pre-solve would make
        /// "re-fit after an edit" a two-step dance. Solving is idempotent, so
        /// passing an already-solved network reproduces the same state.
        /// The returned network is solved too.</para>
        ///
        /// <para>Every duct is sized from its INLET port flow with the duct's own
        /// shape, length and roughness; <see cref="FlexDuct"/> is re-sized as
        /// well (its constructor takes the diameter, and length,
        /// per-metre drop and stretch are carried across unchanged — note that a
        /// flex duct's drop comes from the manufacturer curve, so changing its
        /// diameter changes the velocity but not the per-metre drop).</para>
        ///
        /// <para>Throws <see cref="WentaException"/> when the network is null,
        /// has no <see cref="Terminal"/>, contains a duct that carries no flow,
        /// contains a component type this rebuild does not know, or when
        /// <see cref="ReFitOptions.Method"/> needs an input
        /// <see cref="ReFitOptions"/> does not carry.</para></summary>
        public static ReFitResult Apply(Network network, ReFitOptions options = null)
        {
            if (network == null)
                throw new WentaException("ReFit.Apply: network is null");
            options = options ?? new ReFitOptions();
            Fluid fluid = options.Fluid ?? Fluid.StandardAir();

            if (network.Terminals().Count == 0)
                throw new WentaException(
                    "ReFit.Apply: network has no Terminal component, so no duct carries a flow to size from");

            double oldCritical = network.Solve(fluid);

            var result = new ReFitResult { OldCriticalDpPa = oldCritical };
            var rebuilt = new Network { Name = network.Name };

            foreach (KeyValuePair<string, Component> kv in network.Components)
            {
                string cid = kv.Key;
                Component c = kv.Value;

                RigidDuct rigid = c as RigidDuct;
                FlexDuct flex = c as FlexDuct;
                if (rigid == null && flex == null)
                {
                    rebuilt.Add(cid, CopyNonDuct(cid, c));
                    continue;
                }

                double oldArea = rigid != null ? rigid.CrossSection.Area : flex.Area;
                double oldDiameter = rigid != null
                    ? rigid.CrossSection.HydraulicDiameter : flex.Diameter;
                double flow = c.InletFlowrate() ?? 0.0;
                if (flow <= 0.0)
                    throw new WentaException("ReFit.Apply: duct '" + cid
                        + "' carries no flow (" + flow.ToString(CultureInfo.InvariantCulture)
                        + " m^3/s); it cannot be sized");

                Component resized;
                double newArea, newDiameter;
                if (rigid != null)
                {
                    CrossSection section = SizeSection(cid, rigid.CrossSection, flow,
                        rigid.Length, rigid.AbsoluteRoughness, options, fluid);
                    resized = new RigidDuct(rigid.Name, section, rigid.Length,
                        rigid.AbsoluteRoughness);
                    newArea = section.Area;
                    newDiameter = section.HydraulicDiameter;
                }
                else
                {
                    // A flex duct is always round; size it as such.
                    CrossSection section = SizeSection(cid, new Round(flex.Diameter), flow,
                        flex.Length, RigidDuct.DefaultAbsoluteRoughness, options, fluid);
                    double d = ((Round)section).Diameter;
                    resized = new FlexDuct(flex.Name, d, flex.Length,
                        flex.PressureDropPerMeter, flex.StretchPercentage);
                    newArea = section.Area;
                    newDiameter = d;
                }
                rebuilt.Add(cid, resized);

                result.Changes.Add(new ReFitChange
                {
                    ComponentId = cid,
                    OldDiameterM = oldDiameter,
                    NewDiameterM = newDiameter,
                    OldVelocityMs = flow / oldArea,
                    NewVelocityMs = flow / newArea,
                    Changed = Math.Abs(newDiameter - oldDiameter) > SizeEpsilonM,
                });
            }

            CopyConnections(network, rebuilt);
            result.Network = rebuilt;
            result.NewCriticalDpPa = rebuilt.Solve(fluid);
            return result;
        }

        /// <summary>Diameters closer than this (1 nm) count as the same size.</summary>
        private const double SizeEpsilonM = 1e-9;

        /// <summary>Size one duct's cross-section for <paramref name="flow"/>,
        /// keeping its shape, and snap it to the standard when asked.</summary>
        private static CrossSection SizeSection(string cid, CrossSection old, double flow,
            double length, double roughness, ReFitOptions options, Fluid fluid)
        {
            Round oldRound = old as Round;
            string shape = oldRound != null ? Sizing.ShapeRound : Sizing.ShapeRectangular;

            Sizing.SizingResult sized;
            switch (options.Method)
            {
                case SizingMethod.Velocity:
                    sized = Sizing.VelocityMethod(flow, shape, options.TargetVelocity);
                    break;
                case SizingMethod.EqualFriction:
                    sized = Sizing.EqualFrictionMethod(flow, options.TargetPaPerM, shape,
                        roughness, fluid);
                    break;
                case SizingMethod.PressureDropBudget:
                    // Budget = TargetPaPerM over the duct's own length.
                    sized = Sizing.PressureDropBudget(flow, length,
                        options.TargetPaPerM * length, shape, roughness, fluid);
                    break;
                case SizingMethod.NoiseLimit:
                    throw new WentaException(
                        "ReFit.Apply: SizingMethod.NoiseLimit needs a space type, which"
                        + " ReFitOptions does not carry; use BatchSizing, or"
                        + " SizingMethod.Velocity with TargetVelocity set to the NC limit");
                case SizingMethod.AspectRatio:
                    throw new WentaException(
                        "ReFit.Apply: SizingMethod.AspectRatio is not supported — it needs an"
                        + " aspect ratio and would force every duct rectangular; use BatchSizing");
                default:
                    throw new WentaException("ReFit.Apply: unknown sizing method "
                        + options.Method);
            }

            if (!options.SnapToStandard) return sized.Section;

            Round round = sized.Section as Round;
            if (round != null)
            {
                int mm = Standards.NearestRoundSizeFor(options.Standard, ToMm(round.Diameter), true);
                return new Round(mm / 1000.0);
            }
            Rectangular rect = (Rectangular)sized.Section;
            int[] wh = BatchSizing.SnapRectangular(options.Standard, ToMm(rect.Width),
                ToMm(rect.Height));
            if (wh == null)
                throw new WentaException("ReFit.Apply: no size in the chosen standard is large"
                    + " enough for duct '" + cid + "' (" + rect.Describe() + ")");
            return new Rectangular(wh[0] / 1000.0, wh[1] / 1000.0);
        }

        /// <summary>Millimetres from metres, rounded to a micrometre (as in
        /// <see cref="BatchSizing"/>) so 0.3 m matches the 300 mm table entry.</summary>
        private static double ToMm(double metres)
        {
            return Math.Round(metres * 1000.0, 3);
        }

        /// <summary>Rebuild a non-duct component from its constructor
        /// arguments. Cross-sections are immutable, so they are shared.</summary>
        private static Component CopyNonDuct(string cid, Component c)
        {
            Source source = c as Source;
            if (source != null) return new Source(source.Name);

            Terminal terminal = c as Terminal;
            if (terminal != null)
                return new Terminal(terminal.Name, terminal.Flowrate, terminal.CrossSection,
                    terminal.Zeta);

            Tee tee = c as Tee;
            if (tee != null)
                return new Tee(tee.Name, tee.CrossSection, tee.ZetaStraight, tee.ZetaBranch);

            TwoPortFitting fitting = c as TwoPortFitting;
            if (fitting != null)
                return new TwoPortFitting(fitting.Name, fitting.CrossSection, fitting.Zeta);

            throw new WentaException("ReFit.Apply: cannot copy component '" + cid
                + "' of unsupported type " + c.GetType().Name);
        }

        /// <summary>Recreate every component-to-component connection of
        /// <paramref name="from"/> in <paramref name="to"/>. Every predecessor
        /// of an in-port node is an out-port node "srcId:srcPort"
        /// (<see cref="Network.Add"/> only wires in-port → component and
        /// component → out-port), the same walk
        /// <see cref="NetworkJson.Serialize"/> uses.</summary>
        private static void CopyConnections(Network from, Network to)
        {
            foreach (KeyValuePair<string, Component> kv in from.Components)
            {
                foreach (Port p in kv.Value.Ports)
                {
                    if (!p.IsIn) continue;
                    foreach (string pred in from.Predecessors(Network.PortNodeId(kv.Key, p.Name)))
                    {
                        int colon = pred.LastIndexOf(':');
                        if (colon < 0) continue; // the component node, not a connection
                        to.Connect(pred.Substring(0, colon) + "." + pred.Substring(colon + 1),
                            kv.Key + "." + p.Name);
                    }
                }
            }
        }

        // ---- balancing hints ------------------------------------------------

        /// <summary>Damper advice for every terminal of a SOLVED network.
        ///
        /// <para>Each terminal's branch pressure drop is the sum of the port
        /// pressure drops along the path from the source to that terminal — the
        /// same accumulation <see cref="Solver.CriticalPath"/> performs, so the
        /// largest branch drop equals
        /// <see cref="Solver.CriticalPathPressureDrop"/>. That largest value is
        /// the pressure the system has to deliver
        /// (<see cref="BalancingHint.RequiredDpPa"/>); every other branch drops
        /// less, and its damper must absorb the surplus
        /// (<see cref="Balancing.BalancingZeta"/>,
        /// <see cref="Balancing.DamperOpenPercentage"/>). The critical terminal
        /// therefore comes out with ζ = 0 and 100 % open.</para>
        ///
        /// <para>The dynamic pressure that converts the surplus into ζ is taken
        /// at the terminal's own inlet velocity; terminals without a
        /// cross-section report velocity 0, so in that case the nearest
        /// non-zero upstream port velocity on the branch (i.e. the duct the
        /// damper would sit in) is used instead.</para>
        ///
        /// <para><paramref name="solvedNetwork"/> must already have been solved
        /// — this method is read-only and reads the per-node pressure drops the
        /// solve left behind. Throws <see cref="WentaException"/> when the
        /// network is null or has no terminal.</para></summary>
        public static List<BalancingHint> Hints(Network solvedNetwork, Fluid fluid = null)
        {
            if (solvedNetwork == null)
                throw new WentaException("ReFit.Hints: network is null");
            fluid = fluid ?? Fluid.StandardAir();
            if (solvedNetwork.Terminals().Count == 0)
                throw new WentaException("ReFit.Hints: network has no Terminal component");

            // Longest-path accumulation over the topological order, mirroring
            // Solver.CriticalPath: dist[n] = max over predecessors + dp[n].
            List<string> topo = solvedNetwork.TopoOrder();
            var dist = new Dictionary<string, double>(topo.Count);
            var prev = new Dictionary<string, string>(topo.Count);
            foreach (string n in topo)
            {
                List<string> preds = solvedNetwork.Predecessors(n);
                string bestP = null;
                double bestD = 0.0;
                foreach (string p in preds)
                {
                    double d = dist[p];
                    if (bestP == null || d > bestD) { bestP = p; bestD = d; }
                }
                prev[n] = bestP;
                dist[n] = bestD + solvedNetwork.NodeDp(n);
            }

            // Port lookup by graph node id, for the damper velocity.
            var portOf = new Dictionary<string, Port>();
            foreach (KeyValuePair<string, Component> kv in solvedNetwork.Components)
                foreach (Port p in kv.Value.Ports)
                    portOf[Network.PortNodeId(kv.Key, p.Name)] = p;

            var hints = new List<BalancingHint>();
            var nodes = new List<string>();
            double required = 0.0;
            foreach (KeyValuePair<string, Component> kv in solvedNetwork.Components)
            {
                if (!(kv.Value is Terminal)) continue;
                string node = Network.PortNodeId(kv.Key, kv.Value.Ports[0].Name);
                nodes.Add(node);
                double branch = dist[node];
                if (branch > required) required = branch;
                hints.Add(new BalancingHint
                {
                    TerminalId = kv.Key,
                    AvailableDpPa = branch,
                });
            }

            for (int i = 0; i < hints.Count; i++)
            {
                BalancingHint h = hints[i];
                h.RequiredDpPa = required;
                double surplus = required - h.AvailableDpPa;
                h.SurplusDpPa = surplus > 0.0 ? surplus : 0.0;
                double velocity = DamperVelocity(nodes[i], portOf, prev);
                h.DamperZeta = Balancing.BalancingZeta(required, h.AvailableDpPa, velocity,
                    fluid.Density);
                h.DamperOpenPercent = Balancing.DamperOpenPercentage(h.DamperZeta);
            }
            return hints;
        }

        /// <summary>Velocity [m/s] the terminal's damper would see: the
        /// terminal's own inlet velocity, or — when that is zero or unset, as it
        /// is for a terminal with no cross-section — the first non-zero port
        /// velocity walking back up the branch.</summary>
        private static double DamperVelocity(string terminalNode,
            IDictionary<string, Port> portOf, IDictionary<string, string> prev)
        {
            string node = terminalNode;
            while (node != null)
            {
                Port p;
                if (portOf.TryGetValue(node, out p) && p.Velocity.HasValue
                    && p.Velocity.Value > 0.0)
                    return p.Velocity.Value;
                node = prev[node];
            }
            return 0.0;
        }

        /// <summary>CSV header, matching <see cref="HintsAsCsv"/>.</summary>
        public static readonly string[] HintFields =
        {
            "terminal_id",
            "available_dp_pa",
            "required_dp_pa",
            "surplus_dp_pa",
            "damper_zeta",
            "damper_open_percent",
        };

        /// <summary>The hints as CSV with a header row; invariant culture, no
        /// trailing newline (the style of <see cref="Results.ResultsAsCsv"/>).</summary>
        public static string HintsAsCsv(IList<BalancingHint> hints)
        {
            var lines = new List<string> { string.Join(",", HintFields) };
            if (hints == null) return lines[0];
            foreach (BalancingHint h in hints)
            {
                lines.Add(string.Join(",", new[]
                {
                    Quote(h.TerminalId),
                    h.AvailableDpPa.ToString(CultureInfo.InvariantCulture),
                    h.RequiredDpPa.ToString(CultureInfo.InvariantCulture),
                    h.SurplusDpPa.ToString(CultureInfo.InvariantCulture),
                    h.DamperZeta.ToString(CultureInfo.InvariantCulture),
                    h.DamperOpenPercent.ToString(CultureInfo.InvariantCulture),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Quote a free-text CSV cell (same rule as
        /// <see cref="BatchSizing.ToCsv"/>).</summary>
        internal static string Quote(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
