using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>A single marking row for a ducted component.</summary>
    public sealed class Mark
    {
        /// <summary>Branch number assigned by downstream BFS from the source
        /// (<c>1, 2, …</c>).</summary>
        public uint BranchNo;
        /// <summary>Component id within the network.</summary>
        public string ComponentId;
        /// <summary>Component kind, e.g. <c>"RigidDuct"</c>
        /// (<c>Component.GetType().Name</c>).</summary>
        public string Kind;
        /// <summary>Hydraulic diameter in millimetres, rounded to the nearest
        /// mm (non-null for ducts).</summary>
        public double? SizeMm;
        /// <summary>Volumetric flow in m^3/s from the solved results, if
        /// available.</summary>
        public double? FlowM3s;
    }

    /// <summary>Duct marking: branch numbering and per-duct ID marks.
    /// Port of `venti/src/marking.rs`.
    ///
    /// <see cref="AssignBranchMarks"/> walks a duct network downstream from
    /// its source(s) using a breadth-first search and assigns a deterministic
    /// branch number to every <see cref="RigidDuct"/>. Branch numbers start at
    /// 1 and increment for each duct encountered, so a simple chain yields
    /// <c>1, 2, 3, …</c> and the two legs split at a tee receive distinct
    /// branch numbers. Each mark carries the component id, kind, the duct's
    /// rounded hydraulic diameter in mm and, when the network has been
    /// solved, the flow in m^3/s.</summary>
    public static class Marking
    {
        /// <summary>Assign a deterministic branch number to every
        /// <see cref="RigidDuct"/> in <paramref name="network"/>.
        ///
        /// The traversal is a breadth-first search seeded from every
        /// <see cref="Source"/> and follows connection edges downstream. Each
        /// rigid duct is assigned the next branch number in discovery order.
        /// Neighbours discovered at the same step are sorted and de-duplicated
        /// so the numbering is stable regardless of iteration order over the
        /// network's internal collections. Returns an empty list for an empty
        /// network.
        ///
        /// <paramref name="network"/>'s successors are derived from the
        /// public <see cref="Network.Predecessors"/> adjacency (there is no
        /// public forward-adjacency accessor), inverted once up front so the
        /// BFS itself runs in linear time.
        ///
        /// <c>SizeMm</c> is the duct's hydraulic diameter expressed in
        /// millimetres and rounded to the nearest integer millimetre. It is
        /// read directly from <see cref="RigidDuct.CrossSection"/>'s
        /// <c>HydraulicDiameter</c>: for a circular duct the hydraulic
        /// diameter equals the physical diameter, so using it directly
        /// reproduces the round-duct diameter while also remaining sensible
        /// for any cross-section via the equivalent-diameter definition.
        /// <c>FlowM3s</c> is read from the duct's inlet port, so it is
        /// non-null only after the network has been solved / flowrates
        /// propagated.</summary>
        public static List<Mark> AssignBranchMarks(Network network)
        {
            var marks = new List<Mark>();
            if (network.Components.Count == 0)
                return marks;

            // Forward adjacency (node id -> successor node ids), built once
            // from the public Predecessors accessor since Network does not
            // expose a public Successors map.
            var succ = BuildSuccessors(network);

            // Deterministic BFS seed: source component ids, sorted.
            var seeds = new List<string>();
            foreach (var kv in network.Components)
                if (kv.Value is Source) seeds.Add(kv.Key);
            seeds.Sort(StringComparer.Ordinal);

            var visited = new HashSet<string>();
            var queue = new Queue<string>(seeds);
            uint nextBranch = 1;

            while (queue.Count > 0)
            {
                string cid = queue.Dequeue();
                if (!visited.Add(cid)) continue;

                Component component;
                if (!network.Components.TryGetValue(cid, out component)) continue;

                RigidDuct duct = component as RigidDuct;
                if (duct != null)
                {
                    marks.Add(new Mark
                    {
                        BranchNo = nextBranch,
                        ComponentId = cid,
                        Kind = component.GetType().Name,
                        SizeMm = Math.Round(duct.CrossSection.HydraulicDiameter * 1000.0),
                        FlowM3s = InletFlow(component),
                    });
                    nextBranch++;
                }

                // Discover downstream components through this component's
                // outlet ports.
                var downstream = new List<string>();
                foreach (Port port in component.Outlets)
                {
                    string pid = Network.PortNodeId(cid, port.Name);
                    List<string> neighbours;
                    if (succ.TryGetValue(pid, out neighbours))
                    {
                        foreach (string node in neighbours)
                        {
                            int colon = node.IndexOf(':');
                            if (colon < 0) continue;
                            string ncid = node.Substring(0, colon);
                            if (ncid != cid && !visited.Contains(ncid))
                                downstream.Add(ncid);
                        }
                    }
                }
                downstream.Sort(StringComparer.Ordinal);
                string prev = null;
                foreach (string ncid in downstream)
                {
                    if (ncid == prev) continue; // de-duplicate consecutive
                    prev = ncid;
                    queue.Enqueue(ncid);
                }
            }

            return marks;
        }

        /// <summary>Render marks as a comma-separated values string with a
        /// header row: <c>branch_no,component_id,kind,size_mm,flow_m3s</c>.</summary>
        public static string MarksAsCsv(IList<Mark> marks)
        {
            var lines = new List<string> { "branch_no,component_id,kind,size_mm,flow_m3s" };
            foreach (Mark m in marks)
            {
                lines.Add(string.Join(",", new[]
                {
                    m.BranchNo.ToString(CultureInfo.InvariantCulture),
                    m.ComponentId,
                    m.Kind,
                    FmtOpt(m.SizeMm),
                    FmtOpt(m.FlowM3s),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Flowrate on the component's first inlet port, if the
        /// network was solved.</summary>
        private static double? InletFlow(Component component)
        {
            foreach (Port p in component.Inlets)
                return p.Flowrate;
            return null;
        }

        /// <summary>Format an optional double (empty string for null).</summary>
        private static string FmtOpt(double? v)
        {
            return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// <summary>Invert <see cref="Network.Predecessors"/> over every node
        /// in the network (components and ports alike) into a forward
        /// adjacency map, mirroring the Rust `Network::successors`.</summary>
        private static Dictionary<string, List<string>> BuildSuccessors(Network network)
        {
            var succ = new Dictionary<string, List<string>>();
            foreach (var kv in network.Components)
            {
                AddSuccessorsFor(network, kv.Key, succ);
                foreach (Port p in kv.Value.Ports)
                    AddSuccessorsFor(network, p.NodeId, succ);
            }
            return succ;
        }

        private static void AddSuccessorsFor(Network network, string nodeId,
            Dictionary<string, List<string>> succ)
        {
            foreach (string pred in network.Predecessors(nodeId))
            {
                List<string> list;
                if (!succ.TryGetValue(pred, out list))
                {
                    list = new List<string>();
                    succ[pred] = list;
                }
                list.Add(nodeId);
            }
        }
    }
}
