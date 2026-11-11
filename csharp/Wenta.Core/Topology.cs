using System;
using System.Collections.Generic;

namespace Wenta
{

    public sealed class Polyline
    {
        public readonly List<Point2> Points;

        public Polyline(List<Point2> points)
        {
            Points = points;
        }
    }

    public sealed class TraceOptions
    {
        public double Snap = 1e-4;
        public double DefaultDiameter = 0.2;
        public Dictionary<string, double> Diameters = new Dictionary<string, double>();
        public Dictionary<string, double> Flows = new Dictionary<string, double>();
        public Dictionary<string, CrossSection> Sections = new Dictionary<string, CrossSection>();
        public Point2? SourceAt;
        public bool TeeSectionFromUpstream;
        // When set, the tee's straight leg is the downstream run that best
        // continues the direction of the incoming run (drawing order no
        // longer matters); otherwise the first run in chain order is
        // straight, as in venti::topology.
        public bool StraightByDirection;
    }

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

    public sealed class TracedSystem
    {
        public readonly Network Network;
        public readonly List<Chain> Chains;
        public Point2 SourcePoint;
        public readonly Dictionary<string, Point2> TerminalPoints = new Dictionary<string, Point2>();
        public readonly Dictionary<string, Point2> TeePoints = new Dictionary<string, Point2>();

        public TracedSystem(Network network, List<Chain> chains)
        {
            Network = network;
            Chains = chains;
        }

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

        public double TotalLengthM()
        {
            double sum = 0.0;
            foreach (Chain c in Chains) sum += c.LengthM;
            return sum;
        }
    }

    public static class Topology
    {
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

        // Unit direction of a chain at junction vertex jv: flowing into jv
        // when incoming, flowing out of jv otherwise.
        private static Point2 EndDirection(Chain ch, IntPair ends, int jv, bool incoming)
        {
            List<Point2> p = ch.Points;
            Point2 a, b;
            if (ends.B == jv) { a = p[p.Count - 2]; b = p[p.Count - 1]; }
            else { a = p[1]; b = p[0]; }
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (!incoming) { dx = -dx; dy = -dy; }
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len > 0.0 ? new Point2(dx / len, dy / len) : new Point2(0.0, 0.0);
        }

        private static readonly Comparison<IntPair> ByChainIndex =
            delegate (IntPair x, IntPair y) { return x.A.CompareTo(y.A); };

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

