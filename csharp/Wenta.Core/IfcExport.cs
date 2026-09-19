using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wenta
{
    /// <summary>Options for <see cref="IfcExport"/>.</summary>
    public sealed class IfcExportOptions
    {
        /// <summary>IfcProject.Name (also seeds the project GlobalId).</summary>
        public string ProjectName = "Wenta";
        /// <summary>IfcSite.Name.</summary>
        public string SiteName = "Site";
        /// <summary>IfcBuilding.Name.</summary>
        public string BuildingName = "Building";
        /// <summary>IfcBuildingStorey.Name.</summary>
        public string StoreyName = "Level 0";
        /// <summary>Storey elevation [m] (IfcBuildingStorey.Elevation and the
        /// storey placement's Z, relative to the building).</summary>
        public double ElevationM = 0.0;
        /// <summary>Duct centreline height above the storey [m]. Absolute
        /// centreline Z = ElevationM + DuctCentreHeightM.</summary>
        public double DuctCentreHeightM = 3.0;
        /// <summary>FILE_NAME author (header only).</summary>
        public string Author = "";
        /// <summary>FILE_NAME organization (header only).</summary>
        public string Organisation = "";
        /// <summary>FILE_NAME name used by <see cref="IfcExport.ToIfc"/>;
        /// <see cref="IfcExport.Save"/> uses the target file name instead.</summary>
        public string FileName = "wenta.ifc";
    }

    /// <summary>IFC4 export of a <see cref="TracedSystem"/> as an ISO 10303-21
    /// STEP Physical File (issue #61, library half; the CAD command is plugin
    /// work). Dependency-free: hand-written SPF text, no IFC toolkit.
    ///
    /// Emitted structure (one instance per line, sequential <c>#n=</c> ids):
    /// <list type="bullet">
    /// <item>HEADER: FILE_DESCRIPTION 'ViewDefinition [ReferenceView_V1.2]',
    /// FILE_NAME (name, local timestamp, author, organisation), FILE_SCHEMA IFC4.</item>
    /// <item>IfcProject with IfcUnitAssignment (metre, m², m³, second, radian,
    /// derived m³/s) and a 3D 'Model' IfcGeometricRepresentationContext plus a
    /// 'Body' sub-context.</item>
    /// <item>IfcSite → IfcBuilding → IfcBuildingStorey (IfcRelAggregates). The
    /// storey placement is at Z = <see cref="IfcExportOptions.ElevationM"/>;
    /// every product is placed relative to the storey.</item>
    /// <item>Per chain, one IfcDuctSegment (RIGIDSEGMENT) per straight leg
    /// between consecutive chain points, legs ordered upstream → downstream:
    /// IfcLocalPlacement at the leg start with the local Z axis along the
    /// leg, IfcExtrudedAreaSolid of an IfcCircleProfileDef (radius D/2)
    /// extruded by the leg length ('Body'/'SweptSolid'). Name =
    /// "&lt;chainId&gt;-&lt;legIndex&gt;", Tag = chain id.</item>
    /// <item>Per <see cref="Tee"/> an IfcDuctFitting (JUNCTION); per
    /// <see cref="Terminal"/> an IfcAirTerminal (DIFFUSER); per
    /// <see cref="Source"/> an IfcBuildingElementProxy named "Source". Each
    /// is a short vertical cylinder centred on the component's vertex
    /// (the issue only asks for segments/fittings/terminals; the source is
    /// kept as a neutral proxy rather than guessing an AHU/fan class).</item>
    /// <item>All products contained in the storey via
    /// IfcRelContainedInSpatialStructure.</item>
    /// <item>One IfcPropertySet "Pset_Wenta" per product: FlowRate
    /// (IfcVolumetricFlowRateMeasure, m³/s), Diameter
    /// (IfcPositiveLengthMeasure) and, for segments, Length (leg length).</item>
    /// </list>
    ///
    /// Component positions are not stored on the network, so they are
    /// recovered from the chain endpoints plus network connectivity (see
    /// <see cref="ComponentPositions"/>): coincident chain endpoints
    /// (within 1e-6 m) form a vertex; a vertex with three chain ends is a
    /// tee, one with a single end is the source or a terminal; the mapping
    /// is fixed by walking the tree from any tee/end chain.
    ///
    /// Flowrates: <see cref="Solver.PropagateFlowrates"/> is always run on
    /// <c>system.Network</c> before export (fluid-independent and idempotent;
    /// it does not touch pressure drops), so callers need not solve first.
    ///
    /// GlobalIds are the standard 22-character IFC base-64 compression of a
    /// GUID whose 16 bytes are the MD5 of "wenta:&lt;key&gt;" (MD5 is used as a
    /// stable hash, not for security), so re-exporting the same system gives
    /// byte-identical output apart from the FILE_NAME timestamp.
    ///
    /// Numbers use the invariant culture and always contain a decimal point
    /// (STEP real syntax); strings double apostrophes and backslashes and
    /// encode non-ASCII as \X2\…\X0\; lines end in "\n".</summary>
    public static class IfcExport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Chain endpoints closer than this [m] are the same vertex.</summary>
        public const double VertexToleranceM = 1e-6;

        private const string IfcBase64 =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        // ---- public API ------------------------------------------------------

        /// <summary>Render <paramref name="system"/> as IFC4 SPF text. See the
        /// class summary for the structure. Runs
        /// <see cref="Solver.PropagateFlowrates"/> on the network.</summary>
        /// <exception cref="WentaException">null system/network/chains, a chain
        /// with fewer than two points or a non-positive diameter, a
        /// non-finite option value, or a network whose graph has a cycle.</exception>
        public static string ToIfc(TracedSystem system, IfcExportOptions opts = null)
        {
            if (opts == null) opts = new IfcExportOptions();
            return Build(system, opts, opts.FileName);
        }

        /// <summary>Write <see cref="ToIfc"/> output to <paramref name="path"/>
        /// (UTF-8 without BOM; the content is pure ASCII). FILE_NAME carries
        /// the file name of <paramref name="path"/>.</summary>
        public static void Save(TracedSystem system, string path, IfcExportOptions opts = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new WentaException("IfcExport.Save: path is empty");
            if (opts == null) opts = new IfcExportOptions();
            File.WriteAllText(path, Build(system, opts, Path.GetFileName(path)), Utf8NoBom);
        }

        /// <summary>2D position of every Source / Tee / Terminal component of
        /// <c>system.Network</c> that could be located from the chain
        /// endpoints (see class summary). Components that are not adjacent to
        /// any chain are absent from the result.</summary>
        public static Dictionary<string, Point2> ComponentPositions(TracedSystem system)
        {
            CheckSystem(system);
            Layout lay = Analyse(system);
            var result = new Dictionary<string, Point2>();
            foreach (KeyValuePair<string, int> kv in lay.CompVertex)
                result[kv.Key] = lay.Verts[kv.Value];
            return result;
        }

        /// <summary>Deterministic IFC GlobalId for <paramref name="key"/>:
        /// <see cref="CompressGuid"/> of a GUID built from MD5("wenta:" + key).</summary>
        public static string DeterministicGlobalId(string key)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("wenta:" + (key ?? "")));
                return CompressGuid(new Guid(hash));
            }
        }

        /// <summary>Standard IFC GUID compression: the 16 GUID bytes in RFC
        /// 4122 / IFC field order (Data1, Data2, Data3 big-endian, then Data4)
        /// encoded base-64 with the IFC alphabet <c>0-9A-Za-z_$</c>: the first
        /// byte as 2 characters, then five 3-byte groups as 4 characters each
        /// (22 characters, first character always '0'..'3').</summary>
        public static string CompressGuid(Guid guid)
        {
            byte[] le = guid.ToByteArray(); // Data1..Data3 little-endian
            byte[] b = new byte[16];
            b[0] = le[3]; b[1] = le[2]; b[2] = le[1]; b[3] = le[0];
            b[4] = le[5]; b[5] = le[4];
            b[6] = le[7]; b[7] = le[6];
            Array.Copy(le, 8, b, 8, 8);

            char[] outc = new char[22];
            EncodeBase64(b[0], outc, 0, 2);
            for (int g = 0; g < 5; g++)
            {
                int o = 1 + 3 * g;
                uint n = ((uint)b[o] << 16) | ((uint)b[o + 1] << 8) | b[o + 2];
                EncodeBase64(n, outc, 2 + 4 * g, 4);
            }
            return new string(outc);
        }

        /// <summary>Inverse of <see cref="CompressGuid"/>.</summary>
        /// <exception cref="WentaException">Not 22 characters, a character
        /// outside the IFC alphabet, or a first byte above 255.</exception>
        public static Guid ExpandGuid(string globalId)
        {
            if (globalId == null || globalId.Length != 22)
                throw new WentaException("IFC GlobalId must be 22 characters");
            byte[] b = new byte[16];
            uint first = DecodeBase64(globalId, 0, 2);
            if (first > 255)
                throw new WentaException("IFC GlobalId first character must be '0'..'3'");
            b[0] = (byte)first;
            for (int g = 0; g < 5; g++)
            {
                uint n = DecodeBase64(globalId, 2 + 4 * g, 4);
                int o = 1 + 3 * g;
                b[o] = (byte)(n >> 16);
                b[o + 1] = (byte)(n >> 8);
                b[o + 2] = (byte)n;
            }
            byte[] le = new byte[16];
            le[0] = b[3]; le[1] = b[2]; le[2] = b[1]; le[3] = b[0];
            le[4] = b[5]; le[5] = b[4];
            le[6] = b[7]; le[7] = b[6];
            Array.Copy(b, 8, le, 8, 8);
            return new Guid(le);
        }

        private static void EncodeBase64(uint number, char[] dst, int pos, int len)
        {
            for (int i = len - 1; i >= 0; i--)
            {
                dst[pos + i] = IfcBase64[(int)(number % 64)];
                number /= 64;
            }
        }

        private static uint DecodeBase64(string s, int pos, int len)
        {
            uint n = 0;
            for (int i = 0; i < len; i++)
            {
                int v = IfcBase64.IndexOf(s[pos + i]);
                if (v < 0)
                    throw new WentaException("IFC GlobalId has an illegal character '" + s[pos + i] + "'");
                n = n * 64 + (uint)v;
            }
            return n;
        }

        // ---- STEP text helpers ----------------------------------------------

        /// <summary>Sequential <c>#n=ENTITY(...);</c> line writer.</summary>
        private sealed class StepFile
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private int _next = 1;

            public int Add(string entity)
            {
                int id = _next++;
                _sb.Append('#').Append(id.ToString(Inv)).Append('=').Append(entity).Append(";\n");
                return id;
            }

            public string Data { get { return _sb.ToString(); } }
        }

        /// <summary>STEP real: invariant "R" with a guaranteed decimal point
        /// ("5." / "1.E-05"), as Part 21 requires.</summary>
        private static string Real(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new WentaException("IfcExport: non-finite number " + v.ToString("R", Inv));
            string s = v.ToString("R", Inv);
            int e = s.IndexOf('E');
            string mant = e >= 0 ? s.Substring(0, e) : s;
            string exp = e >= 0 ? s.Substring(e) : "";
            if (mant.IndexOf('.') < 0) mant += ".";
            return mant + exp;
        }

        /// <summary>STEP string literal: apostrophes and backslashes doubled,
        /// control characters dropped, non-ASCII as \X2\HHHH…\X0\.</summary>
        private static string Str(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('\'');
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == '\'') { sb.Append("''"); i++; }
                else if (ch == '\\') { sb.Append("\\\\"); i++; }
                else if (ch < 0x20 || ch == 0x7F) { i++; }
                else if (ch < 0x7F) { sb.Append(ch); i++; }
                else
                {
                    sb.Append("\\X2\\");
                    while (i < s.Length && s[i] >= 0x80)
                    {
                        sb.Append(((int)s[i]).ToString("X4", Inv));
                        i++;
                    }
                    sb.Append("\\X0\\");
                }
            }
            sb.Append('\'');
            return sb.ToString();
        }

        private static string Ref(int id) { return "#" + id.ToString(Inv); }

        private static string Refs(IList<int> ids)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('#').Append(ids[i].ToString(Inv));
            }
            return sb.ToString();
        }

        private static string Gid(string key) { return "'" + DeterministicGlobalId(key) + "'"; }

        private static string Point3(double x, double y, double z)
        {
            return "IFCCARTESIANPOINT((" + Real(x) + "," + Real(y) + "," + Real(z) + "))";
        }

        private static string Dir3(double x, double y, double z)
        {
            return "IFCDIRECTION((" + Real(x) + "," + Real(y) + "," + Real(z) + "))";
        }

        // ---- layout: where each component sits --------------------------------

        private sealed class Layout
        {
            public readonly List<Point2> Verts = new List<Point2>();
            public readonly List<int> Degree = new List<int>();      // chain ends per vertex
            public readonly List<double> Diameter = new List<double>(); // max incident chain D
            public int[] StartV;                                       // vertex at Points[0]
            public int[] EndV;                                         // vertex at Points[last]
            public bool[] Reversed;                                    // Points run downstream→upstream
            public readonly Dictionary<string, int> CompVertex = new Dictionary<string, int>();
        }

        private static void CheckSystem(TracedSystem system)
        {
            if (system == null)
                throw new WentaException("IfcExport: system is null");
            if (system.Network == null || system.Chains == null)
                throw new WentaException("IfcExport: system has no network or chains");
        }

        private static int VertexOf(Layout lay, Point2 p)
        {
            for (int i = 0; i < lay.Verts.Count; i++)
                if (lay.Verts[i].DistanceTo(p) <= VertexToleranceM) return i;
            lay.Verts.Add(p);
            lay.Degree.Add(0);
            lay.Diameter.Add(0.0);
            return lay.Verts.Count - 1;
        }

        /// <summary>Component id connected to the chain's inlet (predecessor
        /// out-port) or outlet (successor in-port); null if unknown.</summary>
        private static string NeighbourComponent(Network net, string chainId, bool inlet)
        {
            Component c;
            if (chainId == null || !net.Components.TryGetValue(chainId, out c)) return null;
            foreach (Port p in c.Ports)
            {
                if (p.IsIn != inlet || p.NodeId == null) continue;
                List<string> nb = inlet ? net.Predecessors(p.NodeId) : net.Successors(p.NodeId);
                foreach (string n in nb)
                {
                    int k = n.LastIndexOf(':');
                    if (k > 0) return n.Substring(0, k);
                }
                return null;
            }
            return null;
        }

        private static bool IsTee(Network net, string id)
        {
            Component c;
            return id != null && net.Components.TryGetValue(id, out c) && c is Tee;
        }

        private static void Assign(Layout lay, string id, int v)
        {
            if (id != null && !lay.CompVertex.ContainsKey(id)) lay.CompVertex[id] = v;
        }

        private static Layout Analyse(TracedSystem system)
        {
            Network net = system.Network;
            int n = system.Chains.Count;
            var lay = new Layout();
            lay.StartV = new int[n];
            lay.EndV = new int[n];
            lay.Reversed = new bool[n];
            string[] inComp = new string[n];
            string[] outComp = new string[n];

            for (int ci = 0; ci < n; ci++)
            {
                Chain ch = system.Chains[ci];
                if (ch == null || ch.Points == null || ch.Points.Count < 2)
                    throw new WentaException("IfcExport: chain " + (ch == null ? ci.ToString(Inv) : ch.Id)
                        + " has fewer than two points");
                if (!(ch.Diameter > 0.0))
                    throw new WentaException("IfcExport: chain " + ch.Id + " has non-positive diameter");
                int a = VertexOf(lay, ch.Points[0]);
                int b = VertexOf(lay, ch.Points[ch.Points.Count - 1]);
                lay.StartV[ci] = a;
                lay.EndV[ci] = b;
                lay.Degree[a]++;
                lay.Degree[b]++;
                if (ch.Diameter > lay.Diameter[a]) lay.Diameter[a] = ch.Diameter;
                if (ch.Diameter > lay.Diameter[b]) lay.Diameter[b] = ch.Diameter;
                inComp[ci] = NeighbourComponent(net, ch.Id, true);
                outComp[ci] = NeighbourComponent(net, ch.Id, false);
            }

            // Fix component -> vertex by propagation over the tree: a chain
            // with a known end fixes its other end; a chain joining a 3-end
            // vertex to a 1-end vertex puts its Tee neighbour on the former.
            bool[] done = new bool[n];
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int ci = 0; ci < n; ci++)
                {
                    if (done[ci]) continue;
                    int a = lay.StartV[ci], b = lay.EndV[ci];
                    if (a == b) { done[ci] = true; continue; } // degenerate loop chain
                    int va = -1, vb = -1;
                    bool haveIn = inComp[ci] != null && lay.CompVertex.TryGetValue(inComp[ci], out va);
                    bool haveOut = outComp[ci] != null && lay.CompVertex.TryGetValue(outComp[ci], out vb);
                    if (haveIn)
                    {
                        Assign(lay, outComp[ci], va == a ? b : a);
                        done[ci] = true; progress = true;
                    }
                    else if (haveOut)
                    {
                        Assign(lay, inComp[ci], vb == b ? a : b);
                        done[ci] = true; progress = true;
                    }
                    else if (lay.Degree[a] != lay.Degree[b])
                    {
                        int teeEnd = lay.Degree[a] > lay.Degree[b] ? a : b;
                        int oneEnd = teeEnd == a ? b : a;
                        bool inTee = IsTee(net, inComp[ci]);
                        bool outTee = IsTee(net, outComp[ci]);
                        if (inTee && !outTee)
                        {
                            Assign(lay, inComp[ci], teeEnd);
                            Assign(lay, outComp[ci], oneEnd);
                            done[ci] = true; progress = true;
                        }
                        else if (outTee && !inTee)
                        {
                            Assign(lay, outComp[ci], teeEnd);
                            Assign(lay, inComp[ci], oneEnd);
                            done[ci] = true; progress = true;
                        }
                    }
                }
            }
            // Anything left (e.g. a lone source→duct→terminal chain) follows the
            // Topology.Trace convention: Points[0] is the upstream end.
            for (int ci = 0; ci < n; ci++)
            {
                if (done[ci]) continue;
                Assign(lay, inComp[ci], lay.StartV[ci]);
                Assign(lay, outComp[ci], lay.EndV[ci]);
            }

            for (int ci = 0; ci < n; ci++)
            {
                int v;
                if (inComp[ci] != null && lay.CompVertex.TryGetValue(inComp[ci], out v)
                    && v == lay.EndV[ci] && v != lay.StartV[ci])
                    lay.Reversed[ci] = true;
            }
            return lay;
        }

        // ---- builder -----------------------------------------------------------

        /// <summary>Shared entity ids and export state.</summary>
        private sealed class Ctx
        {
            public StepFile F;
            public int DirZ, Identity, Body, StoreyPl;
            public double H; // duct centreline height above the storey
            public readonly Dictionary<string, int> Profiles = new Dictionary<string, int>();
            public readonly List<int> Products = new List<int>();
        }

        private static string Build(TracedSystem system, IfcExportOptions opts, string fileName)
        {
            CheckSystem(system);
            if (double.IsNaN(opts.ElevationM) || double.IsInfinity(opts.ElevationM)
                || double.IsNaN(opts.DuctCentreHeightM) || double.IsInfinity(opts.DuctCentreHeightM))
                throw new WentaException("IfcExport: ElevationM / DuctCentreHeightM must be finite");

            Network net = system.Network;
            Solver.PropagateFlowrates(net); // flows only; fluid-independent, idempotent
            Layout lay = Analyse(system);

            var f = new StepFile();
            var c = new Ctx();
            c.F = f;
            c.H = opts.DuctCentreHeightM;

            // shared geometry
            int origin = f.Add(Point3(0.0, 0.0, 0.0));
            c.DirZ = f.Add(Dir3(0.0, 0.0, 1.0));
            c.Identity = f.Add("IFCAXIS2PLACEMENT3D(" + Ref(origin) + ",$,$)");

            // units
            int uLen = f.Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
            int uArea = f.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            int uVol = f.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            int uTime = f.Add("IFCSIUNIT(*,.TIMEUNIT.,$,.SECOND.)");
            int uAngle = f.Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            int de1 = f.Add("IFCDERIVEDUNITELEMENT(" + Ref(uLen) + ",3)");
            int de2 = f.Add("IFCDERIVEDUNITELEMENT(" + Ref(uTime) + ",-1)");
            int uFlow = f.Add("IFCDERIVEDUNIT((" + Ref(de1) + "," + Ref(de2) + "),.VOLUMETRICFLOWRATEUNIT.,$)");
            int units = f.Add("IFCUNITASSIGNMENT(("
                + Refs(new int[] { uLen, uArea, uVol, uTime, uAngle, uFlow }) + "))");

            // representation contexts
            int ctx = f.Add("IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05," + Ref(c.Identity) + ",$)");
            c.Body = f.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,"
                + Ref(ctx) + ",$,.MODEL_VIEW.,$)");

            // project + spatial structure
            int project = f.Add("IFCPROJECT(" + Gid("project:" + opts.ProjectName) + ",$,"
                + Str(opts.ProjectName) + ",$,$,$,$,(" + Ref(ctx) + ")," + Ref(units) + ")");
            int sitePl = f.Add("IFCLOCALPLACEMENT($," + Ref(c.Identity) + ")");
            int site = f.Add("IFCSITE(" + Gid("site") + ",$," + Str(opts.SiteName) + ",$,$,"
                + Ref(sitePl) + ",$,$,.ELEMENT.,$,$,$,$,$)");
            int bldgPl = f.Add("IFCLOCALPLACEMENT(" + Ref(sitePl) + "," + Ref(c.Identity) + ")");
            int bldg = f.Add("IFCBUILDING(" + Gid("building") + ",$," + Str(opts.BuildingName) + ",$,$,"
                + Ref(bldgPl) + ",$,$,.ELEMENT.,$,$,$)");
            int storeyPt = f.Add(Point3(0.0, 0.0, opts.ElevationM));
            int storeyAx = f.Add("IFCAXIS2PLACEMENT3D(" + Ref(storeyPt) + ",$,$)");
            c.StoreyPl = f.Add("IFCLOCALPLACEMENT(" + Ref(bldgPl) + "," + Ref(storeyAx) + ")");
            int storey = f.Add("IFCBUILDINGSTOREY(" + Gid("storey") + ",$," + Str(opts.StoreyName) + ",$,$,"
                + Ref(c.StoreyPl) + ",$,$,.ELEMENT.," + Real(opts.ElevationM) + ")");
            f.Add("IFCRELAGGREGATES(" + Gid("rel:project-site") + ",$,$,$," + Ref(project) + ",(" + Ref(site) + "))");
            f.Add("IFCRELAGGREGATES(" + Gid("rel:site-building") + ",$,$,$," + Ref(site) + ",(" + Ref(bldg) + "))");
            f.Add("IFCRELAGGREGATES(" + Gid("rel:building-storey") + ",$,$,$," + Ref(bldg) + ",(" + Ref(storey) + "))");

            // duct segments: one per straight leg, upstream -> downstream
            for (int ci = 0; ci < system.Chains.Count; ci++)
            {
                Chain ch = system.Chains[ci];
                List<Point2> pts = ch.Points;
                if (lay.Reversed[ci])
                {
                    pts = new List<Point2>(ch.Points);
                    pts.Reverse();
                }
                double radius = ch.Diameter / 2.0;
                int profile = Profile(c, radius);
                Component duct;
                double? flow = net.Components.TryGetValue(ch.Id, out duct) ? FlowOf(duct) : null;

                int leg = 0;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Point2 a = pts[i], b = pts[i + 1];
                    double len = a.DistanceTo(b);
                    if (!(len > 0.0)) continue;
                    double dx = (b.X - a.X) / len, dy = (b.Y - a.Y) / len;

                    int loc = f.Add(Point3(a.X, a.Y, c.H));
                    int axis = f.Add(Dir3(dx, dy, 0.0));
                    int ax = f.Add("IFCAXIS2PLACEMENT3D(" + Ref(loc) + "," + Ref(axis) + "," + Ref(c.DirZ) + ")");
                    int pl = f.Add("IFCLOCALPLACEMENT(" + Ref(c.StoreyPl) + "," + Ref(ax) + ")");
                    int solid = f.Add("IFCEXTRUDEDAREASOLID(" + Ref(profile) + "," + Ref(c.Identity) + ","
                        + Ref(c.DirZ) + "," + Real(len) + ")");
                    int rep = f.Add("IFCSHAPEREPRESENTATION(" + Ref(c.Body) + ",'Body','SweptSolid',(" + Ref(solid) + "))");
                    int pds = f.Add("IFCPRODUCTDEFINITIONSHAPE($,$,(" + Ref(rep) + "))");

                    string key = "segment:" + ch.Id + ":" + leg.ToString(Inv);
                    string name = ch.Id + "-" + leg.ToString(Inv);
                    int seg = f.Add("IFCDUCTSEGMENT(" + Gid(key) + ",$," + Str(name) + ",$,$,"
                        + Ref(pl) + "," + Ref(pds) + "," + Str(ch.Id) + ",.RIGIDSEGMENT.)");
                    c.Products.Add(seg);
                    AddPset(c, key, seg, flow, ch.Diameter, len);
                    leg++;
                }
            }

            // fittings / terminals / source, in id order for determinism
            var ids = new List<string>(net.Components.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                Component comp = net.Components[id];
                bool isTee = comp is Tee, isTerm = comp is Terminal, isSrc = comp is Source;
                if (!isTee && !isTerm && !isSrc) continue;
                int v;
                if (!lay.CompVertex.TryGetValue(id, out v)) continue; // not adjacent to any chain
                Point2 p = lay.Verts[v];
                double d = lay.Diameter[v];
                if (!(d > 0.0)) d = 0.2;
                double? flow = FlowOf(comp);

                int pl, pds, product;
                string key;
                if (isTee)
                {
                    key = "fitting:" + id;
                    pds = Marker(c, p, 0.6 * d, 1.2 * d, out pl);
                    product = f.Add("IFCDUCTFITTING(" + Gid(key) + ",$," + Str(id) + ",$,$,"
                        + Ref(pl) + "," + Ref(pds) + "," + Str(id) + ",.JUNCTION.)");
                }
                else if (isTerm)
                {
                    key = "terminal:" + id;
                    pds = Marker(c, p, 0.75 * d, 0.1 * d, out pl);
                    product = f.Add("IFCAIRTERMINAL(" + Gid(key) + ",$," + Str(id) + ",$,$,"
                        + Ref(pl) + "," + Ref(pds) + "," + Str(id) + ",.DIFFUSER.)");
                }
                else
                {
                    key = "source:" + id;
                    pds = Marker(c, p, 0.75 * d, 1.5 * d, out pl);
                    product = f.Add("IFCBUILDINGELEMENTPROXY(" + Gid(key) + ",$,'Source',$,$,"
                        + Ref(pl) + "," + Ref(pds) + "," + Str(id) + ",$)");
                }
                c.Products.Add(product);
                AddPset(c, key, product, flow, d, null);
            }

            if (c.Products.Count > 0)
                f.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE(" + Gid("rel:storey-contains") + ",$,$,$,("
                    + Refs(c.Products) + ")," + Ref(storey) + ")");

            // assemble
            var sb = new StringBuilder(f.Data.Length + 512);
            sb.Append("ISO-10303-21;\n");
            sb.Append("HEADER;\n");
            sb.Append("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\n");
            sb.Append("FILE_NAME(").Append(Str(string.IsNullOrEmpty(fileName) ? "wenta.ifc" : fileName))
              .Append(',').Append(Str(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)))
              .Append(",(").Append(Str(opts.Author)).Append("),(").Append(Str(opts.Organisation)).Append("),")
              .Append("'Wenta.Core IfcExport','Wenta','');\n");
            sb.Append("FILE_SCHEMA(('IFC4'));\n");
            sb.Append("ENDSEC;\n");
            sb.Append("DATA;\n");
            sb.Append(f.Data);
            sb.Append("ENDSEC;\n");
            sb.Append("END-ISO-10303-21;\n");
            return sb.ToString();
        }

        /// <summary>Flow through a component [m³/s]: first inlet port, else
        /// first port (a Source only has an outlet); null if unset.</summary>
        private static double? FlowOf(Component comp)
        {
            double? q = comp.InletFlowrate();
            if (q == null && comp.Ports.Count > 0) q = comp.Ports[0].Flowrate;
            return q;
        }

        /// <summary>Shared IfcCircleProfileDef per radius.</summary>
        private static int Profile(Ctx c, double radius)
        {
            string key = Real(radius);
            int id;
            if (!c.Profiles.TryGetValue(key, out id))
            {
                id = c.F.Add("IFCCIRCLEPROFILEDEF(.AREA.,$,$," + key + ")");
                c.Profiles[key] = id;
            }
            return id;
        }

        /// <summary>Vertical cylinder of the given radius/height centred on
        /// (p, duct height); returns the IfcProductDefinitionShape id.</summary>
        private static int Marker(Ctx c, Point2 p, double radius, double height, out int placement)
        {
            StepFile f = c.F;
            // marker sizes are scaled diameters (0.6·D etc.): round away the
            // binary noise so the text stays readable (0.15, not 0.15000000000000002)
            radius = Math.Round(radius, 9);
            height = Math.Round(height, 9);
            int loc = f.Add(Point3(p.X, p.Y, c.H - height / 2.0));
            int ax = f.Add("IFCAXIS2PLACEMENT3D(" + Ref(loc) + ",$,$)");
            placement = f.Add("IFCLOCALPLACEMENT(" + Ref(c.StoreyPl) + "," + Ref(ax) + ")");
            int profile = Profile(c, radius);
            int solid = f.Add("IFCEXTRUDEDAREASOLID(" + Ref(profile) + "," + Ref(c.Identity) + ","
                + Ref(c.DirZ) + "," + Real(height) + ")");
            int rep = f.Add("IFCSHAPEREPRESENTATION(" + Ref(c.Body) + ",'Body','SweptSolid',(" + Ref(solid) + "))");
            return f.Add("IFCPRODUCTDEFINITIONSHAPE($,$,(" + Ref(rep) + "))");
        }

        /// <summary>"Pset_Wenta" on <paramref name="product"/>: FlowRate (if
        /// known), Diameter, Length (if given).</summary>
        private static void AddPset(Ctx c, string key, int product, double? flow, double diameter, double? length)
        {
            StepFile f = c.F;
            var props = new List<int>();
            if (flow != null)
                props.Add(f.Add("IFCPROPERTYSINGLEVALUE('FlowRate',$,IFCVOLUMETRICFLOWRATEMEASURE("
                    + Real(flow.Value) + "),$)"));
            props.Add(f.Add("IFCPROPERTYSINGLEVALUE('Diameter',$,IFCPOSITIVELENGTHMEASURE("
                + Real(diameter) + "),$)"));
            if (length != null)
                props.Add(f.Add("IFCPROPERTYSINGLEVALUE('Length',$,IFCPOSITIVELENGTHMEASURE("
                    + Real(length.Value) + "),$)"));
            int pset = f.Add("IFCPROPERTYSET(" + Gid("pset:" + key) + ",$,'Pset_Wenta',$,(" + Refs(props) + "))");
            f.Add("IFCRELDEFINESBYPROPERTIES(" + Gid("relpset:" + key) + ",$,$,$,(" + Ref(product) + "),"
                + Ref(pset) + ")");
        }
    }
}
