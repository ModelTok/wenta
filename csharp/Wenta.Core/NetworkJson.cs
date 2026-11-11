using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Wenta
{
    public sealed class ComponentMeta
    {
        public string Guid;

        public string DrawingScope;

        // Optional identity and drawing data (#20, #22). They are written
        // only when set, so a network without them serialises unchanged.
        public string CatalogEntry;
        public string Provenance;
        public string Role;                 // "damper", "attenuator", ... on a TwoPortFitting
        public List<Point2> LayoutMm;       // duct centreline in drawing units
        public Point2? AtMm;                // insertion point of a point component
    }

    public static class NetworkJson
    {
        public const int SchemaVersion = 1;


        public static string Serialize(Network net, IDictionary<string, ComponentMeta> meta = null)
        {
            return Write(net, meta, false);
        }

        // The python/wenta NetworkDesignSchema shape: components keyed by id
        // with a "type" discriminator, connections as source/target
        // "component:port". The extra keys (guid, drawing_scope, layout_mm,
        // ...) are ignored by pydantic's default model config. Parse reads
        // both shapes.
        public static string SerializeDesign(Network net, IDictionary<string, ComponentMeta> meta = null)
        {
            return Write(net, meta, true);
        }

        private static string Write(Network net, IDictionary<string, ComponentMeta> meta, bool design)
        {
            if (net == null)
                throw new WentaException("NetworkJson.Serialize: network is null");

            var root = new Dictionary<string, object>();
            root["schema_version"] = SchemaVersion;
            root["name"] = net.Name ?? "";
            var components = new ArrayList();
            var byId = new Dictionary<string, object>();
            var connections = new ArrayList();
            root["components"] = design ? (object)byId : components;
            root["connections"] = connections;

            bool writeBack = meta != null && !meta.IsReadOnly;
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                string cid = kv.Key;
                Component c = kv.Value;

                ComponentMeta m = null;
                if (meta != null) meta.TryGetValue(cid, out m);
                if (m == null)
                {
                    m = new ComponentMeta();
                    if (writeBack) meta[cid] = m;
                }
                if (string.IsNullOrEmpty(m.Guid))
                    m.Guid = System.Guid.NewGuid().ToString("D");

                Dictionary<string, object> cd = ComponentToDict(cid, c, m);
                if (design)
                {
                    var typed = new Dictionary<string, object>();
                    typed["type"] = WentaClassOf(c);
                    foreach (KeyValuePair<string, object> e in cd)
                        if (e.Key != "id" && e.Key != "wenta_class") typed[e.Key] = e.Value;
                    byId[cid] = typed;
                }
                else components.Add(cd);

                foreach (Port p in c.Ports)
                {
                    if (!p.IsIn) continue;
                    foreach (string pred in net.Predecessors(Network.PortNodeId(cid, p.Name)))
                    {
                        int colon = pred.LastIndexOf(':');
                        if (colon < 0) continue;
                        var e = new Dictionary<string, object>();
                        if (design)
                        {
                            e["source"] = pred;
                            e["target"] = Network.PortNodeId(cid, p.Name);
                        }
                        else
                        {
                            e["from"] = pred.Substring(0, colon) + "." + pred.Substring(colon + 1);
                            e["to"] = cid + "." + p.Name;
                        }
                        connections.Add(e);
                    }
                }
            }

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            return ser.Serialize(root);
        }

        public static void Save(Network net, string path, IDictionary<string, ComponentMeta> meta = null)
        {
            File.WriteAllText(path, Serialize(net, meta));
        }


        public static Network Parse(string json)
        {
            Dictionary<string, ComponentMeta> ignored;
            return Parse(json, out ignored);
        }

        public static Network Parse(string json, out Dictionary<string, ComponentMeta> meta)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root;
            try
            {
                root = ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new WentaException("invalid network JSON (" + ex.Message + ")");
            }
            if (root == null)
                throw new WentaException("invalid network JSON: empty document");

            int version = root.ContainsKey("schema_version") ? ParseVersion(root["schema_version"]) : 1;
            if (version > SchemaVersion)
                throw new WentaException("network JSON declares schema_version "
                    + version.ToString(CultureInfo.InvariantCulture)
                    + "; this build supports schema_version "
                    + SchemaVersion.ToString(CultureInfo.InvariantCulture) + " or older");

            object compsObj;
            root.TryGetValue("components", out compsObj);
            var keyed = compsObj as Dictionary<string, object>;
            if (keyed != null)
            {
                // NetworkDesignSchema shape: {"<id>": {...}, ...}
                var list = new ArrayList();
                foreach (KeyValuePair<string, object> kv in keyed)
                {
                    var cd = kv.Value as Dictionary<string, object>;
                    if (cd == null)
                        throw new WentaException("network JSON: component '" + kv.Key + "' is not an object");
                    var copy = new Dictionary<string, object>(cd);
                    copy["id"] = kv.Key;
                    list.Add(copy);
                }
                compsObj = list;
            }
            if (!(compsObj is ArrayList))
                throw new WentaException("network JSON has no 'components' array");

            var net = new Network();
            net.Name = Str(root, "name", "network") ?? "";
            meta = new Dictionary<string, ComponentMeta>();

            int index = 0;
            foreach (object o in (ArrayList)compsObj)
            {
                string at = "components[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var d = o as Dictionary<string, object>;
                if (d == null)
                    throw new WentaException("network JSON: " + at + " is not an object");
                string cid = Str(d, "id", at);
                if (string.IsNullOrEmpty(cid))
                    throw new WentaException("network JSON: " + at + " has no 'id'");
                if (net.Components.ContainsKey(cid))
                    throw new WentaException("network JSON: duplicate component id '" + cid + "'");
                string ctx = "component '" + cid + "'";
                string cls = Str(d, "wenta_class", ctx) ?? Str(d, "type", ctx);
                if (string.IsNullOrEmpty(cls))
                    throw new WentaException("network JSON: " + ctx + " has no 'wenta_class'");
                string name = Str(d, "name", ctx) ?? cid;

                net.Add(cid, BuildComponent(cls, name, d, ctx + " (" + cls + ")"));

                var m = new ComponentMeta();
                m.Guid = Str(d, "guid", ctx);
                m.DrawingScope = Str(d, "drawing_scope", ctx);
                m.CatalogEntry = Str(d, "catalog_entry", ctx);
                m.Provenance = Str(d, "provenance", ctx);
                m.Role = Str(d, "role", ctx);
                m.LayoutMm = ParsePoints(d, "layout_mm", ctx);
                List<Point2> atPts = ParsePoints(d, "at_mm", ctx);
                if (atPts != null)
                {
                    if (atPts.Count != 1)
                        throw new WentaException("network JSON: " + ctx + ": 'at_mm' must be one [x, y] point");
                    m.AtMm = atPts[0];
                }
                meta[cid] = m;
                index++;
            }

            object connsObj;
            if (root.TryGetValue("connections", out connsObj) && connsObj != null)
            {
                var conns = connsObj as ArrayList;
                if (conns == null)
                    throw new WentaException("network JSON: 'connections' must be an array");
                index = 0;
                foreach (object o in conns)
                {
                    string at = "connections[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    var d = o as Dictionary<string, object>;
                    if (d == null)
                        throw new WentaException("network JSON: " + at + " is not an object");
                    string from = Str(d, "from", at) ?? Str(d, "source", at);
                    string to = Str(d, "to", at) ?? Str(d, "target", at);
                    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                        throw new WentaException("network JSON: " + at + " needs both 'from' and 'to'");
                    net.Connect(Endpoint(from), Endpoint(to));
                    index++;
                }
            }
            return net;
        }

        public static Network Load(string path, out Dictionary<string, ComponentMeta> meta)
        {
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                throw new WentaException("cannot read network JSON '" + path + "': " + ex.Message);
            }
            return Parse(json, out meta);
        }


        public static string WentaClassOf(Component c)
        {
            if (c is Source) return "Source";
            if (c is RigidDuct) return "RigidDuct";
            if (c is FlexDuct) return "FlexDuct";
            if (c is Terminal) return "Terminal";
            if (c is TwoPortFitting) return "TwoPortFitting";
            if (c is Tee) return "Tee";
            throw new WentaException("NetworkJson: unsupported component type "
                + (c == null ? "null" : c.GetType().Name));
        }

        private static Dictionary<string, object> ComponentToDict(string cid, Component c, ComponentMeta m)
        {
            var d = new Dictionary<string, object>();
            d["id"] = cid;
            d["wenta_class"] = WentaClassOf(c);
            d["guid"] = m.Guid;
            d["drawing_scope"] = m.DrawingScope;
            if (m.CatalogEntry != null) d["catalog_entry"] = m.CatalogEntry;
            if (m.Provenance != null) d["provenance"] = m.Provenance;
            if (m.Role != null) d["role"] = m.Role;
            if (m.LayoutMm != null) d["layout_mm"] = PointsToList(m.LayoutMm);
            if (m.AtMm.HasValue) d["at_mm"] = new ArrayList { m.AtMm.Value.X, m.AtMm.Value.Y };
            d["name"] = c.Name;

            var rd = c as RigidDuct;
            if (rd != null)
            {
                d["cross_section"] = CrossSectionToDict(rd.CrossSection);
                d["length"] = rd.Length;
                d["absolute_roughness"] = rd.AbsoluteRoughness;
                return d;
            }
            var fd = c as FlexDuct;
            if (fd != null)
            {
                d["diameter"] = fd.Diameter;
                d["length"] = fd.Length;
                d["pressure_drop_per_meter"] = fd.PressureDropPerMeter;
                d["stretch_percentage"] = fd.StretchPercentage;
                return d;
            }
            var t = c as Terminal;
            if (t != null)
            {
                d["flowrate"] = t.Flowrate;
                if (t.CrossSection != null) d["cross_section"] = CrossSectionToDict(t.CrossSection);
                d["zeta"] = t.Zeta;
                return d;
            }
            var f = c as TwoPortFitting;
            if (f != null)
            {
                d["cross_section"] = CrossSectionToDict(f.CrossSection);
                d["zeta"] = f.Zeta;
                return d;
            }
            var tee = c as Tee;
            if (tee != null)
            {
                d["cross_section"] = CrossSectionToDict(tee.CrossSection);
                d["zeta_straight"] = tee.ZetaStraight;
                d["zeta_branch"] = tee.ZetaBranch;
                return d;
            }
            return d;
        }

        private static Dictionary<string, object> CrossSectionToDict(CrossSection cs)
        {
            if (cs == null)
                throw new WentaException("NetworkJson: component has no cross-section");
            var d = new Dictionary<string, object>();
            var r = cs as Round;
            if (r != null)
            {
                d["shape"] = "round";
                d["diameter"] = r.Diameter;
                return d;
            }
            var rect = cs as Rectangular;
            if (rect != null)
            {
                d["shape"] = "rectangular";
                d["width"] = rect.Width;
                d["height"] = rect.Height;
                return d;
            }
            throw new WentaException("NetworkJson: unsupported cross-section type " + cs.GetType().Name);
        }

        private static Component BuildComponent(string cls, string name, Dictionary<string, object> d, string ctx)
        {
            switch (cls)
            {
                case "Source":
                    return new Source(name);
                case "RigidDuct":
                    return new RigidDuct(name,
                        RequireCrossSection(d, ctx),
                        RequireDouble(d, "length", ctx),
                        OptionalDouble(d, "absolute_roughness", 0.0001, ctx));
                case "FlexDuct":
                    return new FlexDuct(name,
                        RequireDouble(d, "diameter", ctx),
                        RequireDouble(d, "length", ctx),
                        RequireDouble(d, "pressure_drop_per_meter", ctx),
                        OptionalDouble(d, "stretch_percentage", 100.0, ctx));
                case "Terminal":
                    return new Terminal(name,
                        RequireDouble(d, "flowrate", ctx),
                        OptionalCrossSection(d, ctx),
                        OptionalDouble(d, "zeta", 0.0, ctx));
                case "TwoPortFitting":
                    return new TwoPortFitting(name,
                        RequireCrossSection(d, ctx),
                        RequireDouble(d, "zeta", ctx));
                case "Tee":
                    return new Tee(name,
                        RequireCrossSection(d, ctx),
                        OptionalDouble(d, "zeta_straight", 0.0, ctx),
                        OptionalDouble(d, "zeta_branch", 0.5, ctx));
                default:
                    throw new WentaException("network JSON: " + ctx + ": unknown wenta_class '" + cls + "'");
            }
        }

        private static CrossSection RequireCrossSection(Dictionary<string, object> d, string ctx)
        {
            object v;
            if (!d.TryGetValue("cross_section", out v) || v == null)
                throw new WentaException("network JSON: " + ctx + " has no 'cross_section'");
            return ParseCrossSection(v, ctx);
        }

        private static CrossSection OptionalCrossSection(Dictionary<string, object> d, string ctx)
        {
            object v;
            if (!d.TryGetValue("cross_section", out v) || v == null) return null;
            return ParseCrossSection(v, ctx);
        }

        private static CrossSection ParseCrossSection(object v, string ctx)
        {
            var d = v as Dictionary<string, object>;
            if (d == null)
                throw new WentaException("network JSON: " + ctx + ": 'cross_section' must be an object");
            string inner = ctx + " cross_section";
            string shape = Str(d, "shape", inner);
            if (shape == "round")
                return new Round(RequireDouble(d, "diameter", inner));
            if (shape == "rectangular")
                return new Rectangular(RequireDouble(d, "width", inner), RequireDouble(d, "height", inner));
            throw new WentaException("network JSON: " + inner
                + ": 'shape' must be 'round' or 'rectangular', got '" + (shape ?? "null") + "'");
        }


        private static ArrayList PointsToList(List<Point2> pts)
        {
            var list = new ArrayList();
            foreach (Point2 p in pts) list.Add(new ArrayList { p.X, p.Y });
            return list;
        }

        // "layout_mm": [[x, y], ...]; "at_mm": [x, y].
        private static List<Point2> ParsePoints(Dictionary<string, object> d, string key, string ctx)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            var arr = v as ArrayList;
            if (arr == null)
                throw new WentaException("network JSON: " + ctx + ": '" + key + "' must be an array");
            var pts = new List<Point2>();
            if (arr.Count == 2 && IsNumber(arr[0]) && IsNumber(arr[1]))
            {
                pts.Add(new Point2(ToDouble(arr[0]), ToDouble(arr[1])));
                return pts;
            }
            foreach (object o in arr)
            {
                var xy = o as ArrayList;
                if (xy == null || xy.Count != 2 || !IsNumber(xy[0]) || !IsNumber(xy[1]))
                    throw new WentaException("network JSON: " + ctx + ": '" + key + "' must hold [x, y] pairs");
                pts.Add(new Point2(ToDouble(xy[0]), ToDouble(xy[1])));
            }
            return pts;
        }

        private static string Endpoint(string s)
        {
            if (s.IndexOf('.') >= 0) return s;
            int colon = s.IndexOf(':');
            if (colon < 0) return s;
            return s.Substring(0, colon) + "." + s.Substring(colon + 1);
        }

        private static string Str(Dictionary<string, object> d, string key, string ctx)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            string s = v as string;
            if (s == null)
                throw new WentaException("network JSON: " + ctx + ": '" + key + "' must be a string");
            return s;
        }

        private static bool IsNumber(object v)
        {
            return v is int || v is long || v is double || v is decimal
                || v is float || v is short || v is byte;
        }

        private static double ToDouble(object v)
        {
            if (v is double) return (double)v;
            if (v is decimal)
                return double.Parse(((decimal)v).ToString(CultureInfo.InvariantCulture),
                                    NumberStyles.Float, CultureInfo.InvariantCulture);
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        private static double RequireDouble(Dictionary<string, object> d, string key, string ctx)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null || !IsNumber(v))
                throw new WentaException("network JSON: " + ctx + " has no numeric '" + key + "'");
            return ToDouble(v);
        }

        private static double OptionalDouble(Dictionary<string, object> d, string key, double fallback, string ctx)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (!IsNumber(v))
                throw new WentaException("network JSON: " + ctx + ": '" + key + "' must be a number");
            return ToDouble(v);
        }

        private static int ParseVersion(object v)
        {
            if (v == null || v is string || v is bool)
                throw new WentaException("network JSON: 'schema_version' must be a positive integer");
            double d = ToDouble(v);
            if (d != Math.Floor(d) || d < 1.0 || d > int.MaxValue)
                throw new WentaException("network JSON: 'schema_version' must be a positive integer, got "
                    + d.ToString("R", CultureInfo.InvariantCulture));
            return (int)d;
        }
    }
}
