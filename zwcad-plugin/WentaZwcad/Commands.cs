using System;
using System.IO;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;

[assembly: CommandClass(typeof(WentaZwcad.Commands))]

namespace WentaZwcad
{
    /// <summary>
    /// Wenta duct plugin for ZWCAD 2021 — now powered by Wenta.Core
    /// (the C# port of the wenta library: sizing, solver, catalog, BOM).
    /// All math calls go through Wenta.*; no formula lives in this file.
    /// </summary>
    public class Commands
    {
        private const string LogFile = "wenta_zwcad_test.txt";
        private const string XDataApp = "WENTA";

        [CommandMethod("WENTAHELLO")]
        public void Hello()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string msg = string.Format(
                "\nWenta duct plugin (wenta C# core, MIT). ZWCAD version: {0}",
                Application.Version);
            ed.WriteMessage(msg);
            Log(string.Format("WENTAHELLO ok  ZWCAD {0}", Application.Version));
        }

        [CommandMethod("WENTADUCT")]
        public void DrawDuct()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            // ---- shape keyword ------------------------------------------------
            PromptKeywordOptions shapeOpts = new PromptKeywordOptions("\nDuct shape [Round/Rectangular] <Rectangular>: ");
            shapeOpts.Keywords.Add("Round");
            shapeOpts.Keywords.Add("Rectangular");
            shapeOpts.Keywords.Default = "Rectangular";
            shapeOpts.AllowNone = true;
            PromptResult shapeRes = ed.GetKeywords(shapeOpts);
            if (shapeRes.Status != PromptStatus.OK && shapeRes.Status != PromptStatus.None)
                return;
            bool round = shapeRes.Status == PromptStatus.OK && shapeRes.StringResult == "Round";

            // ---- flow ----------------------------------------------------------
            PromptDoubleOptions flowOpts = new PromptDoubleOptions("\nDesign flow [m³/s]: ");
            flowOpts.DefaultValue = 0.1;
            flowOpts.AllowZero = false;
            flowOpts.AllowNegative = false;
            PromptDoubleResult flowRes = ed.GetDouble(flowOpts);
            if (flowRes.Status != PromptStatus.OK) return;
            double flow = flowRes.Value;

            // ---- target velocity ----------------------------------------------
            PromptDoubleOptions velOpts = new PromptDoubleOptions("\nTarget velocity [m/s]: ");
            velOpts.DefaultValue = 4.0;
            velOpts.AllowZero = false;
            velOpts.AllowNegative = false;
            PromptDoubleResult velRes = ed.GetDouble(velOpts);
            if (velRes.Status != PromptStatus.OK) return;
            double targetV = velRes.Value;

            // ---- size via the wenta core ----------------------------------------
            Wenta.Sizing.SizingResult r = Wenta.Sizing.VelocityMethod(
                flow,
                round ? Wenta.Sizing.ShapeRound : Wenta.Sizing.ShapeRectangular,
                targetV);

            // equal-friction Δp/m at the chosen section (for the label)
            double dpPerM = DpPerMeterAt(flow, r.Section);

