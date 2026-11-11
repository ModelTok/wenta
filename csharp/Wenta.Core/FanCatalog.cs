using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Wenta
{
    // One fan model of a fan catalog: its static-pressure curve plus the
    // optional data used to report it (efficiency for the power estimate,
    // provenance).
    public sealed class FanModel
    {
        public string Id;
        public FanCurve Curve;
        public double? Efficiency;
        public string Source;
        public string Notes;
    }

    // Open, vendor-neutral fan-curve catalog (docs/user-guide.en.md §5.4):
    //   { "name": ..., "version": 1, "fans": [ { "id": ..., "efficiency": 0.6,
    //     "points": [[flow_m3s, static_pa], ...] }, ... ] }
    // Order matters: Fan.PickFan takes the first fan that meets the duty point.
    public sealed class FanCatalog
    {
        public const int SchemaVersion = 1;

        public string Name;
        public string Description;
        public int Version = SchemaVersion;
        public readonly List<FanModel> Fans = new List<FanModel>();

        public static FanCatalog Load(string path)
        {
            return Parse(File.ReadAllText(path), path);
        }

        public static FanCatalog Parse(string json, string origin)
        {
            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new WentaException("invalid fan catalog JSON: " + origin + " (" + ex.Message + ")");
            }
            if (root == null)
                throw new WentaException("invalid fan catalog JSON: " + origin);
            var cat = new FanCatalog();
            cat.Name = Str(root, "name");
            cat.Description = Str(root, "description");
            if (root.ContainsKey("version"))
            {
                object v = root["version"];
                if (!(v is int) || (int)v < 1)
                    throw new WentaException("fan catalog " + origin + ": 'version' must be an integer >= 1");
                cat.Version = (int)v;
            }
            if (cat.Version > SchemaVersion)
                throw new WentaException("fan catalog '" + (cat.Name ?? "?") + "' (" + origin
                    + ") declares version " + cat.Version.ToString(CultureInfo.InvariantCulture)
                    + "; this build supports fan catalog version "
                    + SchemaVersion.ToString(CultureInfo.InvariantCulture) + " or older");
            ArrayList fans = root.ContainsKey("fans") ? root["fans"] as ArrayList : null;
            if (fans == null)
                throw new WentaException("fan catalog JSON has no 'fans' array: " + origin);
            var seen = new Dictionary<string, bool>();
            for (int i = 0; i < fans.Count; i++)
            {
                string where = "fan catalog " + origin + ": fans[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                var f = fans[i] as Dictionary<string, object>;
                if (f == null) throw new WentaException(where + " is not an object");
                var m = new FanModel();
                m.Id = Str(f, "id");
                if (string.IsNullOrEmpty(m.Id)) throw new WentaException(where + " has no 'id'");
                if (seen.ContainsKey(m.Id))
                    throw new WentaException("fan catalog " + origin + ": duplicate fan id '" + m.Id + "'");
                seen[m.Id] = true;
                ArrayList pts = f.ContainsKey("points") ? f["points"] as ArrayList : null;
                if (pts == null)
                    throw new WentaException("fan catalog " + origin + ": fan '" + m.Id + "' has no 'points' array");
                var points = new FanPoint[pts.Count];
                for (int k = 0; k < pts.Count; k++)
                {
                    ArrayList p = pts[k] as ArrayList;
                    if (p == null || p.Count != 2 || !IsNumber(p[0]) || !IsNumber(p[1]))
                        throw new WentaException("fan catalog " + origin + ": fan '" + m.Id + "' point "
                            + k.ToString(CultureInfo.InvariantCulture) + " is not [flow_m3s, static_pa]");
                    points[k] = new FanPoint(Num(p[0]), Num(p[1]));
                }
                m.Curve = new FanCurve(m.Id, points);
                if (f.ContainsKey("efficiency") && f["efficiency"] != null)
                {
                    if (!IsNumber(f["efficiency"]))
                        throw new WentaException("fan catalog " + origin + ": fan '" + m.Id + "' efficiency is not a number");
                    double eta = Num(f["efficiency"]);
                    if (!(eta > 0.0 && eta <= 1.0))
                        throw new WentaException("fan catalog " + origin + ": fan '" + m.Id + "' efficiency must be in (0, 1]");
                    m.Efficiency = eta;
                }
                m.Source = Str(f, "source");
                m.Notes = Str(f, "notes");
                cat.Fans.Add(m);
            }
            return cat;
        }

        public FanCurve[] Curves()
        {
            var c = new FanCurve[Fans.Count];
            for (int i = 0; i < Fans.Count; i++) c[i] = Fans[i].Curve;
            return c;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) ? d[key] as string : null;
        }

        private static bool IsNumber(object o)
        {
            return o is int || o is long || o is decimal || o is double;
        }

        private static double Num(object o)
        {
            return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }
    }

    public sealed class FanCandidate
    {
        public FanModel Fan;
        public double? AvailablePa;   // curve static pressure at the design flow; null outside the curve
        public double? MarginPa;      // Fan.Margin
        public bool Meets;            // margin >= 0
        public double? PowerW;        // Fan.Power(flow, required, η) when the fan meets and has an efficiency
    }

    // WENTAFAN: every catalog fan evaluated at the duty point, and the fan
    // Fan.PickFan selects (the first that meets it). No new math.
    public static class FanSelection
    {
        public static List<FanCandidate> Evaluate(FanCatalog catalog, double designFlowM3s, double requiredStaticPa)
        {
            var list = new List<FanCandidate>();
            foreach (FanModel m in catalog.Fans)
            {
                var c = new FanCandidate { Fan = m };
                c.MarginPa = Fan.Margin(m.Curve, designFlowM3s, requiredStaticPa);
                if (c.MarginPa.HasValue)
                {
                    c.AvailablePa = m.Curve.StaticPressureAt(designFlowM3s);
                    c.Meets = c.MarginPa.Value >= 0.0;
                }
                if (c.Meets && m.Efficiency.HasValue)
                    c.PowerW = Fan.Power(designFlowM3s, requiredStaticPa, m.Efficiency.Value);
                list.Add(c);
            }
            return list;
        }

        public static int? Pick(FanCatalog catalog, double designFlowM3s, double requiredStaticPa)
        {
            return Fan.PickFan(catalog.Curves(), designFlowM3s, requiredStaticPa);
        }

        // Duty point of a drawn network: the flow leaving the source and the
        // critical-path ΔP, plus any fixed extra (AHU, filters, coils).
        public static void DutyPoint(DrawingTrace trace, Fluid fluid, double extraPa,
            out double flowM3s, out double requiredPa)
        {
            if (extraPa < 0.0) throw new WentaException("extra pressure must be >= 0 Pa");
            double dp = trace.Network.Solve(fluid ?? Fluid.StandardAir());
            flowM3s = 0.0;
            foreach (Component c in trace.Network.Terminals()) flowM3s += ((Terminal)c).Flowrate;
            requiredPa = dp + extraPa;
        }
    }
}
