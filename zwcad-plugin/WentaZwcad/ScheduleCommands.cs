using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Wenta;

[assembly: CommandClass(typeof(WentaZwcad.ScheduleCommands))]

namespace WentaZwcad
{
    // #29: the schedule table and the flat patterns of the drawn network.
    // Both read the component records as WENTAREAD does; the quantities come
    // from Wenta.Bom, Wenta.Schedule and Wenta.Development.
    public class ScheduleCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        internal const string SchedLayer = "WENTA-SCHED";
        internal const string FabLayer = "WENTA-FAB";

        private static readonly double[] ColumnWidthsMm = { 1200, 700, 900, 4600, 1100, 1100, 1100, 1500 };
        private const double TextHeightMm = 120.0;
        private const double RowHeightMm = 260.0;

        private static Network Solved(Document doc, string cmd)
        {
            NetworkReadResult r = EditCommands.ReadDrawn(doc, cmd);
            if (r == null) return null;
            r.Network.Solve();
            return r.Network;
        }

        // The first table on WENTA-SCHED, or null.
        private static Table FindTable(Transaction tr, Database db)
        {
            foreach (ObjectId id in CadIo.ModelSpace(tr, db))
            {
                var t = tr.GetObject(id, OpenMode.ForRead) as Table;
                if (t != null && !t.IsErased && t.Layer == SchedLayer) return t;
            }
            return null;
        }

        [CommandMethod("WENTASCHEDULE")]
        public void ScheduleTableCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            try
            {
                Network net = Solved(doc, "WENTASCHEDULE");
                if (net == null) return;
                Bom bom = Bom.Build(net);
                ScheduleTable sched = Schedule.Build(net, bom);
                List<string[]> lines = sched.AllRows();

                bool reused;
                Point3d at;
                int nRows = lines.Count + 1, nCols;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Table table = FindTable(tr, db);
                    reused = table != null;
                    if (reused)
                    {
                        table.UpgradeOpen();
                        at = table.Position;
                    }
                    else
                    {
                        var po = new PromptPointOptions("\nSchedule insertion point (top left) <0,0>: ") { AllowNone = true };
                        PromptPointResult pr = ed.GetPoint(po);
                        if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
                        at = pr.Status == PromptStatus.OK ? pr.Value : Point3d.Origin;
                        CadIo.EnsureLayer(tr, db, SchedLayer, 7);
                        table = new Table { TableStyle = db.Tablestyle, Layer = SchedLayer };
                    }

                    // Row 0 is the title row; then header, rows and totals.
                    nCols = Schedule.Columns.Length;
                    table.SetSize(nRows, nCols);
                    table.Position = at;
                    for (int c = 0; c < nCols; c++) table.SetColumnWidth(c, ColumnWidthsMm[c]);
                    for (int r = 0; r < nRows; r++) table.SetRowHeight(r, RowHeightMm);
                    table.SetTextString(0, 0, sched.Title);
                    table.SetTextHeight(0, 0, TextHeightMm * 1.25);
                    for (int r = 0; r < lines.Count; r++)
                        for (int c = 0; c < nCols; c++)
                        {
                            table.SetTextString(r + 1, c, lines[r][c] ?? "");
                            table.SetTextHeight(r + 1, c, TextHeightMm);
                        }
                    table.GenerateLayout();
                    if (!reused)
                    {
                        CadIo.ModelSpace(tr, db).AppendEntity(table);
                        tr.AddNewlyCreatedDBObject(table, true);
                    }
                    tr.Commit();
                }

                string tmp = Path.GetTempPath();
                string xlsx = Path.Combine(tmp, "wenta_schedule.xlsx");
                string json = Path.Combine(tmp, "wenta_schedule.json");
                string csv = Path.Combine(tmp, "wenta_schedule.csv");
                BomExport.SaveXlsx(bom, xlsx);
                BomExport.SaveJson(bom, json, net.Name);
                File.WriteAllText(csv, sched.ToCsv());

                ed.WriteMessage(string.Format(C,
                    "\nWENTASCHEDULE: {0} {1} rows ({2} items, {3} m duct, {4} m2) on {5} at ({6:0},{7:0}).\n  {8}\n  {9}\n  {10}",
                    reused ? "updated" : "drew", nRows, bom.Rows.Count, bom.TotalLength.ToString("0.000", C),
                    bom.TotalArea.ToString("0.000", C), SchedLayer, at.X, at.Y, xlsx, json, csv));
                Commands.Log(string.Format(C, "WENTASCHEDULE ok {0} rows={1} items={2} length={3} area={4}",
                    reused ? "updated" : "created", nRows, bom.Rows.Count,
                    bom.TotalLength.ToString("0.000", C), bom.TotalArea.ToString("0.000", C)));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nWENTASCHEDULE: " + ex.Message);
                Commands.Log("WENTASCHEDULE FAILED " + ex.Message);
            }
        }

        [CommandMethod("WENTAFLAT")]
        public void FlatPatternsCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            try
            {
                Network net = Solved(doc, "WENTAFLAT");
                if (net == null) return;

                var po = new PromptPointOptions("\nBase point for the flat patterns <0,0>: ") { AllowNone = true };
                PromptPointResult pr = ed.GetPoint(po);
                if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
                Point3d bp = pr.Status == PromptStatus.OK ? pr.Value : Point3d.Origin;
                double? gap = CadIo.PromptDouble(ed, "\nGap between pieces (mm)", 500.0, true);
                if (gap == null) return;

                FlatPatternSet set = Schedule.FlatPatterns(net, new Wenta.Point2(bp.X, bp.Y), gap.Value);
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CadIo.EnsureLayer(tr, db, FabLayer, 4);
                    CadIo.ClearLayer(tr, db, FabLayer);
                    BlockTableRecord ms = CadIo.ModelSpace(tr, db);
                    foreach (FlatPattern p in set.Patterns)
                    {
                        var pl = new ZwSoft.ZwCAD.DatabaseServices.Polyline(4) { Layer = FabLayer };
                        for (int i = 0; i < p.OutlineMm.Count; i++)
                            pl.AddVertexAt(i, new Point2d(p.OutlineMm[i].X, p.OutlineMm[i].Y), 0, 0, 0);
                        pl.Closed = true;
                        ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        var t = new DBText
                        {
                            Position = new Point3d(p.LabelAtMm.X, p.LabelAtMm.Y, 0.0),
                            Height = TextHeightMm, Layer = FabLayer,
                            TextString = p.ItemId + "  " + p.Description,
                        };
                        ms.AppendEntity(t);
                        tr.AddNewlyCreatedDBObject(t, true);
                    }
                    tr.Commit();
                }

                string csv = Path.Combine(Path.GetTempPath(), "wenta_flat.csv");
                File.WriteAllText(csv, set.ToCsv());
                foreach (string s in set.Skipped) ed.WriteMessage("\n  skipped " + s);
                ed.WriteMessage(string.Format(C,
                    "\nWENTAFLAT: {0} flat pattern(s), {1} m2 of sheet, {2} skipped, on {3}. {4}",
                    set.Patterns.Count, set.TotalAreaM2.ToString("0.000", C), set.Skipped.Count, FabLayer, csv));
                Commands.Log(string.Format(C, "WENTAFLAT ok patterns={0} sheet={1} skipped={2}",
                    set.Patterns.Count, set.TotalAreaM2.ToString("0.000", C), set.Skipped.Count));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nWENTAFLAT: " + ex.Message);
                Commands.Log("WENTAFLAT FAILED " + ex.Message);
            }
        }
    }
}