        public static TracedSystem Trace(IList<Polyline> polylines, TraceOptions opts)
        {
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

            for (int i = 0; i < n; i++)
            {
                if (verts[i].Degree >= 4)
                    throw new WentaException("vertex " + i + " has degree " + verts[i].Degree
                        + "; only tees (degree 3) are supported");
            }

            List<int> ends = new List<int>();
            for (int i = 0; i < n; i++)
                if (verts[i].Degree == 1) ends.Add(i);
            if (ends.Count < 2)
                throw new WentaException("a network needs at least one source and one terminal end");
            int sourceV = ends[0];
            if (opts.SourceAt.HasValue)
            {
                double best = double.PositiveInfinity;
                foreach (int e in ends)
                {
                    double d2 = verts[e].Point.DistanceSquaredTo(opts.SourceAt.Value);
                    if (d2 < best) { best = d2; sourceV = e; }
                }
            }

            List<Chain> chains = new List<Chain>();
            List<IntPair> chainEnds = new List<IntPair>();
            HashSet<long> seen = new HashSet<long>();
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
                    int reached = -1;
                    while (true)
                    {
                        if (!seen.Add(EncodePair(cur, nxt))) break;
                        seen.Add(EncodePair(nxt, cur));
                        pathPts.Add(verts[nxt].Point);
                        if (verts[nxt].Degree != 2)
                        {
                            reached = nxt;
                            break;
                        }
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

            Dictionary<int, List<IntPair>> jadj = new Dictionary<int, List<IntPair>>();
            for (int ci = 0; ci < chainEnds.Count; ci++)
            {
                int a = chainEnds[ci].A, b = chainEnds[ci].B;
                AddJAdj(jadj, a, new IntPair(ci, b));
                AddJAdj(jadj, b, new IntPair(ci, a));
            }

            Dictionary<int, IntPair> dir = new Dictionary<int, IntPair>();
            Dictionary<int, Dictionary<int, string>> teeLeg = new Dictionary<int, Dictionary<int, string>>();

            HashSet<int> visited = new HashSet<int>();
            Queue<IntPair> queue = new Queue<IntPair>();
            queue.Enqueue(new IntPair(sourceV, -1));
            visited.Add(sourceV);
            while (queue.Count > 0)
            {
                IntPair item = queue.Dequeue();
                int jv = item.A;
                int cameIn = item.B;

                List<IntPair> incident;
                if (!jadj.TryGetValue(jv, out incident)) incident = new List<IntPair>();
                List<IntPair> downstream = new List<IntPair>();
                foreach (IntPair inc in incident)
                    if (inc.A != cameIn) downstream.Add(inc);
                downstream.Sort(ByChainIndex);
                if (opts.StraightByDirection && cameIn != -1 && downstream.Count > 1)
                {
                    Point2 inDir = EndDirection(chains[cameIn], chainEnds[cameIn], jv, true);
                    int best = 0;
                    double bestCos = double.NegativeInfinity;
                    for (int k = 0; k < downstream.Count; k++)
                    {
                        int dc = downstream[k].A;
                        Point2 outDir = EndDirection(chains[dc], chainEnds[dc], jv, false);
                        double cos = inDir.X * outDir.X + inDir.Y * outDir.Y;
                        if (cos > bestCos + 1e-12) { bestCos = cos; best = k; }
                    }
                    if (best != 0)
                    {
                        IntPair straight = downstream[best];
                        downstream.RemoveAt(best);
                        downstream.Insert(0, straight);
                    }
                }

                for (int k = 0; k < downstream.Count; k++)
                {
                    int ci = downstream[k].A;
                    int other = downstream[k].B;
                    if (visited.Contains(other)) continue;
                    dir[ci] = new IntPair(jv, other);
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

            for (int v = 0; v < n; v++)
            {
                if (verts[v].Degree != 2 && !visited.Contains(v))
                    throw new WentaException(
                        "junction vertex " + v + " is unreachable from the source (loop/unsupported)");
            }

            Network net = new Network();
            net.Name = "traced";
            string sourceId = "src";
            net.Add(sourceId, new Source("source"));

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

            Dictionary<int, string> teeOf = new Dictionary<int, string>();
            for (int i = 0; i < n; i++)
            {
                if (verts[i].Degree == 3)
                {
                    string t = "tee" + teeOf.Count;
                    teeOf[i] = t;
                    CrossSection r = new Round(opts.DefaultDiameter);
                    if (opts.TeeSectionFromUpstream)
                    {
                        foreach (KeyValuePair<int, IntPair> kv in dir)
                        {
                            if (kv.Value.B != i) continue;
                            r = ChainSection(chains[kv.Key], opts);
                            break;
                        }
                    }
                    net.Add(t, new Tee(t, r, 0.0, 0.5));
                }
            }

            for (int ci = 0; ci < chains.Count; ci++)
            {
                Chain ch = chains[ci];
                IntPair d;
                if (!dir.TryGetValue(ci, out d))
                    throw new WentaException("chain direction missing");
                int up = d.A, down = d.B;

                net.Add(ch.Id, new RigidDuct(ch.Id, ChainSection(ch, opts), ch.LengthM,
                    RigidDuct.DefaultAbsoluteRoughness));

                string ductIn = ch.Id + ".inlet";
                string ductOut = ch.Id + ".outlet";

                string upTerm;
                if (terminalOf.TryGetValue(up, out upTerm))
                    throw new WentaException("upstream end " + upTerm + " is a terminal (invalid tree)");

                string upTee;
                if (teeOf.TryGetValue(up, out upTee))
                {
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
                    net.Connect(sourceId, ductIn);
                }

                string downTerm, downTee;
                if (terminalOf.TryGetValue(down, out downTerm))
                    net.Connect(ductOut, downTerm);
                else if (teeOf.TryGetValue(down, out downTee))
                    net.Connect(ductOut, downTee + ".combined");
                else
                    throw new WentaException("downstream end resolved to the source (invalid tree)");
            }

            TracedSystem traced = new TracedSystem(net, chains);
            traced.SourcePoint = verts[sourceV].Point;
            foreach (KeyValuePair<int, string> kv in terminalOf)
                traced.TerminalPoints[kv.Value] = verts[kv.Key].Point;
            foreach (KeyValuePair<int, string> kv in teeOf)
                traced.TeePoints[kv.Value] = verts[kv.Key].Point;
            return traced;
        }

        private static CrossSection ChainSection(Chain ch, TraceOptions opts)
        {
            CrossSection section;
            if (opts.Sections.TryGetValue(ch.Id, out section) && section != null)
                return section;
            return new Round(ch.Diameter);
        }
    }
}
