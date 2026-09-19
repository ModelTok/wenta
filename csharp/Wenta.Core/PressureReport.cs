using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>One row of a <see cref="PressureReport"/>: a single port node
    /// on the critical path, in source→terminal order.</summary>
    public sealed class PressureReportRow
    {
        /// <summary>Component id within the network (the part of the port node
        /// id before the ':').</summary>
        public string ComponentId;
        /// <summary>Component kind, e.g. <c>"RigidDuct"</c>
        /// (<c>Component.GetType().Name</c>).</summary>
        public string Kind;
        /// <summary>Port name, e.g. <c>"inlet"</c>, <c>"branch"</c>.</summary>
        public string Port;
        /// <summary>Volumetric flow through the port [m^3/s]; null when the
        /// network has not been solved.</summary>
        public double? FlowM3s;
        /// <summary>Air velocity at the port [m/s]; null when the port's flow
        /// has not been set (network not yet solved).</summary>
        public double? VelocityMs;
        /// <summary>Pressure drop booked on this port node [Pa].</summary>
        public double DpPa;
        /// <summary>Running sum of <see cref="DpPa"/> from the source up to
        /// and including this row [Pa].</summary>
        public double CumulativeDpPa;
        /// <summary>This row's share of the critical-path drop
        /// (<c>DpPa / CriticalDpPa * 100</c>); 0 when the critical drop is 0.</summary>
        public double SharePercent;
    }

    /// <summary>Critical-path pressure-drop report (WENTAPRESSURE, issue #47):
    /// the longest source→terminal path of a solved <see cref="Network"/>,
    /// one row per port node with its drop, the running total and its share
    /// of the critical drop.
    ///
    /// <see cref="Build"/> is read-only over a network the caller has already
    /// solved (like <see cref="Results.ExtractResults"/> and
    /// <see cref="Marking.AssignBranchMarks"/>); it never calls
    /// <see cref="Network.Solve"/>. On an unsolved network every drop is 0
    /// and the reported path is an arbitrary source→terminal route.</summary>
    public sealed class PressureReport
    {
        /// <summary><see cref="Network.Name"/> of the reported network.</summary>
        public string NetworkName;
        /// <summary>Total pressure drop along the critical path [Pa]. Equals
        /// <see cref="Solver.CriticalPathPressureDrop"/> (component nodes carry
        /// no drop, so the sum over port rows is the sum over the path).</summary>
        public double CriticalDpPa;
        /// <summary>One row per port node on the critical path, in path
        /// order (source outlet first, terminal inlet last).</summary>
        public List<PressureReportRow> Rows = new List<PressureReportRow>();

        /// <summary>CSV header, matching <see cref="ToCsv"/>.</summary>
        public static readonly string[] Fields =
        {
            "component_id",
            "kind",
            "port",
            "flow_m3s",
            "velocity_ms",
            "dp_pa",
            "cumulative_dp_pa",
            "share_percent",
        };

        /// <summary>Build the report from an already-solved network. Walks
        /// <see cref="Solver.CriticalPath"/> (extended through any trailing
        /// zero-drop nodes to the terminal — see <see cref="ExtendToTerminal"/>);
        /// pure component nodes (ids without ':') are skipped as rows but
        /// supply the component kind for the port rows. Throws
        /// <see cref="WentaException"/> when the network has
        /// no <see cref="Source"/> or no <see cref="Terminal"/>.</summary>
        public static PressureReport Build(Network solvedNetwork)
        {
            if (solvedNetwork == null)
                throw new WentaException("network is null");
            if (solvedNetwork.Sources().Count == 0)
                throw new WentaException("network has no Source component");
            if (solvedNetwork.Terminals().Count == 0)
                throw new WentaException("network has no Terminal component");

            var report = new PressureReport { NetworkName = solvedNetwork.Name ?? "" };

            List<string> path = Solver.CriticalPath(solvedNetwork);
            ExtendToTerminal(solvedNetwork, path);
            double cumulative = 0.0;
            foreach (string nodeId in path)
            {
                int colon = nodeId.IndexOf(':');
                if (colon < 0) continue; // pure component node

                string cid = nodeId.Substring(0, colon);
                string portName = nodeId.Substring(colon + 1);
                Component component;
                if (!solvedNetwork.Components.TryGetValue(cid, out component))
                    throw new WentaException("critical path references unknown component '" + cid + "'");
                Port port = component.Port_(portName);

                double dp = solvedNetwork.NodeDp(nodeId);
                cumulative += dp;
                report.Rows.Add(new PressureReportRow
                {
                    ComponentId = cid,
                    Kind = component.GetType().Name,
                    Port = portName,
                    FlowM3s = port.Flowrate,
                    // A port's velocity counts as "set" once it has a flowrate
                    // (same convention as Results.ExtractResults).
                    VelocityMs = port.Flowrate.HasValue ? port.Velocity : null,
                    DpPa = dp,
                    CumulativeDpPa = cumulative,
                });
            }

            report.CriticalDpPa = cumulative;
            foreach (PressureReportRow row in report.Rows)
                row.SharePercent = cumulative != 0.0 ? row.DpPa / cumulative * 100.0 : 0.0;
            return report;
        }

        /// <summary><see cref="Solver.CriticalPath"/> ends at the *first* node
        /// (in topological order) that reaches the maximum cumulative drop,
        /// so it stops short of the terminal whenever the trailing nodes add
        /// 0 Pa (e.g. a duct outlet, a terminal with no cross-section). Every
        /// node downstream of that end carries 0 Pa (otherwise it would have
        /// been the maximum), so following successors to the nearest
        /// <see cref="Terminal"/> yields a source→terminal path with the same
        /// total. Breadth-first over successors, insertion-order tie-break;
        /// leaves the path unchanged when no terminal is reachable.</summary>
        private static void ExtendToTerminal(Network network, List<string> path)
        {
            if (path.Count == 0) return;
            string end = path[path.Count - 1];
            if (IsTerminalNode(network, end)) return;

            var prev = new Dictionary<string, string>();
            var queue = new Queue<string>();
            prev[end] = null;
            queue.Enqueue(end);
            string found = null;
            while (queue.Count > 0 && found == null)
            {
                string n = queue.Dequeue();
                foreach (string s in network.Successors(n))
                {
                    if (prev.ContainsKey(s)) continue;
                    prev[s] = n;
                    if (IsTerminalNode(network, s)) { found = s; break; }
                    queue.Enqueue(s);
                }
            }
            if (found == null) return;

            var tail = new List<string>();
            for (string cur = found; cur != end; cur = prev[cur])
                tail.Add(cur);
            tail.Reverse();
            path.AddRange(tail);
        }

        private static bool IsTerminalNode(Network network, string nodeId)
        {
            Component c;
            return network.Components.TryGetValue(nodeId, out c) && c is Terminal;
        }

        /// <summary>Fixed-width, human-readable table (header, one line per
        /// row, then a closing <c>critical path ΔP = N Pa</c> line).</summary>
        public string ToText()
        {
            string[] headerLabels = { "Component", "Kind", "Port", "Q [m³/s]", "V [m/s]", "ΔP [Pa]", "Σ ΔP [Pa]", "Share [%]" };
            int[] headerWidths = { 12, 14, 10, 10, 8, 9, 10, 9 };

            var headerParts = new string[headerLabels.Length];
            for (int i = 0; i < headerLabels.Length; i++)
                headerParts[i] = headerLabels[i].PadRight(headerWidths[i]);
            string header = string.Join(" | ", headerParts);
            string sepLine = new string('-', header.Length);

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(NetworkName))
                lines.Add("network: " + NetworkName);
            lines.Add(sepLine);
            lines.Add(header);
            lines.Add(sepLine);
            foreach (PressureReportRow r in Rows)
            {
                string q = r.FlowM3s.HasValue
                    ? r.FlowM3s.Value.ToString("0.000", CultureInfo.InvariantCulture)
                    : "—";
                string v = r.VelocityMs.HasValue
                    ? r.VelocityMs.Value.ToString("0.00", CultureInfo.InvariantCulture)
                    : "—";
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} | {1,-14} | {2,-10} | {3,10} | {4,8} | {5,9} | {6,10} | {7,9}",
                    r.ComponentId, r.Kind, r.Port, q, v,
                    r.DpPa.ToString("0.00", CultureInfo.InvariantCulture),
                    r.CumulativeDpPa.ToString("0.00", CultureInfo.InvariantCulture),
                    r.SharePercent.ToString("0.0", CultureInfo.InvariantCulture)));
            }
            lines.Add(sepLine);
            lines.Add("critical path ΔP = "
                + CriticalDpPa.ToString("0.00", CultureInfo.InvariantCulture) + " Pa");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>CSV with header
        /// <c>component_id,kind,port,flow_m3s,velocity_ms,dp_pa,cumulative_dp_pa,share_percent</c>,
        /// invariant culture, no trailing newline (same style as
        /// <see cref="Results.ResultsAsCsv"/>).</summary>
        public string ToCsv(char delimiter = ',')
        {
            string sep = delimiter.ToString();
            var lines = new List<string> { string.Join(sep, Fields) };
            foreach (PressureReportRow r in Rows)
            {
                lines.Add(string.Join(sep, new[]
                {
                    r.ComponentId,
                    r.Kind,
                    r.Port,
                    Results.FmtOpt(r.FlowM3s),
                    Results.FmtOpt(r.VelocityMs),
                    r.DpPa.ToString(CultureInfo.InvariantCulture),
                    r.CumulativeDpPa.ToString(CultureInfo.InvariantCulture),
                    r.SharePercent.ToString(CultureInfo.InvariantCulture),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }
    }
}
