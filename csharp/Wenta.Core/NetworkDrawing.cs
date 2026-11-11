using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    // Network <-> drawing round trip (#20, #22), host-agnostic half of
    // WENTAWRITE and WENTAREAD. Every component becomes one entity: ducts
    // as centrelines drawn to scale, the other components as point
    // markers. The entity's WENTA tag carries the whole component record
    // (id, type, parameters, upstream connections), so the drawing can be
    // read back without re-tracing and re-written in place by GUID. No
    // engineering math lives here.

    public sealed class DrawnComponent
    {
        public string Handle;
        public WentaTag Tag;
        public List<Point2> PointsMm;   // duct centreline vertices, or null
        public Point2? AtMm;            // point component position, or null
        public string Label;            // annotation text (writer only)
    }

    public sealed class NetworkReadResult
    {
        public Network Network;
        public readonly Dictionary<string, ComponentMeta> Meta = new Dictionary<string, ComponentMeta>();
        public readonly Dictionary<string, string> HandleOf = new Dictionary<string, string>();
        public readonly List<string> Errors = new List<string>();
        // Component ids whose length was taken from edited geometry.
        public readonly List<string> LengthUpdated = new List<string>();
        // WENTA tags that are not component records (WENTATAG markers,
        // traced-duct tags, labels); they are left alone.
        public int Ignored;

        public bool Ok { get { return Errors.Count == 0; } }
    }

    public static class NetworkDrawing
    {
        public const string LayerDuct = "WENTA-DUCT";
        public const string LayerAnno = "WENTA-ANNO";

        public const string ClassFlex = "duct:flex";
        public const string ClassTee = "fitting:tee";
        public const string ClassFitting = "fitting";
        public const string ClassAttenuator = "attenuator";

        // A drawn length that differs from the stored one by more than this
        // is an edit: the reader takes the drawn length and reports it.
        public const double LengthToleranceMm = 0.5;

        // Vertical spacing between the layouts of separate sources.
        public const double SourceSpacingMm = 20000.0;

        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static readonly string[] RecordKeys =
        {
            "cid", "type", "name", "scope", "catalog", "provenance",
            "shape", "d_mm", "w_mm", "h_mm", "d_m", "w_m", "h_m",
            "length_m", "roughness_m", "dp_pa_m", "stretch_pct",
            "flow_m3s", "zeta", "zeta_straight", "zeta_branch",
        };

        public static bool IsDuct(Component c)
        {
            return c is RigidDuct || c is FlexDuct;
        }

        public static bool IsRecord(WentaTag tag)
        {
            return tag != null && !tag.Legacy && !string.IsNullOrEmpty(tag.Get("cid"));
        }

        public static string TagClassOf(Component c, ComponentMeta m)
        {
            if (c is RigidDuct) return WentaTag.ClassDuct;
            if (c is FlexDuct) return ClassFlex;
            if (c is Tee) return ClassTee;
            if (c is Terminal) return WentaTag.ClassTerminal;
            if (c is Source) return WentaTag.ClassSource;
            if (c is TwoPortFitting)
                return m != null && !string.IsNullOrEmpty(m.Role) ? m.Role : ClassFitting;
            throw new WentaException("NetworkDrawing: unsupported component type " + c.GetType().Name);
        }

        // ------------------------------------------------------------ write

        // One DrawnComponent per network component, in network order. Uses
        // the layout stored in `meta` when every component has one, and
        // otherwise lays the network out from `originMm` (duct lengths to
        // scale, tee branches at right angles) and stores that layout in
        // `meta`. GUIDs missing from `meta` are assigned.
        public static List<DrawnComponent> Write(Network net, IDictionary<string, ComponentMeta> meta,
            Point2 originMm, List<string> warnings)
        {
            if (net == null) throw new WentaException("NetworkDrawing.Write: network is null");
            if (meta == null || meta.IsReadOnly)
                throw new WentaException("NetworkDrawing.Write: needs a writable metadata dictionary");
            warnings = warnings ?? new List<string>();

            bool complete = true, any = false;
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                ComponentMeta m;
                if (!meta.TryGetValue(kv.Key, out m) || m == null)
                {
                    m = new ComponentMeta();
                    meta[kv.Key] = m;
                }
                if (string.IsNullOrEmpty(m.Guid)) m.Guid = Guid.NewGuid().ToString("D");
                bool has = IsDuct(kv.Value) ? m.LayoutMm != null && m.LayoutMm.Count >= 2 : m.AtMm.HasValue;
                if (has) any = true; else complete = false;
            }
            if (!complete)
            {
                if (any)
                    warnings.Add("the JSON stores a layout for only some components; the whole network was laid out again");
                AutoLayout(net, meta, originMm, warnings);
            }

            var list = new List<DrawnComponent>();
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                ComponentMeta m = meta[kv.Key];
                var d = new DrawnComponent
                {
                    Tag = Record(net, kv.Key, kv.Value, m),
                    Label = LabelText(kv.Key, kv.Value, m),
                };
                if (IsDuct(kv.Value)) d.PointsMm = new List<Point2>(m.LayoutMm);
                else d.AtMm = m.AtMm;
                list.Add(d);
            }
            return list;
        }

        // The WENTA tag that carries a component's full record.
        public static WentaTag Record(Network net, string cid, Component c, ComponentMeta m)
        {
            m = m ?? new ComponentMeta();
            var t = new WentaTag { Class = TagClassOf(c, m), Guid = m.Guid };
            t.Set("cid", cid);
            t.Set("type", NetworkJson.WentaClassOf(c));
            t.Set("name", c.Name);
            t.Set("scope", m.DrawingScope);
            t.Set("catalog", m.CatalogEntry);
            t.Set("provenance", m.Provenance);

            var rd = c as RigidDuct;
            var fd = c as FlexDuct;
            var term = c as Terminal;
            var fit = c as TwoPortFitting;
            var tee = c as Tee;
            if (rd != null)
            {
                SetSection(t, rd.CrossSection);
                t.Set("length_m", rd.Length);
                t.Set("roughness_m", rd.AbsoluteRoughness);
            }
            else if (fd != null)
            {
                SetSection(t, new Round(fd.Diameter));
                t.Set("length_m", fd.Length);
                t.Set("dp_pa_m", fd.PressureDropPerMeter);
                t.Set("stretch_pct", fd.StretchPercentage);
            }
            else if (term != null)
            {
                t.Set("flow_m3s", term.Flowrate);
                t.Set("zeta", term.Zeta);
                if (term.CrossSection != null) SetSection(t, term.CrossSection);
            }
            else if (fit != null)
            {
                SetSection(t, fit.CrossSection);
                t.Set("zeta", fit.Zeta);
            }
            else if (tee != null)
            {
                SetSection(t, tee.CrossSection);
                t.Set("zeta_straight", tee.ZetaStraight);
                t.Set("zeta_branch", tee.ZetaBranch);
            }

            if (net != null)
                foreach (Port p in c.Ports)
                {
                    if (!p.IsIn) continue;
                    var ups = new List<string>();
                    foreach (string pred in net.Predecessors(Network.PortNodeId(cid, p.Name)))
                        if (pred.IndexOf(':') > 0) ups.Add(pred);
                    if (ups.Count > 0) t.Set("in." + p.Name, string.Join(";", ups.ToArray()));
                }
            return t;
        }

        // Replaces the record keys of `existing` with those of `record` and
        // keeps everything else (label handles, balancing results, ...).
        public static void MergeInto(WentaTag existing, WentaTag record)
        {
            var drop = new List<string>();
            foreach (string k in existing.Values.Keys)
                if (k.StartsWith("in.", StringComparison.Ordinal) || Array.IndexOf(RecordKeys, k) >= 0)
                    drop.Add(k);
            foreach (string k in drop) existing.Values.Remove(k);
            foreach (KeyValuePair<string, string> kv in record.Values) existing.Values[kv.Key] = kv.Value;
            existing.Class = record.Class;
            existing.Guid = record.Guid;
            existing.Legacy = false;
        }

        public static string LabelText(string cid, Component c, ComponentMeta m)
        {
            var rd = c as RigidDuct;
            var fd = c as FlexDuct;
            if (rd != null)
                return cid + "  " + DrawingSizing.SizeText(rd.CrossSection) + "  " + rd.Length.ToString("0.00", C) + " m";
            if (fd != null)
                return cid + "  flex Ø" + (fd.Diameter * 1000.0).ToString("0", C) + "  " + fd.Length.ToString("0.00", C) + " m";
            var term = c as Terminal;
            if (term != null) return cid + "  " + term.Flowrate.ToString("0.000", C) + " m³/s";
            if (c is Source) return cid + "  SRC";
            if (c is Tee) return cid + "  TEE";
            var fit = c as TwoPortFitting;
            if (fit != null)
                return cid + "  " + TagClassOf(c, m).ToUpperInvariant() + "  ζ=" + fit.Zeta.ToString("0.00", C);
            return cid;
        }

        private static void SetSection(WentaTag t, CrossSection cs)
        {
            t.SetSection(cs);
            Round r = cs as Round;
            Rectangular q = cs as Rectangular;
            if (r != null) t.Set("d_m", r.Diameter);
            else if (q != null)
            {
                t.Set("w_m", q.Width);
                t.Set("h_m", q.Height);
            }
        }

        // Depth-first from each source: a duct is drawn along the current
        // direction at its own length, point components sit where they
        // join, a tee's straight leg keeps the direction and its branch
        // turns 90°, alternating sides from tee to tee.
        private static void AutoLayout(Network net, IDictionary<string, ComponentMeta> meta,
            Point2 originMm, List<string> warnings)
        {
            foreach (ComponentMeta m in meta.Values)
            {
                m.LayoutMm = null;
                m.AtMm = null;
            }
            var placed = new HashSet<string>();
            var stack = new Stack<KeyValuePair<string, Point2[]>>(); // cid -> {position, direction}
            int sources = 0, tees = 0;
            var roots = new List<string>();
            foreach (KeyValuePair<string, Component> kv in net.Components)
                if (kv.Value is Source) roots.Add(kv.Key);
            for (int i = roots.Count - 1; i >= 0; i--)
                stack.Push(new KeyValuePair<string, Point2[]>(roots[i], new[]
                {
                    new Point2(originMm.X, originMm.Y - SourceSpacingMm * i), new Point2(1.0, 0.0),
                }));
            sources = roots.Count;

            while (stack.Count > 0)
            {
                KeyValuePair<string, Point2[]> item = stack.Pop();
                string cid = item.Key;
                if (!placed.Add(cid)) continue;
                Component c = net.Components[cid];
                Point2 p = item.Value[0], dir = item.Value[1];
                Point2 outAt = Place(c, meta[cid], p, dir);

                int side = 1;
                if (c is Tee) { side = tees % 2 == 0 ? 1 : -1; tees++; }
                var next = new List<KeyValuePair<string, Point2[]>>();
                foreach (Port port in c.Ports)
                {
                    if (port.IsIn) continue;
                    Point2 d = dir;
                    if (c is Tee && port.Name == "branch")
                        d = side > 0 ? new Point2(-dir.Y, dir.X) : new Point2(dir.Y, -dir.X);
                    foreach (string succ in net.Successors(Network.PortNodeId(cid, port.Name)))
                    {
                        int colon = succ.IndexOf(':');
                        if (colon <= 0) continue;
                        next.Add(new KeyValuePair<string, Point2[]>(succ.Substring(0, colon), new[] { outAt, d }));
                    }
                }
                for (int i = next.Count - 1; i >= 0; i--) stack.Push(next[i]);
            }

            int loose = 0;
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                if (placed.Contains(kv.Key)) continue;
                Point2 p = new Point2(originMm.X + 1000.0 * loose, originMm.Y - SourceSpacingMm * Math.Max(1, sources));
                Place(kv.Value, meta[kv.Key], p, new Point2(1.0, 0.0));
                loose++;
                warnings.Add("component '" + kv.Key + "' is not reached from a source; it was drawn apart");
            }
        }

        private static Point2 Place(Component c, ComponentMeta m, Point2 p, Point2 dir)
        {
            double len = DuctLength(c);
            if (IsDuct(c))
            {
                Point2 end = new Point2(p.X + dir.X * len * 1000.0, p.Y + dir.Y * len * 1000.0);
                m.LayoutMm = new List<Point2> { p, end };
                return end;
            }
            m.AtMm = p;
            return p;
        }

        private static double DuctLength(Component c)
        {
            var rd = c as RigidDuct;
            if (rd != null) return rd.Length;
            var fd = c as FlexDuct;
            if (fd != null) return fd.Length;
            return 0.0;
        }

        public static double PolylineLengthMm(IList<Point2> pts)
        {
            double len = 0.0;
            if (pts == null) return 0.0;
            for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
            return len;
        }

        // ------------------------------------------------------------- read

        // Rebuilds the network from the component records in the drawing.
        // Refuses (Errors) on orphaned or inconsistent tags: duplicated
        // GUIDs or ids (copied entities), connections to components that
        // are no longer drawn, unknown ports, a record on the wrong kind of
        // entity, missing values, unconnected components.
        public static NetworkReadResult Read(IList<DrawnComponent> items, string name)
        {
            var r = new NetworkReadResult();
            r.Network = new Network { Name = name ?? "" };
            var records = new List<DrawnComponent>();
            foreach (DrawnComponent d in items ?? new List<DrawnComponent>())
            {
                if (d == null || d.Tag == null) continue;
                if (IsRecord(d.Tag)) records.Add(d);
                else r.Ignored++;
            }
            if (records.Count == 0)
            {
                r.Errors.Add("no WENTAWRITE component records in the drawing");
                return r;
            }

            var guidOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var added = new List<DrawnComponent>();
            foreach (DrawnComponent d in records)
            {
                string cid = d.Tag.Get("cid");
                string who = Who(d);
                if (string.IsNullOrEmpty(d.Tag.Guid))
                    r.Errors.Add(who + " has no GUID");
                else
                {
                    string other;
                    if (guidOwner.TryGetValue(d.Tag.Guid, out other))
                    {
                        r.Errors.Add("entities " + other + " and " + (d.Handle ?? "?") + " carry the same GUID "
                            + d.Tag.Guid + " (a copied entity?); erase the copy");
                        continue;
                    }
                    guidOwner[d.Tag.Guid] = d.Handle ?? "?";
                }
                if (r.Network.Components.ContainsKey(cid))
                {
                    r.Errors.Add("component id '" + cid + "' is used by entities " + r.HandleOf[cid]
                        + " and " + (d.Handle ?? "?"));
                    continue;
                }
                Component c;
                ComponentMeta m;
                try
                {
                    c = Build(d, r, out m);
                }
                catch (WentaException ex)
                {
                    r.Errors.Add(who + ": " + ex.Message);
                    continue;
                }
                r.Network.Add(cid, c);
                r.Meta[cid] = m;
                r.HandleOf[cid] = d.Handle ?? "";
                added.Add(d);
            }

            foreach (DrawnComponent d in added)
            {
                string cid = d.Tag.Get("cid");
                Component c = r.Network.Components[cid];
                foreach (KeyValuePair<string, string> kv in d.Tag.Values)
                {
                    if (!kv.Key.StartsWith("in.", StringComparison.Ordinal)) continue;
                    string port = kv.Key.Substring(3);
                    Port inPort = FindPort(c, port);
                    if (inPort == null || !inPort.IsIn)
                    {
                        r.Errors.Add(Who(d) + " lists a connection into '" + port + "', which is not an inlet of a "
                            + NetworkJson.WentaClassOf(c));
                        continue;
                    }
                    foreach (string up in kv.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = up.LastIndexOf(':');
                        string upId = colon > 0 ? up.Substring(0, colon) : up;
                        string upPort = colon > 0 ? up.Substring(colon + 1) : null;
                        Component u;
                        if (!r.Network.Components.TryGetValue(upId, out u))
                        {
                            r.Errors.Add("orphaned tag: " + Who(d) + " is fed by '" + up
                                + "', which is not in the drawing (erased, or copied without its network?)");
                            continue;
                        }
                        Port outPort = upPort == null ? null : FindPort(u, upPort);
                        if (outPort == null || outPort.IsIn)
                        {
                            r.Errors.Add(Who(d) + " is fed by '" + up + "', which is not an outlet of a "
                                + NetworkJson.WentaClassOf(u));
                            continue;
                        }
                        r.Network.Connect(upId + "." + upPort, cid + "." + port);
                    }
                }
            }

            if (r.Errors.Count == 0)
                foreach (string problem in r.Network.Validate())
                    r.Errors.Add("network: " + problem);
            return r;
        }

        private static string Who(DrawnComponent d)
        {
            return "entity " + (d.Handle ?? "?") + " ('" + d.Tag.Get("cid") + "')";
        }

        private static Port FindPort(Component c, string name)
        {
            foreach (Port p in c.Ports) if (p.Name == name) return p;
            return null;
        }

        private static Component Build(DrawnComponent d, NetworkReadResult r, out ComponentMeta m)
        {
            WentaTag t = d.Tag;
            string cid = t.Get("cid");
            string type = t.Get("type");
            string name = t.Get("name") ?? cid;
            if (string.IsNullOrEmpty(type)) throw new WentaException("record has no 'type'");

            m = new ComponentMeta
            {
                Guid = t.Guid,
                DrawingScope = t.Get("scope"),
                CatalogEntry = t.Get("catalog"),
                Provenance = t.Get("provenance"),
            };

            bool duct = type == "RigidDuct" || type == "FlexDuct";
            if (duct)
            {
                if (d.PointsMm == null || d.PointsMm.Count < 2)
                    throw new WentaException("a " + type + " record on an entity that is not a centreline");
                m.LayoutMm = new List<Point2>(d.PointsMm);
            }
            else
            {
                if (!d.AtMm.HasValue)
                    throw new WentaException("a " + type + " record on an entity that is not a point marker");
                m.AtMm = d.AtMm;
            }

            string expected = null;
            Component c;
            switch (type)
            {
                case "RigidDuct":
                    expected = WentaTag.ClassDuct;
                    c = new RigidDuct(name, ReadSection(t, true), Length(d, cid, r),
                        Opt(t, "roughness_m", 0.0001));
                    break;
                case "FlexDuct":
                    expected = ClassFlex;
                    Round fr = ReadSection(t, true) as Round;
                    if (fr == null) throw new WentaException("a flexible duct must be round");
                    c = new FlexDuct(name, fr.Diameter, Length(d, cid, r), Req(t, "dp_pa_m"),
                        Opt(t, "stretch_pct", 100.0));
                    break;
                case "Terminal":
                    expected = WentaTag.ClassTerminal;
                    c = new Terminal(name, Req(t, "flow_m3s"), ReadSection(t, false), Opt(t, "zeta", 0.0));
                    break;
                case "Source":
                    expected = WentaTag.ClassSource;
                    c = new Source(name);
                    break;
                case "Tee":
                    expected = ClassTee;
                    c = new Tee(name, ReadSection(t, true), Opt(t, "zeta_straight", 0.0), Opt(t, "zeta_branch", 0.5));
                    break;
                case "TwoPortFitting":
                    if (t.Class == WentaTag.ClassDuct || t.Class == ClassFlex || t.Class == ClassTee
                        || t.Class == WentaTag.ClassTerminal || t.Class == WentaTag.ClassSource
                        || string.IsNullOrEmpty(t.Class))
                        throw new WentaException("class '" + (t.Class ?? "") + "' does not match type TwoPortFitting");
                    if (t.Class != ClassFitting) m.Role = t.Class;
                    c = new TwoPortFitting(name, ReadSection(t, true), Req(t, "zeta"));
                    break;
                default:
                    throw new WentaException("unknown type '" + type + "'");
            }
            if (expected != null && t.Class != expected)
                throw new WentaException("class '" + (t.Class ?? "") + "' does not match type " + type);
            return c;
        }

        private static double Length(DrawnComponent d, string cid, NetworkReadResult r)
        {
            double drawn = PolylineLengthMm(d.PointsMm) / 1000.0;
            double? stored = d.Tag.GetDouble("length_m");
            if (drawn <= 0.0) throw new WentaException("the centreline has zero length");
            if (stored.HasValue && Math.Abs(drawn - stored.Value) * 1000.0 <= LengthToleranceMm)
                return stored.Value;
            r.LengthUpdated.Add(cid);
            return drawn;
        }

        private static CrossSection ReadSection(WentaTag t, bool required)
        {
            string shape = t.Get("shape");
            if (shape == Sizing.ShapeRound)
            {
                double? dm = t.GetDouble("d_m");
                if (dm.HasValue && dm.Value > 0.0) return new Round(dm.Value);
            }
            else if (shape == Sizing.ShapeRectangular)
            {
                double? w = t.GetDouble("w_m"), h = t.GetDouble("h_m");
                if (w.HasValue && h.HasValue && w.Value > 0.0 && h.Value > 0.0) return new Rectangular(w.Value, h.Value);
            }
            CrossSection mm = t.Section();
            if (mm != null || !required) return mm;
            throw new WentaException("record has no cross-section");
        }

        private static double Req(WentaTag t, string key)
        {
            double? v = t.GetDouble(key);
            if (!v.HasValue) throw new WentaException("record has no numeric '" + key + "'");
            return v.Value;
        }

        private static double Opt(WentaTag t, string key, double fallback)
        {
            string s = t.Get(key);
            if (s == null) return fallback;
            double? v = t.GetDouble(key);
            if (!v.HasValue) throw new WentaException("'" + key + "' is not a number: " + s);
            return v.Value;
        }
    }
}