            Database db = doc.Database;
            string label;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Entity outline;
                if (round)
                {
                    Wenta.Round rs = (Wenta.Round)r.Section;
                    // Drawing units are mm and the section sits in the 0..D square,
                    // like the rectangle below (the label is placed at top = D mm).
                    double d = rs.Diameter * 1000.0;
                    outline = new Circle(new Point3d(d / 2.0, d / 2.0, 0), Vector3d.ZAxis, d / 2.0);
                    label = string.Format("Ø{0} mm", Math.Round(d));
                }
                else
                {
                    Wenta.Rectangular rs = (Wenta.Rectangular)r.Section;
                    double w = rs.Width * 1000.0, h = rs.Height * 1000.0;
                    Polyline pl = new Polyline(4);
                    pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(w, 0), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(w, h), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(0, h), 0, 0, 0);
                    pl.Closed = true;
                    outline = pl;
                    label = string.Format("{0}×{1} mm", Math.Round(w), Math.Round(h));
                }
                ms.AppendEntity(outline);
                tr.AddNewlyCreatedDBObject(outline, true);

                // ---- annotation -------------------------------------------------
                double top = round ? ((Wenta.Round)r.Section).Diameter * 1000.0
                                   : ((Wenta.Rectangular)r.Section).Height * 1000.0;
                DBText text = new DBText();
                text.Position = new Point3d(0, top + 30, 0);
                text.Height = 40;
                text.TextString = string.Format("{0}  {1:0.000} m³/s  {2:0.00} m/s  {3:0.00} Pa/m",
                    label, flow, r.Velocity, dpPerM);
                ms.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                // ---- design record in XData --------------------------------------
                AttachXData(tr, db, outline, flow, r.Velocity, round ? 1 : 0, label);

                tr.Commit();
            }

            ed.WriteMessage(string.Format(
                "\nSized: {0} · {1:0.000} m³/s · v={2:0.00} m/s (target {3:0.0}) · {4:0.00} Pa/m",
                label, flow, r.Velocity, targetV, dpPerM));
            Log(string.Format("WENTADUCT ok  {0}  {1:0.000} m3/s  {2:0.00} m/s  {3:0.0000} Pa/m",
                label, flow, r.Velocity, dpPerM));
        }

        [CommandMethod("WENTACATALOG")]
        public void CatalogInfo()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            string path = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "example-generic.json");
            if (!File.Exists(path))
            {
                ed.WriteMessage("\nNo example catalog beside the DLL: " + path);
                return;
            }
            try
            {
                Wenta.ZetaCatalog cat = Wenta.ZetaCatalog.Load(path);
                Wenta.ZetaCatalog.CatalogEntry hit =
                    cat.Match("rect_elbow", new double[] { 400, 200 });
                string msg = string.Format(
                    "\nCatalog '{0}' (v{1}) loaded: {2} entries. " +
                    "rect_elbow 400×200 → ζ={3} (source: {4})",
                    cat.Name, cat.Version, cat.Fittings.Count,
                    cat.ZetaFor("rect_elbow", new double[] { 400, 200 }),
                    hit != null ? hit.Source : "(correlation fallback)");
                ed.WriteMessage(msg);
                Log(string.Format("WENTACATALOG ok  {0} entries", cat.Fittings.Count));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nCatalog load failed: " + ex.Message);
                Log("WENTACATALOG FAILED " + ex.Message);
            }
        }

        [CommandMethod("WENTABOM")]
        public void BomDemo()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            // Reference tee network (same topology as the parity vector):
            // AHU -> D315 duct 20 m -> tee -> (D200 duct 5 m -> T 0.06) +
            //                             (flex D125 3 m -> T 0.04)
            var net = new Wenta.Network { Name = "bom-demo" };
            net.Add("ahu", new Wenta.Source("AHU"));
            net.Add("duct", new Wenta.RigidDuct("duct", new Wenta.Round(0.315), 20.0));
            net.Add("tee", new Wenta.Tee("tee", new Wenta.Round(0.315), 0.1, 0.4));
            net.Add("d2", new Wenta.RigidDuct("d2", new Wenta.Round(0.2), 5.0));
            net.Add("flex", new Wenta.FlexDuct("flex", 0.125, 3.0, 2.0, 100.0));
            net.Add("t1", new Wenta.Terminal("t1", 0.06));
            net.Add("t2", new Wenta.Terminal("t2", 0.04));
            net.Connect("ahu", "duct");
            net.Connect("duct", "tee");
            net.Connect("tee.straight", "d2");
            net.Connect("tee.branch", "flex");
            net.Connect("d2", "t1");
            net.Connect("flex", "t2");

            double dp = net.Solve();
            Wenta.Bom bom = Wenta.Bom.Build(net);

            string csvPath = Path.Combine(Path.GetTempPath(), "wenta_bom.csv");
            File.WriteAllText(csvPath, bom.ToCsv());

            ed.WriteMessage(string.Format(
                "\nNetwork solved: critical path {0:0.00} Pa. BOM: {1} rows, " +
                "{2:0.0} m duct, {3:0.00} m² sheet metal. CSV: {4}",
                dp, bom.Rows.Count, bom.TotalLength, bom.TotalArea, csvPath));
            Log(string.Format("WENTABOM ok  {0} rows  {1:0.00} Pa", bom.Rows.Count, dp));
        }

        // ---------------------------------------------------------------- helpers

        private static double DpPerMeterAt(double flow, Wenta.CrossSection s)
        {
            Wenta.Fluid air = Wenta.Fluid.StandardAir();
            double v = flow / s.Area;
            double re = Wenta.Friction.Reynolds(v, s.HydraulicDiameter, air.KinematicViscosity);
            double f = Wenta.Friction.FrictionFactor(re,
                Wenta.Friction.RelativeRoughness(0.0001, s.HydraulicDiameter));
            return Wenta.Losses.StraightPressureDrop(f, 1.0, s.HydraulicDiameter, v, air.Density);
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), LogFile),
                    string.Format("{0:u}  {1}\r\n", DateTime.Now, message));
            }
            catch (System.Exception) { }
        }

        private static void AttachXData(Transaction tr, Database db, Entity e,
            double flow, double velocity, int shapeCode, string sizeLabel)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!rat.Has(XDataApp))
            {
                var ra = new RegAppTableRecord();
                ra.Name = XDataApp;
                rat.Add(ra);
                tr.AddNewlyCreatedDBObject(ra, true);
            }
            e.XData = new ResultBuffer(
                new TypedValue(1001, XDataApp),
                new TypedValue(1000, "duct"),
                new TypedValue(1040, flow),
                new TypedValue(1040, velocity),
                new TypedValue(1070, shapeCode),
                new TypedValue(1000, sizeLabel));
        }
    }
}
