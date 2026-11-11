using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Wenta
{
    // #59: smart labels. One label per drawn duct with its attributes
    // (id, branch number, size, flow, velocity, Δp/m) from the solved
    // network and Marking's branch numbers (numbered by topology from the
    // source, so they do not depend on draw order). WENTALABEL turns them
    // into attributed block references on WENTA-ANNO; the editing commands
    // refresh them after every edit. The label style is a small JSON file.

    public sealed class LabelStyle
    {
        public const int SchemaVersion = 1;

        // Attribute tags in block order; Fields picks the visible ones.
        public static readonly string[] AllFields = { "id", "branch", "size", "flow", "velocity", "dpm" };

        public double TextHeightMm = 100.0;
        public double OffsetMm = -250.0;
        public string[] Fields = (string[])AllFields.Clone();
        public int FlowDecimals = 3;
        public int VelocityDecimals = 2;
        public int DpmDecimals = 2;

        public static LabelStyle Default() { return new LabelStyle(); }

        public bool Shows(string field) { return Array.IndexOf(Fields, field) >= 0; }

        public static LabelStyle Parse(string json, string source)
        {
            Dictionary<string, object> root;
            try { root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception ex) { throw new WentaException(source + ": not a label style JSON (" + ex.Message + ")"); }
            if (root == null) throw new WentaException(source + ": empty label style");
            var s = new LabelStyle();
            object v;
            if (root.TryGetValue("schema_version", out v) && Convert.ToInt32(v, CultureInfo.InvariantCulture) != SchemaVersion)
                throw new WentaException(source + ": schema_version must be " + SchemaVersion);
            if (root.TryGetValue("text_height_mm", out v)) s.TextHeightMm = Num(v, source, "text_height_mm", true);
            if (root.TryGetValue("offset_mm", out v)) s.OffsetMm = Num(v, source, "offset_mm", false);
            if (root.TryGetValue("flow_decimals", out v)) s.FlowDecimals = Dec(v, source, "flow_decimals");
            if (root.TryGetValue("velocity_decimals", out v)) s.VelocityDecimals = Dec(v, source, "velocity_decimals");
            if (root.TryGetValue("dpm_decimals", out v)) s.DpmDecimals = Dec(v, source, "dpm_decimals");
            if (root.TryGetValue("fields", out v))
            {
                var list = v as ArrayList;
                if (list == null || list.Count == 0) throw new WentaException(source + ": fields must be a non-empty list");
                var fields = new List<string>();
                foreach (object o in list)
                {
                    string f = Convert.ToString(o, CultureInfo.InvariantCulture);
                    if (Array.IndexOf(AllFields, f) < 0)
                        throw new WentaException(source + ": unknown field '" + f + "' (" + string.Join(", ", AllFields) + ")");
                    if (!fields.Contains(f)) fields.Add(f);
                }
                s.Fields = fields.ToArray();
            }
            return s;
        }

        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["schema_version"] = SchemaVersion;
            root["text_height_mm"] = TextHeightMm;
            root["offset_mm"] = OffsetMm;
            root["fields"] = Fields;
            root["flow_decimals"] = FlowDecimals;
            root["velocity_decimals"] = VelocityDecimals;
            root["dpm_decimals"] = DpmDecimals;
            return new JavaScriptSerializer().Serialize(root);
        }

        public static LabelStyle Load(string path)
        {
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }

        private static double Num(object v, string source, string key, bool positive)
        {
            double d;
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { throw new WentaException(source + ": " + key + " must be a number"); }
            if (double.IsNaN(d) || double.IsInfinity(d) || (positive && d <= 0.0))
                throw new WentaException(source + ": " + key + (positive ? " must be positive" : " must be finite"));
            return d;
        }

        private static int Dec(object v, string source, string key)
        {
            double d = Num(v, source, key, false);
            if (d < 0 || d > 6 || d != Math.Floor(d)) throw new WentaException(source + ": " + key + " must be a whole number 0..6");
            return (int)d;
        }
    }

    public sealed class SmartLabel
    {
        public string ComponentId;
        public uint BranchNo;              // 0 for flexible ducts (Marking numbers rigid ducts)
        // Attribute values by field (LabelStyle.AllFields), already formatted.
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        // The visible text in the style's field order.
        public string Text(LabelStyle style)
        {
            var parts = new List<string>();
            foreach (string f in style.Fields)
            {
                string s;
                if (Values.TryGetValue(f, out s) && s.Length > 0) parts.Add(s);
            }
            return string.Join("  ", parts.ToArray());
        }
    }

    public static class Annotation
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static string Fixed(double v, int decimals)
        {
            return v.ToString("F" + decimals.ToString(C), C);
        }

        // Labels for every rigid and flexible duct of a solved network,
        // sorted by branch number (flexible ducts last, by id).
        public static List<SmartLabel> Build(Network network, LabelStyle style)
        {
            if (network == null) throw new WentaException("Annotation.Build: network is null");
            if (style == null) style = LabelStyle.Default();
            var branch = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (Mark m in Marking.AssignBranchMarks(network)) branch[m.ComponentId] = m.BranchNo;

            var labels = new List<SmartLabel>();
            foreach (KeyValuePair<string, Component> kv in network.Components)
            {
                var rd = kv.Value as RigidDuct;
                var fd = kv.Value as FlexDuct;
                if (rd == null && fd == null) continue;
                double length = rd != null ? rd.Length : fd.Length;
                Port inlet = null;
                foreach (Port p in kv.Value.Inlets) { inlet = p; break; }
                if (inlet == null) continue;
                var l = new SmartLabel { ComponentId = kv.Key };
                uint no;
                if (branch.TryGetValue(kv.Key, out no)) l.BranchNo = no;
                l.Values["id"] = kv.Key;
                l.Values["branch"] = l.BranchNo > 0 ? "#" + l.BranchNo.ToString(C) : "";
                l.Values["size"] = rd != null ? DrawingSizing.SizeText(rd.CrossSection)
                                              : "flex Ø" + (fd.Diameter * 1000.0).ToString("0", C);
                l.Values["flow"] = inlet.Flowrate.HasValue ? Fixed(inlet.Flowrate.Value, style.FlowDecimals) + " m³/s" : "";
                l.Values["velocity"] = inlet.Velocity.HasValue ? Fixed(inlet.Velocity.Value, style.VelocityDecimals) + " m/s" : "";
                l.Values["dpm"] = length > 0.0 ? Fixed(kv.Value.TotalPressureDrop() / length, style.DpmDecimals) + " Pa/m" : "";
                labels.Add(l);
            }
            labels.Sort((a, b) =>
            {
                uint ka = a.BranchNo == 0 ? uint.MaxValue : a.BranchNo, kb = b.BranchNo == 0 ? uint.MaxValue : b.BranchNo;
                int k = ka.CompareTo(kb);
                return k != 0 ? k : string.CompareOrdinal(a.ComponentId, b.ComponentId);
            });
            return labels;
        }
    }
}
