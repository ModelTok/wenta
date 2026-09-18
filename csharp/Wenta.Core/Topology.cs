using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Host-agnostic geometry &amp; topology — the M3 "trace" and "draw"
    /// core. Port of `venti/src/topology.rs`.
    ///
    /// Turns 2D duct centreline polylines into a <see cref="Wenta.Network"/> and
    /// back, with no CAD dependency (works headless). A thin CAD adapter later
    /// maps ZWCAD entities to polylines/segments; all topology math lives here.
    ///
    /// <list type="bullet">
    /// <item><see cref="Topology.Trace"/> — coalesce polylines into a graph,
    /// split at junctions/endpoints, and build a Network (Source / RigidDuct /
    /// Tee / Terminal) with flow rooted at one source endpoint.</item>
    /// <item><see cref="TracedSystem.Flatten"/> — project back into drawable
    /// <see cref="DuctSegment"/> primitives (defined in Geometry.cs; mirrors
    /// this module's Rust `Segment` type and is shared with clash detection).</item>
    /// </list>
    ///
    /// Scope: round ducts, one source, no closed loops (a tree). Supported:
    /// degree-1 endpoints (source / terminal) and degree-3 tees. Degree &gt;= 4
    /// and cycles are rejected. Tee "straight/branch" legs are assigned by
    /// traversal order (geometry-accurate alignment can be layered on later);
    /// connectivity — and therefore the solved pressure drops — is exact.</summary>

    /// <summary>A polyline duct centreline (metres). Consecutive points are
    /// joined.</summary>
    public sealed class Polyline
    {
        public readonly List<Point2> Points;

        public Polyline(List<Point2> points)
        {
            Points = points;
        }
    }

    /// <summary>Options controlling <see cref="Topology.Trace"/>.</summary>
    public sealed class TraceOptions
    {
        /// <summary>Coalescing tolerance for shared endpoints [m].</summary>
        public double Snap = 1e-4;
        /// <summary>Default round duct diameter [m].</summary>
        public double DefaultDiameter = 0.2;
        /// <summary>Diameter [m] per chain id (overrides default).</summary>
        public Dictionary<string, double> Diameters = new Dictionary<string, double>();
        /// <summary>Terminal flowrates [m^3/s] per terminal id (0 if absent).</summary>
        public Dictionary<string, double> Flows = new Dictionary<string, double>();
    }

    /// <summary>A maximal straight duct chain and its geometry.</summary>
    public sealed class Chain
    {
        public readonly string Id;
        public readonly List<Point2> Points;
        public readonly double LengthM;
        public readonly double Diameter;

        public Chain(string id, List<Point2> points, double lengthM, double diameter)
        {
            Id = id;
            Points = points;
            LengthM = lengthM;
            Diameter = diameter;
        }
    }

    /// <summary>The result of tracing: a network plus the geometry needed to
    /// draw it.</summary>
    public sealed class TracedSystem
    {
        public readonly Network Network;
        public readonly List<Chain> Chains;

        public TracedSystem(Network network, List<Chain> chains)
        {
            Network = network;
            Chains = chains;
        }

        /// <summary>Project the traced ducts into drawable
        /// <see cref="DuctSegment"/> primitives.</summary>
        public List<DuctSegment> Flatten()
        {
            List<DuctSegment> segments = new List<DuctSegment>(Chains.Count);
            foreach (Chain c in Chains)
            {
                segments.Add(new DuctSegment(
                    c.Id, c.Points[0], c.Points[c.Points.Count - 1], c.Diameter));
            }
            return segments;
        }

        /// <summary>Total traced straight ductwork length [m].</summary>
        public double TotalLengthM()
        {
            double sum = 0.0;
            foreach (Chain c in Chains) sum += c.LengthM;
            return sum;
        }
    }

    /// <summary>Trace 2D polylines into a <see cref="Network"/>, and flatten a
    /// traced network back to drawable segments. Port of
    /// `venti/src/topology.rs`.</summary>
    public static class Topology
    {
        // A vertex in the coalesced polyline graph.
        private sealed class Vertex
        {
            public readonly Point2 Point;
            public int Degree;

            public Vertex(Point2 point)
            {
                Point = point;
                Degree = 0;
            }
        }

        // Generic (int, int) pair used throughout this file in place of tuple
        // syntax: undirected edges, chain endpoints, (chainIndex, otherVertex)
        // junction-adjacency entries, and (upstreamVid, downstreamVid) chain
        // directions.
        private struct IntPair
        {
            public readonly int A;
            public readonly int B;

            public IntPair(int a, int b)
            {
                A = a;
                B = b;
            }
        }

        // Grid cell key for vertex snapping; avoids tuple keys in the cache.
        private struct GridKey : IEquatable<GridKey>
        {
            public readonly long X;
            public readonly long Y;

            public GridKey(long x, long y)
            {
                X = x;
                Y = y;
            }

            public bool Equals(GridKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj) { return obj is GridKey && Equals((GridKey)obj); }
            public override int GetHashCode() { unchecked { return (int)(X * 397 ^ Y); } }
        }

        // Deterministic junction-adjacency order: by chain index.
        private static readonly Comparison<IntPair> ByChainIndex =
            delegate (IntPair x, IntPair y) { return x.A.CompareTo(y.A); };

        // Coalesce a point to an existing vertex within `snap`, or create a
        // new one. Mirrors the 3x3 neighbour-cell probe in the Rust original.
        private static int SnapVertex(Point2 p, double snap, List<Vertex> verts,
            Dictionary<GridKey, int> cache)
        {
            long kx = (long)(p.X / snap);
            long ky = (long)(p.Y / snap);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    int existing;
                    if (cache.TryGetValue(new GridKey(kx + dx, ky + dy), out existing))
                    {
                        if (verts[existing].Point.DistanceSquaredTo(p) <= snap * snap)
                            return existing;
                    }
                }
            }
            int id = verts.Count;
            verts.Add(new Vertex(p));
            cache[new GridKey(kx, ky)] = id;
            return id;
        }

        private static long EncodePair(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        private static Dictionary<int, string> GetOrAddTeeLeg(
            Dictionary<int, Dictionary<int, string>> teeLeg, int teeVid)
        {
            Dictionary<int, string> legs;
            if (!teeLeg.TryGetValue(teeVid, out legs))
            {
                legs = new Dictionary<int, string>();
                teeLeg[teeVid] = legs;
            }
            return legs;
        }

        private static void AddJAdj(Dictionary<int, List<IntPair>> jadj, int vertex, IntPair entry)
        {
            List<IntPair> list;
            if (!jadj.TryGetValue(vertex, out list))
            {
                list = new List<IntPair>();
                jadj[vertex] = list;
            }
            list.Add(entry);
        }

        /// <summary>Trace 2D polylines into a <see cref="TracedSystem"/>.</summary>
        /// <exception cref="WentaException">The input has no usable geometry,
        /// contains a degree &gt;= 4 junction, has fewer than one source and
        /// one terminal end, or a junction is unreachable from the source
        /// (loop / unsupported topology).</exception>
        public static TracedSystem Trace(IList<Polyline> polylines, TraceOptions opts)
        {
            // Null, empty, or nothing with >= 2 points -> unusable.
            bool anyUsable = false;
            if (polylines != null)
            {
                foreach (Polyline p in polylines)
                {
                    if (p.Points.Count >= 2) { anyUsable = true; break; }
                }
            }
            if (!anyUsable)
                throw new WentaException("no usable polylines");

            // 1. Coalesce vertices and build an undirected adjacency.
            List<Vertex> verts = new List<Vertex>();
            Dictionary<GridKey, int> cache = new Dictionary<GridKey, int>();
            List<IntPair> edges = new List<IntPair>();

            foreach (Polyline poly in polylines)
            {
                if (poly.Points.Count < 2) continue;
                int prev = SnapVertex(poly.Points[0], opts.Snap, verts, cache);
                for (int i = 1; i < poly.Points.Count; i++)
                {
                    int cur = SnapVertex(poly.Points[i], opts.Snap, verts, cache);
                    if (cur != prev) edges.Add(new IntPair(prev, cur));
                    prev = cur;
                }
            }
            // (anyUsable guarantees at least one SnapVertex call, so verts is
            // non-empty here.)

            // 2. Degrees + adjacency.
            int n = verts.Count;
            List<int>[] adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (IntPair e in edges)
            {
                verts[e.A].Degree++;
                verts[e.B].Degree++;
                adj[e.A].Add(e.B);
                adj[e.B].Add(e.A);
            }

            // Reject unsupported geometry.
            for (int i = 0; i < n; i++)
            {
                if (verts[i].Degree >= 4)
                    throw new WentaException("vertex " + i + " has degree " + verts[i].Degree
                        + "; only tees (degree 3) are supported");
            }

            // 3. Degree-1 ends; pick one source, the rest are terminals.
            List<int> ends = new List<int>();
            for (int i = 0; i < n; i++)
                if (verts[i].Degree == 1) ends.Add(i);
            if (ends.Count < 2)
                throw new WentaException("a network needs at least one source and one terminal end");
            int sourceV = ends[0];

            // 4. Enumerate maximal chains between junction/end vertices (degree != 2).
            List<Chain> chains = new List<Chain>();
            List<IntPair> chainEnds = new List<IntPair>();
            HashSet<long> seen = new HashSet<long>(); // ordered (from,to)
            int chainId = 0;

            for (int v = 0; v < n; v++)
            {
                if (verts[v].Degree == 2) continue;
                foreach (int nb in adj[v])
                {
                    if (seen.Contains(EncodePair(v, nb))) continue;

                    List<Point2> pathPts = new List<Point2> { verts[v].Point };
                    int cur = v;
                    int nxt = nb;
                    int reached = -1; // junction/end vertex the chain arrives at
                    while (true)
                    {
                        if (!seen.Add(EncodePair(cur, nxt))) break;
                        seen.Add(EncodePair(nxt, cur)); // undirected visit
                        pathPts.Add(verts[nxt].Point);
                        if (verts[nxt].Degree != 2)
                        {
                            reached = nxt;
                            break;
                        }
                        // advance through the degree-2 vertex: its single
                        // neighbour other than the one we came from
                        int next = -1;
                        foreach (int t in adj[nxt]) if (t != cur) { next = t; break; }
                        if (next < 0) break;
                        cur = nxt;
                        nxt = next;
                    }
                    if (reached >= 0)
                    {
                        string id = "duct" + chainId;
                        chainId++;
                        double diameter;
                        if (!opts.Diameters.TryGetValue(id, out diameter))
                            diameter = opts.DefaultDiameter;
                        double lengthM = 0.0;
                        for (int i = 1; i < pathPts.Count; i++)
                            lengthM += pathPts[i - 1].DistanceTo(pathPts[i]);
                        chains.Add(new Chain(id, pathPts, lengthM, diameter));
                        chainEnds.Add(new IntPair(v, reached));
                    }
                }
            }

            // 5. Root a BFS from the source over the junction graph (junctions
            //    = degree != 2 vertices) to assign flow direction and tee leg
            //    ports.
            // junction -> [(chainIdx, otherJunctionVid)]
            Dictionary<int, List<IntPair>> jadj = new Dictionary<int, List<IntPair>>();
            for (int ci = 0; ci < chainEnds.Count; ci++)
            {
                int a = chainEnds[ci].A, b = chainEnds[ci].B;
                AddJAdj(jadj, a, new IntPair(ci, b));
                AddJAdj(jadj, b, new IntPair(ci, a));
            }

            // chain -> (upstreamVid, downstreamVid)
            Dictionary<int, IntPair> dir = new Dictionary<int, IntPair>();
            // teeVid -> (chainIdx -> port name among {combined, straight, branch})
            Dictionary<int, Dictionary<int, string>> teeLeg = new Dictionary<int, Dictionary<int, string>>();

            HashSet<int> visited = new HashSet<int>();
            Queue<IntPair> queue = new Queue<IntPair>(); // (vertex, cameInChainIdx or -1)
            queue.Enqueue(new IntPair(sourceV, -1));
            visited.Add(sourceV);
            while (queue.Count > 0)
            {
                IntPair item = queue.Dequeue();
                int jv = item.A;
                int cameIn = item.B; // -1 == None

                List<IntPair> incident;
                if (!jadj.TryGetValue(jv, out incident)) incident = new List<IntPair>();
                List<IntPair> downstream = new List<IntPair>();
                foreach (IntPair inc in incident)
                    if (inc.A != cameIn) downstream.Add(inc);
                downstream.Sort(ByChainIndex);

                for (int k = 0; k < downstream.Count; k++)
                {
                    int ci = downstream[k].A;
                    int other = downstream[k].B;
                    if (visited.Contains(other)) continue;
                    dir[ci] = new IntPair(jv, other);
                    // if this junction is a tee, classify its legs
                    if (verts[jv].Degree == 3)
                    {
                        Dictionary<int, string> legs = GetOrAddTeeLeg(teeLeg, jv);
                        if (cameIn != -1)
                            legs[cameIn] = "combined";
                        legs[ci] = (k == 0) ? "straight" : "branch";
                    }
                    visited.Add(other);
                    queue.Enqueue(new IntPair(other, ci));
                }
            }

            // Reject unreachable junctions (would indicate a loop / unsupported input).
            for (int v = 0; v < n; v++)
            {
                if (verts[v].Degree != 2 && !visited.Contains(v))
                    throw new WentaException(
                        "junction vertex " + v + " is unreachable from the source (loop/unsupported)");
            }

            // 6. Build the network.
            Network net = new Network();
            net.Name = "traced";
            string sourceId = "src";
            net.Add(sourceId, new Source("source"));

            // terminals: degree-1 ends other than the source
            Dictionary<int, string> terminalOf = new Dictionary<int, string>();
            foreach (int e in ends)
            {
                if (e == sourceV) continue;
                string tid = "term" + terminalOf.Count;
                double flow;
                if (!opts.Flows.TryGetValue(tid, out flow)) flow = 0.0;
                terminalOf[e] = tid;
                net.Add(tid, new Terminal(tid, flow, null, 0.0));
            }

            // tees: degree-3 junctions
            Dictionary<int, string> teeOf = new Dictionary<int, string>();
            for (int i = 0; i < n; i++)
            {
                if (verts[i].Degree == 3)
                {
                    string t = "tee" + teeOf.Count;
                    teeOf[i] = t;
                    Round r = new Round(opts.DefaultDiameter);
                    net.Add(t, new Tee(t, r, 0.0, 0.5));
                }
            }

            // ducts + connections
            // For each chain, `up` is the junction that feeds it, `down` the
            // junction it feeds. A tee's leg roles:
            //   - the chain feeding INTO a tee connects to its `combined` (In) port;
            //   - chains LEAVING a tee connect from its `straight`/`branch` (Out) legs.
            for (int ci = 0; ci < chains.Count; ci++)
            {
                Chain ch = chains[ci];
                IntPair d;
                if (!dir.TryGetValue(ci, out d))
                    throw new WentaException("chain direction missing");
                int up = d.A, down = d.B;

                Round rd = new Round(ch.Diameter);
                net.Add(ch.Id, new RigidDuct(ch.Id, rd, ch.LengthM, RigidDuct.DefaultAbsoluteRoughness));

                string ductIn = ch.Id + ".inlet";
                string ductOut = ch.Id + ".outlet";

                // --- upstream leg (what feeds this duct) ---
                string upTerm;
                if (terminalOf.TryGetValue(up, out upTerm))
                    throw new WentaException("upstream end " + upTerm + " is a terminal (invalid tree)");

                string upTee;
                if (teeOf.TryGetValue(up, out upTee))
                {
                    // this chain leaves the tee via one of its Out legs
                    Dictionary<int, string> legs;
                    string port = null;
                    if (teeLeg.TryGetValue(up, out legs))
                        legs.TryGetValue(ci, out port);
                    if (port == null)
                        throw new WentaException(
                            "tee " + upTee + " has no leg assigned for chain " + ch.Id);
                    net.Connect(upTee + "." + port, ductIn);
                }
                else
                {
                    // degree-1 that isn't a terminal => the source root
                    net.Connect(sourceId, ductIn);
                }

                // --- downstream leg (what this duct feeds) ---
                string downTerm, downTee;
                if (terminalOf.TryGetValue(down, out downTerm))
                    net.Connect(ductOut, downTerm);
                else if (teeOf.TryGetValue(down, out downTee))
                    net.Connect(ductOut, downTee + ".combined"); // feeds the tee's In leg
                else
                    throw new WentaException("downstream end resolved to the source (invalid tree)");
            }

            return new TracedSystem(net, chains);
        }
    }
}
