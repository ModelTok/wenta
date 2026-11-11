using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    // Host-agnostic half of the ZWCAD commands: the drawing is described as
    // duct centrelines and point markers in drawing units (millimetres), and
    // this module turns it into a Wenta.Network and back into per-entity
    // results. No engineering math lives here; it only maps data onto the
    // existing Topology, Solver, BatchSizing, ReFit and Sound functions.

    public sealed class WentaTag
    {
        public const string AppName = "WENTA";
        public const string SchemaMarker = "v2";

        public const string ClassDuct = "duct";
        public const string ClassTerminal = "terminal";
        public const string ClassSource = "source";
        public const string ClassDamper = "damper";
        public const string ClassLabel = "label";

        public string Class;
        public string Guid;
        public bool Legacy;
        public readonly SortedDictionary<string, string> Values =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        public static WentaTag New(string wentaClass)
        {
            return new WentaTag { Class = wentaClass, Guid = System.Guid.NewGuid().ToString("D") };
        }

        public string Get(string key)
        {
            string v;
            return Values.TryGetValue(key, out v) ? v : null;
        }

        public double? GetDouble(string key)
        {
            string v = Get(key);
            double d;
            if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            return null;
        }

        public void Set(string key, string value)
        {
            if (value == null) Values.Remove(key);
            else Values[key] = value;
        }

        public void Set(string key, double value)
        {
            Values[key] = value.ToString("R", CultureInfo.InvariantCulture);
        }

        public CrossSection Section()
        {
            string shape = Get("shape");
            if (shape == Sizing.ShapeRound)
            {
                double? d = GetDouble("d_mm");
                if (d.HasValue && d.Value > 0.0) return new Round(d.Value / 1000.0);
            }
            else if (shape == Sizing.ShapeRectangular)
            {
                double? w = GetDouble("w_mm"), h = GetDouble("h_mm");
                if (w.HasValue && h.HasValue && w.Value > 0.0 && h.Value > 0.0)
                    return new Rectangular(w.Value / 1000.0, h.Value / 1000.0);
            }
            return null;
        }

        public void SetSection(CrossSection section)
        {
            Round r = section as Round;
            Rectangular q = section as Rectangular;
            Values.Remove("d_mm");
            Values.Remove("w_mm");
            Values.Remove("h_mm");
            if (r != null)
            {
                Set("shape", Sizing.ShapeRound);
                Set("d_mm", Math.Round(r.Diameter * 1000.0, 3));
            }
            else if (q != null)
            {
                Set("shape", Sizing.ShapeRectangular);
                Set("w_mm", Math.Round(q.Width * 1000.0, 3));
                Set("h_mm", Math.Round(q.Height * 1000.0, 3));
            }
        }

        // XData layout (after the 1001 application name): 1000 class,
        // 1000 "v2", 1000 guid, then 1000 key / 1000 value pairs. All values
        // are stored as invariant-culture strings so the record is
        // locale-independent.
        public List<KeyValuePair<int, object>> ToXData()
        {
            var list = new List<KeyValuePair<int, object>>();
            list.Add(new KeyValuePair<int, object>(1000, Class ?? ""));
            list.Add(new KeyValuePair<int, object>(1000, SchemaMarker));
            list.Add(new KeyValuePair<int, object>(1000, Guid ?? ""));
            foreach (KeyValuePair<string, string> kv in Values)
            {
                list.Add(new KeyValuePair<int, object>(1000, kv.Key));
                list.Add(new KeyValuePair<int, object>(1000, kv.Value));
            }
            return list;
        }

        // Accepts the v2 layout and the legacy WENTADUCT record
        // (class, flow, velocity, shape code, size label). Returns null for
        // anything that is not a WENTA record.
        public static WentaTag FromXData(IList<KeyValuePair<int, object>> data)
        {
            if (data == null || data.Count == 0) return null;
            int i = 0;
            if (data[0].Key == 1001)
            {
                if (!string.Equals(data[0].Value as string, AppName, StringComparison.Ordinal))
                    return null;
                i = 1;
            }
            if (i >= data.Count || data[i].Key != 1000) return null;
            var tag = new WentaTag { Class = data[i].Value as string };
            i++;
            if (i < data.Count && data[i].Key == 1000
                && string.Equals(data[i].Value as string, SchemaMarker, StringComparison.Ordinal))
            {
                i++;
                if (i < data.Count && data[i].Key == 1000) { tag.Guid = data[i].Value as string; i++; }
                while (i + 1 < data.Count)
                {
                    string key = data[i].Value as string;
                    object value = data[i + 1].Value;
                    if (key != null && value != null)
                        tag.Values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
                    i += 2;
                }
                return tag;
            }

            tag.Legacy = true;
            var nums = new List<double>();
            int shapeCode = -1;
            string label = null;
            for (; i < data.Count; i++)
            {
                if (data[i].Key == 1040) nums.Add(Convert.ToDouble(data[i].Value, CultureInfo.InvariantCulture));
                else if (data[i].Key == 1070) shapeCode = Convert.ToInt32(data[i].Value, CultureInfo.InvariantCulture);
                else if (data[i].Key == 1000) label = data[i].Value as string;
            }
            if (nums.Count > 0) tag.Set("flow_m3s", nums[0]);
            if (nums.Count > 1) tag.Set("velocity_ms", nums[1]);
            double[] mm = ParseSizeLabel(label);
            if (shapeCode == 1 && mm != null && mm.Length == 1)
            {
                tag.Set("shape", Sizing.ShapeRound);
                tag.Set("d_mm", mm[0]);
            }
            else if (shapeCode == 0 && mm != null && mm.Length == 2)
            {
                tag.Set("shape", Sizing.ShapeRectangular);
                tag.Set("w_mm", mm[0]);
                tag.Set("h_mm", mm[1]);
            }
            return tag;
        }

        // "Ø200 mm" -> {200}; "400×200 mm" -> {400, 200}; anything else -> null.
        public static double[] ParseSizeLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string s = label.Replace("mm", "").Replace("Ø", "").Trim();
            string[] parts = s.Split(new[] { '×', 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2) return null;
            var r = new double[parts.Length];
            for (int k = 0; k < parts.Length; k++)
            {
                if (!double.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r[k]))
                    return null;
            }
            return r;
        }
    }

    public sealed class DrawnDuct
    {
        public string Handle;
        public readonly List<Point2> PointsMm = new List<Point2>();
        public CrossSection Section;
    }

    public sealed class DrawnMarker
    {
        public string Handle;
        public string Class;
        public Point2 AtMm;
        public double FlowM3s;
    }

    public sealed class DrawingTraceOptions
    {
        public double SnapMm = 1.0;
        public double MarkerToleranceMm = 150.0;
        public double DefaultDiameterMm = 200.0;
        public string Name = "drawing";
    }

    public sealed class DrawingTrace
    {
        public TracedSystem System;
        public readonly Dictionary<string, List<string>> ChainHandles = new Dictionary<string, List<string>>();
        public readonly Dictionary<string, string> DuctChain = new Dictionary<string, string>();
        public readonly Dictionary<string, string> TerminalMarker = new Dictionary<string, string>();
        public readonly Dictionary<string, string> MarkerTerminal = new Dictionary<string, string>();
        public readonly Dictionary<string, string> DamperChain = new Dictionary<string, string>();
        public readonly List<string> Warnings = new List<string>();

        public Network Network { get { return System.Network; } }

        public static DrawingTrace Build(IList<DrawnDuct> ducts, IList<DrawnMarker> markers,
            DrawingTraceOptions options = null)
        {
            options = options ?? new DrawingTraceOptions();
            markers = markers ?? new List<DrawnMarker>();
            if (ducts == null || ducts.Count == 0)
                throw new WentaException("no duct centrelines selected");

            var polylines = new List<Polyline>(ducts.Count);
            foreach (DrawnDuct d in ducts)
            {
                var pts = new List<Point2>(d.PointsMm.Count);
                foreach (Point2 p in d.PointsMm) pts.Add(ToM(p));
                polylines.Add(new Polyline(pts));
            }

            var opts = new TraceOptions
            {
                Snap = options.SnapMm / 1000.0,
                DefaultDiameter = options.DefaultDiameterMm / 1000.0,
                TeeSectionFromUpstream = true,
                StraightByDirection = true,
            };

            DrawnMarker source = null;
            foreach (DrawnMarker m in markers)
            {
                if (m.Class != WentaTag.ClassSource) continue;
                if (source != null)
                    throw new WentaException("more than one source marker in the selection");
                source = m;
            }
            if (source != null) opts.SourceAt = ToM(source.AtMm);

            TracedSystem first = Topology.Trace(polylines, opts);
            var result = new DrawingTrace();

            if (source != null && first.SourcePoint.DistanceTo(ToM(source.AtMm)) * 1000.0 > options.MarkerToleranceMm)
                result.Warnings.Add("source marker is " + Mm(first.SourcePoint.DistanceTo(ToM(source.AtMm)) * 1000.0)
                    + " mm from the nearest open duct end; that end was used");

            foreach (Chain ch in first.Chains)
            {
                var handles = new List<string>();
                CrossSection section = null;
                bool mixed = false;
                for (int k = 1; k < ch.Points.Count; k++)
                {
                    Point2 a = ch.Points[k - 1], b = ch.Points[k];
                    Point2 mid = new Point2((a.X + b.X) * 500.0, (a.Y + b.Y) * 500.0);
                    DrawnDuct owner = OwnerOf(ducts, mid, Math.Max(options.SnapMm, 1e-6));
                    if (owner == null) continue;
                    if (!handles.Contains(owner.Handle)) handles.Add(owner.Handle);
                    if (!result.DuctChain.ContainsKey(owner.Handle ?? ""))
                        result.DuctChain[owner.Handle ?? ""] = ch.Id;
                    if (owner.Section != null)
                    {
                        if (section == null) section = owner.Section;
                        else if (!SameSection(section, owner.Section)) mixed = true;
                    }
                }
                result.ChainHandles[ch.Id] = handles;
                if (section != null)
                {
                    opts.Sections[ch.Id] = section;
                    Round r = section as Round;
                    opts.Diameters[ch.Id] = r != null ? r.Diameter : section.HydraulicDiameter;
                }
                if (mixed)
                    result.Warnings.Add("run " + ch.Id + " joins ducts of different sizes without a tee; the first size was used");
            }

            var terminalMarkers = new List<DrawnMarker>();
            foreach (DrawnMarker m in markers)
                if (m.Class == WentaTag.ClassTerminal) terminalMarkers.Add(m);

            var pairs = new List<KeyValuePair<double, KeyValuePair<string, DrawnMarker>>>();
            foreach (KeyValuePair<string, Point2> kv in first.TerminalPoints)
                foreach (DrawnMarker m in terminalMarkers)
                {
                    double dmm = kv.Value.DistanceTo(ToM(m.AtMm)) * 1000.0;
                    if (dmm <= options.MarkerToleranceMm)
                        pairs.Add(new KeyValuePair<double, KeyValuePair<string, DrawnMarker>>(
                            dmm, new KeyValuePair<string, DrawnMarker>(kv.Key, m)));
                }
            pairs.Sort(delegate (KeyValuePair<double, KeyValuePair<string, DrawnMarker>> x,
                                 KeyValuePair<double, KeyValuePair<string, DrawnMarker>> y)
            {
                int c = x.Key.CompareTo(y.Key);
                return c != 0 ? c : string.CompareOrdinal(x.Value.Key, y.Value.Key);
            });
            var usedMarkers = new HashSet<DrawnMarker>();
            foreach (var p in pairs)
            {
                string tid = p.Value.Key;
                DrawnMarker m = p.Value.Value;
                if (opts.Flows.ContainsKey(tid) || usedMarkers.Contains(m)) continue;
                opts.Flows[tid] = m.FlowM3s;
                usedMarkers.Add(m);
                result.TerminalMarker[tid] = m.Handle;
                result.MarkerTerminal[m.Handle ?? ""] = tid;
            }

            var missing = new List<string>();
            foreach (KeyValuePair<string, Point2> kv in first.TerminalPoints)
            {
                double f;
                if (!opts.Flows.TryGetValue(kv.Key, out f) || f <= 0.0)
                    missing.Add("(" + Mm(kv.Value.X * 1000.0) + ", " + Mm(kv.Value.Y * 1000.0) + ")");
            }
            if (missing.Count > 0)
                throw new WentaException("open duct end(s) without a terminal flow: "
                    + string.Join(" ", missing.ToArray())
                    + "; tag each with WENTATAG Terminal");
            foreach (DrawnMarker m in terminalMarkers)
                if (!usedMarkers.Contains(m))
                    result.Warnings.Add("terminal marker at (" + Mm(m.AtMm.X) + ", " + Mm(m.AtMm.Y)
                        + ") is not on an open duct end; ignored");

            result.System = Topology.Trace(polylines, opts);
            result.System.Network.Name = options.Name;

            foreach (DrawnMarker m in markers)
            {
                if (m.Class != WentaTag.ClassDamper) continue;
                string chain = result.NearestChain(m.AtMm, options.MarkerToleranceMm);
                if (chain == null)
                    result.Warnings.Add("damper marker at (" + Mm(m.AtMm.X) + ", " + Mm(m.AtMm.Y)
                        + ") is not on a traced duct; ignored");
                else
                    result.DamperChain[m.Handle ?? ""] = chain;
            }
            return result;
        }

        public string NearestChain(Point2 atMm, double toleranceMm)
        {
            string best = null;
            double bestD = double.PositiveInfinity;
            Point2 p = ToM(atMm);
            foreach (Chain ch in System.Chains)
                for (int k = 1; k < ch.Points.Count; k++)
                {
                    double d = SegmentDistance(p, ch.Points[k - 1], ch.Points[k]) * 1000.0;
                    if (d < bestD) { bestD = d; best = ch.Id; }
                }
            return bestD <= toleranceMm ? best : null;
        }

        // The traced duct (chain) whose outlet feeds the terminal, or null.
        public string ChainFeeding(string terminalId)
        {
            foreach (string pred in Network.Predecessors(Network.PortNodeId(terminalId, "inlet")))
            {
                int colon = pred.IndexOf(':');
                if (colon > 0) return pred.Substring(0, colon);
            }
            return null;
        }

        // Terminals reached from a chain without passing a tee (the chain
        // feeds them directly), used to assign dampers to branches.
        public string TerminalFedBy(string chainId)
        {
            foreach (string next in Network.Successors(Network.PortNodeId(chainId, "outlet")))
            {
                int colon = next.IndexOf(':');
                if (colon <= 0) continue;
                string cid = next.Substring(0, colon);
                if (Network.Components[cid] is Terminal) return cid;
            }
            return null;
        }

        public List<string> CriticalChains(PressureReport report)
        {
            var list = new List<string>();
            foreach (PressureReportRow r in report.Rows)
                if (r.Kind == "RigidDuct" && !list.Contains(r.ComponentId)) list.Add(r.ComponentId);
            return list;
        }

        public Chain ChainById(string id)
        {
            foreach (Chain c in System.Chains) if (c.Id == id) return c;
            return null;
        }

        internal static Point2 ToM(Point2 mm) { return new Point2(mm.X / 1000.0, mm.Y / 1000.0); }

        internal static string Mm(double v)
        {
            return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
        }

        private static bool SameSection(CrossSection a, CrossSection b)
        {
            Round ra = a as Round, rb = b as Round;
            if (ra != null && rb != null) return Math.Abs(ra.Diameter - rb.Diameter) < 1e-9;
            Rectangular qa = a as Rectangular, qb = b as Rectangular;
            if (qa != null && qb != null)
                return Math.Abs(qa.Width - qb.Width) < 1e-9 && Math.Abs(qa.Height - qb.Height) < 1e-9;
            return false;
        }

        private static DrawnDuct OwnerOf(IList<DrawnDuct> ducts, Point2 pointMm, double tolMm)
        {
            DrawnDuct best = null;
            double bestD = double.PositiveInfinity;
            foreach (DrawnDuct d in ducts)
                for (int k = 1; k < d.PointsMm.Count; k++)
                {
                    double dist = SegmentDistance(pointMm, d.PointsMm[k - 1], d.PointsMm[k]);
                    if (dist < bestD) { bestD = dist; best = d; }
                }
            return bestD <= tolMm ? best : null;
        }

        internal static double SegmentDistance(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 > 0.0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2 : 0.0;
            if (t < 0.0) t = 0.0; else if (t > 1.0) t = 1.0;
            return p.DistanceTo(new Point2(a.X + t * dx, a.Y + t * dy));
        }
    }

    public sealed class ChainSizing
    {
        public string ChainId;
        public double FlowM3s;
        public BatchSizingResult Batch;
        public CrossSection Snapped;
        public double VelocityMs;
        public string Label;
    }

    public static class DrawingSizing
    {
        // Sizes every traced run from the flow the solver propagated into it.
        // The template supplies method, shape and targets; each run's flow
        // (and length, for the pressure-budget method) is filled in.
        public static List<ChainSizing> Plan(DrawingTrace trace, BatchSizingRequest template,
            Standard standard = Standard.En1505_1506, Fluid fluid = null)
        {
            if (trace == null) throw new WentaException("trace is null");
            if (template == null) template = new BatchSizingRequest();
            fluid = fluid ?? Fluid.StandardAir();
            trace.Network.Solve(fluid);

            var requests = new List<BatchSizingRequest>();
            foreach (Chain ch in trace.System.Chains)
            {
                Component c = trace.Network.Components[ch.Id];
                requests.Add(new BatchSizingRequest
                {
                    Id = ch.Id,
                    FlowrateM3s = c.InletFlowrate() ?? 0.0,
                    Shape = template.Shape,
                    Method = template.Method,
                    TargetVelocity = template.TargetVelocity,
                    TargetPaPerM = template.TargetPaPerM,
                    LengthM = ch.LengthM,
                    BudgetPa = template.BudgetPa,
                    SpaceType = template.SpaceType,
                    AspectRatio = template.AspectRatio,
                    AbsoluteRoughnessM = template.AbsoluteRoughnessM,
                });
            }

            List<BatchSizingResult> sized = BatchSizing.Size(requests, standard, fluid);
            var plan = new List<ChainSizing>(sized.Count);
            for (int i = 0; i < sized.Count; i++)
            {
                BatchSizingResult b = sized[i];
                var cs = new ChainSizing { ChainId = b.Id, FlowM3s = requests[i].FlowrateM3s, Batch = b };
                if (b.Error == null)
                {
                    if (b.SnappedRoundMm.HasValue)
                        cs.Snapped = new Round(b.SnappedRoundMm.Value / 1000.0);
                    else if (b.SnappedRectMm != null)
                        cs.Snapped = new Rectangular(b.SnappedRectMm[0] / 1000.0, b.SnappedRectMm[1] / 1000.0);
                    else
                        cs.Snapped = b.Result.Section;
                    cs.VelocityMs = cs.FlowM3s / cs.Snapped.Area;
                    cs.Label = Label(cs.Snapped, cs.FlowM3s, cs.VelocityMs);
                }
                plan.Add(cs);
            }
            return plan;
        }

        public static string SizeText(CrossSection s)
        {
            Round r = s as Round;
            if (r != null)
                return "Ø" + Math.Round(r.Diameter * 1000.0).ToString("0", CultureInfo.InvariantCulture);
            Rectangular q = s as Rectangular;
            if (q != null)
                return Math.Round(q.Width * 1000.0).ToString("0", CultureInfo.InvariantCulture) + "×"
                    + Math.Round(q.Height * 1000.0).ToString("0", CultureInfo.InvariantCulture);
            return "?";
        }

        public static string Label(CrossSection s, double flowM3s, double velocityMs)
        {
            return SizeText(s) + "  " + flowM3s.ToString("0.000", CultureInfo.InvariantCulture)
                + " m³/s  " + velocityMs.ToString("0.00", CultureInfo.InvariantCulture) + " m/s";
        }
    }

    public sealed class DamperSetting
    {
        public string MarkerHandle;
        public string ChainId;
        public string TerminalId;
        public BalancingHint Hint;
        public bool NeedsHardware;
    }

    public static class DrawingBalancing
    {
        // Per-terminal balancing hints (ReFit.Hints) plus, for every damper
        // marker, the hint of the terminal its run feeds directly.
        public static List<DamperSetting> Dampers(DrawingTrace trace, List<BalancingHint> hints)
        {
            var byTerminal = new Dictionary<string, BalancingHint>();
            foreach (BalancingHint h in hints) byTerminal[h.TerminalId] = h;
            var list = new List<DamperSetting>();
            foreach (KeyValuePair<string, string> kv in trace.DamperChain)
            {
                var s = new DamperSetting { MarkerHandle = kv.Key, ChainId = kv.Value };
                s.TerminalId = trace.TerminalFedBy(kv.Value);
                BalancingHint h;
                if (s.TerminalId != null && byTerminal.TryGetValue(s.TerminalId, out h))
                {
                    s.Hint = h;
                    s.NeedsHardware = NeedsHardware(h);
                }
                list.Add(s);
            }
            return list;
        }

        // A butterfly damper fully closed (0 % open per the clamped
        // inversion) cannot absorb the surplus: the branch needs a
        // hardware change (smaller duct, orifice or a different damper).
        public static bool NeedsHardware(BalancingHint h)
        {
            return h.SurplusDpPa > 0.0 && h.DamperOpenPercent <= 0.0;
        }
    }

    public sealed class RoomSound
    {
        public string TerminalId;
        public string ChainId;
        public double VelocityMs;
        public double DiameterM;
        public double SoundPowerDb;
        public double PressureLevelDb;
        public double NcLimit;
        public bool Ok;
    }

    public static class DrawingSound
    {
        // Regenerated noise of the run feeding each terminal (Sound.
        // RegeneratedNoiseRound with the run's hydraulic diameter), carried
        // into the room with the room equation (Sound.DuctPressureLevel) and
        // checked against the space type's NC limit. Sorted worst first
        // (largest exceedance of the limit).
        public static List<RoomSound> Rooms(DrawingTrace trace, string spaceType,
            double roomSurfaceAreaM2, double absorption, Fluid fluid = null)
        {
            fluid = fluid ?? Fluid.StandardAir();
            double limit;
            if (!Sound.NoiseLimitsNc.TryGetValue(spaceType, out limit))
                throw new WentaException("unknown space type '" + spaceType + "'");
            trace.Network.Solve(fluid);
            var rooms = new List<RoomSound>();
            foreach (KeyValuePair<string, Component> kv in trace.Network.Components)
            {
                if (!(kv.Value is Terminal)) continue;
                string chain = trace.ChainFeeding(kv.Key);
                if (chain == null) continue;
                RigidDuct duct = (RigidDuct)trace.Network.Components[chain];
                double v = (duct.InletFlowrate() ?? 0.0) / duct.CrossSection.Area;
                double d = duct.CrossSection.HydraulicDiameter;
                double lw = Sound.RegeneratedNoiseRound(v, d, fluid.Density);
                double lp = Sound.DuctPressureLevel(lw, roomSurfaceAreaM2, absorption);
                rooms.Add(new RoomSound
                {
                    TerminalId = kv.Key,
                    ChainId = chain,
                    VelocityMs = v,
                    DiameterM = d,
                    SoundPowerDb = lw,
                    PressureLevelDb = lp,
                    NcLimit = limit,
                    Ok = Sound.NcOk(spaceType, lp),
                });
            }
            rooms.Sort(delegate (RoomSound a, RoomSound b)
            {
                int c = (b.PressureLevelDb - b.NcLimit).CompareTo(a.PressureLevelDb - a.NcLimit);
                return c != 0 ? c : string.CompareOrdinal(a.TerminalId, b.TerminalId);
            });
            return rooms;
        }
    }

    public sealed class InsulationSizing
    {
        public double DiameterM;
        public double RequiredM;
        public double SelectedM;
        public double HeatLossWPerM;   // with the selected thickness
    }

    public static class DrawingInsulation
    {
        public const string Condensation = "condensation";
        public const string HeatLoss = "heatloss";

        // WENTAINSULATE for one round run: the EN ISO 12241 thickness from
        // Insulation (condensation or heat-loss purpose), snapped to the
        // standard step, and the heat loss with that step. Insulation's
        // model is cylindrical, so rectangular sections are refused rather
        // than approximated. When no insulation is required (the bare duct
        // already meets the purpose) SelectedM is 0, not the smallest step.
        public static InsulationSizing Size(CrossSection section, string purpose, string material,
            double airTempC, double ambientTempC, double dewPointC, double targetWPerM)
        {
            Round r = section as Round;
            if (r == null)
                throw new WentaException("insulation is sized for round ducts only (got "
                    + DrawingSizing.SizeText(section) + ")");
            double k;
            if (!Insulation.MaterialConductivity(material, out k))
                throw new WentaException("unknown insulation material '" + material + "'");
            double req;
            if (purpose == Condensation)
                req = Insulation.RequiredThicknessCondensation(airTempC, dewPointC, ambientTempC, k,
                    r.Diameter, Insulation.DefaultInnerHtc, Insulation.DefaultOuterHtc);
            else if (purpose == HeatLoss)
                req = Insulation.RequiredThicknessHeatLoss(airTempC, ambientTempC, targetWPerM, k,
                    r.Diameter, Insulation.DefaultInnerHtc, Insulation.DefaultOuterHtc);
            else
                throw new WentaException("unknown insulation purpose '" + purpose + "'");
            double sel = req > 0.0 ? Insulation.SelectThickness(req) : 0.0;
            return new InsulationSizing
            {
                DiameterM = r.Diameter,
                RequiredM = req,
                SelectedM = sel,
                HeatLossWPerM = Insulation.HeatLossWithInsulation(airTempC, ambientTempC, k, r.Diameter, sel,
                    Insulation.DefaultInnerHtc, Insulation.DefaultOuterHtc),
            };
        }
    }
}
