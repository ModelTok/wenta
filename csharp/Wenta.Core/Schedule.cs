using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wenta
{
    // #29: the schedule table WENTASCHEDULE draws on WENTA-SCHED and the flat
    // patterns WENTAFLAT draws on WENTA-FAB, both from the drawn network.
    // The rows are Bom's (with Marking's branch numbers); the patterns are
    // Development's round-duct developments laid out side by side. Nothing
    // here computes a quantity of its own.

    public sealed class ScheduleTable
    {
        public string Title;
        public string[] Header;
        public readonly List<string[]> Rows = new List<string[]>();
        public string[] Totals;

        // Every line of the table (header, rows, totals) as cells.
        public List<string[]> AllRows()
        {
            var all = new List<string[]> { Header };
            all.AddRange(Rows);
            all.Add(Totals);
            return all;
        }

        // The table as CSV (comma-separated, quoted where a cell needs it).
        public string ToCsv()
        {
            var sb = new StringBuilder();
            foreach (string[] row in AllRows())
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    string cell = row[i] ?? "";
                    if (cell.IndexOfAny(new[] { ',', '"', '\n' }) >= 0) cell = "\"" + cell.Replace("\"", "\"\"") + "\"";
                    sb.Append(cell);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    public sealed class FlatPattern
    {
        public string ItemId;
        public string Description;
        public FlatPiece Piece;
        // Closed outline in mm (4 corners), laid out from the base point.
        public readonly List<Point2> OutlineMm = new List<Point2>();
        public Point2 LabelAtMm;
    }

    public sealed class FlatPatternSet
    {
        public readonly List<FlatPattern> Patterns = new List<FlatPattern>();
        // Components that are not developed, with the reason.
        public readonly List<string> Skipped = new List<string>();
        public double TotalAreaM2;

        public string ToCsv()
        {
            var sb = new StringBuilder("item_id,description,width_m,length_m,area_m2\n");
            foreach (FlatPattern p in Patterns)
                sb.Append(string.Join(",", new[]
                {
                    p.ItemId, p.Description.Replace(",", ";"),
                    p.Piece.WidthM.ToString("R", CultureInfo.InvariantCulture),
                    p.Piece.LengthM.ToString("R", CultureInfo.InvariantCulture),
                    p.Piece.AreaM2.ToString("R", CultureInfo.InvariantCulture),
                })).Append('\n');
            return sb.ToString();
        }
    }

    public static class Schedule
    {
        public static readonly string[] Columns =
        {
            "Item", "Branch", "Kind", "Description", "Length [m]", "Area [m2]", "Flow [m3/s]", "KNR code",
        };

        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static int KindOrder(string kind)
        {
            switch (kind)
            {
                case "source": return 0;
                case "duct": return 1;
                case "flex": return 2;
                case "fitting": return 3;
                case "terminal": return 4;
                default: return 5;
            }
        }

        // The schedule of a solved network: one row per component, sorted
        // by kind then id, and a totals row (Bom.TotalLength, TotalArea).
        public static ScheduleTable Build(Network network, Bom bom)
        {
            if (network == null || bom == null) throw new WentaException("Schedule.Build: network and bom are required");
            var branch = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (Mark m in Marking.AssignBranchMarks(network)) branch[m.ComponentId] = m.BranchNo;

            var rows = new List<Bom.BomRow>(bom.Rows);
            rows.Sort((a, b) =>
            {
                int k = KindOrder(a.Kind).CompareTo(KindOrder(b.Kind));
                return k != 0 ? k : string.CompareOrdinal(a.ItemId, b.ItemId);
            });

            var t = new ScheduleTable { Title = "Wenta schedule: " + (network.Name ?? ""), Header = (string[])Columns.Clone() };
            foreach (Bom.BomRow r in rows)
            {
                uint no;
                bool ducted = r.Kind == "duct" || r.Kind == "flex";
                t.Rows.Add(new[]
                {
                    r.ItemId,
                    branch.TryGetValue(r.ItemId, out no) ? no.ToString(C) : "",
                    r.Kind,
                    r.Description ?? "",
                    ducted ? r.Length.ToString("0.000", C) : "",
                    ducted ? r.Area.ToString("0.000", C) : "",
                    r.Flowrate > 0.0 ? r.Flowrate.ToString("0.0000", C) : "",
                    r.KnrCode ?? "",
                });
            }
            t.Totals = new[]
            {
                "Total", "", rows.Count.ToString(C) + " items", "",
                bom.TotalLength.ToString("0.000", C), bom.TotalArea.ToString("0.000", C), "", "",
            };
            return t;
        }

        // Round rigid ducts developed flat (Development.RoundDuctDevelopment:
        // circumference × length), laid out left to right from `originMm`
        // with `gapMm` between pieces, in component order sorted by id.
        // Other components are listed in Skipped with the reason.
        public static FlatPatternSet FlatPatterns(Network network, Point2 originMm, double gapMm)
        {
            if (network == null) throw new WentaException("Schedule.FlatPatterns: network is null");
            if (!(gapMm >= 0.0)) throw new WentaException("gap must not be negative");
            var set = new FlatPatternSet();
            var ids = new List<string>(network.Components.Keys);
            ids.Sort(StringComparer.Ordinal);
            double x = originMm.X;
            foreach (string id in ids)
            {
                Component c = network.Components[id];
                var rd = c as RigidDuct;
                if (rd == null)
                {
                    if (c is FlexDuct) set.Skipped.Add(id + ": flexible duct, bought by length, not developed");
                    continue;
                }
                var round = rd.CrossSection as Round;
                if (round == null)
                {
                    set.Skipped.Add(id + ": rectangular duct, not developed (round developments only)");
                    continue;
                }
                FlatPiece piece = Development.RoundDuctDevelopment(round.Diameter, rd.Length);
                var p = new FlatPattern
                {
                    ItemId = id,
                    Description = string.Format(C, "Ø{0} × {1} m, flat {2} × {3} mm", (round.Diameter * 1000.0).ToString("0", C),
                        rd.Length.ToString("0.###", C), (piece.WidthM * 1000.0).ToString("0", C), (piece.LengthM * 1000.0).ToString("0", C)),
                    Piece = piece,
                };
                double w = piece.WidthM * 1000.0, l = piece.LengthM * 1000.0, y = originMm.Y;
                p.OutlineMm.Add(new Point2(x, y));
                p.OutlineMm.Add(new Point2(x + w, y));
                p.OutlineMm.Add(new Point2(x + w, y + l));
                p.OutlineMm.Add(new Point2(x, y + l));
                p.LabelAtMm = new Point2(x, y - 150.0);
                set.Patterns.Add(p);
                set.TotalAreaM2 += piece.AreaM2;
                x += w + gapMm;
            }
            return set;
        }
    }
}
