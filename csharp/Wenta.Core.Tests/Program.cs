using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Wenta;

namespace Wenta.Core.Tests
{
    internal static class Program
    {
        private static int _pass, _fail;

        private static void Main(string[] args)
        {
            string dir = args.Length > 0
                ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vectors");

            RunUnits(Path.Combine(dir, "units.csv"));
            RunFluid(Path.Combine(dir, "fluid.csv"));
            RunGeometry(Path.Combine(dir, "geometry.csv"));
            RunFriction(Path.Combine(dir, "friction.csv"));
            RunLosses(Path.Combine(dir, "losses.csv"));
            RunFlex(Path.Combine(dir, "flex.csv"));
            RunSizes(Path.Combine(dir, "sizes.csv"));
            RunFittings(Path.Combine(dir, "fittings.csv"));
            RunElbow(Path.Combine(dir, "elbow.csv"));
            RunSizing(Path.Combine(dir, "sizing.csv"));
            RunSolver(Path.Combine(dir, "solver.csv"));
            RunCatalog();
            RunBom();
            RunBalancing();
            RunRoom();
            RunReCorrections();
            RunStandards();
            RunSettings();
            RunResults();
            RunAnalysis();
            RunMarking();
            RunFabrication();
            RunDevelopment();
            RunClash();
            RunTopology();
            RunFan();
            RunInsulation();
            RunSound();
            RunElectrical();
            RunNetworkJson();
            RunResolve();
            RunCatalogMerge(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs"));
            RunKnrMap();
            RunBomExport();
            RunPressureReport();
            RunBatchSizing();
            RunIfcExport();
            RunMultiDrawing();
            RunReFit();
            RunQuickConnect();
            RunDrawing();
            RunFanCatalog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs"));
            RunDrawingInsulation();
            RunNetworkDrawing();
            RunMarkdown(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs"));
            RunAttenuators(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs"));
            RunSolverVentiSemantics();
            RunNetworkEdit();
            RunSchedule();
            RunPerformance();

            Console.WriteLine();
            Console.WriteLine("==== " + _pass + " passed, " + _fail + " failed ====");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }


        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly double[] _d = new double[8];

        private static string[] _cols;

        private static void ExpectError(bool error, string id, Action act)
        {
            if (!error) { act(); return; }
            try
            {
                act();
                Fail(id, "expected WentaException, got success");
            }
            catch (WentaException) { Pass(id); }
        }

        private static void Check(string id, double actual, double expected, double tol)
        {
            double denom = Math.Abs(expected) > 1.0 ? Math.Abs(expected) : 1.0;
            if (Math.Abs(actual - expected) <= tol * denom) Pass(id);
            else Fail(id, "got " + actual.ToString("R", C) + ", want "
                       + expected.ToString("R", C));
        }

        private static void CheckInt(string id, int actual, int expected)
        {
            if (actual == expected) Pass(id);
            else Fail(id, "got " + actual + ", want " + expected);
        }

        private static void Pass(string id) { _pass++; }

        private static void Fail(string id, string why)
        {
            _fail++;
            Console.WriteLine("FAIL " + id + ": " + why);
        }

        private static double[] Args(string joined)
        {
            string[] parts = joined.Split(',');
            var r = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                r[i] = double.Parse(parts[i], C);
            return r;
        }

        private static IEnumerable<string[]> Rows(string path)
        {
            using (var sr = new StreamReader(path))
            {
                string header = sr.ReadLine();
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    _cols = line.Split(';');
                    yield return _cols;
                }
            }
        }

        private static double E1 { get { return double.Parse(_cols[_cols.Length - 2], C); } }
        private static bool IsError
        {
            get { return _cols.Length >= 1 && _cols[_cols.Length - 1] == "error"; }
        }


        private static void RunUnits(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string id = c[0], op = c[1];
                double a = 0.0;
                if (op != "ach") a = double.Parse(c[2], C);
                switch (op)
                {
                    case "ach":
                        double[] aa = Args(c[2]);
                        ExpectError(IsError, id, delegate
                        {
                            Check(id, Units.AirChangesPerHour(aa[0], aa[1]), E1, 1e-12);
                        });
                        break;
                    default:
                        double r = op == "cfm_to_m3s" ? Units.CfmToM3s(a)
                                  : op == "m3s_to_cfm" ? Units.M3sToCfm(a)
                                  : op == "inwc_to_pa" ? Units.InwcToPa(a)
                                  : op == "pa_to_inwc" ? Units.PaToInwc(a)
                                  : op == "ft_to_m" ? Units.FtToM(a)
                                  : op == "m_to_ft" ? Units.MToFt(a)
                                  : op == "in_to_m" ? Units.InToM(a)
                                  : op == "m_to_in" ? Units.MToIn(a)
                                  : op == "fpm_to_ms" ? Units.FpmToMs(a)
                                  : op == "ms_to_fpm" ? Units.MsToFpm(a)
                                  : op == "f_to_c" ? Units.FToC(a)
                                  : Units.CToF(a);
                        Check(id, r, E1, 1e-12);
                        break;
                }
            }
        }

        private static void RunFluid(string path)
        {
            foreach (string[] c in Rows(path))
            {
                double[] a = Args(c[2]);
                ExpectError(IsError, c[0], delegate
                {
                    Fluid f = Fluid.AirAtAltitude(a[0], a.Length > 1 ? a[1] : 20.0);
                    Check(c[0], f.Density, double.Parse(c[3], C), 1e-12);
                    Check(c[0], f.DynamicViscosity, double.Parse(c[4], C), 1e-12);
                });
            }
        }

        private static void RunGeometry(string path)
        {
            foreach (string[] c in Rows(path))
            {
                double[] a = Args(c[2]);
                switch (c[1])
                {
                    case "round":
                        ExpectError(IsError, c[0], delegate
                        {
                            Round r = new Round(a[0]);
                            Check(c[0], r.Area, double.Parse(c[3], C), 1e-12);
                            Check(c[0], r.HydraulicDiameter, double.Parse(c[4], C), 1e-12);
                        });
                        break;
                    case "rect":
                        ExpectError(IsError, c[0], delegate
                        {
                            Rectangular r = new Rectangular(a[0], a[1]);
                            Check(c[0], r.Area, double.Parse(c[3], C), 1e-12);
                            Check(c[0], r.HydraulicDiameter, double.Parse(c[4], C), 1e-12);
                        });
                        break;
                    case "eq_round":
                        ExpectError(IsError, c[0], delegate
                        {
                            Check(c[0], Geometry.EquivalentRoundDiameter(a[0], a[1]),
                                  double.Parse(c[3], C), 1e-12);
                        });
                        break;
                }
            }
        }

        private static void RunFriction(string path)
        {
            foreach (string[] c in Rows(path))
            {
                double[] a = Args(c[2]);
                double r = 0.0;
                switch (c[1])
                {
                    case "reynolds": r = Friction.Reynolds(a[0], a[1], a[2]); break;
                    case "rel_rough": r = Friction.RelativeRoughness(a[0], a[1]); break;
                    case "friction_factor": r = Friction.FrictionFactor(a[0], a[1]); break;
                    case "colebrook": r = Friction.FrictionFactorColebrook(a[0], a[1]); break;
                }
                Check(c[0], r, E1, 1e-12);
            }
        }

        private static void RunLosses(string path)
        {
            foreach (string[] c in Rows(path))
            {
                double[] a = Args(c[2]);
                double r = c[1] == "straight"
                    ? Losses.StraightPressureDrop(a[0], a[1], a[2], a[3], a[4])
                    : Losses.LocalPressureDrop(a[0], a[1], a[2]);
                Check(c[0], r, E1, 1e-12);
            }
        }

        private static void RunFlex(string path)
        {
            foreach (string[] c in Rows(path))
            {
                double[] a = Args(c[2]);
                Check(c[0], Flex.StretchCorrectionFactor(a[0], a[1]), E1, 1e-12);
            }
        }

        private static void RunSizes(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string[] parts = c[2].Split(',');
                CheckInt(c[0],
                    StandardSizes.NearestRoundSize(
                        double.Parse(parts[0], C),
                        parts[1].Equals("True", StringComparison.OrdinalIgnoreCase)),
                    (int)double.Parse(c[3], C));
            }
        }

        private static void RunFittings(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string id = c[0];
                double[] a = c[1] == "mitered" ? new double[0] : Args(c[2]);
                switch (c[1])
                {
                    case "reducer":
                        ExpectError(IsError, id, delegate
                        { Check(id, FittingsLibrary.ReducerRound(a[0], a[1], a[2]), D(c[3]), 1e-12); });
                        break;
                    case "expander":
                        ExpectError(IsError, id, delegate
                        { Check(id, FittingsLibrary.ExpanderRound(a[0], a[1], a[2]), D(c[3]), 1e-12); });
                        break;
                    case "tee_branch":
                        FittingsLibrary.JunctionTeeBranch(a[0], a[1], a[2], a[3],
                            out double z1, out double z2);
                        Check(id, z1, D(c[3]), 1e-12);
                        Check(id, z2, D(c[4]), 1e-12);
                        break;
                    case "tee_combine":
                        FittingsLibrary.JunctionTeeCombine(a[0], a[1], a[2], a[3],
                            out double k1, out double k2);
                        Check(id, k1, D(c[3]), 1e-12);
                        Check(id, k2, D(c[4]), 1e-12);
                        break;
                    case "damper":
                        ExpectError(IsError, id, delegate
                        { Check(id, FittingsLibrary.DamperButterfly(a[0]), D(c[3]), 1e-12); });
                        break;
                    case "diffuser":
                        Check(id, FittingsLibrary.DiffuserCeiling(a[0]), D(c[3]), 1e-12);
                        break;
                    case "grille":
                        ExpectError(IsError, id, delegate
                        { Check(id, FittingsLibrary.GrilleReturn(a[0]), D(c[3]), 1e-12); });
                        break;
                    case "rect_elbow":
                        Check(id, FittingsLibrary.RectangularElbow(a[0], a[1], a[2], a[3]),
                              D(c[3]), 1e-12);
                        break;
                    case "mitered":
                        string[] mp = c[2].Split(',');
                        ExpectError(IsError, id, delegate
                        {
                            Check(id, FittingsLibrary.MiteredElbow(
                                double.Parse(mp[0], C),
                                mp[1].Equals("True", StringComparison.OrdinalIgnoreCase)),
                                  D(c[3]), 1e-12);
                        });
                        break;
                }
            }
        }

        private static double D(string s)
        {
            return s.Length == 0 ? 0.0 : double.Parse(s, C);
        }

        private static void RunElbow(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string id = c[0];
                double[] a = Args(c[2]);
                double r = a[0], d = a[1], ang = a[2];
                double tol = id.Contains("grid") ? 1e-9 : 2e-4;
                ExpectError(IsError, id, delegate
                {
                    Check(id, new ElbowRound(r, d, ang).Zeta(), D(c[3]), tol);
                });
            }
        }

        private static void RunSizing(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string id = c[0];
                int comma = c[2].IndexOf(',');
                bool bad = IsError;
                double[] a = c[1] == "noise_limit"
                    ? new[] { double.Parse(c[2].Substring(0, comma), C) }
                    : Args(c[2]);
                int expectedIdx = bad ? 0 : (int)double.Parse(c[3], C);
                double expectedV = bad ? 0.0 : D(c[4]);
                double expectedDpm = bad ? 0.0 : D(c[5]);
                ExpectError(IsError, id, delegate
                {
                    Sizing.SizingResult r;
                    switch (c[1])
                    {
                        case "velocity_round":
                            r = Sizing.VelocityMethod(a[0], Sizing.ShapeRound, a[1]);
                            Check(id, r.Velocity, expectedV, 1e-12);
                            CheckInt(id, RoundIndex((Round)r.Section), expectedIdx);
                            return;
                        case "velocity_rect":
                            r = Sizing.VelocityMethod(a[0], Sizing.ShapeRectangular, a[1]);
                            Check(id, r.Velocity, expectedV, 1e-12);
                            CheckInt(id, RectIndex((Rectangular)r.Section), expectedIdx);
                            return;
                        case "equal_friction":
                            r = Sizing.EqualFrictionMethod(a[0], a[1], Sizing.ShapeRound);
                            Check(id, r.Velocity, expectedV, 1e-12);
                            Check(id, r.PressureDropPerMeter, expectedDpm, 1e-12);
                            CheckInt(id, RoundIndex((Round)r.Section), expectedIdx);
                            return;
                        case "equal_friction_rect":
                            r = Sizing.EqualFrictionMethod(a[0], a[1], Sizing.ShapeRectangular);
                            Check(id, r.Velocity, expectedV, 1e-12);
                            Check(id, r.PressureDropPerMeter, expectedDpm, 1e-12);
                            CheckInt(id, RectIndex((Rectangular)r.Section), expectedIdx);
                            return;
                        case "noise_limit":
                            r = Sizing.NoiseLimitMethod(a[0], c[2].Substring(comma + 1));
                            Check(id, r.Velocity, expectedV, 1e-12);
                            CheckInt(id, RoundIndex((Round)r.Section), expectedIdx);
                            return;
                        case "aspect":
                            r = Sizing.AspectRatioMethod(a[0], a[1], a[2]);
                            Check(id, r.Velocity, expectedV, 1e-12);
                            CheckInt(id, RectIndex((Rectangular)r.Section), expectedIdx);
                            return;
                    }
                });
            }
        }

        private static int RoundIndex(Round s)
        {
            return Array.IndexOf(StandardSizes.RoundDuctSizes, (int)Math.Round(s.Diameter * 1000.0));
        }

        private static int RectIndex(Rectangular s)
        {
            int w = (int)Math.Round(s.Width * 1000.0), h = (int)Math.Round(s.Height * 1000.0);
            int[][] table = StandardSizes.RectangularDuctSizes;
            for (int i = 0; i < table.Length; i++)
                if (table[i][0] == w && table[i][1] == h) return i;
            return -1;
        }


        private static void RunSolver(string path)
        {
            foreach (string[] c in Rows(path))
            {
                string id = c[0];
                if (id == "net_readme")
                {
                    var net = new Network { Name = "readme" };
                    net.Add("ahu", new Source("AHU"));
                    net.Add("duct", new RigidDuct("duct", new Round(0.2), 20.0));
                    net.Add("term", new Terminal("terminal", 0.1));
                    net.Connect("ahu", "duct");
                    net.Connect("duct", "term");
                    double dp = net.Solve();
                    Check(id, dp, D(c[3]), 1e-12);
                    Port inlet = net.Components["duct"].Port_("inlet");
                    Check(id, inlet.Flowrate ?? 0.0, D(c[4]), 1e-12);
                    Check(id, inlet.PressureDrop, D(c[5]), 1e-12);
                }
                else if (id == "net_tee")
                {
                    var net = new Network { Name = "tee" };
                    net.Add("ahu", new Source("AHU"));
                    net.Add("duct", new RigidDuct("duct", new Round(0.315), 20.0));
                    net.Add("tee", new Tee("tee", new Round(0.315), 0.1, 0.4));
                    net.Add("d2", new RigidDuct("d2", new Round(0.2), 5.0));
                    net.Add("flex", new FlexDuct("flex", 0.125, 3.0, 2.0, 100.0));
                    net.Add("t1", new Terminal("t1", 0.06));
                    net.Add("t2", new Terminal("t2", 0.04));
                    net.Connect("ahu", "duct");
                    net.Connect("duct", "tee");
                    net.Connect("tee.straight", "d2");
                    net.Connect("tee.branch", "flex");
                    net.Connect("d2", "t1");
                    net.Connect("flex", "t2");
                    double dp = net.Solve();
                    Check(id, dp, D(c[3]), 1e-12);
                    Check(id, net.Components["duct"].Port_("inlet").Flowrate ?? 0.0, D(c[4]), 1e-12);
                    Check(id, net.Components["tee"].Port_("straight").Flowrate ?? 0.0, D(c[5]), 1e-12);
                    Check(id, net.Components["tee"].Port_("branch").Flowrate ?? 0.0, D(c[6]), 1e-12);
                }
            }
        }

        private static void RunCatalog()
        {
            string json =
                "{\n" +
                "  \"name\": \"example-generic-rect\",\n" +
                "  \"version\": 1,\n" +
                "  \"fittings\": [\n" +
                "    {\"id\":\"rect-elbow-r1.5\",\"type\":\"rect_elbow\"," +
                "     \"size_min_mm\":[100,100],\"size_max_mm\":[1200,2000]," +
                "     \"zeta\":0.17,\"source\":\"Hendiger tab. 4.2 (R=1.5W)\"," +
                "     \"knr\":\"KNR 2-08 03xx\"},\n" +
                "    {\"id\":\"rect-elbow-r1.0\",\"type\":\"rect_elbow\"," +
                "     \"size_min_mm\":[100,100],\"size_max_mm\":[1200,2000]," +
                "     \"zeta\":0.21,\"source\":\"Hendiger tab. 4.2 (R=1.0W)\"," +
                "     \"knr\":\"KNR 2-08 03xx\"},\n" +
                "    {\"id\":\"round-elbow-r1.0\",\"type\":\"round_elbow\"," +
                "     \"zeta\":0.24,\"source\":\"Hendiger tab. 4.3 (R/D=1.0)\"," +
                "     \"knr\":\"KNR 2-08 03xx\"},\n" +
                "    {\"id\":\"tee-branch-typical\",\"type\":\"tee\"," +
                "     \"zeta\":0.45,\"source\":\"generic branch tee\"," +
                "     \"knr\":\"KNR 2-08 03xx\"},\n" +
                "    {\"id\":\"vav-box\",\"type\":\"damper\"," +
                "     \"size_min_mm\":[100],\"size_max_mm\":[630]," +
                "     \"zeta\":0.35,\"source\":\"generic VAV box fully open\"," +
                "     \"knr\":\"KNR 2-08 04xx\"}\n" +
                "  ]\n" +
                "}";
            ZetaCatalog cat = ZetaCatalog.Parse(json, "inline");

            CheckInt("cat_count", cat.Fittings.Count, 5);
            Check("cat_name", cat.Name == "example-generic-rect" ? 1.0 : 0.0, 1.0, 1e-12);
            CheckInt("cat_version", cat.Version, 1);

            ZetaCatalog.CatalogEntry vav = cat.ById("vav-box");
            Check("cat_byid_zeta", vav.Zeta, 0.35, 1e-12);
            Check("cat_byid_source",
                vav.Source == "generic VAV box fully open" ? 1.0 : 0.0, 1.0, 1e-12);
            Check("cat_byid_knr", vav.Knr.Length > 0 ? 1.0 : 0.0, 1.0, 1e-12);
            Check("cat_byid_missing", cat.ById("nope") == null ? 1.0 : 0.0, 1.0, 1e-12);

            Check("cat_match_rect", cat.Match("rect_elbow", new[]{ 400.0, 200.0 }).Zeta, 0.17, 1e-12);
            Check("cat_match_outside", cat.Match("rect_elbow", new[]{ 2000.0, 3000.0 }) == null ? 1.0 : 0.0, 1.0, 1e-12);
            Check("cat_match_nowindow", cat.Match("round_elbow", new[]{ 315.0 }).Zeta, 0.24, 1e-12);

            Check("cat_zeta_catalog", cat.ZetaFor("tee", new double[0]), 0.45, 1e-12);
            Check("cat_zeta_fallback", cat.ZetaFor("grille", new[]{ 300.0 }), 0.2875, 1e-12);
            ExpectError(true, "cat_zeta_unknown", delegate { cat.ZetaFor("nonsense", new double[0]); });
        }

        private static void RunBom()
        {
            var net = new Network { Name = "bom-tee" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new RigidDuct("duct", new Round(0.315), 20.0));
            net.Add("tee", new Tee("tee", new Round(0.315), 0.1, 0.4));
            net.Add("d2", new RigidDuct("d2", new Round(0.2), 5.0));
            net.Add("flex", new FlexDuct("flex", 0.125, 3.0, 2.0, 100.0));
            net.Add("t1", new Terminal("t1", 0.06));
            net.Add("t2", new Terminal("t2", 0.04));
            net.Connect("ahu", "duct");
            net.Connect("duct", "tee");
            net.Connect("tee.straight", "d2");
            net.Connect("tee.branch", "flex");
            net.Connect("d2", "t1");
            net.Connect("flex", "t2");
            net.Solve();

            Bom bom = Bom.Build(net);
            CheckInt("bom_rows", bom.Rows.Count, 7);

            Bom.BomRow duct = FindBom(bom, "duct");
            CrossSection cs = new Round(0.315);
            Check("bom_duct_length", duct.Length, 20.0, 1e-12);
            Check("bom_duct_area", duct.Area, cs.Area * 20.0, 1e-12);
            Check("bom_duct_kind", duct.Kind == "duct" ? 1.0 : 0.0, 1.0, 1e-12);
            Check("bom_duct_knr", duct.KnrCode.Length > 0 ? 1.0 : 0.0, 1.0, 1e-12);

            Bom.BomRow d2 = FindBom(bom, "d2");
            Check("bom_d2_area", d2.Area, Math.PI * (0.2 / 2.0) * (0.2 / 2.0) * 5.0, 1e-12);

            Bom.BomRow flex = FindBom(bom, "flex");
            Check("bom_flex_length", flex.Length, 3.0, 1e-12);
            Check("bom_flex_area", flex.Area, Math.PI * (0.125 / 2.0) * (0.125 / 2.0) * 3.0, 1e-12);
            Check("bom_flex_kind", flex.Kind == "flex" ? 1.0 : 0.0, 1.0, 1e-12);

            Check("bom_source_len", FindBom(bom, "ahu").Length, 0.0, 1e-12);
            Check("bom_source_knr_empty", FindBom(bom, "ahu").KnrCode.Length == 0 ? 1.0 : 0.0, 1.0, 1e-12);
            Check("bom_terminal_kind", FindBom(bom, "t1").Kind == "terminal" ? 1.0 : 0.0, 1.0, 1e-12);
            Check("bom_fitting_kind", FindBom(bom, "tee").Kind == "fitting" ? 1.0 : 0.0, 1.0, 1e-12);

            double len = cs.Area * 20.0 + Math.PI * (0.2 / 2.0) * (0.2 / 2.0) * 5.0
                       + Math.PI * (0.125 / 2.0) * (0.125 / 2.0) * 3.0;
            Check("bom_total_length", bom.TotalLength, 28.0, 1e-12);
            Check("bom_total_area", bom.TotalArea, len, 1e-12);

            string csv = bom.ToCsv();
            int lines = 0;
            for (int i = 0; i < csv.Length; i++)
                if (csv[i] == '\n') lines++;
            Check("bom_csv_lines", lines == 8 ? 1.0 : 0.0, 1.0, 1e-12);
        }

        private static void RunBalancing()
        {
            Check("bal_zeta_closed", Balancing.RequiredZeta(9.632, 4.0, 1.204), 1.0, 1e-12);
            Check("bal_roundtrip",
                Losses.LocalPressureDrop(Balancing.RequiredZeta(9.632, 4.0, 1.204), 4.0, 1.204),
                9.632, 1e-12);
            Check("bal_zero_dynamic", Balancing.RequiredZeta(5.0, 0.0, 1.204), 0.0, 1e-12);

            Check("bal_delta", Balancing.BalancingZeta(30.0, 10.736, 4.0, 1.204), 2.0, 1e-12);
            Check("bal_already_met", Balancing.BalancingZeta(10.0, 20.0, 4.0, 1.204), 0.0, 1e-12);
            Check("bal_exact", Balancing.BalancingZeta(10.0, 10.0, 4.0, 1.204), 0.0, 1e-12);
            double z = Balancing.BalancingZeta(30.0, 10.736, 4.0, 1.204);
            Check("bal_added_dp",
                Losses.LocalPressureDrop(z, 4.0, 1.204), 30.0 - 10.736, 1e-9);

            double[] roundTripZetas = new double[] { 0.1, 0.5, 1.0, 2.5, 5.0, 8.0, 10.0 };
            for (int i = 0; i < roundTripZetas.Length; i++)
            {
                double zeta = roundTripZetas[i];
                double open = Balancing.DamperOpenPercentage(zeta);
                double back = FittingsLibrary.DamperButterfly(open);
                Check("bal_open_rt" + i, back, zeta, 1e-9);
            }
            Check("bal_floor", Balancing.DamperOpenPercentage(0.05), 100.0, 1e-12);
            Check("bal_floor_rt",
                FittingsLibrary.DamperButterfly(Balancing.DamperOpenPercentage(0.1)), 0.1, 1e-12);
        }

        private static void RunRoom()
        {
            Check("room_net_over", new RoomBalance(0.3, 0.1).NetM3s(), 0.2, 1e-12);
            Check("room_net_under", new RoomBalance(0.1, 0.3).NetM3s(), -0.2, 1e-12);
            Check("room_net_equal", new RoomBalance(0.2, 0.2).NetM3s(), 0.0, 1e-12);

            RoomBalance bal = new RoomBalance(0.5, 0.25);
            Check("room_tol_low", bal.IsBalanced(0.249) ? 1.0 : 0.0, 0.0, 1e-12);
            Check("room_tol_eq", bal.IsBalanced(0.25) ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_tol_hi", bal.IsBalanced(0.251) ? 1.0 : 0.0, 1.0, 1e-12);

            Check("room_ach", Units.AirChangesPerHour(0.1, 100.0), 3.6, 1e-12);
            Check("room_ach_zero_flow", Units.AirChangesPerHour(0.0, 100.0), 0.0, 1e-12);
            ExpectError(true, "room_ach_bad_volume", delegate { Units.AirChangesPerHour(0.1, 0.0); });

            ExpectError(true, "room_neg_supply", delegate { new RoomBalance(-0.1, 0.2); });
            ExpectError(true, "room_neg_exhaust", delegate { new RoomBalance(0.1, -0.2); });
            new RoomBalance(0.0, 0.0);
            Check("room_zero_ok", 1.0, 1.0, 1e-12);

            Check("room_imb_pure_supply", new RoomBalance(0.3, 0.0).ImbalanceFraction() ?? 0.0, 1.0, 1e-12);
            Check("room_imb_pure_exhaust", new RoomBalance(0.0, 0.3).ImbalanceFraction() ?? 0.0, -1.0, 1e-12);
            Check("room_imb_balanced", new RoomBalance(0.2, 0.2).ImbalanceFraction() ?? 99.0, 0.0, 1e-12);
            Check("room_imb_half", new RoomBalance(0.5, 0.25).ImbalanceFraction() ?? 0.0, 0.5, 1e-12);
            Check("room_imb_none", new RoomBalance(0.0, 0.0).ImbalanceFraction() == null ? 1.0 : 0.0, 1.0, 1e-12);

            RoomBalanceSet set = new RoomBalanceSet();
            set.Add("bathroom", new RoomBalance(0.0625, 0.125));
            set.Add("living", new RoomBalance(0.25, 0.125));
            set.Add("kitchen", new RoomBalance(0.0625, 0.1875));
            Check("room_tot_supply", set.TotalSupplyM3s(), 0.375, 1e-12);
            Check("room_tot_exhaust", set.TotalExhaustM3s(), 0.4375, 1e-12);
            Check("room_tot_net", set.OverallNetM3s(), -0.0625, 1e-12);
            Check("room_tot_bal_eq", set.IsBalanced(0.0625) ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_tot_bal_low", set.IsBalanced(0.0624) ? 1.0 : 0.0, 0.0, 1e-12);

            RoomBalanceSet csvSet = new RoomBalanceSet();
            csvSet.AddWithVolume("bathroom", new RoomBalance(0.02, 0.05), 5.0);
            csvSet.Add("corridor", new RoomBalance(0.10, 0.10));
            string csv = csvSet.CsvRender();
            Check("room_csv_header", Contains(csv, "name,supply_m3s,exhaust_m3s,net_m3s,ach") ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_csv_row1", Contains(csv, "bathroom,0.02,0.05,-0.03,14.4") ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_csv_row2", Contains(csv, "corridor,0.1,0.1,0,") ? 1.0 : 0.0, 1.0, 1e-12);
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle) >= 0;
        }

        private static Bom.BomRow FindBom(Bom bom, string id)
        {
            foreach (Bom.BomRow r in bom.Rows)
                if (r.ItemId == id) return r;
            throw new WentaException("bom row not found: " + id);
        }


        private static void CheckTrue(string id, bool cond)
        {
            if (cond) Pass(id);
            else Fail(id, "condition false");
        }

        private static void CheckStr(string id, string actual, string expected)
        {
            if (actual == expected) Pass(id);
            else Fail(id, "got \"" + actual + "\", want \"" + expected + "\"");
        }

        private static void ExpectOk(string id, Action act)
        {
            try { act(); Pass(id); }
            catch (WentaException e) { Fail(id, "unexpected WentaException: " + e.Message); }
        }

        private static bool SameInts(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SamePairs(int[][] a, int[][] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i][0] != b[i][0] || a[i][1] != b[i][1]) return false;
            return true;
        }

        private static void RunReCorrections()
        {
            Check("re.corr_at_ref", ReCorrections.ReCorrection(ReCorrections.ReRef, 0.2), 1.0, 1e-12);
            CheckTrue("re.corr_higher_re_lt1", ReCorrections.ReCorrection(ReCorrections.ReRef * 10.0, 0.2) < 1.0);
            Check("re.corr_clamp_hi", ReCorrections.ReCorrection(ReCorrections.ReRef * 100.0, 1.5), 0.75, 1e-9);
            Check("re.corr_clamp_lo", ReCorrections.ReCorrection(ReCorrections.ReRef / 100.0, 1.5), 1.5, 1e-9);
            ExpectError(true, "re.corr_zero_re_err", delegate { ReCorrections.ReCorrection(0.0, 0.2); });

            Check("re.size_at_ref", ReCorrections.SizeCorrection(ReCorrections.DRefM, 0.15), 1.0, 1e-12);
            CheckTrue("re.size_smaller_gt1", ReCorrections.SizeCorrection(0.1, 0.15) > 1.0);
            Check("re.size_clamp", ReCorrections.SizeCorrection(0.01, 1.0), 1.3, 1e-9);
            ExpectError(true, "re.size_zero_d_err", delegate { ReCorrections.SizeCorrection(0.0, 0.1); });

            Fluid air = Fluid.StandardAir();
            Check("re.std_air_rho", air.Density, 1.204, 1e-12);
            Check("re.std_air_mu", air.DynamicViscosity, 1.825e-5, 1e-12);
            double z = ReCorrections.CorrectedZeta(0.21, 4.0, 0.2, 0.2, 0.15, air);
            CheckTrue("re.elbow_within_bounds", z > 0.21 * 0.75 && z < 0.21 * 1.5);
            double reLo = ReCorrections.CorrectedZeta(0.21, 0.5, 0.2, 0.2, 0.15, air);
            double reHi = ReCorrections.CorrectedZeta(0.21, 15.0, 0.2, 0.2, 0.15, air);
            CheckTrue("re.elbow_monotonic_re", reHi <= reLo);

            double zc = ReCorrections.CorrectedZeta(0.3, 6.0, 0.3, 0.2, 0.15, air);
            CheckTrue("re.corrected_zeta_range", zc > 0.2 && zc < 0.5);
        }

        private static void RunStandards()
        {
            Standard[] all = { Standard.En1505_1506, Standard.AsHrae, Standard.Din };
            foreach (Standard st in all)
            {
                string tag = "standards." + st + ".";
                int[] round = Standards.StandardRoundSizes(st);
                CheckTrue(tag + "round_nonempty", round.Length > 0);
                bool increasing = true;
                for (int i = 1; i < round.Length; i++)
                    if (!(round[i - 1] < round[i])) increasing = false;
                CheckTrue(tag + "round_strictly_increasing", increasing);

                int[][] rect = Standards.StandardRectangularSizes(st);
                CheckTrue(tag + "rect_nonempty", rect.Length > 0);
                bool positive = true;
                foreach (int[] wh in rect)
                    if (!(wh[0] > 0 && wh[1] > 0)) positive = false;
                CheckTrue(tag + "rect_positive_dims", positive);
            }

            CheckTrue("standards.en_round_canonical",
                SameInts(Standards.StandardRoundSizes(Standard.En1505_1506), StandardSizes.RoundDuctSizes));
            CheckTrue("standards.en_rect_canonical",
                SamePairs(Standards.StandardRectangularSizes(Standard.En1505_1506), StandardSizes.RectangularDuctSizes));

            CheckInt("standards.en_300_up", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, true), 300);
            CheckInt("standards.en_300_closest", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, false), 300);
            CheckInt("standards.en_120_up", Standards.NearestRoundSizeFor(Standard.En1505_1506, 120.0, true), 125);
            CheckInt("standards.en_110_closest", Standards.NearestRoundSizeFor(Standard.En1505_1506, 110.0, false), 100);
            CheckInt("standards.en_clamp_low", Standards.NearestRoundSizeFor(Standard.En1505_1506, 10.0, true), 63);
            CheckInt("standards.en_clamp_high", Standards.NearestRoundSizeFor(Standard.En1505_1506, 9999.0, true), 1250);

            CheckInt("standards.ashrae_300", Standards.NearestRoundSizeFor(Standard.AsHrae, 300.0, true), 305);
            CheckInt("standards.en_300", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, true), 300);
            CheckInt("standards.ashrae_228", Standards.NearestRoundSizeFor(Standard.AsHrae, 228.0, true), 229);
            CheckInt("standards.en_228", Standards.NearestRoundSizeFor(Standard.En1505_1506, 228.0, true), 250);
            CheckInt("standards.ashrae_150", Standards.NearestRoundSizeFor(Standard.AsHrae, 150.0, true), 152);
            CheckInt("standards.en_150", Standards.NearestRoundSizeFor(Standard.En1505_1506, 150.0, true), 150);

            CheckInt("standards.din_250", Standards.NearestRoundSizeFor(Standard.Din, 250.0, true), 250);
            CheckInt("standards.din_300_up", Standards.NearestRoundSizeFor(Standard.Din, 300.0, true), 315);
            CheckInt("standards.din_300_closest", Standards.NearestRoundSizeFor(Standard.Din, 300.0, false), 315);
        }

        private static void RunSettings()
        {
            ExpectOk("settings.defaults_valid", delegate { new ProjectSettings().Validate(); });

            ExpectError(true, "settings.reject_zero_diameter", delegate
            {
                ProjectSettings s = new ProjectSettings(); s.DefaultDiameterMm = 0.0; s.Validate();
            });
            ExpectError(true, "settings.reject_negative_roughness", delegate
            {
                ProjectSettings s = new ProjectSettings(); s.AbsoluteRoughnessM = -1.0; s.Validate();
            });
            ExpectError(true, "settings.reject_zero_velocity", delegate
            {
                ProjectSettings s = new ProjectSettings(); s.TargetVelocityMs = 0.0; s.Validate();
            });
            ExpectError(true, "settings.reject_negative_pa_per_m", delegate
            {
                ProjectSettings s = new ProjectSettings(); s.TargetPaPerM = -0.5; s.Validate();
            });

            CheckTrue("settings.units_default_si", new ProjectSettings().UnitSystem == UnitSystem.Si);
            CheckTrue("settings.units_default_not_ip", new ProjectSettings().UnitSystem != UnitSystem.Ip);

            CheckTrue("settings.standard_default_en", new ProjectSettings().Standard == Standard.En1505_1506);

            Standard[] all = { Standard.En1505_1506, Standard.AsHrae, Standard.Din };
            foreach (Standard st in all)
            {
                ProjectSettings s = new ProjectSettings();
                s.Standard = st;
                CheckTrue("settings.standard_roundtrip_" + st, s.Standard == st);
            }
        }

        private static Network SolvedChain()
        {
            Round r = new Round(0.2);
            var net = new Network { Name = "chain" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new RigidDuct("duct", r, 10.0, 0.0001));
            net.Add("fit", new TwoPortFitting("elbow", r, 0.5));
            net.Add("term", new Terminal("term", 0.1, r, 1.0));
            net.Connect("ahu", "duct");
            net.Connect("duct", "fit");
            net.Connect("fit", "term");
            net.Solve();
            return net;
        }

        private static ComponentResult FindResult(List<ComponentResult> rows, string id)
        {
            foreach (ComponentResult r in rows)
                if (r.ComponentId == id) return r;
            throw new WentaException("result row not found: " + id);
        }

        private static void RunResults()
        {
            List<ComponentResult> res = Results.ExtractResults(SolvedChain());
            CheckInt("results.row_count", res.Count, 4);
            CheckStr("results.type_duct", FindResult(res, "duct").ComponentType, "RigidDuct");
            CheckStr("results.type_fit", FindResult(res, "fit").ComponentType, "TwoPortFitting");
            CheckStr("results.type_term", FindResult(res, "term").ComponentType, "Terminal");
            CheckStr("results.type_ahu", FindResult(res, "ahu").ComponentType, "Source");

            ComponentResult duct = FindResult(Results.ExtractResults(SolvedChain()), "duct");
            Check("results.duct_flow_in", duct.FlowrateIn ?? double.NaN, 0.1, 1e-12);
            CheckTrue("results.duct_velocity_positive", (duct.VelocityIn ?? -1.0) > 0.0);
            CheckTrue("results.duct_dp_positive", duct.PressureDrop > 0.0);

            string csv = Results.ResultsAsCsv(SolvedChain(), ',');
            string[] lines = csv.Split('\n');
            CheckInt("results.csv_lines", lines.Length, 5);
            CheckTrue("results.csv_header", lines[0].StartsWith("component_id,"));

            string summary = Results.ResultsSummary(SolvedChain());
            CheckTrue("results.summary_has_id", Contains(summary, "ID"));
            CheckTrue("results.summary_has_type", Contains(summary, "RigidDuct"));

            var lone = new Network { Name = "chain" };
            lone.Add("term", new Terminal("term", 0.1, new Round(0.2), 1.0));
            List<ComponentResult> loneRes = Results.ExtractResults(lone);
            CheckInt("results.unsolved_rows", loneRes.Count, 1);
            CheckTrue("results.unsolved_terminal_seeded",
                loneRes[0].FlowrateIn.HasValue && loneRes[0].FlowrateIn.Value == 0.1);
        }

        private static Network BuildChain(double flowrate, double length)
        {
            Round r = new Round(0.2);
            var net = new Network { Name = "chain" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new RigidDuct("duct", r, length, 0.0001));
            net.Add("term", new Terminal("term", flowrate, r, 1.0));
            net.Connect("ahu", "duct");
            net.Connect("duct", "term");
            return net;
        }

        private static Network BuildFlexChain(double flowrate, double length)
        {
            var net = new Network { Name = "flex-chain" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new FlexDuct("fduct", 0.2, length, 2.0, 100.0));
            net.Add("term", new Terminal("term", flowrate, null, 1.0));
            net.Connect("ahu", "duct");
            net.Connect("duct", "term");
            return net;
        }

        private static BranchInfo FindBranch(AnalysisSummary s, string kind)
        {
            foreach (BranchInfo b in s.Branches)
                if (b.Kind == kind) return b;
            return null;
        }

        private static void RunAnalysis()
        {
            Fluid air = Fluid.StandardAir();

            AnalysisSummary s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckTrue("analysis.n_branches_ge1", s.NBranches >= 1);
            BranchInfo branch = FindBranch(s, "RigidDuct");
            CheckTrue("analysis.duct_branch_present", branch != null);
            if (branch != null)
            {
                CheckTrue("analysis.duct_velocity_positive", branch.VelocityMs > 0.0);
                CheckTrue("analysis.duct_flow_positive", branch.FlowM3s > 0.0);
                CheckTrue("analysis.duct_dp_positive", branch.PressureDropPa > 0.0);
            }

            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckTrue("analysis.noise_present", s.Branches[0].RegeneratedNoiseDb.HasValue);
            double db = s.Branches[0].RegeneratedNoiseDb ?? -1.0;
            CheckTrue("analysis.noise_plausible", db > 0.0 && db < 90.0);

            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            bool allNonNeg = true;
            foreach (BranchInfo b in s.Branches)
                if (b.BalancingZeta.HasValue && b.BalancingZeta.Value < 0.0) allNonNeg = false;
            CheckTrue("analysis.balancing_zeta_non_negative", allNonNeg);

            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckInt("analysis.n_branches_ducts_only", s.NBranches, 1);
            CheckInt("analysis.branches_len_matches", s.Branches.Count, s.NBranches);
            CheckStr("analysis.branch_kind", s.Branches[0].Kind, "RigidDuct");

            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckTrue("analysis.critical_dp_positive", s.CriticalDpPa > 0.0);

            s = Analysis.Analyze(BuildFlexChain(0.1, 10.0), air);
            CheckInt("analysis.flex_n_branches", s.NBranches, 1);
            CheckStr("analysis.flex_kind", s.Branches[0].Kind, "FlexDuct");
            CheckTrue("analysis.flex_velocity_positive", s.Branches[0].VelocityMs > 0.0);
            CheckTrue("analysis.flex_noise_some", s.Branches[0].RegeneratedNoiseDb.HasValue);
            CheckTrue("analysis.flex_zeta_some", s.Branches[0].BalancingZeta.HasValue);

            Round r = new Round(0.2);
            var cyc = new Network { Name = "cyclic" };
            cyc.Add("s", new Source("AHU"));
            cyc.Add("d0", new RigidDuct("d0", r, 5.0, 0.0001));
            cyc.Add("d1", new RigidDuct("d1", r, 5.0, 0.0001));
            cyc.Add("t", new Terminal("t", 0.1, r, 1.0));
            cyc.Connect("s", "d0");
            cyc.Connect("d0", "d1");
            cyc.Connect("d1", "t");
            cyc.Connect("d1", "d0");
            try
            {
                Analysis.Analyze(cyc, air);
                Fail("analysis.cyclic_is_error", "expected WentaException, got success");
            }
            catch (WentaException e)
            {
                CheckTrue("analysis.cyclic_is_error", Contains(e.Message, "cycle"));
            }
        }

        private static RigidDuct Rigid(string name, Round r)
        {
            return new RigidDuct(name, r, 10.0, 0.0001);
        }

        private static Mark FindMark(List<Mark> marks, string id)
        {
            foreach (Mark m in marks)
                if (m.ComponentId == id) return m;
            return null;
        }

        private static void RunMarking()
        {
            Round r = new Round(0.2);

            var chain = new Network { Name = "chain" };
            chain.Add("ahu", new Source("AHU"));
            chain.Add("d1", Rigid("d1", r));
            chain.Add("d2", Rigid("d2", r));
            chain.Add("term", new Terminal("term", 0.1, r, 1.0));
            chain.Connect("ahu", "d1");
            chain.Connect("d1", "d2");
            chain.Connect("d2", "term");
            List<Mark> marks = Marking.AssignBranchMarks(chain);
            CheckInt("marking.chain_count", marks.Count, 2);
            if (marks.Count == 2)
            {
                CheckInt("marking.chain_no1", (int)marks[0].BranchNo, 1);
                CheckInt("marking.chain_no2", (int)marks[1].BranchNo, 2);
                CheckStr("marking.chain_id1", marks[0].ComponentId, "d1");
                CheckStr("marking.chain_id2", marks[1].ComponentId, "d2");
            }

            var tee = new Network { Name = "tee" };
            tee.Add("ahu", new Source("AHU"));
            tee.Add("d1", Rigid("d1", r));
            tee.Add("tee", new Tee("tee", r, 0.3, 0.5));
            tee.Add("d2", Rigid("d2", r));
            tee.Add("d3", Rigid("d3", r));
            tee.Add("t2", new Terminal("t2", 0.05, r, 1.0));
            tee.Add("t3", new Terminal("t3", 0.05, r, 1.0));
            tee.Connect("ahu", "d1");
            tee.Connect("d1", "tee");
            tee.Connect("tee.straight", "d2");
            tee.Connect("tee.branch", "d3");
            tee.Connect("d2", "t2");
            tee.Connect("d3", "t3");
            marks = Marking.AssignBranchMarks(tee);
            CheckInt("marking.tee_count", marks.Count, 3);
            if (marks.Count == 3)
            {
                CheckInt("marking.tee_no1", (int)marks[0].BranchNo, 1);
                CheckInt("marking.tee_no2", (int)marks[1].BranchNo, 2);
                CheckInt("marking.tee_no3", (int)marks[2].BranchNo, 3);
            }
            Mark d2 = FindMark(marks, "d2"), d3 = FindMark(marks, "d3");
            CheckTrue("marking.tee_legs_distinct", d2 != null && d3 != null && d2.BranchNo != d3.BranchNo);

            var size = new Network { Name = "size" };
            size.Add("ahu", new Source("AHU"));
            size.Add("d1", Rigid("d1", r));
            size.Add("term", new Terminal("term", 0.1, r, 1.0));
            size.Connect("ahu", "d1");
            size.Connect("d1", "term");
            marks = Marking.AssignBranchMarks(size);
            CheckInt("marking.size_count", marks.Count, 1);
            CheckTrue("marking.size_some", marks[0].SizeMm.HasValue);
            Check("marking.size_200", marks[0].SizeMm ?? double.NaN, 200.0, 1e-12);
            CheckStr("marking.size_kind", marks[0].Kind, "RigidDuct");

            var flow = new Network { Name = "flow" };
            flow.Add("ahu", new Source("AHU"));
            flow.Add("d1", Rigid("d1", r));
            flow.Add("term", new Terminal("term", 0.1, r, 1.0));
            flow.Connect("ahu", "d1");
            flow.Connect("d1", "term");
            List<Mark> before = Marking.AssignBranchMarks(flow);
            CheckTrue("marking.flow_none_before_solve", !before[0].FlowM3s.HasValue);
            flow.Solve();
            List<Mark> after = Marking.AssignBranchMarks(flow);
            CheckTrue("marking.flow_some_after_solve",
                after[0].FlowM3s.HasValue && after[0].FlowM3s.Value == 0.1);

            var manual = new List<Mark>
            {
                new Mark { BranchNo = 1, ComponentId = "d1", Kind = "RigidDuct", SizeMm = 200.0, FlowM3s = 0.1 },
                new Mark { BranchNo = 2, ComponentId = "d2", Kind = "RigidDuct", SizeMm = 160.0, FlowM3s = null },
            };
            string[] csvLines = Marking.MarksAsCsv(manual).Split('\n');
            CheckInt("marking.csv_lines", csvLines.Length, 3);
            if (csvLines.Length == 3)
            {
                CheckStr("marking.csv_header", csvLines[0], "branch_no,component_id,kind,size_mm,flow_m3s");
                CheckStr("marking.csv_row1", csvLines[1], "1,d1,RigidDuct,200,0.1");
                CheckStr("marking.csv_row2", csvLines[2], "2,d2,RigidDuct,160,");
            }

            List<Mark> empty = Marking.AssignBranchMarks(new Network { Name = "empty" });
            CheckInt("marking.empty_count", empty.Count, 0);
            CheckStr("marking.empty_csv", Marking.MarksAsCsv(empty), "branch_no,component_id,kind,size_mm,flow_m3s");
        }

        private static void RunFabrication()
        {
            Check("fabrication.round_area", Fabrication.DuctSurfaceAreaM2(new Round(0.2), 10.0), Math.PI * 0.2 * 10.0, 1e-12);
            Check("fabrication.rect_area", Fabrication.DuctSurfaceAreaM2(new Rectangular(0.3, 0.2), 5.0), 2.0 * (0.3 + 0.2) * 5.0, 1e-12);
            CheckTrue("fabrication.zero_length_area", Fabrication.DuctSurfaceAreaM2(new Round(0.2), 0.0) == 0.0);
            ExpectError(true, "fabrication.negative_length_err", delegate { Fabrication.DuctSurfaceAreaM2(new Round(0.2), -1.0); });

            Check("fabrication.weight_default_density", Fabrication.DuctWeightKg(1.0, 1.0, null), 7.85, 1e-9);
            double w = Fabrication.DuctWeightKg(2.0, 0.5, 8000.0);
            Check("fabrication.weight_custom_formula", w, 2.0 * 0.0005 * 8000.0, 1e-9);
            Check("fabrication.weight_custom_value", w, 8.0, 1e-9);

            ExpectError(true, "fabrication.weight_no_gauge", delegate { Fabrication.DuctWeightKg(1.0, null, null); });
            ExpectError(true, "fabrication.weight_zero_gauge", delegate { Fabrication.DuctWeightKg(1.0, 0.0, null); });
            ExpectError(true, "fabrication.weight_negative_area", delegate { Fabrication.DuctWeightKg(-1.0, 1.0, null); });
            ExpectError(true, "fabrication.weight_zero_density", delegate { Fabrication.DuctWeightKg(1.0, 1.0, 0.0); });

            Fabrication.FabricationBreakout b = new Fabrication.FabricationBreakout(20.0, 3.5);
            CheckTrue("fabrication.breakout_straight", b.StraightM == 20.0);
            CheckTrue("fabrication.breakout_fittings", b.FittingsM == 3.5);
            Check("fabrication.breakout_total", b.TotalM(), 23.5, 1e-12);

            ExpectError(true, "fabrication.breakout_neg_straight", delegate { new Fabrication.FabricationBreakout(-1.0, 2.0); });
            ExpectError(true, "fabrication.breakout_neg_fittings", delegate { new Fabrication.FabricationBreakout(1.0, -2.0); });
            ExpectError(true, "fabrication.breakout_neg_both", delegate { new Fabrication.FabricationBreakout(-1.0, -2.0); });
            ExpectOk("fabrication.breakout_zero_ok", delegate { new Fabrication.FabricationBreakout(0.0, 0.0); });

            var cuts = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("B", 3.0),
                new KeyValuePair<string, double>("A", 10.0),
                new KeyValuePair<string, double>("A", 2.0),
                new KeyValuePair<string, double>("B", 1.0),
            };
            List<KeyValuePair<string, double>> sched = Fabrication.CuttingSchedule(cuts);
            CheckInt("fabrication.schedule_count", sched.Count, 2);
            if (sched.Count == 2)
            {
                CheckStr("fabrication.schedule_key0", sched[0].Key, "A");
                CheckTrue("fabrication.schedule_val0", sched[0].Value == 12.0);
                CheckStr("fabrication.schedule_key1", sched[1].Key, "B");
                CheckTrue("fabrication.schedule_val1", sched[1].Value == 4.0);
            }

            CheckInt("fabrication.schedule_empty", Fabrication.CuttingSchedule(new List<KeyValuePair<string, double>>()).Count, 0);
        }

        private static void RunDevelopment()
        {
            FlatPiece p = Development.RoundDuctDevelopment(0.25, 4.0);
            double circ = Math.PI * 0.25;
            Check("development.duct_width", p.WidthM, circ, 1e-12);
            CheckTrue("development.duct_length", p.LengthM == 4.0);
            Check("development.duct_area", p.AreaM2, circ * 4.0, 1e-12);

            p = Development.RoundElbowDevelopment(0.3, 0.2, 45.0, 5);
            double arc = 45.0 * Math.PI / 180.0 * 0.3;
            double width = Math.PI * 0.2;
            Check("development.elbow_length", p.LengthM, arc, 1e-12);
            Check("development.elbow_area", p.AreaM2, width * arc, 1e-12);

            p = Development.ReducerConeDevelopment(0.1, 0.3, 0.5);
            double slant = Math.Sqrt(0.5 * 0.5 + Math.Pow((0.3 - 0.1) / 2.0, 2));
            double rw = Math.PI * (0.1 + 0.3) / 2.0;
            Check("development.reducer_length", p.LengthM, slant, 1e-12);
            Check("development.reducer_width", p.WidthM, rw, 1e-12);
            Check("development.reducer_area", p.AreaM2, rw * slant, 1e-12);

            p = Development.RoundElbowDevelopment(0.3, 0.2, 180.0, 7);
            Check("development.elbow180_length", p.LengthM, Math.PI * 0.3, 1e-12);
            Check("development.elbow180_area", p.AreaM2, Math.PI * 0.2 * Math.PI * 0.3, 1e-12);

            FlatPiece a = Development.RoundElbowDevelopment(0.3, 0.2, 90.0, 3);
            FlatPiece bb = Development.RoundElbowDevelopment(0.3, 0.2, 90.0, 12);
            CheckTrue("development.segments_same_area", a.AreaM2 == bb.AreaM2);

            p = Development.ReducerConeDevelopment(0.2, 0.2, 1.0);
            Check("development.straight_reducer_length", p.LengthM, 1.0, 1e-12);
            Check("development.straight_reducer_width", p.WidthM, Math.PI * 0.2, 1e-12);

            ExpectError(true, "development.duct_zero_d", delegate { Development.RoundDuctDevelopment(0.0, 1.0); });
            ExpectError(true, "development.duct_neg_len", delegate { Development.RoundDuctDevelopment(1.0, -1.0); });
            ExpectError(true, "development.duct_nan_d", delegate { Development.RoundDuctDevelopment(double.NaN, 1.0); });
            ExpectError(true, "development.elbow_zero_r", delegate { Development.RoundElbowDevelopment(0.0, 0.2, 90.0, 5); });
            ExpectError(true, "development.elbow_zero_d", delegate { Development.RoundElbowDevelopment(0.3, 0.0, 90.0, 5); });
            ExpectError(true, "development.elbow_zero_angle", delegate { Development.RoundElbowDevelopment(0.3, 0.2, 0.0, 5); });
            ExpectError(true, "development.elbow_zero_segments", delegate { Development.RoundElbowDevelopment(0.3, 0.2, 90.0, 0); });
            ExpectError(true, "development.elbow_inf_r", delegate { Development.RoundElbowDevelopment(double.PositiveInfinity, 0.2, 90.0, 5); });
            ExpectError(true, "development.reducer_zero_small", delegate { Development.ReducerConeDevelopment(0.0, 0.3, 0.5); });
            ExpectError(true, "development.reducer_neg_len", delegate { Development.ReducerConeDevelopment(0.1, 0.3, -1.0); });
            ExpectError(true, "development.reducer_nan_large", delegate { Development.ReducerConeDevelopment(0.1, double.NaN, 0.5); });
        }

        private static DuctSegment Seg(string id, double x0, double y0, double x1, double y1, double d)
        {
            return new DuctSegment(id, new Point2(x0, y0), new Point2(x1, y1), d);
        }

        private static void RunClash()
        {
            List<Clash> c = ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("A", 0.0, -1.0, 0.0, 1.0, 0.2),
                Seg("B", -1.0, 0.0, 1.0, 0.0, 0.2),
            }, 0.0);
            CheckInt("clash.crossing_count", c.Count, 1);
            if (c.Count == 1)
            {
                CheckTrue("clash.crossing_distance", c[0].DistanceM == 0.0);
                CheckStr("clash.crossing_a", c[0].A, "A");
                CheckStr("clash.crossing_b", c[0].B, "B");
            }

            CheckInt("clash.far_parallel", ClashDetection.ClashCount(ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("x", 0.0, 0.0, 10.0, 0.0, 0.1),
                Seg("y", 0.0, 5.0, 10.0, 5.0, 0.1),
            }, 0.0)), 0);

            var touching = new List<DuctSegment>
            {
                Seg("a", 0.0, 0.0, 10.0, 0.0, 0.2),
                Seg("b", 0.0, 0.2, 10.0, 0.2, 0.2),
            };
            CheckInt("clash.touching_no_clearance", ClashDetection.ClashCount(ClashDetection.FindClashes(touching, 0.0)), 0);
            CheckInt("clash.touching_with_clearance", ClashDetection.ClashCount(ClashDetection.FindClashes(touching, 0.1)), 1);
            var far = new List<DuctSegment>
            {
                Seg("a", 0.0, 0.0, 10.0, 0.0, 0.2),
                Seg("b", 0.0, 3.0, 10.0, 3.0, 0.2),
            };
            CheckInt("clash.far_no_clearance", ClashDetection.ClashCount(ClashDetection.FindClashes(far, 0.0)), 0);
            CheckInt("clash.far_big_clearance", ClashDetection.ClashCount(ClashDetection.FindClashes(far, 3.0)), 1);

            c = ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("s1", 0.0, 0.0, 5.0, 0.0, 0.3),
                Seg("s2", 0.0, 0.0, 5.0, 0.0, 0.3),
            }, 0.0);
            CheckInt("clash.identical_count", ClashDetection.ClashCount(c), 1);
            CheckTrue("clash.identical_distance", c.Count == 1 && c[0].DistanceM == 0.0);
            c = ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("s1", 0.0, 0.0, 5.0, 0.0, 0.3),
                Seg("s2", 3.0, 0.0, 8.0, 0.0, 0.3),
            }, 0.0);
            CheckInt("clash.overlap_count", ClashDetection.ClashCount(c), 1);
            CheckTrue("clash.overlap_distance", c.Count == 1 && c[0].DistanceM == 0.0);

            c = ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("p", 0.0, 0.0, 5.0, 0.0, 0.2),
                Seg("q", 4.0, 0.0, 9.0, 0.0, 0.2),
            }, 0.0);
            CheckInt("clash.collinear_count", ClashDetection.ClashCount(c), 1);
            CheckTrue("clash.collinear_distance", c.Count == 1 && c[0].DistanceM == 0.0);
            var gapped = new List<DuctSegment>
            {
                Seg("p", 0.0, 0.0, 4.0, 0.0, 0.2),
                Seg("q", 6.0, 0.0, 10.0, 0.0, 0.2),
            };
            CheckInt("clash.gapped_no_clearance", ClashDetection.ClashCount(ClashDetection.FindClashes(gapped, 0.0)), 0);
            c = ClashDetection.FindClashes(gapped, 2.5);
            CheckInt("clash.gapped_with_clearance", ClashDetection.ClashCount(c), 1);
            if (c.Count == 1) Check("clash.gapped_distance", c[0].DistanceM, 2.0, 1e-9);

            var manual = new List<Clash> { new Clash("A", "B", 0.0), new Clash("C", "D", 0.05) };
            CheckStr("clash.csv_rows", ClashDetection.ClashesAsCsv(manual), "a,b,distance_m\nA,B,0\nC,D,0.05\n");
            CheckStr("clash.csv_empty", ClashDetection.ClashesAsCsv(new List<Clash>()), "a,b,distance_m\n");

            ExpectError(true, "clash.negative_clearance_err", delegate
            {
                ClashDetection.FindClashes(new List<DuctSegment> { Seg("a", 0.0, 0.0, 1.0, 0.0, 0.1) }, -1.0);
            });

            c = ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("a", 0.0, 0.0, 0.0, 0.0, 0.2),
                Seg("b", 0.1, 0.0, 0.1, 1.0, 0.2),
            }, 0.0);
            CheckInt("clash.degenerate_count", ClashDetection.ClashCount(c), 1);
            if (c.Count == 1) Check("clash.degenerate_distance", c[0].DistanceM, 0.1, 1e-9);
            CheckInt("clash.degenerate_far", ClashDetection.ClashCount(ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("a", 0.0, 0.0, 0.0, 0.0, 0.2),
                Seg("b", 1.0, 0.0, 1.0, 1.0, 0.2),
            }, 0.0)), 0);
        }

        private static Polyline Pl(params double[] xy)
        {
            var pts = new List<Point2>();
            for (int i = 0; i + 1 < xy.Length; i += 2)
                pts.Add(new Point2(xy[i], xy[i + 1]));
            return new Polyline(pts);
        }

        private static void RunTopology()
        {
            TracedSystem sys = Topology.Trace(new List<Polyline> { Pl(0.0, 0.0, 5.0, 0.0) }, new TraceOptions());
            CheckInt("topology.single_chains", sys.Chains.Count, 1);
            Check("topology.single_length", sys.TotalLengthM(), 5.0, 1e-9);
            CheckInt("topology.single_network_len", sys.Network.Components.Count, 3);
            CheckInt("topology.single_flatten_len", sys.Flatten().Count, 1);
            Check("topology.single_default_diameter", sys.Flatten()[0].Diameter, 0.2, 1e-12);

            sys = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 0.0, 1.0, 0.0),
                Pl(1.0, 0.0, 3.0, 0.0),
            }, new TraceOptions());
            CheckInt("topology.collinear_chains", sys.Chains.Count, 1);
            Check("topology.collinear_length", sys.TotalLengthM(), 3.0, 1e-9);

            var teeOpts = new TraceOptions();
            teeOpts.Diameters["duct1"] = 0.3;
            teeOpts.Flows["term0"] = 0.06;
            teeOpts.Flows["term1"] = 0.04;
            sys = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 1.0, 1.0, 1.0, 2.0, 1.0),
                Pl(2.0, 1.0, 3.0, 1.0),
                Pl(2.0, 1.0, 2.0, 0.0),
            }, teeOpts);
            CheckInt("topology.tee_chains", sys.Chains.Count, 3);
            CheckInt("topology.tee_network_len", sys.Network.Components.Count, 7);
            CheckTrue("topology.tee_total_length", sys.TotalLengthM() == 4.0);
            double dp = sys.Network.Solve(new Fluid(1.204, 1.825e-5));
            CheckTrue("topology.tee_solve_dp_positive", dp > 0.0);
            CheckInt("topology.tee_flatten_len", sys.Flatten().Count, 3);

            ExpectError(true, "topology.rejects_degree4", delegate
            {
                Topology.Trace(new List<Polyline>
                {
                    Pl(-1.0, 0.0, 0.0, 0.0, 1.0, 0.0),
                    Pl(0.0, -1.0, 0.0, 0.0, 0.0, 1.0),
                }, new TraceOptions());
            });

            var flowOpts = new TraceOptions();
            flowOpts.Flows["term0"] = 0.06;
            flowOpts.Flows["term1"] = 0.04;
            sys = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 1.0, 2.0, 1.0),
                Pl(2.0, 1.0, 3.0, 1.0),
                Pl(2.0, 1.0, 2.0, 0.0),
            }, flowOpts);
            bool sawTerm0 = false, sawTerm1 = false;
            foreach (var kv in sys.Network.Components)
            {
                Terminal tm = kv.Value as Terminal;
                if (tm == null) continue;
                if (kv.Key == "term0") { sawTerm0 = true; Check("topology.term0_flow", tm.Flowrate, 0.06, 1e-9); }
                if (kv.Key == "term1") { sawTerm1 = true; Check("topology.term1_flow", tm.Flowrate, 0.04, 1e-9); }
            }
            CheckTrue("topology.terminals_present", sawTerm0 && sawTerm1);
        }

        private static FanPoint FP(double flow, double pressure)
        {
            return new FanPoint(flow, pressure);
        }

        private static FanCurve SampleCurve()
        {
            return new FanCurve("sample", new[] { FP(0.0, 300.0), FP(0.1, 250.0), FP(0.25, 100.0), FP(0.4, 0.0) });
        }

        private static void RunFan()
        {
            FanCurve fan = SampleCurve();

            for (int i = 0; i < fan.Points.Length; i++)
                Check("fan.knot" + i, fan.StaticPressureAt(fan.Points[i].FlowM3s), fan.Points[i].StaticPressurePa, 1e-12);

            Check("fan.mid_seg1", fan.StaticPressureAt(0.05), 275.0, 1e-9);
            Check("fan.quarter_seg2", fan.StaticPressureAt(0.1375), 212.5, 1e-9);
            Check("fan.mid_seg3", fan.StaticPressureAt(0.325), 50.0, 1e-9);

            ExpectError(true, "fan.below_range_err", delegate { fan.StaticPressureAt(-0.01); });
            ExpectError(true, "fan.above_range_err", delegate { fan.StaticPressureAt(0.41); });
            ExpectError(true, "fan.nan_err", delegate { fan.StaticPressureAt(double.NaN); });
            ExpectOk("fan.lower_boundary_ok", delegate { fan.StaticPressureAt(0.0); });
            ExpectOk("fan.upper_boundary_ok", delegate { fan.StaticPressureAt(0.4); });

            Check("fan.power_basic", Fan.Power(1.5, 800.0, 0.4), 3000.0, 1e-9);
            Check("fan.power_zero_flow", Fan.Power(0.0, 800.0, 0.4), 0.0, 1e-12);
            Check("fan.power_unit_efficiency", Fan.Power(1.5, 800.0, 1.0), 1200.0, 1e-9);

            ExpectError(true, "fan.power_eta_zero", delegate { Fan.Power(1.0, 100.0, 0.0); });
            ExpectError(true, "fan.power_eta_negative", delegate { Fan.Power(1.0, 100.0, -0.1); });
            ExpectError(true, "fan.power_eta_gt1", delegate { Fan.Power(1.0, 100.0, 1.5); });
            ExpectError(true, "fan.power_negative_flow", delegate { Fan.Power(-1.0, 100.0, 0.5); });
            ExpectError(true, "fan.power_negative_pressure", delegate { Fan.Power(1.0, -100.0, 0.5); });

            FanCurve weak = new FanCurve("weak", new[] { FP(0.0, 300.0), FP(0.5, 150.0) });
            FanCurve strong = new FanCurve("strong", new[] { FP(0.0, 500.0), FP(0.5, 400.0) });
            FanCurve mid = new FanCurve("mid", new[] { FP(0.0, 450.0), FP(0.5, 300.0) });
            FanCurve[] curves = { weak, mid, strong };
            CheckTrue("fan.pick_250", Fan.PickFan(curves, 0.3, 250.0) == 1);
            CheckTrue("fan.pick_200", Fan.PickFan(curves, 0.3, 200.0) == 0);
            CheckTrue("fan.pick_exact_330", Fan.PickFan(curves, 0.3, 330.0) == 1);

            FanCurve small = new FanCurve("small", new[] { FP(0.0, 200.0), FP(0.3, 100.0) });
            CheckTrue("fan.pick_none_too_high", Fan.PickFan(new[] { small }, 0.15, 5000.0) == null);
            CheckTrue("fan.pick_none_empty", Fan.PickFan(new FanCurve[0], 0.15, 100.0) == null);

            FanCurve narrow = new FanCurve("narrow", new[] { FP(0.0, 400.0), FP(0.2, 100.0) });
            FanCurve wide = new FanCurve("wide", new[] { FP(0.0, 400.0), FP(0.6, 250.0) });
            CheckTrue("fan.pick_skips_out_of_range", Fan.PickFan(new[] { narrow, wide }, 0.4, 300.0) == 1);

            Check("fan.curve_at_0_3", fan.StaticPressureAt(0.3), 200.0 / 3.0, 1e-9);
            Check("fan.margin_positive", Fan.Margin(fan, 0.3, 40.0) ?? double.NaN, 26.66666666, 1e-7);
            CheckTrue("fan.margin_negative", (Fan.Margin(fan, 0.3, 100.0) ?? 1.0) < 0.0);
            CheckTrue("fan.margin_zero", Math.Abs(Fan.Margin(fan, 0.3, 200.0 / 3.0) ?? 1.0) < 1e-9);
            CheckTrue("fan.margin_undefined_out_of_range", Fan.Margin(fan, 0.9, 40.0) == null);
            ExpectError(true, "fan.margin_negative_flow_err", delegate { Fan.Margin(fan, -0.1, 40.0); });
            ExpectError(true, "fan.margin_negative_pressure_err", delegate { Fan.Margin(fan, 0.3, -1.0); });

            ExpectError(true, "fan.curve_one_point", delegate { new FanCurve("one", new[] { FP(0.1, 100.0) }); });
            ExpectError(true, "fan.curve_equal_flows", delegate { new FanCurve("dup", new[] { FP(0.1, 100.0), FP(0.1, 90.0) }); });
            ExpectError(true, "fan.curve_decreasing_flow", delegate { new FanCurve("down", new[] { FP(0.2, 100.0), FP(0.1, 90.0) }); });
            ExpectError(true, "fan.curve_nan", delegate { new FanCurve("nan", new[] { FP(0.1, double.NaN), FP(0.2, 90.0) }); });
            ExpectError(true, "fan.curve_inf", delegate { new FanCurve("inf", new[] { FP(double.PositiveInfinity, 100.0), FP(0.2, 90.0) }); });
            ExpectOk("fan.curve_valid", delegate { new FanCurve("ok", new[] { FP(0.0, 0.0), FP(0.5, 100.0) }); });
        }

        private static void RunInsulation()
        {
            const double MW = 0.035, D = 0.2, HI = 10.0, HE = 8.0;

            CheckTrue("insulation.no_risk_zero",
                Insulation.RequiredThicknessCondensation(40.0, 5.0, 15.0, MW, D, HI, HE) == 0.0);

            double dry = Insulation.RequiredThicknessCondensation(8.0, 12.0, 24.0, MW, D, HI, HE);
            double humid = Insulation.RequiredThicknessCondensation(8.0, 17.0, 24.0, MW, D, HI, HE);
            CheckTrue("insulation.humid_ge_dry", humid >= dry);

            double t = Insulation.RequiredThicknessCondensation(8.0, 15.8, 24.0, MW, D, HI, HE);
            double q = Insulation.HeatLossWithInsulation(8.0, 24.0, MW, D, t, HI, HE);
            double rOut = 1.0 / (HE * Math.PI * (D + 2.0 * t));
            double ts = 24.0 + q * rOut;
            CheckTrue("insulation.cond_surface_above_dew", ts >= 15.8 - 1e-6);
            CheckTrue("insulation.cond_thickness_range", t > 0.0 && t < 0.1);

            double strict = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 10.0, MW, D, HI, HE);
            double loose = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 50.0, MW, D, HI, HE);
            CheckTrue("insulation.strict_ge_loose", strict >= loose);
            ExpectError(true, "insulation.zero_target_err", delegate
            {
                Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 0.0, MW, D, HI, HE);
            });

            double t25 = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 25.0, MW, D, HI, HE);
            double q25 = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, t25, HI, HE));
            CheckTrue("insulation.heat_loss_meets_target", q25 <= 25.0 + 1e-9);

            double thin = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, 0.02, HI, HE));
            double thick = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, 0.06, HI, HE));
            CheckTrue("insulation.thick_lt_thin", thick < thin);

            CheckTrue("insulation.select_20", Insulation.SelectThickness(0.020) == 0.02);
            CheckTrue("insulation.select_45", Insulation.SelectThickness(0.045) == 0.05);
            CheckTrue("insulation.select_61", Insulation.SelectThickness(0.061) == 0.08);
            CheckTrue("insulation.select_1mm", Insulation.SelectThickness(0.001) == 0.02);
            ExpectError(true, "insulation.select_above_max_err", delegate { Insulation.SelectThickness(0.200); });

            double cond;
            CheckTrue("insulation.material_mineral_wool",
                Insulation.MaterialConductivity("mineral_wool", out cond) && cond == 0.035);
            CheckTrue("insulation.material_pir",
                Insulation.MaterialConductivity("PIR", out cond) && cond == 0.024);
            CheckTrue("insulation.material_bogus", !Insulation.MaterialConductivity("bogus", out cond));
            CheckInt("insulation.materials_count", Insulation.Materials.Length, 5);

            ExpectError(true, "insulation.zero_conductivity_err", delegate
            {
                Insulation.RequiredThicknessCondensation(8.0, 15.0, 24.0, 0.0, D, HI, HE);
            });
            ExpectError(true, "insulation.zero_diameter_err", delegate
            {
                Insulation.RequiredThicknessCondensation(8.0, 15.0, 24.0, MW, 0.0, HI, HE);
            });
            ExpectError(true, "insulation.negative_thickness_err", delegate
            {
                Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, -0.01, HI, HE);
            });
        }

        private static void RunSound()
        {
            double lb = Sound.RegeneratedNoiseRound(2.0, 0.2);
            double hi = Sound.RegeneratedNoiseRound(6.0, 0.2);
            CheckTrue("sound.faster_is_louder", hi > lb);
            Check("sound.v6_scaling", hi - lb, 60.0 * Math.Log10(3.0), 1e-9);

            double small = Sound.RegeneratedNoiseRound(4.0, 0.1);
            double large = Sound.RegeneratedNoiseRound(4.0, 0.5);
            CheckTrue("sound.bigger_is_quieter", large < small);
            Check("sound.d2_scaling", large - small, -20.0 * Math.Log10(5.0), 1e-9);

            double lw = Sound.RegeneratedNoiseRound(2.0, 0.5);
            Check("sound.closed_form", lw, 10.0 + 60.0 * Math.Log10(2.0) - 20.0 * Math.Log10(0.5), 1e-9);

            double thin = Sound.RegeneratedNoiseRound(4.0, 0.2, 1.0);
            double dense = Sound.RegeneratedNoiseRound(4.0, 0.2, 1.5);
            CheckTrue("sound.denser_is_louder", dense > thin);
            Check("sound.density_scaling", dense - thin, 10.0 * Math.Log10(1.5 / 1.0), 1e-9);

            ExpectError(true, "sound.regen_zero_velocity", delegate { Sound.RegeneratedNoiseRound(0.0, 0.2); });
            ExpectError(true, "sound.regen_negative_velocity", delegate { Sound.RegeneratedNoiseRound(-3.0, 0.2); });
            ExpectError(true, "sound.regen_zero_diameter", delegate { Sound.RegeneratedNoiseRound(4.0, 0.0); });
            ExpectError(true, "sound.regen_negative_density", delegate { Sound.RegeneratedNoiseRound(4.0, 0.2, -1.0); });

            double lp = Sound.DuctPressureLevel(60.0, 100.0, 0.2);
            Check("sound.room_eq_closed_form", lp, 60.0 + 10.0 * Math.Log10(4.0 * (1.0 - 0.2) / (0.2 * 100.0)), 1e-9);

            CheckTrue("sound.absorption_quieter",
                Sound.DuctPressureLevel(60.0, 100.0, 0.45) < Sound.DuctPressureLevel(60.0, 100.0, 0.05));

            ExpectError(true, "sound.room_zero_area", delegate { Sound.DuctPressureLevel(60.0, 0.0, 0.2); });
            ExpectError(true, "sound.room_zero_alpha", delegate { Sound.DuctPressureLevel(60.0, 100.0, 0.0); });
            ExpectError(true, "sound.room_unit_alpha", delegate { Sound.DuctPressureLevel(60.0, 100.0, 1.0); });
            ExpectError(true, "sound.room_negative_area", delegate { Sound.DuctPressureLevel(60.0, -5.0, 0.2); });

            CheckTrue("sound.nc_office_35", Sound.NcOk("office", 35.0));
            CheckTrue("sound.nc_office_34_9", Sound.NcOk("office", 34.9));
            CheckTrue("sound.nc_office_35_1", !Sound.NcOk("office", 35.1));
            CheckTrue("sound.nc_bedroom_26", !Sound.NcOk("bedroom", 26.0));

            CheckTrue("sound.nc_limit_studio", Sound.NoiseLimitsNc["studio"] == 25.0);
            ExpectError(true, "sound.nc_bogus_space", delegate { Sound.NcOk("bogus", 10.0); });
            CheckTrue("sound.nc_target_eq", Sound.NcOkTarget(35.0, 35.0) == Sound.NcOk("office", 35.0));
            CheckTrue("sound.nc_target_under", Sound.NcOkTarget(35.0, 34.0) == Sound.NcOk("office", 34.0));
            CheckTrue("sound.nc_target_over", Sound.NcOkTarget(35.0, 36.0) == Sound.NcOk("office", 36.0));
        }

        private static ElectricalData Pump()
        {
            return new ElectricalData("P-01", "centrifugal pump", 1500.0);
        }

        private static void RunElectrical()
        {
            ElectricalData p = Pump();
            p.VoltageV = 230.0;
            p.PowerFactor = 0.9;
            double expected = 1500.0 / (230.0 * 0.9);
            Check("electrical.current", p.Current() ?? double.NaN, expected, 1e-9);
            Check("electrical.current_stored", p.CurrentA ?? double.NaN, expected, 1e-9);

            Check("electrical.power_kw", Pump().PowerKw(), 1.5, 1e-12);
            Check("electrical.power_kw_big", new ElectricalData("H-01", "heater", 12000.0).PowerKw(), 12.0, 1e-12);

            var s = new ElectricalSchedule();
            s.Add(new ElectricalData("F-01", "supply fan", 5500.0));
            s.Add(new ElectricalData("P-01", "circulation pump", 1500.0));
            s.Add(new ElectricalData("H-01", "electric heater", 2000.0));
            Check("electrical.total_power", s.TotalPowerW(), 9000.0, 1e-9);

            s = new ElectricalSchedule();
            ElectricalData fan = new ElectricalData("F-01", "supply fan", 5500.0);
            fan.VoltageV = 400.0;
            fan.PowerFactor = 0.85;
            s.Add(fan);
            ElectricalData pump = new ElectricalData("P-01", "circulation pump", 1500.0);
            pump.VoltageV = 230.0;
            pump.PowerFactor = 0.9;
            s.Add(pump);
            Check("electrical.total_current", s.TotalCurrentA() ?? double.NaN,
                5500.0 / (400.0 * 0.85) + 1500.0 / (230.0 * 0.9), 1e-9);

            p = Pump();
            CheckTrue("electrical.current_none_no_data", p.Current() == null);
            CheckTrue("electrical.current_a_none_no_data", p.CurrentA == null);
            p.PowerFactor = 0.9;
            CheckTrue("electrical.current_none_pf_only", p.Current() == null);
            ElectricalData m = new ElectricalData("M-01", "motor", 1000.0);
            m.VoltageV = 230.0;
            CheckTrue("electrical.current_none_voltage_only", m.Current() == null);

            s = new ElectricalSchedule();
            fan = new ElectricalData("F-01", "supply fan", 5500.0);
            fan.VoltageV = 400.0;
            fan.PowerFactor = 0.85;
            s.Add(fan);
            s.Add(new ElectricalData("H-01", "electric heater", 2000.0));
            CheckTrue("electrical.total_current_none_when_missing", s.TotalCurrentA() == null);

            fan = new ElectricalData("F-01", "supply fan", 5500.0);
            fan.VoltageV = 400.0;
            fan.PowerFactor = 0.85;
            fan.FrequencyHz = 50.0;
            fan.Current();
            s = new ElectricalSchedule();
            s.Add(fan);
            s.Add(new ElectricalData("H-01", "electric heater", 2000.0));
            string[] lines = Electrical.AsCsv(s).Split('\n');
            CheckInt("electrical.csv_lines", lines.Length, 3);
            if (lines.Length == 3)
            {
                CheckStr("electrical.csv_header", lines[0],
                    "component_id,device_type,power_w,power_kw,voltage_v,current_a,power_factor,frequency_hz");
                CheckTrue("electrical.csv_row1_prefix", lines[1].StartsWith("F-01,supply fan,5500,5.5,400,"));
                CheckTrue("electrical.csv_row1_suffix", lines[1].EndsWith(",0.85,50"));
                CheckStr("electrical.csv_row2", lines[2], "H-01,electric heater,2000,2,,,,");
            }

            ExpectError(true, "electrical.negative_power_err", delegate { new ElectricalData("X-01", "heater", -5.0); });
            ExpectError(true, "electrical.nan_power_err", delegate { new ElectricalData("X-02", "heater", double.NaN); });
            ExpectError(true, "electrical.inf_power_err", delegate { new ElectricalData("X-03", "heater", double.PositiveInfinity); });
            ExpectOk("electrical.zero_power_ok", delegate { new ElectricalData("X-04", "heater", 0.0); });

            s = new ElectricalSchedule();
            CheckInt("electrical.len_empty", s.Len(), 0);
            CheckTrue("electrical.is_empty", s.IsEmpty());
            s.Add(new ElectricalData("A-01", "AHU", 2000.0));
            s.Add(new ElectricalData("A-02", "AHU", 3000.0));
            CheckInt("electrical.len_two", s.Len(), 2);
            CheckTrue("electrical.not_empty", !s.IsEmpty());
            CheckInt("electrical.iter_count", new List<ElectricalData>(s.Iter()).Count, 2);
        }

        private static void RunResolve()
        {
            Network tee = TeeNetwork();
            double dp1 = tee.Solve();
            double flow1 = tee.Components["duct"].Port_("inlet").Flowrate ?? 0.0;
            double dp2 = tee.Solve();
            Check("resolve.dp_identical", dp2, dp1, 0.0);
            Check("resolve.flow_identical", tee.Components["duct"].Port_("inlet").Flowrate ?? 0.0, flow1, 0.0);
            Check("resolve.flow_is_demand_sum", flow1, 0.1, 1e-12);

            Fluid air = Fluid.StandardAir();
            AnalysisSummary fresh = Analysis.Analyze(TeeNetwork(), air);
            AnalysisSummary again = Analysis.Analyze(tee, air);
            Check("resolve.analysis_critical_dp", again.CriticalDpPa, fresh.CriticalDpPa, 0.0);
            CheckInt("resolve.analysis_branches", again.NBranches, fresh.NBranches);
            for (int i = 0; i < fresh.Branches.Count; i++)
                Check("resolve.analysis_flow_" + fresh.Branches[i].ComponentId,
                    again.Branches[i].FlowM3s, fresh.Branches[i].FlowM3s, 0.0);
        }

        private static Network TeeNetwork()
        {
            var net = new Network { Name = "tee" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new RigidDuct("duct", new Round(0.315), 20.0));
            net.Add("tee", new Tee("tee", new Round(0.315), 0.1, 0.4));
            net.Add("d2", new RigidDuct("d2", new Round(0.2), 5.0));
            net.Add("flex", new FlexDuct("flex", 0.125, 3.0, 2.0, 100.0));
            net.Add("t1", new Terminal("t1", 0.06));
            net.Add("t2", new Terminal("t2", 0.04));
            net.Connect("ahu", "duct");
            net.Connect("duct", "tee");
            net.Connect("tee.straight", "d2");
            net.Connect("tee.branch", "flex");
            net.Connect("d2", "t1");
            net.Connect("flex", "t2");
            return net;
        }

        private static void RunNetworkJson()
        {
            Network tee = TeeNetwork();
            double dpTee = tee.Solve();
            string json = NetworkJson.Serialize(TeeNetwork());
            Dictionary<string, ComponentMeta> meta;
            Network back = NetworkJson.Parse(json, out meta);
            CheckInt("json.tee_component_count", back.Components.Count, 7);
            CheckStr("json.tee_name", back.Name, "tee");
            CheckStr("json.tee_class_tee", NetworkJson.WentaClassOf(back.Components["tee"]), "Tee");
            CheckStr("json.tee_class_flex", NetworkJson.WentaClassOf(back.Components["flex"]), "FlexDuct");
            Check("json.tee_dp_roundtrip", back.Solve(), dpTee, 1e-12);
            Check("json.tee_dp_vector", dpTee, 7.629497821799035, 1e-12);
            Check("json.tee_branch_flow", back.Components["tee"].Port_("branch").Flowrate ?? 0.0, 0.04, 1e-12);
            CheckInt("json.tee_meta_count", meta.Count, 7);
            CheckTrue("json.tee_guid_generated", meta["duct"].Guid != null && meta["duct"].Guid.Length >= 32);
            CheckStr("json.reserialize_stable", NetworkJson.Serialize(back, meta), json);

            var chain = new Network { Name = "readme" };
            chain.Add("ahu", new Source("AHU"));
            chain.Add("duct", new RigidDuct("duct", new Round(0.2), 20.0));
            chain.Add("term", new Terminal("terminal", 0.1));
            chain.Connect("ahu", "duct");
            chain.Connect("duct", "term");
            var chainMeta = new Dictionary<string, ComponentMeta>
            {
                { "duct", new ComponentMeta { Guid = "0f8fad5b-d9cb-469f-a165-70867728950e", DrawingScope = "Model" } }
            };
            string chainJson = NetworkJson.Serialize(chain, chainMeta);
            CheckTrue("json.schema_version_field", chainJson.Contains("\"schema_version\":1"));
            CheckTrue("json.wenta_class_field", chainJson.Contains("\"wenta_class\":\"RigidDuct\""));
            Dictionary<string, ComponentMeta> meta2;
            Network chainBack = NetworkJson.Parse(chainJson, out meta2);
            Check("json.chain_dp", chainBack.Solve(), 14.13473757973617, 1e-12);
            CheckStr("json.guid_roundtrip", meta2["duct"].Guid, "0f8fad5b-d9cb-469f-a165-70867728950e");
            CheckStr("json.scope_roundtrip", meta2["duct"].DrawingScope, "Model");

            ExpectError(true, "json.err_no_components", () => NetworkJson.Parse("{\"schema_version\":1,\"name\":\"x\"}"));
            ExpectError(true, "json.err_unknown_class", () => NetworkJson.Parse(
                "{\"schema_version\":1,\"components\":[{\"id\":\"a\",\"wenta_class\":\"Nozzle\"}],\"connections\":[]}"));
            ExpectError(true, "json.err_schema_version", () => NetworkJson.Parse(
                "{\"schema_version\":2,\"components\":[],\"connections\":[]}"));
            ExpectError(true, "json.err_not_json", () => NetworkJson.Parse("not json"));
        }

        private static void RunCatalogMerge(string catalogDir)
        {
            ZetaCatalog generic = ZetaCatalog.Load(Path.Combine(catalogDir, "example-generic.json"));
            ZetaCatalog round = ZetaCatalog.Load(Path.Combine(catalogDir, "example-generic-round.json"));
            ZetaCatalog vendor = ZetaCatalog.Load(Path.Combine(catalogDir, "example-vendor-style.json"));
            CheckInt("catmerge.generic_count", generic.Fittings.Count, 5);
            CheckInt("catmerge.round_count", round.Fittings.Count, 8);
            CheckInt("catmerge.vendor_count", vendor.Fittings.Count, 10);
            CheckInt("catmerge.load_no_warnings", generic.Warnings.Count + round.Warnings.Count + vendor.Warnings.Count, 0);
            CheckInt("catmerge.schema_version", ZetaCatalog.SchemaVersion, 1);

            ZetaCatalog merged = ZetaCatalog.Merge(generic, vendor);
            CheckInt("catmerge.merged_count", merged.Fittings.Count, 13);
            CheckInt("catmerge.warning_count", merged.Warnings.Count, 2);
            CheckStr("catmerge.merged_name", merged.Name, "example-generic-rect+ExampleVent-fictional");
            CheckStr("catmerge.warning_text", merged.Warnings[1],
                "id vav-box: zeta 0.35 (source generic VAV box fully open (example entry)) overridden by " +
                "zeta 0.3 (source ExampleVent VAV-1 fully open (fictional example - not real vendor data))");
            Check("catmerge.overridden_elbow", merged.ById("rect-elbow-r1.0").Zeta, 0.19, 1e-12);
            Check("catmerge.overridden_vav", merged.ById("vav-box").Zeta, 0.30, 1e-12);
            CheckStr("catmerge.override_in_place", merged.Fittings[1].Id, "rect-elbow-r1.0");
            Check("catmerge.base_kept", merged.ById("rect-elbow-r1.5").Zeta, 0.17, 1e-12);
            Check("catmerge.vendor_only", merged.ById("EV-RB-280-630").Zeta, 0.24, 1e-12);
            CheckStr("catmerge.appended_first", merged.Fittings[5].Id, "EV-RB-100-250");
            CheckStr("catmerge.appended_last", merged.Fittings[12].Id, "EV-RG-200-1000");
            Check("catmerge.zeta_damper_small", merged.ZetaFor("damper", new[] { 300.0 }), 0.30, 1e-12);
            Check("catmerge.zeta_damper_large", merged.ZetaFor("damper", new[] { 800.0 }), 0.12, 1e-12);
            Check("catmerge.zeta_rect_elbow_big", merged.ZetaFor("rect_elbow", new[] { 1400.0, 800.0 }), 0.16, 1e-12);
            Check("catmerge.zeta_grille_vendor", merged.ZetaFor("grille", new[] { 300.0, 200.0 }), 0.31, 1e-12);
            Check("catmerge.zeta_grille_fallback", generic.ZetaFor("grille", new[] { 300.0, 200.0 }), 0.2875, 1e-12);

            ZetaCatalog three = ZetaCatalog.Merge(new[] { generic, round, vendor });
            CheckInt("catmerge.threeway_count", three.Fittings.Count, 21);
            CheckInt("catmerge.threeway_warnings", three.Warnings.Count, 2);
            CheckInt("catmerge.warnings_carried", ZetaCatalog.Merge(merged, round).Warnings.Count, 2);
            CheckInt("catmerge.self_merge_warns_all", ZetaCatalog.Merge(generic, generic).Warnings.Count, 5);

            ExpectError(true, "catmerge.err_newer_version", () =>
                ZetaCatalog.Parse("{\"name\":\"v99\",\"version\":99,\"fittings\":[]}", "inline-v99"));
            ExpectError(true, "catmerge.err_bool_zeta", () =>
                ZetaCatalog.Parse("{\"name\":\"b\",\"version\":1,\"fittings\":[{\"id\":\"x\",\"zeta\":true}]}", "inline-bool"));
        }

        private static void RunKnrMap()
        {
            var net = new Network { Name = "knr-tee" };
            net.Add("ahu", new Source("AHU"));
            net.Add("duct", new RigidDuct("duct", new Round(0.315), 20.0));
            net.Add("tee", new Tee("tee", new Round(0.315), 0.1, 0.4));
            net.Add("d2", new RigidDuct("d2", new Round(0.2), 5.0));
            net.Add("flex", new FlexDuct("flex", 0.125, 3.0, 2.0, 100.0));
            net.Add("t1", new Terminal("t1", 0.06));
            net.Add("t2", new Terminal("t2", 0.04));
            net.Connect("ahu", "duct");
            net.Connect("duct", "tee");
            net.Connect("tee.straight", "d2");
            net.Connect("tee.branch", "flex");
            net.Connect("d2", "t1");
            net.Connect("flex", "t2");
            net.Solve();

            KnrMap def = KnrMap.Default();
            CheckStr("knr.default_duct", def.CodeFor("duct"), Bom.KnrMap["duct"]);
            CheckStr("knr.default_fitting_any_shape", def.CodeFor("fitting", "round", "tee"), "KNR 2-08 0301 (configure)");
            CheckStr("knr.default_source_empty", def.CodeFor("source"), "");
            CheckInt("knr.default_overrides", def.Overrides.Count, 0);
            CheckTrue("knr.default_unknown_null", def.CodeFor("nonsense", "round") == null);
            CheckStr("knr.default_unknown_listed", def.Unmapped[0], "nonsense shape=round");

            Bom legacy = Bom.Build(net);
            Bom explicitDefault = Bom.Build(net, KnrMap.Default());
            CheckStr("knr.default_bom_csv_identical", explicitDefault.ToCsv(), legacy.ToCsv());
            CheckStr("knr.default_bom_duct_code", FindBom(legacy, "duct").KnrCode, "KNR 2-08 0101 (configure)");
            CheckInt("knr.default_bom_unmapped", legacy.Unmapped.Count, 0);
            CheckStr("knr.default_bom_origin", legacy.Mapping.Origin, "default");

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs", "knr-example.json");
            KnrMap file = KnrMap.Load(path);
            CheckStr("knr.file_edition", file.Edition, "KNR 2-08 example (configure per your edition)");
            CheckInt("knr.file_version", file.Version, 1);
            CheckInt("knr.file_codes", file.Codes.Count, 5);
            CheckInt("knr.file_overrides", file.Overrides.Count, 3);
            Bom fromFile = Bom.Build(net, file);
            CheckStr("knr.file_duct_round", FindBom(fromFile, "duct").KnrCode, "2-08 01xx-A (placeholder: round sheet-metal duct)");
            CheckStr("knr.file_tee_override", FindBom(fromFile, "tee").KnrCode, "2-08 03xx-T (placeholder: tee / branch piece)");
            CheckStr("knr.file_flex", FindBom(fromFile, "flex").KnrCode, "2-08 02xx-A (placeholder: flexible duct)");
            CheckStr("knr.file_terminal", FindBom(fromFile, "t1").KnrCode, "2-08 04xx-A (placeholder: air terminal)");
            CheckStr("knr.file_source_empty", FindBom(fromFile, "ahu").KnrCode, "");
            CheckInt("knr.file_unmapped", fromFile.Unmapped.Count, 0);
            CheckTrue("knr.file_csv_differs", fromFile.ToCsv() != legacy.ToCsv());

            var rect = new Network { Name = "knr-rect" };
            rect.Add("src", new Source("src"));
            rect.Add("rd", new RigidDuct("rd", new Rectangular(0.4, 0.2), 4.0));
            rect.Add("damp", new TwoPortFitting("damp", new Rectangular(0.4, 0.2), 0.3));
            rect.Add("rd2", new RigidDuct("rd2", new Rectangular(0.4, 0.2), 4.0));
            rect.Add("el", new TwoPortFitting("el", new Round(0.315), 0.2));
            rect.Add("out", new Terminal("out", 0.2));
            rect.Connect("src", "rd");
            rect.Connect("rd", "damp");
            rect.Connect("damp", "rd2");
            rect.Connect("rd2", "el");
            rect.Connect("el", "out");
            rect.Solve();
            Bom rectBom = Bom.Build(rect, file);
            CheckStr("knr.file_duct_rect_override", FindBom(rectBom, "rd").KnrCode, "2-08 01xx-B (placeholder: rectangular sheet-metal duct)");
            CheckStr("knr.file_inline_rect_override", FindBom(rectBom, "damp").KnrCode, "2-08 03xx-R (placeholder: rectangular in-line fitting)");
            CheckStr("knr.file_inline_round_base", FindBom(rectBom, "el").KnrCode, "2-08 03xx-A (placeholder: generic fitting)");

            KnrMap partial = KnrMap.Parse(
                "{\"schema_version\":1,\"edition\":\"partial\",\"codes\":{\"duct\":\"D-1\",\"source\":\"\"}," +
                " \"overrides\":[{\"match\":{\"kind\":\"fitting\",\"type\":\"tee\"},\"code\":\"T-1\"}]}", "inline-partial");
            Bom partialBom = Bom.Build(net, partial);
            CheckStr("knr.partial_duct", FindBom(partialBom, "duct").KnrCode, "D-1");
            CheckStr("knr.partial_tee_via_override_only", FindBom(partialBom, "tee").KnrCode, "T-1");
            CheckStr("knr.partial_flex_empty", FindBom(partialBom, "flex").KnrCode, "");
            CheckStr("knr.partial_terminal_empty", FindBom(partialBom, "t2").KnrCode, "");
            CheckInt("knr.partial_unmapped_count", partialBom.Unmapped.Count, 2);
            CheckStr("knr.partial_unmapped_flex", partialBom.Unmapped[0], "flex shape=round");
            CheckStr("knr.partial_unmapped_terminal", partialBom.Unmapped[1], "terminal");
            CheckInt("knr.partial_map_unmapped_count", partial.Unmapped.Count, 2);
            CheckTrue("knr.partial_override_needs_type", partial.CodeFor("fitting") == null);
            CheckStr("knr.partial_unmapped_fitting", partial.Unmapped[2], "fitting");

            try
            {
                KnrMap.Parse("{\"schema_version\":2,\"edition\":\"future\",\"codes\":{}}", "inline-v2");
                Fail("knr.err_schema_version_2", "expected WentaException, got success");
            }
            catch (WentaException e)
            {
                CheckTrue("knr.err_schema_version_2", Contains(e.Message, "schema_version 2") && Contains(e.Message, "schema_version 1"));
            }
            ExpectError(true, "knr.err_schema_version_string", () => KnrMap.Parse("{\"schema_version\":\"1\",\"codes\":{}}", "inline"));
            ExpectError(true, "knr.err_codes_not_object", () => KnrMap.Parse("{\"schema_version\":1,\"codes\":\"2-08\"}", "inline"));
            ExpectError(true, "knr.err_codes_missing", () => KnrMap.Parse("{\"schema_version\":1}", "inline"));
            ExpectError(true, "knr.err_code_not_string", () => KnrMap.Parse("{\"schema_version\":1,\"codes\":{\"duct\":101}}", "inline"));
            ExpectError(true, "knr.err_override_no_kind", () => KnrMap.Parse(
                "{\"schema_version\":1,\"codes\":{},\"overrides\":[{\"match\":{\"shape\":\"round\"},\"code\":\"x\"}]}", "inline"));
            ExpectError(true, "knr.err_override_no_code", () => KnrMap.Parse(
                "{\"schema_version\":1,\"codes\":{},\"overrides\":[{\"match\":{\"kind\":\"duct\"}}]}", "inline"));
            ExpectError(true, "knr.err_not_json", () => KnrMap.Parse("not json", "inline"));
            ExpectOk("knr.ok_version_omitted", () => KnrMap.Parse("{\"codes\":{\"duct\":\"D\"}}", "inline"));
        }

        private static void RunBomExport()
        {
            Network net = TeeNetwork();
            net.Solve();
            Bom bom = Bom.Build(net);

            string json = BomExport.ToJson(bom, "bom-tee");
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var root = ser.Deserialize<Dictionary<string, object>>(json);
            CheckInt("bomx.schema_version", Convert.ToInt32(root["schema_version"]), 1);
            CheckStr("bomx.network", (string)root["network"], "bom-tee");
            var rows = (System.Collections.ArrayList)root["rows"];
            CheckInt("bomx.row_count", rows.Count, bom.Rows.Count);
            CheckInt("bomx.row_count_7", rows.Count, 7);
            var totals = (Dictionary<string, object>)root["totals"];
            Check("bomx.total_length", Convert.ToDouble(totals["length"], CultureInfo.InvariantCulture), bom.TotalLength, 1e-12);
            Check("bomx.total_area", Convert.ToDouble(totals["area"], CultureInfo.InvariantCulture), bom.TotalArea, 1e-12);
            Check("bomx.total_length_28", bom.TotalLength, 28.0, 1e-12);
            var first = (Dictionary<string, object>)rows[0];
            CheckStr("bomx.first_item", (string)first["item_id"], bom.Rows[0].ItemId);
            CheckTrue("bomx.first_has_knr_key", first.ContainsKey("knr_code"));
            CheckTrue("bomx.null_network", BomExport.ToJson(bom).Contains("\"network\":null"));
            ExpectError(true, "bomx.err_null_bom", () => BomExport.ToJson(null));

            byte[] xlsx = BomExport.ToXlsx(bom);
            CheckTrue("bomx.xlsx_signature", xlsx.Length > 4 && xlsx[0] == 0x50 && xlsx[1] == 0x4B && xlsx[2] == 3 && xlsx[3] == 4);
            string text = System.Text.Encoding.UTF8.GetString(xlsx);
            foreach (string part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml",
                                            "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml" })
                CheckTrue("bomx.xlsx_part_" + part.Replace('/', '_'), text.Contains(part));
            CheckTrue("bomx.xlsx_totals_row", text.Contains("<row r=\"9\""));
            CheckTrue("bomx.xlsx_no_extra_row", !text.Contains("<row r=\"10\""));
            CheckTrue("bomx.xlsx_total_label", text.Contains("TOTAL"));
            CheckTrue("bomx.xlsx_sheet_name", text.Contains("name=\"BOM\""));
            CheckTrue("bomx.xlsx_sheet_name_sanitised",
                System.Text.Encoding.UTF8.GetString(BomExport.ToXlsx(bom, "a/b:c")).Contains("name=\"a_b_c\""));
        }

        private static void RunPressureReport()
        {
            Network net = TeeNetwork();
            double dp = net.Solve();
            PressureReport rep = PressureReport.Build(net);
            Check("pressure.critical_dp", rep.CriticalDpPa, dp, 0.0);
            CheckInt("pressure.row_count", rep.Rows.Count, 8);
            CheckStr("pressure.first_component", rep.Rows[0].ComponentId, "ahu");
            CheckStr("pressure.last_component", rep.Rows[rep.Rows.Count - 1].ComponentId, "t2");
            CheckStr("pressure.last_kind", rep.Rows[rep.Rows.Count - 1].Kind, "Terminal");
            Check("pressure.cumulative_reaches_total", rep.Rows[rep.Rows.Count - 1].CumulativeDpPa, dp, 1e-9);
            double shares = 0.0, cum = 0.0;
            bool monotonic = true;
            foreach (PressureReportRow r in rep.Rows)
            {
                shares += r.SharePercent;
                if (r.CumulativeDpPa < cum - 1e-12) monotonic = false;
                cum = r.CumulativeDpPa;
            }
            Check("pressure.shares_sum_100", shares, 100.0, 1e-9);
            CheckTrue("pressure.cumulative_monotonic", monotonic);
            PressureReportRow flex = null;
            foreach (PressureReportRow r in rep.Rows)
                if (r.ComponentId == "flex" && r.Port == "inlet") flex = r;
            CheckTrue("pressure.flex_row_present", flex != null);
            if (flex != null)
            {
                Check("pressure.flex_dp", flex.DpPa, 6.0, 1e-12);
                Check("pressure.flex_share", flex.SharePercent, 78.642135303542176, 1e-9);
            }
            string csv = rep.ToCsv();
            string[] lines = csv.Split('\n');
            CheckInt("pressure.csv_lines", lines.Length, 9);
            CheckStr("pressure.csv_header", lines[0], "component_id,kind,port,flow_m3s,velocity_ms,dp_pa,cumulative_dp_pa,share_percent");
            CheckStr("pressure.csv_first_row", lines[1], "ahu,Source,outlet,0.1,0,0,0,0");
            CheckTrue("pressure.text_has_total", rep.ToText().Contains("7.63 Pa"));
            CheckStr("pressure.deterministic", PressureReport.Build(net).ToCsv(), csv);

            var noTerminal = new Network { Name = "src-only" };
            noTerminal.Add("ahu", new Source("AHU"));
            ExpectError(true, "pressure.err_no_terminal", () => PressureReport.Build(noTerminal));
        }

        private static void RunBatchSizing()
        {
            var reqs = new List<BatchSizingRequest>
            {
                new BatchSizingRequest { Id = "v", FlowrateM3s = 0.1, Method = SizingMethod.Velocity, TargetVelocity = 4.0 },
                new BatchSizingRequest { Id = "ef", FlowrateM3s = 0.1, Method = SizingMethod.EqualFriction, TargetPaPerM = 1.0 },
                new BatchSizingRequest { Id = "pb", FlowrateM3s = 0.1, Method = SizingMethod.PressureDropBudget, LengthM = 10.0, BudgetPa = 10.0 },
                new BatchSizingRequest { Id = "nl", FlowrateM3s = 0.1, Method = SizingMethod.NoiseLimit, SpaceType = "office" },
                new BatchSizingRequest { Id = "ar", FlowrateM3s = 0.1, Method = SizingMethod.AspectRatio, Shape = Sizing.ShapeRectangular, TargetVelocity = 4.0, AspectRatio = 2.0 },
                new BatchSizingRequest { Id = "bad", FlowrateM3s = -0.1, Method = SizingMethod.Velocity },
            };
            List<BatchSizingResult> res = BatchSizing.Size(reqs);
            CheckInt("batch.count", res.Count, 6);
            foreach (BatchSizingResult r in res)
                if (r.Id != "bad") CheckTrue("batch.ok_" + r.Id, r.Error == null && r.Result != null);
            CheckInt("batch.velocity_snap", res[0].SnappedRoundMm ?? -1, 200);
            Check("batch.velocity_ms", res[0].Result.Velocity, 3.1830988618379066, 1e-12);
            CheckInt("batch.equal_friction_snap", res[1].SnappedRoundMm ?? -1, 200);
            CheckInt("batch.budget_snap", res[2].SnappedRoundMm ?? -1, 200);
            CheckInt("batch.noise_snap", res[3].SnappedRoundMm ?? -1, 200);
            CheckTrue("batch.aspect_rect_snap", res[4].SnappedRectMm != null && res[4].SnappedRectMm[0] == 100 && res[4].SnappedRectMm[1] == 250);
            CheckTrue("batch.aspect_no_round_snap", res[4].SnappedRoundMm == null);
            CheckStr("batch.bad_error", res[5].Error, "flowrate must be positive, got " + (-0.1));
            CheckTrue("batch.bad_no_result", res[5].Result == null && res[5].SnappedRoundMm == null);
            int[] rect = BatchSizing.SnapRectangular(Standard.En1505_1506, 210.0, 260.0);
            CheckTrue("batch.snap_rect_up", rect != null && rect[0] == 250 && rect[1] == 300);
            CheckTrue("batch.snap_rect_none", BatchSizing.SnapRectangular(Standard.En1505_1506, 5000.0, 5000.0) == null);
            CheckInt("batch.din_round", BatchSizing.Size(new[] { reqs[0] }, Standard.Din)[0].SnappedRoundMm ?? -1, 200);
            CheckInt("batch.ashrae_round", BatchSizing.Size(new[] { reqs[0] }, Standard.AsHrae)[0].SnappedRoundMm ?? -1, 203);
            string csv = BatchSizing.ToCsv(res);
            CheckTrue("batch.csv_header", csv.StartsWith("id,shape,diameter_m,"));
            CheckInt("batch.csv_lines", csv.Split('\n').Length, 7);
        }

        private static TracedSystem TracedTee()
        {
            var opts = new TraceOptions();
            opts.Diameters["duct1"] = 0.3;
            opts.Flows["term0"] = 0.06;
            opts.Flows["term1"] = 0.04;
            return Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 1.0, 1.0, 1.0, 2.0, 1.0),
                Pl(2.0, 1.0, 3.0, 1.0),
                Pl(2.0, 1.0, 2.0, 0.0),
            }, opts);
        }

        private static int CountOf(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static void RunIfcExport()
        {
            string ifc = IfcExport.ToIfc(TracedTee());
            CheckTrue("ifc.starts", ifc.StartsWith("ISO-10303-21;"));
            CheckTrue("ifc.ends", ifc.TrimEnd().EndsWith("END-ISO-10303-21;"));
            CheckTrue("ifc.schema", ifc.Contains("FILE_SCHEMA(('IFC4'))"));
            CheckInt("ifc.duct_segments", CountOf(ifc, "IFCDUCTSEGMENT("), 4);
            CheckInt("ifc.duct_fittings", CountOf(ifc, "IFCDUCTFITTING("), 1);
            CheckInt("ifc.air_terminals", CountOf(ifc, "IFCAIRTERMINAL("), 2);
            CheckInt("ifc.source_proxy", CountOf(ifc, "IFCBUILDINGELEMENTPROXY("), 1);
            CheckInt("ifc.property_sets", CountOf(ifc, "IFCPROPERTYSET("), 8);
            CheckInt("ifc.storey", CountOf(ifc, "IFCBUILDINGSTOREY("), 1);
            CheckTrue("ifc.flow_in_pset", ifc.Contains("IFCVOLUMETRICFLOWRATEMEASURE(0.1)"));

            var ids = new HashSet<int>();
            var refs = new List<int>();
            bool dup = false;
            foreach (string raw in ifc.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                int id = int.Parse(line.Substring(1, eq - 1), CultureInfo.InvariantCulture);
                if (!ids.Add(id)) dup = true;
                for (int i = eq; i < line.Length; i++)
                {
                    if (line[i] != '#') continue;
                    int j = i + 1;
                    while (j < line.Length && char.IsDigit(line[j])) j++;
                    if (j > i + 1) refs.Add(int.Parse(line.Substring(i + 1, j - i - 1), CultureInfo.InvariantCulture));
                }
            }
            CheckTrue("ifc.ids_unique", !dup);
            bool allResolve = true;
            foreach (int r in refs) if (!ids.Contains(r)) allResolve = false;
            CheckTrue("ifc.refs_resolve", allResolve);
            CheckInt("ifc.entity_count", ids.Count, 127);

            string a = ifc, b = IfcExport.ToIfc(TracedTee());
            string[] la = a.Split('\n'), lb = b.Split('\n');
            bool same = la.Length == lb.Length;
            for (int i = 0; same && i < la.Length; i++)
                if (la[i] != lb[i] && !la[i].StartsWith("FILE_NAME")) same = false;
            CheckTrue("ifc.deterministic", same);
            CheckStr("ifc.guid_roundtrip", IfcExport.CompressGuid(IfcExport.ExpandGuid(IfcExport.DeterministicGlobalId("duct1"))),
                IfcExport.DeterministicGlobalId("duct1"));
            CheckInt("ifc.guid_len", IfcExport.DeterministicGlobalId("x").Length, 22);

            string single = IfcExport.ToIfc(Topology.Trace(new List<Polyline> { Pl(0.0, 0.0, 5.0, 0.0) }, new TraceOptions()));
            CheckInt("ifc.single_segments", CountOf(single, "IFCDUCTSEGMENT("), 1);
            CheckInt("ifc.single_fittings", CountOf(single, "IFCDUCTFITTING("), 0);
            CheckInt("ifc.single_terminals", CountOf(single, "IFCAIRTERMINAL("), 1);
        }

        private static void RunMultiDrawing()
        {
            const string gTee = "11111111-1111-1111-1111-111111111111";
            const string gL1 = "22222222-2222-2222-2222-222222222222";
            const string gL2 = "33333333-3333-3333-3333-333333333333";

            var riser = new Network { Name = "riser" };
            riser.Add("ahu", new Source("AHU"));
            riser.Add("rd", new RigidDuct("riser duct", new Round(0.315), 6.0));
            riser.Add("tee", new Tee("riser tee", new Round(0.315), 0.1, 0.4));
            riser.Connect("ahu", "rd");
            riser.Connect("rd", "tee");
            string riserJson = NetworkJson.Serialize(riser,
                new Dictionary<string, ComponentMeta> { { "tee", new ComponentMeta { Guid = gTee } } });

            var l1 = new Network { Name = "L1" };
            l1.Add("d", new RigidDuct("L1 duct", new Round(0.2), 5.0));
            l1.Add("t", new Terminal("L1 diffuser", 0.06));
            l1.Connect("d", "t");
            string l1Json = NetworkJson.Serialize(l1,
                new Dictionary<string, ComponentMeta> { { "d", new ComponentMeta { Guid = gL1 } } });

            var l2 = new Network { Name = "L2" };
            l2.Add("d", new RigidDuct("L2 duct", new Round(0.125), 4.0));
            l2.Add("t", new Terminal("L2 diffuser", 0.04));
            l2.Connect("d", "t");
            string l2Json = NetworkJson.Serialize(l2,
                new Dictionary<string, ComponentMeta> { { "d", new ComponentMeta { Guid = gL2 } } });

            Func<DrawingDocument[]> load = () => new DrawingDocument[]
            {
                MultiDrawing.LoadDocument(riserJson, "riser"),
                MultiDrawing.LoadDocument(l1Json, "L1"),
                MultiDrawing.LoadDocument(l2Json, "L2")
            };
            Func<DrawingLink[]> goodLinks = () => new DrawingLink[]
            {
                new DrawingLink { FromGuid = gTee, FromPort = "straight", ToGuid = gL1 },
                new DrawingLink { FromGuid = gTee, FromPort = "branch", ToGuid = gL2 }
            };
            Func<List<string>, string, bool> has = (list, needle) =>
            {
                foreach (string s in list) if (s.Contains(needle)) return true;
                return false;
            };

            DrawingDocument[] docs = load();
            CheckInt("multi.riser_count", docs[0].Network.Components.Count, 3);
            CheckStr("multi.riser_guid_kept", docs[0].Meta["tee"].Guid, gTee);
            CheckStr("multi.riser_scope_stamped", docs[0].Meta["tee"].DrawingScope, "riser");
            CheckStr("multi.l1_scope_stamped", docs[1].Meta["d"].DrawingScope, "L1");
            CheckTrue("multi.guid_kept_for_unnamed", docs[0].Meta["ahu"].Guid != null
                && docs[0].Meta["ahu"].Guid.Length >= 32);

            string bare = "{\"schema_version\":1,\"name\":\"bare\",\"components\":"
                + "[{\"id\":\"t\",\"wenta_class\":\"Terminal\",\"flowrate\":0.05}],\"connections\":[]}";
            DrawingDocument bareDoc = MultiDrawing.LoadDocument(bare, "L9");
            CheckTrue("multi.guid_generated", bareDoc.Meta["t"].Guid != null
                && bareDoc.Meta["t"].Guid.Length >= 32);
            CheckStr("multi.scope_generated", bareDoc.Meta["t"].DrawingScope, "L9");
            string scopedJson = NetworkJson.Serialize(l1, new Dictionary<string, ComponentMeta>
                { { "d", new ComponentMeta { Guid = gL1, DrawingScope = "Model" } } });
            CheckStr("multi.scope_preserved",
                MultiDrawing.LoadDocument(scopedJson, "L1").Meta["d"].DrawingScope, "Model");
            ExpectError(true, "multi.err_no_scope", () => MultiDrawing.LoadDocument(bare, ""));

            Dictionary<string, ComponentMeta> mergedMeta;
            Network merged = MultiDrawing.Merge(docs, goodLinks(), out mergedMeta);
            CheckInt("multi.merged_count", merged.Components.Count, 7);
            CheckTrue("multi.merged_id_riser", merged.Components.ContainsKey("riser/tee"));
            CheckTrue("multi.merged_id_l1", merged.Components.ContainsKey("L1/d"));
            CheckTrue("multi.merged_id_l2", merged.Components.ContainsKey("L2/t"));
            CheckStr("multi.merged_name", merged.Name, "riser+L1+L2");
            CheckInt("multi.merged_meta_count", mergedMeta.Count, 7);
            CheckStr("multi.merged_meta_guid", mergedMeta["L1/d"].Guid, gL1);
            CheckStr("multi.merged_meta_scope", mergedMeta["L2/d"].DrawingScope, "L2");
            CheckInt("multi.merged_structurally_valid", merged.Validate().Count, 0);

            var flat = new Network { Name = "flat" };
            flat.Add("ahu", new Source("AHU"));
            flat.Add("rd", new RigidDuct("riser duct", new Round(0.315), 6.0));
            flat.Add("tee", new Tee("riser tee", new Round(0.315), 0.1, 0.4));
            flat.Add("d1", new RigidDuct("L1 duct", new Round(0.2), 5.0));
            flat.Add("t1", new Terminal("L1 diffuser", 0.06));
            flat.Add("d2", new RigidDuct("L2 duct", new Round(0.125), 4.0));
            flat.Add("t2", new Terminal("L2 diffuser", 0.04));
            flat.Connect("ahu", "rd");
            flat.Connect("rd", "tee");
            flat.Connect("tee.straight", "d1");
            flat.Connect("d1", "t1");
            flat.Connect("tee.branch", "d2");
            flat.Connect("d2", "t2");
            double dpFlat = flat.Solve();
            double dpMerged = merged.Solve();
            Check("multi.dp_matches_single_drawing", dpMerged, dpFlat, 1e-12);
            Check("multi.dp_value", dpMerged, 5.8375402667641145, 1e-12);
            Check("multi.riser_flow", merged.Components["riser/rd"].Port_("inlet").Flowrate ?? 0.0, 0.10, 1e-12);
            Check("multi.straight_flow", merged.Components["riser/tee"].Port_("straight").Flowrate ?? 0.0, 0.06, 1e-12);
            Check("multi.branch_flow", merged.Components["riser/tee"].Port_("branch").Flowrate ?? 0.0, 0.04, 1e-12);

            string json = MultiDrawing.ToJson(merged, mergedMeta);
            Dictionary<string, ComponentMeta> backMeta;
            Network back = NetworkJson.Parse(json, out backMeta);
            CheckInt("multi.json_count", back.Components.Count, 7);
            Check("multi.json_dp", back.Solve(), dpMerged, 1e-12);
            CheckStr("multi.json_guid", backMeta["L2/d"].Guid, gL2);
            CheckStr("multi.json_scope", backMeta["riser/tee"].DrawingScope, "riser");

            CheckInt("multi.validate_ok", MultiDrawing.Validate(load(), goodLinks()).Count, 0);

            DrawingDocument[] dup = load();
            dup[2].Meta["d"].Guid = gTee;
            List<string> dupProblems = MultiDrawing.Validate(dup, goodLinks());
            CheckTrue("multi.validate_duplicate_guid", has(dupProblems, "duplicate guid '" + gTee + "'"));
            ExpectError(true, "multi.err_duplicate_guid", () =>
            {
                Dictionary<string, ComponentMeta> m;
                MultiDrawing.Merge(dup, goodLinks(), out m);
            });

            var unknownLinks = new DrawingLink[]
            {
                new DrawingLink { FromGuid = gTee, FromPort = "straight", ToGuid = gL1 },
                new DrawingLink { FromGuid = gTee, FromPort = "branch", ToGuid = "44444444-4444-4444-4444-444444444444" }
            };
            List<string> unknownProblems = MultiDrawing.Validate(load(), unknownLinks);
            CheckTrue("multi.validate_unknown_guid",
                has(unknownProblems, "unknown guid '44444444-4444-4444-4444-444444444444'"));
            ExpectError(true, "multi.err_unknown_guid", () =>
            {
                Dictionary<string, ComponentMeta> m;
                MultiDrawing.Merge(load(), unknownLinks, out m);
            });

            var onlyL1 = new DrawingLink[]
                { new DrawingLink { FromGuid = gTee, FromPort = "straight", ToGuid = gL1 } };
            List<string> orphan = MultiDrawing.Validate(load(), onlyL1);
            CheckInt("multi.validate_orphan_count", orphan.Count, 1);
            CheckTrue("multi.validate_orphan_storey", has(orphan, "drawing 'L2' has no Source and no incoming link"));

            DrawingDocument[] sd = load();
            var sameDocLinks = new DrawingLink[]
                { new DrawingLink { FromGuid = sd[0].Meta["rd"].Guid, ToGuid = gTee } };
            CheckTrue("multi.validate_same_document",
                has(MultiDrawing.Validate(sd, sameDocLinks), "both ends in drawing 'riser'"));
            ExpectError(true, "multi.err_same_document", () =>
            {
                Dictionary<string, ComponentMeta> m;
                MultiDrawing.Merge(sd, sameDocLinks, out m);
            });

            var badPorts = new DrawingLink[]
            {
                new DrawingLink { FromGuid = gTee, FromPort = "combined", ToGuid = gL1 },
                new DrawingLink { FromGuid = gTee, FromPort = "branch", ToGuid = gL2, ToPort = "outlet" }
            };
            List<string> portProblems = MultiDrawing.Validate(load(), badPorts);
            CheckInt("multi.validate_port_count", portProblems.Count, 2);
            CheckTrue("multi.validate_from_not_outlet", has(portProblems, "'riser/tee.combined', which is not an out-port"));
            CheckTrue("multi.validate_to_not_inlet", has(portProblems, "'L2/d.outlet', which is not an in-port"));

            var ambiguous = new DrawingLink[]
            {
                new DrawingLink { FromGuid = gTee, ToGuid = gL1 },
                new DrawingLink { FromGuid = gTee, FromPort = "branch", ToGuid = gL2 }
            };
            CheckTrue("multi.validate_ambiguous_port",
                has(MultiDrawing.Validate(load(), ambiguous), "out-ports; name one"));
            ExpectError(true, "multi.err_ambiguous_port", () =>
            {
                Dictionary<string, ComponentMeta> m;
                MultiDrawing.Merge(load(), ambiguous, out m);
            });
        }

        private static void RunReFit()
        {
            ReFitResult r = ReFit.Apply(TeeNetwork(), new ReFitOptions { TargetVelocity = 4.0 });
            Check("refit.old_critical", r.OldCriticalDpPa, 7.6294978217990357, 1e-12);
            Check("refit.new_critical", r.NewCriticalDpPa, 20.198176310792714, 1e-12);
            CheckInt("refit.change_count", r.Changes.Count, 3);
            CheckInt("refit.network_components", r.Network.Components.Count, 7);
            CheckInt("refit.network_valid", r.Network.Validate().Count, 0);

            ReFitChange duct = null, d2 = null, flex = null;
            foreach (ReFitChange c in r.Changes)
            {
                if (c.ComponentId == "duct") duct = c;
                if (c.ComponentId == "d2") d2 = c;
                if (c.ComponentId == "flex") flex = c;
            }
            CheckTrue("refit.rows_present", duct != null && d2 != null && flex != null);
            Check("refit.duct_old_d", duct.OldDiameterM, 0.315, 1e-12);
            Check("refit.duct_new_d", duct.NewDiameterM, 0.2, 1e-12);
            Check("refit.duct_new_v", duct.NewVelocityMs, 3.1830988618379066, 1e-12);
            CheckTrue("refit.duct_changed", duct.Changed);
            Check("refit.d2_new_d", d2.NewDiameterM, 0.15, 1e-12);
            CheckTrue("refit.d2_changed", d2.Changed);
            Check("refit.flex_same_d", flex.NewDiameterM, flex.OldDiameterM, 0.0);
            CheckTrue("refit.flex_unchanged", !flex.Changed);

            ReFitResult again = ReFit.Apply(r.Network, new ReFitOptions { TargetVelocity = 4.0 });
            Check("refit.idempotent_old", again.OldCriticalDpPa, r.NewCriticalDpPa, 0.0);
            Check("refit.idempotent_new", again.NewCriticalDpPa, r.NewCriticalDpPa, 0.0);
            bool anyChanged = false;
            foreach (ReFitChange c in again.Changes) if (c.Changed) anyChanged = true;
            CheckTrue("refit.idempotent_no_changes", !anyChanged);

            string csv = r.ToCsv();
            CheckStr("refit.csv_header", csv.Split('\n')[0],
                "component_id,old_diameter_m,new_diameter_m,old_velocity_ms,new_velocity_ms,changed");
            CheckInt("refit.csv_lines", csv.Split('\n').Length, 4);

            Network net = TeeNetwork();
            double critical = net.Solve();
            List<BalancingHint> hints = ReFit.Hints(net);
            CheckInt("refit.hint_count", hints.Count, 2);
            BalancingHint t1 = null, t2 = null;
            foreach (BalancingHint h in hints)
            {
                if (h.TerminalId == "t1") t1 = h;
                if (h.TerminalId == "t2") t2 = h;
            }
            CheckTrue("refit.hints_present", t1 != null && t2 != null);
            Check("refit.t2_is_critical", t2.AvailableDpPa, critical, 1e-12);
            Check("refit.t2_no_surplus", t2.SurplusDpPa, 0.0, 0.0);
            Check("refit.t2_zeta_zero", t2.DamperZeta, 0.0, 0.0);
            Check("refit.t2_fully_open", t2.DamperOpenPercent, 100.0, 0.0);
            Check("refit.t1_available", t1.AvailableDpPa, 3.0063159227848875, 1e-12);
            Check("refit.t1_surplus", t1.SurplusDpPa, 4.6231818990141482, 1e-12);
            Check("refit.t1_zeta", t1.DamperZeta, 2.10543449693368, 1e-12);
            Check("refit.t1_open_pct", t1.DamperOpenPercent, 55.217922145866439, 1e-12);
            foreach (BalancingHint h in hints)
            {
                Check("refit.balance_" + h.TerminalId, h.AvailableDpPa + h.SurplusDpPa, h.RequiredDpPa, 1e-9);
                CheckTrue("refit.open_range_" + h.TerminalId,
                    h.DamperOpenPercent > 0.0 && h.DamperOpenPercent <= 100.0);
            }
            CheckStr("refit.hint_csv_header", ReFit.HintsAsCsv(hints).Split('\n')[0],
                "terminal_id,available_dp_pa,required_dp_pa,surplus_dp_pa,damper_zeta,damper_open_percent");

            var noTerm = new Network { Name = "no-terminal" };
            noTerm.Add("ahu", new Source("AHU"));
            ExpectError(true, "refit.err_no_terminal", () => ReFit.Apply(noTerm));
        }

        private static void RunQuickConnect()
        {
            var opts = new QuickConnectOptions { FlowrateM3s = 0.1 };
            List<ConnectorPiece> plan = QuickConnect.Plan(new Round(0.315), new Round(0.2), opts);
            CheckInt("qc.reducer_pieces", plan.Count, 1);
            CheckTrue("qc.reducer_kind", plan[0].Kind == ConnectorKind.Reducer);
            Check("qc.reducer_length", plan[0].LengthM, 0.43675586148169621, 1e-12);
            Check("qc.reducer_zeta", plan[0].Zeta, 0.23997651801461317, 1e-12);
            Check("qc.reducer_dp", plan[0].PressureDropPa, 1.4637452320667652, 1e-12);

            plan = QuickConnect.Plan(new Round(0.2), new Round(0.315), opts);
            CheckTrue("qc.expander_kind", plan[0].Kind == ConnectorKind.Expander);
            Check("qc.expander_length", plan[0].LengthM, 0.43675586148169621, 1e-12);
            Check("qc.expander_zeta", plan[0].Zeta, 0.21375642331622594, 1e-12);
            Check("qc.expander_dp", plan[0].PressureDropPa, 1.3038148400574676, 1e-12);

            plan = QuickConnect.Plan(new Round(0.2), new Rectangular(0.3, 0.15), opts);
            CheckTrue("qc.transition_kind", plan[0].Kind == ConnectorKind.Transition);
            Check("qc.transition_length", plan[0].LengthM, 0.37978770563625752, 1e-12);
            Check("qc.transition_zeta", plan[0].Zeta, 0.054674682037962032, 1e-12);

            var gap = new QuickConnectOptions { FlowrateM3s = 0.1, GapM = 0.4 };
            plan = QuickConnect.Plan(new Round(0.2), new Round(0.2), gap);
            CheckInt("qc.spacer_pieces", plan.Count, 1);
            CheckTrue("qc.spacer_kind", plan[0].Kind == ConnectorKind.Spacer);
            Check("qc.spacer_length", plan[0].LengthM, 0.4, 1e-12);
            Check("qc.spacer_zeta", plan[0].Zeta, 0.0, 0.0);
            Check("qc.spacer_total_length", QuickConnect.TotalLengthM(plan), 0.4, 1e-12);

            var flexOpts = new QuickConnectOptions { FlowrateM3s = 0.1, GapM = 0.8, PreferFlex = true };
            plan = QuickConnect.Plan(new Round(0.2), new Round(0.2), flexOpts);
            CheckInt("qc.flex_pieces", plan.Count, 1);
            CheckTrue("qc.flex_kind", plan[0].Kind == ConnectorKind.Flex);
            Check("qc.flex_length", plan[0].LengthM, 0.8, 1e-12);

            var longGap = new QuickConnectOptions { FlowrateM3s = 0.1, GapM = 2.0, PreferFlex = true };
            plan = QuickConnect.Plan(new Round(0.2), new Round(0.2), longGap);
            CheckInt("qc.flex_capped_pieces", plan.Count, 2);
            Check("qc.flex_capped_length", plan[0].LengthM, 1.5, 1e-12);
            Check("qc.flex_capped_spacer", plan[1].LengthM, 0.5, 1e-12);
            Check("qc.flex_capped_total", QuickConnect.TotalLengthM(plan), 2.0, 1e-12);

            plan = QuickConnect.Plan(new Round(0.2), new Round(0.2), new QuickConnectOptions { FlowrateM3s = 0.1 });
            CheckInt("qc.direct_pieces", plan.Count, 1);
            CheckTrue("qc.direct_kind", plan[0].Kind == ConnectorKind.Direct);
            Check("qc.direct_length", plan[0].LengthM, 0.0, 0.0);
            Check("qc.direct_zeta", plan[0].Zeta, 0.0, 0.0);
            Check("qc.direct_dp_total", QuickConnect.TotalPressureDropPa(plan), 0.0, 0.0);

            CheckTrue("qc.csv_header", QuickConnect.ToCsv(plan).StartsWith("kind,"));
            ExpectError(true, "qc.err_zero_flow", () =>
                QuickConnect.Plan(new Round(0.2), new Round(0.15), new QuickConnectOptions { FlowrateM3s = 0.0 }));
            ExpectError(true, "qc.err_negative_gap", () =>
                QuickConnect.Plan(new Round(0.2), new Round(0.15), new QuickConnectOptions { FlowrateM3s = 0.1, GapM = -0.5 }));
            ExpectError(true, "qc.err_bad_angle", () =>
                QuickConnect.Plan(new Round(0.2), new Round(0.15),
                    new QuickConnectOptions { FlowrateM3s = 0.1, MaxTaperAngleDeg = 90.0 }));
        }

        private static Network BenchNetwork(int tees, int runLength)
        {
            var net = new Network { Name = "bench" };
            net.Add("ahu", new Source("AHU"));
            string upstream = "ahu";
            for (int t = 0; t < tees; t++)
            {
                string duct = "trunk" + t;
                net.Add(duct, new RigidDuct(duct, new Round(0.4), 3.0));
                net.Connect(upstream, duct);
                if (t == tees - 1)
                {
                    net.Add("termEnd", new Terminal("termEnd", 0.05));
                    net.Connect(duct, "termEnd");
                    break;
                }
                string tee = "tee" + t;
                net.Add(tee, new Tee(tee, new Round(0.4), 0.1, 0.4));
                net.Connect(duct, tee);
                upstream = tee + ".straight";
                string prev = null;
                for (int i = 0; i < runLength; i++)
                {
                    string id = "b" + t + "_" + i;
                    net.Add(id, new RigidDuct(id, new Round(0.2), 4.0));
                    if (prev == null) net.Connect(tee + ".branch", id);
                    else net.Connect(prev, id);
                    prev = id;
                }
                string term = "term" + t;
                net.Add(term, new Terminal(term, 0.05));
                net.Connect(prev, term);
            }
            return net;
        }

        private static DrawnDuct Dd(string handle, CrossSection section, params double[] xy)
        {
            var d = new DrawnDuct { Handle = handle, Section = section };
            for (int i = 0; i + 1 < xy.Length; i += 2)
                d.PointsMm.Add(new Point2(xy[i], xy[i + 1]));
            return d;
        }

        private static DrawnMarker Mk(string handle, string cls, double x, double y, double flow)
        {
            return new DrawnMarker { Handle = handle, Class = cls, AtMm = new Point2(x, y), FlowM3s = flow };
        }

        // Reference floor-plan layout (mm), the one zwcad-plugin/network_test.scr
        // draws: main 5 m Ø250 from the AHU end at (0,0) to a tee at (5000,0);
        // straight run 3 m Ø200 to (8000,0) at 0.06 m³/s; branch 3 m Ø160 down
        // to (5000,-3000) at 0.04 m³/s.
        private static List<DrawnDuct> ReferenceDucts()
        {
            return new List<DrawnDuct>
            {
                Dd("A", new Round(0.25), 0, 0, 2500, 0, 5000, 0),
                Dd("B", new Round(0.2), 5000, 0, 8000, 0),
                Dd("C", new Round(0.16), 5000, 0, 5000, -3000),
            };
        }

        private static List<DrawnMarker> ReferenceMarkers()
        {
            return new List<DrawnMarker>
            {
                Mk("S", WentaTag.ClassSource, 0, 0, 0),
                Mk("T1", WentaTag.ClassTerminal, 8000, 30, 0.06),
                Mk("T2", WentaTag.ClassTerminal, 5000, -3000, 0.04),
                Mk("D2", WentaTag.ClassDamper, 5000, -2000, 0),
            };
        }

        // #27 prerequisite: Wenta.Core solver semantics against the tested
        // behaviour of venti::network (legacy venti/src/network/solver.rs:
        // multi_source_separate_demands_and_dp,
        // multi_source_merged_total_demand_and_dp,
        // cycle_detection_and_solve_error, large_chain_solves_fast,
        // empty_network_edge_cases_do_not_panic).
        private static void RunSolverVentiSemantics()
        {
            var r = new Round(0.2);
            var sep = new Network { Name = "two-source-separate" };
            sep.Add("s1", new Source("S1"));
            sep.Add("s2", new Source("S2"));
            sep.Add("d1", new RigidDuct("d1", r, 5.0, 0.0001));
            sep.Add("d2", new RigidDuct("d2", r, 5.0, 0.0001));
            sep.Add("t1", new Terminal("t1", 0.1, r, 1.0));
            sep.Add("t2", new Terminal("t2", 0.2, r, 1.0));
            sep.Connect("s1", "d1"); sep.Connect("d1", "t1");
            sep.Connect("s2", "d2"); sep.Connect("d2", "t2");
            double dpSep = sep.Solve();
            Check("solver.venti_separate_s1", sep.Components["s1"].Ports[0].Flowrate.Value, 0.1, 1e-12);
            Check("solver.venti_separate_s2", sep.Components["s2"].Ports[0].Flowrate.Value, 0.2, 1e-12);
            CheckTrue("solver.venti_separate_dp", dpSep > 0.0);

            var mg = new Network { Name = "two-source-merged" };
            mg.Add("s1", new Source("S1"));
            mg.Add("s2", new Source("S2"));
            mg.Add("d1", new RigidDuct("d1", r, 5.0, 0.0001));
            mg.Add("d2", new RigidDuct("d2", r, 5.0, 0.0001));
            mg.Add("common", new RigidDuct("common", r, 5.0, 0.0001));
            mg.Add("tee", new Tee("tee", r, 0.1, 0.2));
            mg.Add("t1", new Terminal("t1", 0.1, r, 1.0));
            mg.Add("t2", new Terminal("t2", 0.2, r, 1.0));
            mg.Connect("s1", "d1"); mg.Connect("s2", "d2");
            mg.Connect("d1", "common"); mg.Connect("d2", "common");
            mg.Connect("common", "tee.combined");
            mg.Connect("tee.straight", "t1"); mg.Connect("tee.branch", "t2");
            double dpMg = mg.Solve();
            Check("solver.venti_merged_s1_total", mg.Components["s1"].Ports[0].Flowrate.Value, 0.3, 1e-12);
            Check("solver.venti_merged_s2_total", mg.Components["s2"].Ports[0].Flowrate.Value, 0.3, 1e-12);
            Check("solver.venti_merged_t1", mg.Components["t1"].Ports[0].Flowrate.Value, 0.1, 1e-12);
            Check("solver.venti_merged_t2", mg.Components["t2"].Ports[0].Flowrate.Value, 0.2, 1e-12);
            CheckTrue("solver.venti_merged_dp", dpMg > 0.0);

            var cyc = new Network { Name = "cyclic" };
            cyc.Add("s", new Source("AHU"));
            cyc.Add("d0", new RigidDuct("d0", r, 5.0, 0.0001));
            cyc.Add("d1", new RigidDuct("d1", r, 5.0, 0.0001));
            cyc.Add("t", new Terminal("t", 0.1, r, 1.0));
            cyc.Connect("s", "d0"); cyc.Connect("d0", "d1"); cyc.Connect("d1", "t"); cyc.Connect("d1", "d0");
            string topoErr = null, solveErr = null;
            try { cyc.TopoOrder(); } catch (WentaException e) { topoErr = e.Message; }
            try { cyc.Solve(); } catch (WentaException e) { solveErr = e.Message; }
            CheckTrue("solver.venti_cycle_topo", topoErr != null && topoErr.Contains("cycle"));
            CheckTrue("solver.venti_cycle_solve", solveErr != null && solveErr.Contains("cycle"));

            var big = new Network { Name = "large" };
            big.Add("src", new Source("AHU"));
            for (int i = 0; i < 2000; i++) big.Add("d" + i.ToString(C), new RigidDuct("d" + i.ToString(C), r, 1.0, 0.0001));
            big.Add("term", new Terminal("term", 0.1, r, 1.0));
            big.Connect("src", "d0");
            for (int i = 0; i < 1999; i++) big.Connect("d" + i.ToString(C), "d" + (i + 1).ToString(C));
            big.Connect("d1999", "term");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double dpBig = big.Solve();
            sw.Stop();
            CheckTrue("solver.venti_large_chain_dp", dpBig > 0.0);
            Check("solver.venti_large_chain_sum", dpBig,
                2000.0 * big.Components["d0"].Ports[0].PressureDrop + big.Components["term"].Ports[0].PressureDrop, 1e-9);
            CheckTrue("solver.venti_large_chain_fast", sw.Elapsed.TotalMilliseconds < 1000.0);

            foreach (string kind in new[] { "source-only", "terminal-only" })
            {
                var e = new Network { Name = kind };
                if (kind == "source-only") e.Add("s", new Source("AHU"));
                else e.Add("t", new Terminal("t", 0.1, r, 1.0));
                bool ok = true;
                try { e.Solve(); }
                catch (WentaException) { }
                catch (Exception) { ok = false; }
                CheckTrue("solver.venti_" + kind + "_no_crash", ok);
            }
        }

        // #23 / #27: NetworkEdit on the reference network with its layout.
        private static void RunSchedule()
        {
            Network net = ReferenceHandBuilt();
            net.Solve();
            Bom bom = Bom.Build(net);
            ScheduleTable t = Schedule.Build(net, bom);

            CheckInt("sched.rows", t.Rows.Count, bom.Rows.Count);
            CheckInt("sched.rows_7", t.Rows.Count, 7);
            CheckInt("sched.columns", t.Header.Length, 8);
            CheckStr("sched.title", t.Title, "Wenta schedule: hand");
            // Sorted by kind (source, duct, flex, fitting, terminal), then id.
            string order = "";
            foreach (string[] r in t.Rows) order += r[0] + " ";
            CheckStr("sched.order", order, "src branch main straight tee t1 t2 ");
            foreach (string[] r in t.Rows) CheckInt("sched.width_" + r[0], r.Length, 8);
            // Branch numbers from Marking.
            var marks = new Dictionary<string, uint>();
            foreach (Mark m in Marking.AssignBranchMarks(net)) marks[m.ComponentId] = m.BranchNo;
            foreach (string[] r in t.Rows)
                if (marks.ContainsKey(r[0])) CheckStr("sched.branch_" + r[0], r[1], marks[r[0]].ToString(CultureInfo.InvariantCulture));
            string[] mainRow = t.Rows.Find(r => r[0] == "main");
            CheckStr("sched.main_kind", mainRow[2], "duct");
            CheckStr("sched.main_length", mainRow[4], "5.000");
            CheckStr("sched.main_area", mainRow[5], (Math.PI * 0.25 * 0.25 / 4.0 * 5.0).ToString("0.000", CultureInfo.InvariantCulture));
            CheckStr("sched.main_flow", mainRow[6], "0.1000");
            CheckStr("sched.tee_no_length", t.Rows.Find(r => r[0] == "tee")[4], "");
            CheckStr("sched.totals_length", t.Totals[4], bom.TotalLength.ToString("0.000", CultureInfo.InvariantCulture));
            CheckStr("sched.totals_length_11", t.Totals[4], "11.000");
            CheckStr("sched.totals_area", t.Totals[5], bom.TotalArea.ToString("0.000", CultureInfo.InvariantCulture));
            CheckStr("sched.totals_items", t.Totals[2], "7 items");
            CheckInt("sched.all_rows", t.AllRows().Count, 9);

            // Invariant culture whatever the thread culture is.
            CultureInfo saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
                ScheduleTable pl = Schedule.Build(net, Bom.Build(net));
                CheckStr("sched.pl_length", pl.Rows.Find(r => r[0] == "main")[4], "5.000");
                CheckStr("sched.pl_terminal_desc", pl.Rows.Find(r => r[0] == "t1")[3], "terminal 0.06 m3/s");
                CheckTrue("sched.pl_tee_desc", pl.Rows.Find(r => r[0] == "tee")[3].Contains("z=0.5"));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }

            string csv = t.ToCsv();
            string[] lines = csv.TrimEnd('\n').Split('\n');
            CheckInt("sched.csv_lines", lines.Length, 9);
            CheckTrue("sched.csv_header", lines[0].StartsWith("Item,Branch,Kind,Description,", StringComparison.Ordinal));
            CheckTrue("sched.csv_quoted_comma", csv.Contains("\"tee (straight z=0, branch z=0.5)\""));
            CheckTrue("sched.csv_total", lines[8].StartsWith("Total,", StringComparison.Ordinal));
            ExpectError(true, "sched.err_null", () => Schedule.Build(null, bom));

            // Flat patterns: round rigid ducts, circumference × length, side by side.
            FlatPatternSet f = Schedule.FlatPatterns(net, new Point2(1000.0, 2000.0), 500.0);
            CheckInt("flat.count", f.Patterns.Count, 3);
            CheckStr("flat.first", f.Patterns[0].ItemId, "branch");
            Check("flat.branch_width", f.Patterns[0].Piece.WidthM, Math.PI * 0.16, 1e-12);
            Check("flat.branch_length", f.Patterns[0].Piece.LengthM, 3.0, 1e-12);
            Check("flat.branch_x0", f.Patterns[0].OutlineMm[0].X, 1000.0, 1e-9);
            Check("flat.branch_y0", f.Patterns[0].OutlineMm[0].Y, 2000.0, 1e-9);
            Check("flat.branch_x1", f.Patterns[0].OutlineMm[1].X, 1000.0 + Math.PI * 160.0, 1e-9);
            Check("flat.branch_y2", f.Patterns[0].OutlineMm[2].Y, 5000.0, 1e-9);
            Check("flat.main_x0", f.Patterns[1].OutlineMm[0].X, 1000.0 + Math.PI * 160.0 + 500.0, 1e-9);
            Check("flat.straight_x0", f.Patterns[2].OutlineMm[0].X, 1000.0 + Math.PI * 410.0 + 1000.0, 1e-9);
            Check("flat.total_area", f.TotalAreaM2, Math.PI * (0.16 * 3.0 + 0.25 * 5.0 + 0.2 * 3.0), 1e-12);
            CheckTrue("flat.label_below", f.Patterns[0].LabelAtMm.Y < 2000.0);
            CheckTrue("flat.description", f.Patterns[1].Description.StartsWith("Ø250 × 5 m, flat 785 × 5000 mm", StringComparison.Ordinal));
            CheckInt("flat.no_skipped", f.Skipped.Count, 0);
            CheckInt("flat.csv_lines", f.ToCsv().TrimEnd('\n').Split('\n').Length, 4);
            ExpectError(true, "flat.err_gap", () => Schedule.FlatPatterns(net, new Point2(0.0, 0.0), -1.0));

            var mixed = new Network { Name = "mixed" };
            mixed.Add("src", new Source("AHU"));
            mixed.Add("r", new RigidDuct("r", new Rectangular(0.4, 0.2), 2.0));
            mixed.Add("f", new FlexDuct("f", 0.16, 1.0, 1.0));
            mixed.Add("t", new Terminal("t", 0.1, null, 0.0));
            mixed.Connect("src", "r.inlet");
            mixed.Connect("r.outlet", "f.inlet");
            mixed.Connect("f.outlet", "t");
            FlatPatternSet fm = Schedule.FlatPatterns(mixed, new Point2(0.0, 0.0), 100.0);
            CheckInt("flat.mixed_none", fm.Patterns.Count, 0);
            CheckInt("flat.mixed_skipped", fm.Skipped.Count, 2);
            CheckTrue("flat.skip_flex", fm.Skipped.Exists(s => s.StartsWith("f: flexible", StringComparison.Ordinal)));
            CheckTrue("flat.skip_rect", fm.Skipped.Exists(s => s.StartsWith("r: rectangular", StringComparison.Ordinal)));
        }

        private static void RunNetworkEdit()
        {
            Network net = ReferenceHandBuilt();
            var meta = new Dictionary<string, ComponentMeta>();
            NetworkDrawing.Write(net, meta, new Point2(0.0, 0.0), new List<string>());
            string guidT1 = meta["t1"].Guid;
            double dp0 = ReferenceHandBuilt().Solve();

            // Properties.
            Check("edit.get_diameter", NetworkEdit.Get(net.Components["branch"], "diameter").Value, 160.0, 1e-12);
            Check("edit.get_length", NetworkEdit.Get(net.Components["branch"], "length").Value, 3.0, 1e-15);
            Check("edit.get_flow", NetworkEdit.Get(net.Components["t1"], "flow").Value, 0.06, 1e-15);
            CheckTrue("edit.get_none", NetworkEdit.Get(net.Components["tee"], "length") == null);

            EditResult d = NetworkEdit.Set(net, meta, "branch", "diameter", 170.0);
            Check("edit.diameter_snapped", ((Round)((RigidDuct)d.Network.Components["branch"]).CrossSection).Diameter, 0.2, 1e-15);
            CheckTrue("edit.diameter_note", d.Note != null && d.Note.Contains("Ø200"));
            Network h1 = ReferenceHandBuilt();
            h1 = NetworkEdit.Rebuild(h1, new Dictionary<string, Component> { { "branch", new RigidDuct("branch", new Round(0.2), 3.0) } });
            Check("edit.diameter_dp", d.CriticalAfterPa, h1.Solve(), 1e-12);
            Check("edit.before_dp", d.CriticalBeforePa, dp0, 1e-12);
            CheckStr("edit.original_untouched", DrawingSizing.SizeText(((RigidDuct)net.Components["branch"]).CrossSection), "Ø160");

            EditResult f = NetworkEdit.Set(net, meta, "t1", "flow", 0.08);
            var h2 = NetworkEdit.Rebuild(ReferenceHandBuilt(), new Dictionary<string, Component> { { "t1", new Terminal("t1", 0.08, null, 0.0) } });
            Check("edit.flow_dp", f.CriticalAfterPa, h2.Solve(), 1e-12);
            Check("edit.flow_main_carries", f.Network.Components["main"].InletFlowrate().Value, 0.12, 1e-12);

            EditResult z = NetworkEdit.Set(net, meta, "t2", "zeta", 1.5);
            Check("edit.zeta_set", ((Terminal)z.Network.Components["t2"]).Zeta, 1.5, 1e-15);

            ExpectError(true, "edit.err_length_on_terminal", () => NetworkEdit.Set(net, meta, "t1", "length", 2.0));
            ExpectError(true, "edit.err_flow_on_duct", () => NetworkEdit.Set(net, meta, "main", "flow", 0.1));
            ExpectError(true, "edit.err_negative_zeta", () => NetworkEdit.Set(net, meta, "t1", "zeta", -1.0));
            ExpectError(true, "edit.err_unknown_property", () => NetworkEdit.Set(net, meta, "t1", "colour", 1.0));
            ExpectError(true, "edit.err_unknown_component", () => NetworkEdit.Set(net, meta, "nope", "flow", 1.0));
            ExpectError(true, "edit.err_width_on_round", () => NetworkEdit.Set(net, meta, "main", "width", 300.0));

            // Stretch: the duct's end and everything downstream move.
            EditResult s = NetworkEdit.Stretch(net, meta, "straight", 4.0);
            Check("edit.stretch_layout", NetworkDrawing.PolylineLengthMm(s.Meta["straight"].LayoutMm), 4000.0, 1e-9);
            Check("edit.stretch_t1_x", s.Meta["t1"].AtMm.Value.X, meta["t1"].AtMm.Value.X + 1000.0, 1e-9);
            Check("edit.stretch_tee_fixed", s.Meta["tee"].AtMm.Value.X, meta["tee"].AtMm.Value.X, 1e-12);
            Check("edit.stretch_original_layout", NetworkDrawing.PolylineLengthMm(meta["straight"].LayoutMm), 3000.0, 1e-9);
            var h3 = NetworkEdit.Rebuild(ReferenceHandBuilt(), new Dictionary<string, Component> { { "straight", new RigidDuct("straight", new Round(0.2), 4.0) } });
            Check("edit.stretch_dp", s.CriticalAfterPa, h3.Solve(), 1e-12);
            CheckInt("edit.stretch_changed", s.Changed.Count, 2);
            EditResult viaSet = NetworkEdit.Set(net, meta, "straight", "length", 4.0);
            Check("edit.set_length_is_stretch", viaSet.CriticalAfterPa, s.CriticalAfterPa, 1e-15);
            ExpectError(true, "edit.err_stretch_too_short", () => NetworkEdit.Stretch(net, meta, "straight", 0.0002));
            ExpectError(true, "edit.err_stretch_terminal", () => NetworkEdit.Stretch(net, meta, "t1", 2.0));

            // Consistent tags: drawing the edited network and reading it back
            // finds no length edits, the same network and the same GUIDs.
            NetworkReadResult back = RoundTrip(s.Network, s.Meta);
            CheckTrue("edit.stretch_reads_back", back.Ok);
            CheckInt("edit.stretch_no_length_edits", back.LengthUpdated.Count, 0);
            Check("edit.stretch_read_dp", back.Network.Solve(), s.CriticalAfterPa, 1e-12);
            CheckStr("edit.stretch_guid_kept", back.Meta["t1"].Guid, guidT1);

            // Move: the joined ducts' ends follow and take the drawn length.
            EditResult mv = NetworkEdit.Move(net, meta, "tee", 0.0, -500.0);
            Check("edit.move_main_len", ((RigidDuct)mv.Network.Components["main"]).Length, Math.Sqrt(5.0 * 5.0 + 0.5 * 0.5), 1e-12);
            double straightLen = NetworkDrawing.PolylineLengthMm(mv.Meta["straight"].LayoutMm) / 1000.0;
            Check("edit.move_straight_len", ((RigidDuct)mv.Network.Components["straight"]).Length, straightLen, 1e-15);
            Check("edit.move_straight_geom", straightLen, Math.Sqrt(3.0 * 3.0 + 0.5 * 0.5), 1e-12);
            Check("edit.move_branch_len", ((RigidDuct)mv.Network.Components["branch"]).Length, 3.5, 1e-12);
            CheckInt("edit.move_changed", mv.Changed.Count, 4);
            NetworkReadResult mvBack = RoundTrip(mv.Network, mv.Meta);
            CheckTrue("edit.move_reads_back", mvBack.Ok && mvBack.LengthUpdated.Count == 0);
            Check("edit.move_read_dp", mvBack.Network.Solve(), mv.CriticalAfterPa, 1e-12);
            EditResult mt = NetworkEdit.Move(net, meta, "t1", 1000.0, 0.0);
            Check("edit.move_terminal_len", ((RigidDuct)mt.Network.Components["straight"]).Length, 4.0, 1e-12);
            Check("edit.move_terminal_same_as_stretch", mt.CriticalAfterPa, s.CriticalAfterPa, 1e-12);
            ExpectError(true, "edit.err_move_duct", () => NetworkEdit.Move(net, meta, "main", 10.0, 0.0));
            ExpectError(true, "edit.err_move_collapse", () => NetworkEdit.Move(net, meta, "t1", -3000.0, 0.0));
            ExpectError(true, "edit.err_no_layout", () => NetworkEdit.Move(ReferenceHandBuilt(), new Dictionary<string, ComponentMeta>(), "t1", 1.0, 0.0));

            // Downstream walk.
            List<string> downTee = NetworkEdit.Downstream(net, "tee"); downTee.Sort(StringComparer.Ordinal);
            CheckStr("edit.downstream_tee", string.Join(",", downTee.ToArray()), "branch,straight,t1,t2");
            CheckInt("edit.downstream_terminal", NetworkEdit.Downstream(net, "t1").Count, 0);

            // #27 re-fit: one terminal's flow, re-size, re-balance.
            Network dn = DamperNetwork(5.0);
            var dmeta = new Dictionary<string, ComponentMeta>();
            dmeta["dS"] = new ComponentMeta { Role = NetworkEdit.RoleDamper };
            dmeta["dB"] = new ComponentMeta { Role = NetworkEdit.RoleDamper };
            NetworkDrawing.Write(dn, dmeta, new Point2(0.0, 0.0), new List<string>());
            CheckStr("refit.damper_feeding_t1", NetworkEdit.DamperFeeding(dn, dmeta, "t1"), "dS");
            CheckStr("refit.damper_feeding_t2", NetworkEdit.DamperFeeding(dn, dmeta, "t2"), "dB");
            CheckTrue("refit.no_damper_without_role", NetworkEdit.DamperFeeding(dn, new Dictionary<string, ComponentMeta>(), "t1") == null);

            var opt = new ReFitOptions { Method = SizingMethod.Velocity, TargetVelocity = 4.0 };
            ReFlowResult rf = NetworkEdit.ReFlow(dn, dmeta, "t1", 0.2, opt);
            Check("refit.old_flow", rf.OldFlowM3s, 0.06, 1e-15);
            Check("refit.new_flow", ((Terminal)rf.Network.Components["t1"]).Flowrate, 0.2, 1e-15);
            CheckTrue("refit.resized_some", rf.Resized.Count > 0);
            // The legs feeding t1 take ReFit.Apply's sizes for the new flow;
            // the branch to t2 carries the same flow and keeps its size.
            Network manual = NetworkEdit.Rebuild(DamperNetwork(0.0), new Dictionary<string, Component> { { "t1", new Terminal("t1", 0.2, null, 0.0) } });
            ReFitResult mfit = ReFit.Apply(manual, opt);
            var up1 = new List<string>(NetworkEdit.Upstream(dn, "t1")); up1.Sort(StringComparer.Ordinal);
            CheckStr("refit.affected", string.Join(",", up1.ToArray()), "dS,main,src,straight,tee");
            CheckStr("refit.branch_kept", DrawingSizing.SizeText(((RigidDuct)rf.Network.Components["branch"]).CrossSection), "Ø160");
            CheckStr("refit.straight_up", DrawingSizing.SizeText(((RigidDuct)rf.Network.Components["straight"]).CrossSection), "Ø300");
            CheckInt("refit.resized_two", rf.Resized.Count, 2);
            CheckStr("refit.tee_follows_main", DrawingSizing.SizeText(((Tee)rf.Network.Components["tee"]).CrossSection), "Ø300");
            foreach (string duct in new[] { "main", "straight" })
                CheckTrue("refit.size_" + duct, NetworkEdit.SameSection(
                    ((RigidDuct)rf.Network.Components[duct]).CrossSection, ((RigidDuct)mfit.Network.Components[duct]).CrossSection));
            CheckTrue("refit.damper_follows_duct", NetworkEdit.SameSection(
                ((TwoPortFitting)rf.Network.Components["dS"]).CrossSection, ((RigidDuct)rf.Network.Components["straight"]).CrossSection));
            CheckInt("refit.dampers_set", rf.Dampers.Count, 2);
            int open = 0;
            foreach (DamperSettingResult ds in rf.Dampers) if (ds.Zeta == 0.0) open++;
            CheckInt("refit.one_damper_open", open, 1);
            CheckTrue("refit.balanced", rf.ResidualSurplusPa < 1e-9);
            List<BalancingHint> after = ReFit.Hints(rf.Network);
            Check("refit.paths_equal", after[0].AvailableDpPa, after[1].AvailableDpPa, 1e-9);
            Check("refit.critical_reported", rf.CriticalAfterPa, rf.Network.Solve(), 1e-12);
            CheckTrue("refit.fast", rf.ElapsedMs < 200.0);
            ReFlowResult rf0 = NetworkEdit.ReFlow(DamperNetwork(0.0), dmeta, "t1", 0.2, opt);
            Check("refit.ignores_old_damper_setting", rf0.CriticalAfterPa, rf.CriticalAfterPa, 1e-12);
            NetworkReadResult rfBack = RoundTrip(rf.Network, rf.Meta);
            CheckTrue("refit.reads_back", rfBack.Ok && rfBack.LengthUpdated.Count == 0);
            Check("refit.read_dp", rfBack.Network.Solve(), rf.CriticalAfterPa, 1e-12);
            ExpectError(true, "refit.err_not_terminal", () => NetworkEdit.ReFlow(dn, dmeta, "main", 0.1, opt));
            ExpectError(true, "refit.err_zero_flow", () => NetworkEdit.ReFlow(dn, dmeta, "t1", 0.0, opt));
            // The shipped example (zwcad-plugin headless test) is this network.
            Dictionary<string, ComponentMeta> exMeta;
            Network ex = NetworkJson.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "example-dampers-network.json"), out exMeta);
            Check("refit.example_dp", ex.Solve(), DamperNetwork(0.0).Solve(), 1e-12);
            CheckStr("refit.example_damper_role", NetworkEdit.DamperFeeding(ex, exMeta, "t2"), "dB");
            ReFlowResult rfx = NetworkEdit.ReFlow(ex, exMeta, "t1", 0.2, opt);
            Check("refit.example_same", rfx.CriticalAfterPa, rf.CriticalAfterPa, 1e-12);
        }

        // Reference network with a damper on each run, the branch damper at ζ.
        private static Network DamperNetwork(double zetaB)
        {
            var net = new Network { Name = "dampers" };
            net.Add("src", new Source("AHU"));
            net.Add("main", new RigidDuct("main", new Round(0.25), 5.0));
            net.Add("tee", new Tee("tee", new Round(0.25), 0.0, 0.5));
            net.Add("straight", new RigidDuct("straight", new Round(0.2), 3.0));
            net.Add("dS", new TwoPortFitting("dS", new Round(0.2), 0.0));
            net.Add("t1", new Terminal("t1", 0.06, null, 0.0));
            net.Add("branch", new RigidDuct("branch", new Round(0.16), 3.0));
            net.Add("dB", new TwoPortFitting("dB", new Round(0.16), zetaB));
            net.Add("t2", new Terminal("t2", 0.04, null, 0.0));
            net.Connect("src", "main.inlet");
            net.Connect("main.outlet", "tee.combined");
            net.Connect("tee.straight", "straight.inlet");
            net.Connect("straight.outlet", "dS.inlet");
            net.Connect("dS.outlet", "t1");
            net.Connect("tee.branch", "branch.inlet");
            net.Connect("branch.outlet", "dB.inlet");
            net.Connect("dB.outlet", "t2");
            return net;
        }

        // Write the network as records, then read the records back.
        private static NetworkReadResult RoundTrip(Network net, Dictionary<string, ComponentMeta> meta)
        {
            List<DrawnComponent> items = NetworkDrawing.Write(net, meta, new Point2(0.0, 0.0), new List<string>());
            int n = 0;
            foreach (DrawnComponent dc in items) dc.Handle = "H" + (n++).ToString(C);
            return NetworkDrawing.Read(items, net.Name);
        }

        private static Network ReferenceHandBuilt()
        {
            var net = new Network { Name = "hand" };
            net.Add("src", new Source("AHU"));
            net.Add("main", new RigidDuct("main", new Round(0.25), 5.0));
            net.Add("tee", new Tee("tee", new Round(0.25), 0.0, 0.5));
            net.Add("straight", new RigidDuct("straight", new Round(0.2), 3.0));
            net.Add("branch", new RigidDuct("branch", new Round(0.16), 3.0));
            net.Add("t1", new Terminal("t1", 0.06, null, 0.0));
            net.Add("t2", new Terminal("t2", 0.04, null, 0.0));
            net.Connect("src", "main.inlet");
            net.Connect("main.outlet", "tee.combined");
            net.Connect("tee.straight", "straight.inlet");
            net.Connect("tee.branch", "branch.inlet");
            net.Connect("straight.outlet", "t1");
            net.Connect("branch.outlet", "t2");
            return net;
        }

        private static List<KeyValuePair<int, object>> Xd(params object[] codeValue)
        {
            var l = new List<KeyValuePair<int, object>>();
            for (int i = 0; i + 1 < codeValue.Length; i += 2)
                l.Add(new KeyValuePair<int, object>((int)codeValue[i], codeValue[i + 1]));
            return l;
        }

        private static void RunDrawing()
        {
            // Topology plumbing used by the drawing adapter.
            var srcOpts = new TraceOptions { SourceAt = new Point2(3.0, 1.0) };
            TracedSystem rev = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 1.0, 2.0, 1.0), Pl(2.0, 1.0, 3.0, 1.0), Pl(2.0, 1.0, 2.0, 0.0),
            }, srcOpts);
            Check("drawing.topo_source_at_x", rev.SourcePoint.X, 3.0, 0.0);
            CheckInt("drawing.topo_terminal_points", rev.TerminalPoints.Count, 2);
            CheckInt("drawing.topo_tee_points", rev.TeePoints.Count, 1);
            var secOpts = new TraceOptions();
            secOpts.Sections["duct0"] = new Rectangular(0.4, 0.2);
            TracedSystem rect = Topology.Trace(new List<Polyline> { Pl(0.0, 0.0, 5.0, 0.0) }, secOpts);
            CheckTrue("drawing.topo_section_override",
                ((RigidDuct)rect.Network.Components["duct0"]).CrossSection is Rectangular);

            // WENTATRACE / WENTAPRESSURE: the drawn layout equals the hand-built network.
            DrawingTrace tr = DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers());
            CheckInt("drawing.trace_chains", tr.System.Chains.Count, 3);
            CheckInt("drawing.trace_components", tr.Network.Components.Count, 7);
            CheckInt("drawing.trace_warnings", tr.Warnings.Count, 0);
            CheckStr("drawing.trace_chain_A", tr.DuctChain["A"], "duct0");
            CheckStr("drawing.trace_chain_B", tr.DuctChain["B"], "duct1");
            CheckStr("drawing.trace_chain_C", tr.DuctChain["C"], "duct2");
            CheckStr("drawing.trace_marker_T1", tr.MarkerTerminal["T1"], "term0");
            CheckStr("drawing.trace_marker_T2", tr.MarkerTerminal["T2"], "term1");
            CheckStr("drawing.trace_feeding_term1", tr.ChainFeeding("term1"), "duct2");
            CheckStr("drawing.trace_damper_chain", tr.DamperChain["D2"], "duct2");
            double drawnDp = tr.Network.Solve();
            Network hand = ReferenceHandBuilt();
            double handDp = hand.Solve();
            CheckTrue("drawing.trace_equals_hand_built", Math.Abs(drawnDp - handDp) <= 1e-12);
            Check("drawing.trace_source_flow",
                tr.Network.Components["duct0"].InletFlowrate() ?? 0.0, 0.1, 1e-12);
            PressureReport rep = PressureReport.Build(tr.Network);
            PressureReport handRep = PressureReport.Build(hand);
            CheckTrue("drawing.pressure_equals_hand_built", Math.Abs(rep.CriticalDpPa - handRep.CriticalDpPa) <= 1e-12);
            List<string> crit = tr.CriticalChains(rep);
            CheckTrue("drawing.pressure_critical_chains",
                crit.Count == 2 && crit[0] == "duct0" && (crit[1] == "duct1" || crit[1] == "duct2"));
            CheckStr("drawing.pressure_critical_matches_hand",
                crit[1] == "duct1" ? "straight" : "branch",
                handRep.Rows.Find(r => r.Kind == "RigidDuct" && r.ComponentId != "main").ComponentId);

            // Drawing order and direction do not matter: the branch drawn first and
            // every run drawn backwards still traces to the hand-built network (the
            // straight leg is the run that continues the main's direction).
            var reordered = new List<DrawnDuct>
            {
                Dd("C", new Round(0.16), 5000, -3000, 5000, 0),
                Dd("B", new Round(0.2), 8000, 0, 5000, 0),
                Dd("A", new Round(0.25), 5000, 0, 2500, 0, 0, 0),
            };
            DrawingTrace tro = DrawingTrace.Build(reordered, ReferenceMarkers());
            CheckTrue("drawing.trace_order_independent", Math.Abs(tro.Network.Solve() - handDp) <= 1e-12);

            // Source picked from the marker, not from drawing order.
            var flipped = new List<DrawnMarker>
            {
                Mk("S", WentaTag.ClassSource, 8000, 0, 0),
                Mk("T0", WentaTag.ClassTerminal, 0, 0, 0.05),
                Mk("T2", WentaTag.ClassTerminal, 5000, -3000, 0.04),
            };
            DrawingTrace tf = DrawingTrace.Build(ReferenceDucts(), flipped);
            tf.Network.Solve();
            Check("drawing.trace_flipped_source_flow",
                tf.Network.Components[tf.DuctChain["B"]].InletFlowrate() ?? 0.0, 0.09, 1e-12);

            ExpectError(true, "drawing.trace_missing_flow_err", delegate
            {
                DrawingTrace.Build(ReferenceDucts(), new List<DrawnMarker> { Mk("S", WentaTag.ClassSource, 0, 0, 0) });
            });
            ExpectError(true, "drawing.trace_two_sources_err", delegate
            {
                var two = ReferenceMarkers();
                two.Add(Mk("S2", WentaTag.ClassSource, 8000, 0, 0));
                DrawingTrace.Build(ReferenceDucts(), two);
            });
            ExpectError(true, "drawing.trace_no_ducts_err", delegate
            {
                DrawingTrace.Build(new List<DrawnDuct>(), ReferenceMarkers());
            });
            var stray = ReferenceMarkers();
            stray.Add(Mk("X", WentaTag.ClassTerminal, 99000, 99000, 0.5));
            CheckInt("drawing.trace_stray_marker_warns", DrawingTrace.Build(ReferenceDucts(), stray).Warnings.Count, 1);
            var mixed = ReferenceDucts();
            mixed[0] = Dd("A1", new Round(0.25), 0, 0, 2500, 0);
            mixed.Add(Dd("A2", new Round(0.315), 2500, 0, 5000, 0));
            DrawingTrace tm = DrawingTrace.Build(mixed, ReferenceMarkers());
            CheckInt("drawing.trace_mixed_size_warns", tm.Warnings.Count, 1);
            CheckInt("drawing.trace_chain_handles", tm.ChainHandles["duct0"].Count, 2);

            // WENTASIZE: per-run flows from the tagged terminals, EN snap, labels.
            var tmpl = new BatchSizingRequest { Shape = Sizing.ShapeRound, Method = SizingMethod.Velocity, TargetVelocity = 4.0 };
            List<ChainSizing> plan = DrawingSizing.Plan(DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers()), tmpl);
            CheckInt("drawing.size_rows", plan.Count, 3);
            ChainSizing main = plan.Find(p => p.ChainId == "duct0");
            Check("drawing.size_main_flow", main.FlowM3s, 0.1, 1e-12);
            Check("drawing.size_main_snapped", ((Round)main.Snapped).Diameter, 0.2, 1e-12);
            Check("drawing.size_main_velocity", main.VelocityMs, 0.1 / (Math.PI * 0.01), 1e-12);
            CheckStr("drawing.size_main_label", main.Label, "Ø200  0.100 m³/s  3.18 m/s");
            ChainSizing br = plan.Find(p => p.ChainId == "duct2");
            Check("drawing.size_branch_flow", br.FlowM3s, 0.04, 1e-12);
            CheckStr("drawing.size_branch_text", DrawingSizing.SizeText(br.Snapped),
                "Ø" + Standards.NearestRoundSizeFor(Standard.En1505_1506,
                    Math.Round(Math.Sqrt(4.0 * 0.04 / (Math.PI * 4.0)) * 1000.0, 3), true));
            // Re-running on the sized drawing reproduces the same plan (update in place).
            var resized = ReferenceDucts();
            foreach (DrawnDuct d in resized)
            {
                string cid = d.Handle == "A" ? "duct0" : d.Handle == "B" ? "duct1" : "duct2";
                d.Section = plan.Find(p => p.ChainId == cid).Snapped;
            }
            List<ChainSizing> again = DrawingSizing.Plan(DrawingTrace.Build(resized, ReferenceMarkers()), tmpl);
            bool same = again.Count == plan.Count;
            for (int i = 0; same && i < plan.Count; i++) same = again[i].Label == plan[i].Label;
            CheckTrue("drawing.size_rerun_idempotent", same);
            var rectTmpl = new BatchSizingRequest { Shape = Sizing.ShapeRectangular, Method = SizingMethod.AspectRatio, TargetVelocity = 4.0, AspectRatio = 2.0 };
            ChainSizing rmain = DrawingSizing.Plan(DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers()), rectTmpl).Find(p => p.ChainId == "duct0");
            CheckTrue("drawing.size_rect_snapped", rmain.Snapped is Rectangular && rmain.Label.Contains("×"));

            // WENTABALANCE: dampers inherit the ReFit hint of the terminal they feed.
            DrawingTrace tb = DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers());
            tb.Network.Solve();
            List<BalancingHint> hints = ReFit.Hints(tb.Network);
            List<DamperSetting> dampers = DrawingBalancing.Dampers(tb, hints);
            CheckInt("drawing.balance_dampers", dampers.Count, 1);
            CheckStr("drawing.balance_damper_terminal", dampers[0].TerminalId, "term1");
            BalancingHint h1 = hints.Find(x => x.TerminalId == "term1");
            Check("drawing.balance_damper_zeta", dampers[0].Hint.DamperZeta, h1.DamperZeta, 0.0);
            CheckTrue("drawing.balance_needs_hw_closed",
                DrawingBalancing.NeedsHardware(new BalancingHint { SurplusDpPa = 5.0, DamperOpenPercent = 0.0 }));
            CheckTrue("drawing.balance_no_hw_open",
                !DrawingBalancing.NeedsHardware(new BalancingHint { SurplusDpPa = 5.0, DamperOpenPercent = 40.0 }));

            // WENTASOUND: regenerated noise of the feeding run through the room equation.
            List<RoomSound> rooms = DrawingSound.Rooms(DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers()), "office", 60.0, 0.2);
            CheckInt("drawing.sound_rooms", rooms.Count, 2);
            RoomSound r2 = rooms.Find(x => x.TerminalId == "term1");
            double v2 = 0.04 / (Math.PI * 0.08 * 0.08);
            Check("drawing.sound_velocity", r2.VelocityMs, v2, 1e-12);
            Check("drawing.sound_lp", r2.PressureLevelDb,
                Sound.DuctPressureLevel(Sound.RegeneratedNoiseRound(v2, 0.16, 1.204), 60.0, 0.2), 1e-12);
            CheckTrue("drawing.sound_worst_first",
                rooms[0].PressureLevelDb - rooms[0].NcLimit >= rooms[1].PressureLevelDb - rooms[1].NcLimit);
            ExpectError(true, "drawing.sound_bad_space_err", delegate
            {
                DrawingSound.Rooms(DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers()), "hangar", 60.0, 0.2);
            });

            // Entity tags (#22): v2 round trip and the legacy WENTADUCT record.
            WentaTag tag = WentaTag.New(WentaTag.ClassDuct);
            tag.SetSection(new Rectangular(0.4, 0.2));
            tag.Set("flow_m3s", 0.15);
            List<KeyValuePair<int, object>> xd = tag.ToXData();
            xd.Insert(0, new KeyValuePair<int, object>(1001, WentaTag.AppName));
            WentaTag back = WentaTag.FromXData(xd);
            CheckStr("drawing.tag_class", back.Class, "duct");
            CheckStr("drawing.tag_guid", back.Guid, tag.Guid);
            Guid parsed;
            CheckTrue("drawing.tag_guid_format", Guid.TryParse(back.Guid, out parsed));
            Check("drawing.tag_flow", back.GetDouble("flow_m3s") ?? 0.0, 0.15, 0.0);
            CheckTrue("drawing.tag_section", back.Section() is Rectangular && ((Rectangular)back.Section()).Width == 0.4);
            CheckTrue("drawing.tag_not_legacy", !back.Legacy);
            WentaTag legacy = WentaTag.FromXData(Xd(1001, "WENTA", 1000, "duct", 1040, 0.15, 1040, 3.75, 1070, 0, 1000, "400×200 mm"));
            CheckTrue("drawing.tag_legacy", legacy.Legacy && legacy.Guid == null);
            Check("drawing.tag_legacy_w", legacy.GetDouble("w_mm") ?? 0.0, 400.0, 0.0);
            Check("drawing.tag_legacy_h", legacy.GetDouble("h_mm") ?? 0.0, 200.0, 0.0);
            WentaTag legacyRound = WentaTag.FromXData(Xd(1000, "duct", 1040, 0.1, 1040, 3.1, 1070, 1, 1000, "Ø200 mm"));
            Check("drawing.tag_legacy_round", ((Round)legacyRound.Section()).Diameter, 0.2, 1e-12);
            CheckTrue("drawing.tag_foreign_app", WentaTag.FromXData(Xd(1001, "OTHER", 1000, "duct")) == null);
            CheckTrue("drawing.tag_empty", WentaTag.FromXData(Xd()) == null);
            CheckTrue("drawing.tag_bad_label", WentaTag.ParseSizeLabel("abc") == null);
        }

        // WENTAFAN: the open fan-curve catalog and the duty-point pick.
        private static void RunFanCatalog(string catalogDir)
        {
            FanCatalog cat = FanCatalog.Load(Path.Combine(catalogDir, "example-fans.json"));
            CheckStr("fancat.name", cat.Name, "example-fans");
            CheckInt("fancat.count", cat.Fans.Count, 4);
            CheckInt("fancat.points", cat.Fans[2].Curve.Points.Length, 4);
            Check("fancat.point_value", cat.Fans[2].Curve.Points[1].StaticPressurePa, 260.0, 0.0);
            Check("fancat.efficiency", cat.Fans[2].Efficiency ?? 0.0, 0.55, 0.0);

            // Reference network duty point (source flow, critical ΔP + 150 Pa AHU).
            double flow, required;
            DrawingTrace t = DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers());
            FanSelection.DutyPoint(t, null, 150.0, out flow, out required);
            double handDp = ReferenceHandBuilt().Solve();
            Check("fancat.duty_flow", flow, 0.1, 1e-12);
            Check("fancat.duty_pressure", required, handDp + 150.0, 1e-12);
            int? pick = FanSelection.Pick(cat, flow, required);
            CheckTrue("fancat.pick_reference", pick.HasValue && cat.Fans[pick.Value].Id == "ExampleFan EF-160");
            CheckTrue("fancat.pick_matches_fan_pickfan", pick == Fan.PickFan(cat.Curves(), flow, required));
            List<FanCandidate> cands = FanSelection.Evaluate(cat, flow, required);
            CheckTrue("fancat.cand_out_of_range", !cands[0].MarginPa.HasValue && !cands[0].Meets);
            CheckTrue("fancat.cand_too_weak", cands[1].MarginPa.HasValue && !cands[1].Meets && cands[1].PowerW == null);
            Check("fancat.cand_available", cands[2].AvailablePa ?? 0.0, 260.0, 1e-12);
            Check("fancat.cand_margin", cands[2].MarginPa ?? 0.0, 260.0 - required, 1e-12);
            Check("fancat.cand_power", cands[2].PowerW ?? 0.0, Fan.Power(0.1, required, 0.55), 1e-12);
            CheckTrue("fancat.cand_bigger_also_meets", cands[3].Meets);
            CheckTrue("fancat.pick_none", FanSelection.Pick(cat, 0.1, 1000.0) == null);

            ExpectError(true, "fancat.no_fans_err", delegate { FanCatalog.Parse("{\"name\":\"x\"}", "t"); });
            ExpectError(true, "fancat.dup_id_err", delegate
            {
                FanCatalog.Parse("{\"fans\":[{\"id\":\"a\",\"points\":[[0,10],[1,0]]},{\"id\":\"a\",\"points\":[[0,10],[1,0]]}]}", "t");
            });
            ExpectError(true, "fancat.bad_point_err", delegate
            {
                FanCatalog.Parse("{\"fans\":[{\"id\":\"a\",\"points\":[[0,10],[1]]}]}", "t");
            });
            ExpectError(true, "fancat.decreasing_flow_err", delegate
            {
                FanCatalog.Parse("{\"fans\":[{\"id\":\"a\",\"points\":[[1,10],[0,0]]}]}", "t");
            });
            ExpectError(true, "fancat.bad_eta_err", delegate
            {
                FanCatalog.Parse("{\"fans\":[{\"id\":\"a\",\"efficiency\":1.5,\"points\":[[0,10],[1,0]]}]}", "t");
            });
            ExpectError(true, "fancat.future_version_err", delegate
            {
                FanCatalog.Parse("{\"version\":2,\"fans\":[]}", "t");
            });
            ExpectError(true, "fancat.negative_extra_err", delegate
            {
                double a, b;
                FanSelection.DutyPoint(DrawingTrace.Build(ReferenceDucts(), ReferenceMarkers()), null, -1.0, out a, out b);
            });
        }

        // WENTAINSULATE: DrawingInsulation is Insulation + SelectThickness for a round run.
        // ---- Markdown: user guide HTML for WENTAHELP (#37) ----

        private static int Count(string s, string sub)
        {
            int n = 0;
            for (int i = s.IndexOf(sub, StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + sub.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        private static void RunMarkdown(string docsDir)
        {
            CheckStr("md.heading", Markdown.ToHtml("## 3.1 `WENTADUCT` — size"),
                "<h2 id=\"31-wentaduct--size\">3.1 <code>WENTADUCT</code> — size</h2>\n");
            CheckStr("md.paragraph_joined", Markdown.ToHtml("one\ntwo\n\nthree"), "<p>one\ntwo</p>\n<p>three</p>\n");
            CheckStr("md.escape_text", Markdown.ToHtml("a < b & \"c\""), "<p>a &lt; b &amp; &quot;c&quot;</p>\n");
            CheckStr("md.raw_html_escaped", Markdown.ToHtml("<script>x</script>"), "<p>&lt;script&gt;x&lt;/script&gt;</p>\n");
            CheckStr("md.code_span_escaped", Markdown.Inline("`<path>` and `a*b*c`"), "<code>&lt;path&gt;</code> and <code>a*b*c</code>");
            CheckStr("md.double_tick_span", Markdown.Inline("``a ` b``"), "<code>a ` b</code>");
            CheckStr("md.bold_italic", Markdown.Inline("**bold** and *it* but 2 * 3 * 4"), "<strong>bold</strong> and <em>it</em> but 2 * 3 * 4");
            CheckStr("md.link", Markdown.Inline("[guide](user-guide.pl.html#3-polecenia)"), "<a href=\"user-guide.pl.html#3-polecenia\">guide</a>");
            CheckStr("md.link_js_blocked", Markdown.Inline("[x](javascript:alert(1))"), "<a href=\"#\">x</a>)");
            CheckStr("md.autolink", Markdown.Inline("see <https://github.com/ModelTok/wenta>."),
                "see <a href=\"https://github.com/ModelTok/wenta\">https://github.com/ModelTok/wenta</a>.");
            CheckStr("md.fence", Markdown.ToHtml("```json\n{ \"a\": \"<b>\" }\n```"),
                "<pre><code class=\"language-json\">{ &quot;a&quot;: &quot;&lt;b&gt;&quot; }\n</code></pre>\n");
            CheckStr("md.fence_keeps_markup", Markdown.ToHtml("```\n## not a heading\n- not a list\n```"),
                "<pre><code>## not a heading\n- not a list\n</code></pre>\n");
            CheckStr("md.list_continuation", Markdown.ToHtml("- one\n  more\n- two"),
                "<ul>\n<li>one\nmore</li>\n<li>two</li>\n</ul>\n");
            CheckStr("md.list_blank_between_items", Markdown.ToHtml("1. a\n\n2. b\n\npara"),
                "<ol>\n<li>a</li>\n<li>b</li>\n</ol>\n<p>para</p>\n");
            CheckStr("md.ol_start", Markdown.ToHtml("3. c"), "<ol start=\"3\">\n<li>c</li>\n</ol>\n");
            CheckStr("md.bullet_interrupts_paragraph", Markdown.ToHtml("Steps:\n- a"), "<p>Steps:</p>\n<ul>\n<li>a</li>\n</ul>\n");
            CheckStr("md.number_does_not_interrupt", Markdown.ToHtml("In\n2024. it"), "<p>In\n2024. it</p>\n");
            CheckStr("md.rule", Markdown.ToHtml("a\n\n---\n\nb"), "<p>a</p>\n<hr>\n<p>b</p>\n");
            CheckStr("md.table", Markdown.ToHtml("| A | B |\n|---|--:|\n| `x|y` | 2 |\n| 3 |"),
                "<table>\n<thead>\n<tr><th>A</th><th style=\"text-align:right\">B</th></tr>\n</thead>\n<tbody>\n"
                + "<tr><td><code>x|y</code></td><td style=\"text-align:right\">2</td></tr>\n"
                + "<tr><td>3</td><td style=\"text-align:right\"></td></tr>\n</tbody>\n</table>\n");
            CheckStr("md.slug_polish", Markdown.Slug("3.17 Karta wstęgi „Wenta”"), "317-karta-wstęgi-wenta");
            List<Markdown.Section> hs;
            Markdown.ToHtml("## A\n## A", out hs);
            CheckStr("md.unique_ids", hs[0].Id + "," + hs[1].Id, "a,a-1");
            string doc = Markdown.ToHtmlDocument("# Wenta <guide>\n\n## One\n\n### Two\n\ntext", "fallback", "pl");
            CheckTrue("md.doc_title", doc.Contains("<title>Wenta &lt;guide&gt;</title>"));
            CheckTrue("md.doc_lang", doc.Contains("<html lang=\"pl\">"));
            CheckTrue("md.doc_toc", doc.Contains("<li class=\"toc2\"><a href=\"#one\">One</a></li>")
                && doc.Contains("<li class=\"toc3\"><a href=\"#two\">Two</a></li>"));
            CheckTrue("md.doc_dark_mode", doc.Contains("prefers-color-scheme:dark"));
            CheckTrue("md.doc_toc_after_title", doc.IndexOf("</h1>", StringComparison.Ordinal) < doc.IndexOf("<nav class=\"toc\">", StringComparison.Ordinal)
                && doc.IndexOf("<nav class=\"toc\">", StringComparison.Ordinal) < doc.IndexOf("<h2", StringComparison.Ordinal));

            // The real guides convert completely: every heading, table and
            // fence of the source is in the HTML and no Markdown syntax leaks.
            foreach (string lang in new[] { "en", "pl" })
            {
                string path = Path.Combine(docsDir, "user-guide." + lang + ".md");
                if (!File.Exists(path)) { Fail("md.guide_" + lang, "missing " + path); continue; }
                string md = File.ReadAllText(path).Replace("\r\n", "\n");
                string html = Markdown.ToHtmlDocument(md, "Wenta", lang);
                string[] lines = md.Split('\n');
                int h2 = 0, h3 = 0, fences = 0, tables = 0;
                bool inFence = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("```", StringComparison.Ordinal)) { fences++; inFence = !inFence; continue; }
                    if (inFence) continue;
                    if (lines[i].StartsWith("## ", StringComparison.Ordinal)) h2++;
                    if (lines[i].StartsWith("### ", StringComparison.Ordinal)) h3++;
                    if (lines[i].StartsWith("|", StringComparison.Ordinal) && i + 1 < lines.Length
                        && lines[i + 1].StartsWith("|-", StringComparison.Ordinal)) tables++;
                }
                CheckInt("md.guide_" + lang + "_h2", Count(html, "<h2 id="), h2);
                CheckInt("md.guide_" + lang + "_h3", Count(html, "<h3 id="), h3);
                CheckInt("md.guide_" + lang + "_toc", Count(html, "<li class=\"toc"), h2 + h3);
                CheckInt("md.guide_" + lang + "_fences", Count(html, "<pre><code"), fences / 2);
                CheckInt("md.guide_" + lang + "_tables", Count(html, "<table>"), tables);
                CheckTrue("md.guide_" + lang + "_no_fence_leak", !html.Contains("```"));
                CheckTrue("md.guide_" + lang + "_no_bold_leak", !Regex.IsMatch(Regex.Replace(html, "<code>.*?</code>|<pre>.*?</pre>", "", RegexOptions.Singleline), @"\*\*"));
                CheckTrue("md.guide_" + lang + "_no_table_leak", !html.Contains("|---"));
                CheckTrue("md.guide_" + lang + "_balanced_lists", Count(html, "<ul>") == Count(html, "</ul>") && Count(html, "<ol") == Count(html, "</ol>"));
                CheckTrue("md.guide_" + lang + "_has_wentahelp", html.Contains("<code>WENTAHELP</code>"));
            }
        }

        // ---- Attenuators: WENTAATTEN selection and insertion (#48) ----

        private static void RunAttenuators(string catalogDir)
        {
            AttenuatorCatalog cat = AttenuatorCatalog.Load(Path.Combine(catalogDir, "example-attenuators.json"));
            CheckStr("atten.cat_name", cat.Name, "example-attenuators");
            CheckInt("atten.cat_count", cat.Attenuators.Count, 21);
            Attenuator first = cat.Attenuators[0];
            Check("atten.cat_units_d", first.DiameterM, 0.1, 1e-15);
            Check("atten.cat_units_l", first.LengthM, 0.6, 1e-15);

            // Pick: lowest zeta whose insertion loss covers the requirement.
            CheckStr("atten.pick_short", AttenuatorSelection.Pick(cat, 0.2, 7.0).Id, "ExampleAtten EA-200-600");
            CheckStr("atten.pick_mid", AttenuatorSelection.Pick(cat, 0.2, 7.01).Id, "ExampleAtten EA-200-900");
            CheckStr("atten.pick_long", AttenuatorSelection.Pick(cat, 0.2, 10.9).Id, "ExampleAtten EA-200-1200");
            CheckTrue("atten.pick_none_too_loud", AttenuatorSelection.Pick(cat, 0.2, 14.1) == null);
            CheckTrue("atten.pick_none_size", AttenuatorSelection.Pick(cat, 0.18, 1.0) == null);
            var tie = AttenuatorCatalog.Parse("{\"attenuators\":[{\"id\":\"long\",\"diameter_mm\":200,\"length_mm\":900,\"insertion_loss_db\":9,\"zeta\":0.5},"
                + "{\"id\":\"short\",\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":9,\"zeta\":0.5}]}", "tie");
            CheckStr("atten.pick_tie_shorter", AttenuatorSelection.Pick(tie, 0.2, 5.0).Id, "short");

            // Catalog validation.
            ExpectError(true, "atten.cat_bad_json", () => AttenuatorCatalog.Parse("{", "x"));
            ExpectError(true, "atten.cat_no_array", () => AttenuatorCatalog.Parse("{\"name\":\"x\"}", "x"));
            ExpectError(true, "atten.cat_newer_version", () => AttenuatorCatalog.Parse("{\"version\":2,\"attenuators\":[]}", "x"));
            ExpectError(true, "atten.cat_duplicate", () => AttenuatorCatalog.Parse("{\"attenuators\":[{\"id\":\"a\",\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":5,\"zeta\":0.4},{\"id\":\"a\",\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":5,\"zeta\":0.4}]}", "x"));
            ExpectError(true, "atten.cat_negative_zeta", () => AttenuatorCatalog.Parse("{\"attenuators\":[{\"id\":\"a\",\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":5,\"zeta\":-0.1}]}", "x"));
            ExpectError(true, "atten.cat_missing_loss", () => AttenuatorCatalog.Parse("{\"attenuators\":[{\"id\":\"a\",\"diameter_mm\":200,\"length_mm\":600,\"zeta\":0.4}]}", "x"));
            ExpectError(true, "atten.cat_no_id", () => AttenuatorCatalog.Parse("{\"attenuators\":[{\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":5,\"zeta\":0.4}]}", "x"));

            // Reference network (Ø250 main, Ø200 straight run at 0.06 m³/s,
            // Ø160 branch at 0.04 m³/s), studio (NC 25), S = 50 m², α = 0.2.
            Network net = ReferenceHandBuilt();
            var meta = new Dictionary<string, ComponentMeta>();
            NetworkDrawing.Write(net, meta, new Point2(0.0, 0.0), new List<string>());   // stores the layout
            double vS = 0.06 / (Math.PI * 0.01), vB = 0.04 / (Math.PI * 0.0064);
            double lpS = Sound.DuctPressureLevel(Sound.RegeneratedNoiseRound(vS, 0.2), 50.0, 0.2);
            double lpB = Sound.DuctPressureLevel(Sound.RegeneratedNoiseRound(vB, 0.16), 50.0, 0.2);
            Check("atten.ref_lp_straight", lpS, 10.0 + 60.0 * Math.Log10(vS) - 20.0 * Math.Log10(0.2) + 10.0 * Math.Log10(0.32), 1e-12);
            AttenuatorResult r = AttenuatorSelection.Apply(net, meta, cat, "studio", 50.0, 0.2);
            CheckInt("atten.rooms", r.Rooms.Count, 2);
            CheckInt("atten.inserted", r.Inserted, 2);
            RoomAttenuation worst = r.Rooms[0], other = r.Rooms[1];
            CheckStr("atten.worst_first", worst.TerminalId, "t2");
            CheckStr("atten.worst_duct", worst.DuctId, "branch");
            Check("atten.worst_lp", worst.PressureLevelDb, lpB, 1e-12);
            Check("atten.worst_required", worst.RequiredDb, lpB - 25.0, 1e-12);
            CheckStr("atten.worst_pick", worst.Picked.Id, "ExampleAtten EA-160-1200");
            Check("atten.worst_after", worst.LevelAfterDb, lpB - 16.0, 1e-12);
            CheckTrue("atten.worst_ok", worst.Ok && worst.LevelAfterDb <= 25.0);
            CheckStr("atten.other_pick", other.Picked.Id, "ExampleAtten EA-200-1200");
            CheckStr("atten.inserted_id", other.InsertedId, "atten_t1");

            // Spliced in and part of the solve: same ΔP as the hand-built
            // network with the two fittings in place.
            Network hand = new Network { Name = "hand" };
            hand.Add("src", new Source("AHU"));
            hand.Add("main", new RigidDuct("main", new Round(0.25), 5.0));
            hand.Add("tee", new Tee("tee", new Round(0.25), 0.0, 0.5));
            hand.Add("straight", new RigidDuct("straight", new Round(0.2), 3.0));
            hand.Add("branch", new RigidDuct("branch", new Round(0.16), 3.0));
            hand.Add("a1", new TwoPortFitting("a1", new Round(0.2), 0.8));
            hand.Add("a2", new TwoPortFitting("a2", new Round(0.16), 0.8));
            hand.Add("t1", new Terminal("t1", 0.06, null, 0.0));
            hand.Add("t2", new Terminal("t2", 0.04, null, 0.0));
            hand.Connect("src", "main.inlet");
            hand.Connect("main.outlet", "tee.combined");
            hand.Connect("tee.straight", "straight.inlet");
            hand.Connect("tee.branch", "branch.inlet");
            hand.Connect("straight.outlet", "a1.inlet");
            hand.Connect("a1.outlet", "t1");
            hand.Connect("branch.outlet", "a2.inlet");
            hand.Connect("a2.outlet", "t2");
            double dpHand = hand.Solve();
            Check("atten.solve_includes_loss", r.CriticalPa, dpHand, 1e-12);
            CheckTrue("atten.dp_increased", r.CriticalPa > ReferenceHandBuilt().Solve());
            CheckInt("atten.components", r.Network.Components.Count, 9);
            CheckInt("atten.validates", r.Network.Validate().Count, 0);
            ComponentMeta am = r.Meta["atten_t1"];
            CheckStr("atten.meta_role", am.Role, "attenuator");
            CheckStr("atten.meta_catalog", am.CatalogEntry, "ExampleAtten EA-200-1200");
            CheckStr("atten.meta_provenance", am.Provenance, "example-attenuators (fictional example)");
            CheckTrue("atten.meta_guid", !string.IsNullOrEmpty(am.Guid));
            // Placed on the straight run's last segment, 500 mm back from its end.
            List<Point2> sl = meta["straight"].LayoutMm;
            Point2 end = sl[sl.Count - 1];
            CheckTrue("atten.placed_on_run", am.AtMm.HasValue
                && Math.Abs(Math.Sqrt(Math.Pow(am.AtMm.Value.X - end.X, 2) + Math.Pow(am.AtMm.Value.Y - end.Y, 2)) - 500.0) < 1e-9);

            // Drawn and read back like any network: tag class "attenuator",
            // layout kept (no warnings), the read-back solves to the same ΔP.
            var warn = new List<string>();
            List<DrawnComponent> drawn = NetworkDrawing.Write(r.Network, r.Meta, new Point2(0.0, 0.0), warn);
            CheckInt("atten.draw_no_warnings", warn.Count, 0);
            CheckStr("atten.draw_class", ByCid(drawn, "atten_t1").Tag.Class, NetworkDrawing.ClassAttenuator);
            CheckStr("atten.draw_catalog", ByCid(drawn, "atten_t1").Tag.Get("catalog"), "ExampleAtten EA-200-1200");
            NetworkReadResult back = NetworkDrawing.Read(AsDrawn(drawn), "hand");
            CheckTrue("atten.read_ok", back.Ok);
            Check("atten.read_dp", back.Network.Solve(), dpHand, 1e-12);
            CheckStr("atten.read_role", back.Meta["atten_t2"].Role, "attenuator");

            // Re-running finds the fitted attenuators and inserts nothing.
            AttenuatorResult again = AttenuatorSelection.Apply(back.Network, back.Meta, cat, "studio", 50.0, 0.2);
            CheckInt("atten.rerun_inserts_none", again.Inserted, 0);
            CheckTrue("atten.rerun_ok", again.Rooms[0].Ok && again.Rooms[1].Ok);
            CheckStr("atten.rerun_existing", again.Rooms[0].ExistingId, "atten_t2");
            Check("atten.rerun_level", again.Rooms[0].LevelAfterDb, lpB - 16.0, 1e-12);

            // Office (NC 35): the short units are enough.
            AttenuatorResult office = AttenuatorSelection.Apply(ReferenceHandBuilt(), null, cat, "office", 50.0, 0.2);
            CheckStr("atten.office_pick_branch", office.Rooms[0].Picked.Id, "ExampleAtten EA-160-600");
            CheckStr("atten.office_pick_straight", office.Rooms[1].Picked.Id, "ExampleAtten EA-200-600");
            // Industrial (NC 60): nothing needed, network unchanged.
            Network quiet = ReferenceHandBuilt();
            AttenuatorResult ind = AttenuatorSelection.Apply(quiet, null, cat, "industrial", 50.0, 0.2);
            CheckInt("atten.industrial_none", ind.Inserted, 0);
            CheckTrue("atten.industrial_same_network", ReferenceEquals(ind.Network, quiet));
            CheckTrue("atten.industrial_required_zero", ind.Rooms[0].RequiredDb == 0.0 && ind.Rooms[0].Ok);
            // No unit big enough: reported, nothing inserted for that room.
            var small = AttenuatorCatalog.Parse("{\"name\":\"s\",\"attenuators\":[{\"id\":\"s200\",\"diameter_mm\":200,\"length_mm\":600,\"insertion_loss_db\":3,\"zeta\":0.3}]}", "s");
            AttenuatorResult none = AttenuatorSelection.Apply(ReferenceHandBuilt(), null, small, "studio", 50.0, 0.2);
            CheckInt("atten.none_inserted", none.Inserted, 0);
            CheckTrue("atten.none_problem_size", none.Rooms[0].Problem != null && none.Rooms[0].Problem.Contains("Ø160"));
            CheckTrue("atten.none_not_ok", !none.Rooms[0].Ok && !none.Rooms[1].Ok);
            ExpectError(true, "atten.unknown_space", () => AttenuatorSelection.Apply(ReferenceHandBuilt(), null, cat, "garage", 50.0, 0.2));
            // A terminal fed straight from a tee is skipped with a reason.
            Network teeFed = new Network();
            teeFed.Add("s", new Source("s"));
            teeFed.Add("d", new RigidDuct("d", new Round(0.2), 2.0));
            teeFed.Add("t", new Tee("t", new Round(0.2), 0.0, 0.5));
            teeFed.Add("e", new RigidDuct("e", new Round(0.2), 2.0));
            teeFed.Add("a", new Terminal("a", 0.05, null, 0.0));
            teeFed.Add("b", new Terminal("b", 0.05, null, 0.0));
            teeFed.Connect("s", "d.inlet");
            teeFed.Connect("d.outlet", "t.combined");
            teeFed.Connect("t.straight", "e.inlet");
            teeFed.Connect("e.outlet", "a");
            teeFed.Connect("t.branch", "b");
            AttenuatorResult tf = AttenuatorSelection.Apply(teeFed, null, cat, "studio", 50.0, 0.2);
            RoomAttenuation skipped = tf.Rooms[0].TerminalId == "b" ? tf.Rooms[0] : tf.Rooms[1];
            CheckTrue("atten.tee_fed_skipped", skipped.Problem != null && skipped.DuctId == null && skipped.Picked == null);
        }

        // ---- NetworkDrawing: WENTAWRITE / WENTAREAD round trip (#20, #22) ----

        // What ZWCAD stores: the tag goes through XData strings and back.
        private static List<DrawnComponent> AsDrawn(List<DrawnComponent> written)
        {
            var list = new List<DrawnComponent>();
            int h = 1;
            foreach (DrawnComponent d in written)
            {
                var xd = new List<KeyValuePair<int, object>> { new KeyValuePair<int, object>(1001, WentaTag.AppName) };
                xd.AddRange(d.Tag.ToXData());
                list.Add(new DrawnComponent
                {
                    Handle = "H" + (h++).ToString(C),
                    Tag = WentaTag.FromXData(xd),
                    PointsMm = d.PointsMm == null ? null : new List<Point2>(d.PointsMm),
                    AtMm = d.AtMm,
                });
            }
            return list;
        }

        private static DrawnComponent ByCid(List<DrawnComponent> list, string cid)
        {
            foreach (DrawnComponent d in list) if (d.Tag.Get("cid") == cid) return d;
            return null;
        }

        private static bool HasError(NetworkReadResult r, string fragment)
        {
            foreach (string e in r.Errors) if (e.Contains(fragment)) return true;
            return false;
        }

        private static string TagText(WentaTag t)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, object> kv in t.ToXData()) parts.Add(Convert.ToString(kv.Value, C));
            return string.Join("|", parts.ToArray());
        }

        private static void RunNetworkDrawing()
        {
            Network net = TeeNetwork();
            var meta = new Dictionary<string, ComponentMeta>();
            var warnings = new List<string>();
            List<DrawnComponent> written = NetworkDrawing.Write(net, meta, new Point2(1000.0, 2000.0), warnings);
            CheckInt("ndraw.write_count", written.Count, 7);
            CheckInt("ndraw.write_no_warnings", warnings.Count, 0);
            CheckStr("ndraw.class_tee", ByCid(written, "tee").Tag.Class, NetworkDrawing.ClassTee);
            CheckStr("ndraw.class_flex", ByCid(written, "flex").Tag.Class, NetworkDrawing.ClassFlex);
            CheckStr("ndraw.class_duct", ByCid(written, "duct").Tag.Class, WentaTag.ClassDuct);
            CheckStr("ndraw.connection_key", ByCid(written, "flex").Tag.Get("in.inlet"), "tee:branch");

            // To scale: 20 m of duct is 20 000 drawing units, the tee sits at
            // its end, the straight leg carries on and the branch turns 90°.
            DrawnComponent duct = ByCid(written, "duct");
            Check("ndraw.duct_to_scale", NetworkDrawing.PolylineLengthMm(duct.PointsMm), 20000.0, 1e-12);
            Check("ndraw.source_at_origin_x", ByCid(written, "ahu").AtMm.Value.X, 1000.0, 1e-12);
            Check("ndraw.tee_at_duct_end", ByCid(written, "tee").AtMm.Value.X, 21000.0, 1e-12);
            Point2[] d2 = ByCid(written, "d2").PointsMm.ToArray();
            Check("ndraw.straight_keeps_direction", d2[1].X - d2[0].X, 5000.0, 1e-12);
            Point2[] fl = ByCid(written, "flex").PointsMm.ToArray();
            Check("ndraw.branch_turns_x", fl[1].X - fl[0].X, 0.0, 1e-12);
            Check("ndraw.branch_turns_y", Math.Abs(fl[1].Y - fl[0].Y), 3000.0, 1e-12);
            Check("ndraw.terminal_at_branch_end", ByCid(written, "t2").AtMm.Value.Y, fl[1].Y, 1e-12);

            // Read back what the drawing stores: same network, same JSON.
            List<DrawnComponent> drawn = AsDrawn(written);
            NetworkReadResult back = NetworkDrawing.Read(drawn, "tee");
            CheckTrue("ndraw.read_ok", back.Ok);
            CheckInt("ndraw.read_count", back.Network.Components.Count, 7);
            CheckInt("ndraw.read_no_length_edits", back.LengthUpdated.Count, 0);
            Check("ndraw.read_dp", back.Network.Solve(), 7.629497821799035, 1e-12);
            string jsonA = NetworkJson.SerializeDesign(net, meta);
            string jsonB = NetworkJson.SerializeDesign(back.Network, back.Meta);
            CheckStr("ndraw.lossless_json", jsonB, jsonA);
            CheckStr("ndraw.guid_kept", back.Meta["d2"].Guid, meta["d2"].Guid);

            // Write the read result again: the stored layout is reused and
            // every tag comes out identical, so a re-write updates in place.
            var warnings2 = new List<string>();
            List<DrawnComponent> again = NetworkDrawing.Write(back.Network, back.Meta, new Point2(-5e5, -5e5), warnings2);
            CheckInt("ndraw.rewrite_no_warnings", warnings2.Count, 0);
            bool same = again.Count == written.Count;
            for (int i = 0; same && i < again.Count; i++)
                same = TagText(again[i].Tag) == TagText(written[i].Tag);
            CheckTrue("ndraw.rewrite_tags_identical", same);
            Check("ndraw.rewrite_layout_kept", ByCid(again, "tee").AtMm.Value.X, 21000.0, 1e-12);
            NetworkReadResult back2 = NetworkDrawing.Read(AsDrawn(again), "tee");
            CheckStr("ndraw.second_round_trip", NetworkJson.SerializeDesign(back2.Network, back2.Meta), jsonA);

            // The design JSON is the python/wenta NetworkDesignSchema shape
            // and parses back with its layout.
            CheckTrue("ndraw.design_keyed_components", jsonA.Contains("\"components\":{\"ahu\":{\"type\":\"Source\""));
            CheckTrue("ndraw.design_connection", jsonA.Contains("{\"source\":\"tee:branch\",\"target\":\"flex:inlet\"}"));
            Dictionary<string, ComponentMeta> pmeta;
            Network parsed = NetworkJson.Parse(jsonA, out pmeta);
            Check("ndraw.design_parse_dp", parsed.Solve(), 7.629497821799035, 1e-12);
            Check("ndraw.design_parse_layout", pmeta["tee"].AtMm.Value.X, 21000.0, 1e-12);
            CheckStr("ndraw.design_reserialize", NetworkJson.SerializeDesign(parsed, pmeta), jsonA);
            CheckStr("ndraw.legacy_shape_unchanged", NetworkJson.Serialize(TeeNetwork(),
                new Dictionary<string, ComponentMeta> { { "duct", new ComponentMeta { Guid = "g" } } }).Contains("layout_mm") ? "layout" : "none", "none");

            // Editing the geometry updates the record instead of adding one.
            List<DrawnComponent> edited = AsDrawn(written);
            DrawnComponent ed = ByCid(edited, "duct");
            ed.PointsMm[1] = new Point2(ed.PointsMm[1].X + 5000.0, ed.PointsMm[1].Y);
            NetworkReadResult er = NetworkDrawing.Read(edited, "tee");
            CheckTrue("ndraw.edit_ok", er.Ok);
            CheckInt("ndraw.edit_count", er.Network.Components.Count, 7);
            CheckStr("ndraw.edit_reported", string.Join(",", er.LengthUpdated.ToArray()), "duct");
            Check("ndraw.edit_length", ((RigidDuct)er.Network.Components["duct"]).Length, 25.0, 1e-12);
            CheckTrue("ndraw.edit_changes_dp", er.Network.Solve() > 7.629497821799035 + 0.1);
            WentaTag merged = ByCid(edited, "duct").Tag;
            merged.Set("label_h", "1A2");
            NetworkDrawing.MergeInto(merged, NetworkDrawing.Record(er.Network, "duct", er.Network.Components["duct"], er.Meta["duct"]));
            CheckStr("ndraw.merge_updates_length", merged.Get("length_m"), "25");
            CheckStr("ndraw.merge_keeps_label", merged.Get("label_h"), "1A2");
            CheckStr("ndraw.merge_keeps_guid", merged.Guid, meta["duct"].Guid);

            // Orphaned and inconsistent tags are refused.
            List<DrawnComponent> copy = AsDrawn(written);
            DrawnComponent dup = AsDrawn(new List<DrawnComponent> { ByCid(written, "d2") })[0];
            dup.Handle = "H99";
            copy.Add(dup);
            NetworkReadResult cr = NetworkDrawing.Read(copy, "tee");
            CheckTrue("ndraw.refuse_copied_guid", !cr.Ok && HasError(cr, "same GUID"));

            List<DrawnComponent> erased = AsDrawn(written);
            erased.Remove(ByCid(erased, "tee"));
            NetworkReadResult orr = NetworkDrawing.Read(erased, "tee");
            CheckTrue("ndraw.refuse_orphan", !orr.Ok && HasError(orr, "orphaned tag") && HasError(orr, "'tee:branch'"));

            List<DrawnComponent> wrongEntity = AsDrawn(written);
            DrawnComponent we = ByCid(wrongEntity, "d2");
            we.AtMm = we.PointsMm[0];
            we.PointsMm = null;
            CheckTrue("ndraw.refuse_duct_on_point", HasError(NetworkDrawing.Read(wrongEntity, "tee"), "not a centreline"));

            List<DrawnComponent> wrongClass = AsDrawn(written);
            ByCid(wrongClass, "d2").Tag.Class = WentaTag.ClassTerminal;
            CheckTrue("ndraw.refuse_class_mismatch", HasError(NetworkDrawing.Read(wrongClass, "tee"), "does not match type RigidDuct"));

            List<DrawnComponent> badPort = AsDrawn(written);
            ByCid(badPort, "d2").Tag.Set("in.inlet", "tee:side");
            CheckTrue("ndraw.refuse_unknown_port", HasError(NetworkDrawing.Read(badPort, "tee"), "not an outlet"));

            List<DrawnComponent> missing = AsDrawn(written);
            ByCid(missing, "d2").Tag.Set("length_m", (string)null);
            ByCid(missing, "t1").Tag.Set("flow_m3s", (string)null);
            NetworkReadResult mr = NetworkDrawing.Read(missing, "tee");
            CheckTrue("ndraw.refuse_missing_value", HasError(mr, "'flow_m3s'"));
            CheckTrue("ndraw.missing_length_from_geometry", mr.LengthUpdated.Contains("d2"));

            List<DrawnComponent> cut = AsDrawn(written);
            ByCid(cut, "t1").Tag.Set("in.inlet", (string)null);
            CheckTrue("ndraw.refuse_unconnected", HasError(NetworkDrawing.Read(cut, "tee"), "'t1' is not connected"));

            CheckTrue("ndraw.refuse_empty", !NetworkDrawing.Read(new List<DrawnComponent>(), "x").Ok);

            // Tags that are not component records are counted and left alone.
            List<DrawnComponent> mixed = AsDrawn(written);
            mixed.Add(new DrawnComponent { Handle = "M1", Tag = WentaTag.New(WentaTag.ClassTerminal), AtMm = new Point2(0, 0) });
            NetworkReadResult mx = NetworkDrawing.Read(mixed, "tee");
            CheckTrue("ndraw.ignore_markers_ok", mx.Ok);
            CheckInt("ndraw.ignore_markers_count", mx.Ignored, 1);

            // Fitting roles, catalog entry and provenance survive the trip.
            var att = new Network { Name = "att" };
            att.Add("ahu", new Source("AHU"));
            att.Add("d1", new RigidDuct("d1", new Rectangular(0.4, 0.2), 6.0));
            att.Add("sil", new TwoPortFitting("sil", new Rectangular(0.4, 0.2), 1.7));
            att.Add("t", new Terminal("t", 0.12, null, 0.0));
            att.Connect("ahu", "d1");
            att.Connect("d1", "sil");
            att.Connect("sil", "t");
            var am = new Dictionary<string, ComponentMeta>
            {
                { "sil", new ComponentMeta { Role = NetworkDrawing.ClassAttenuator, CatalogEntry = "EA-400x200", Provenance = "example-attenuators.json", DrawingScope = "L1" } },
            };
            List<DrawnComponent> aw = NetworkDrawing.Write(att, am, new Point2(0.0, 0.0), null);
            CheckStr("ndraw.role_class", ByCid(aw, "sil").Tag.Class, NetworkDrawing.ClassAttenuator);
            NetworkReadResult ar = NetworkDrawing.Read(AsDrawn(aw), "att");
            CheckTrue("ndraw.role_read_ok", ar.Ok);
            CheckStr("ndraw.role_roundtrip", ar.Meta["sil"].Role, NetworkDrawing.ClassAttenuator);
            CheckStr("ndraw.catalog_roundtrip", ar.Meta["sil"].CatalogEntry, "EA-400x200");
            CheckStr("ndraw.provenance_roundtrip", ar.Meta["sil"].Provenance, "example-attenuators.json");
            CheckStr("ndraw.scope_roundtrip", ar.Meta["sil"].DrawingScope, "L1");
            Check("ndraw.rect_exact", ((RigidDuct)ar.Network.Components["d1"]).CrossSection.Area, 0.08, 1e-15);
            string attJson = NetworkJson.SerializeDesign(ar.Network, ar.Meta);
            CheckTrue("ndraw.role_in_json", attJson.Contains("\"role\":\"attenuator\""));
            CheckStr("ndraw.role_json_lossless", NetworkJson.SerializeDesign(att, am), attJson);

            // A value that "R" alone does not round-trip on .NET Framework.
            var odd = new WentaTag();
            odd.Set("v", 0.84551240822557006);
            CheckTrue("ndraw.double_roundtrip", double.Parse(odd.Get("v"), C) == 0.84551240822557006);

            // A partial layout is replaced; unreachable components are drawn apart.
            var partial = new Dictionary<string, ComponentMeta>
            {
                { "duct", new ComponentMeta { LayoutMm = new List<Point2> { new Point2(0, 0), new Point2(1, 1) } } },
            };
            var pw = new List<string>();
            NetworkDrawing.Write(TeeNetwork(), partial, new Point2(0, 0), pw);
            CheckTrue("ndraw.partial_layout_warned", pw.Count == 1 && pw[0].Contains("only some"));
            Check("ndraw.partial_layout_replaced", NetworkDrawing.PolylineLengthMm(partial["duct"].LayoutMm), 20000.0, 1e-12);
            Network loose = TeeNetwork();
            loose.Add("spare", new RigidDuct("spare", new Round(0.1), 2.0));
            var lw = new List<string>();
            NetworkDrawing.Write(loose, new Dictionary<string, ComponentMeta>(), new Point2(0, 0), lw);
            CheckTrue("ndraw.unreached_warned", lw.Count == 1 && lw[0].Contains("'spare'"));
        }

        private static void RunDrawingInsulation()
        {
            double k = 0.035, d = 0.2, hi = Insulation.DefaultInnerHtc, ho = Insulation.DefaultOuterHtc;
            InsulationSizing c = DrawingInsulation.Size(new Round(d), DrawingInsulation.Condensation, "mineral_wool",
                8.0, 24.0, 15.8, 0.0);
            double reqC = Insulation.RequiredThicknessCondensation(8.0, 15.8, 24.0, k, d, hi, ho);
            Check("drawinsul.cond_required", c.RequiredM, reqC, 1e-12);
            Check("drawinsul.cond_selected", c.SelectedM, Insulation.SelectThickness(reqC), 1e-12);
            CheckTrue("drawinsul.cond_step_ge_required", c.SelectedM >= c.RequiredM);
            Check("drawinsul.cond_heat_loss", c.HeatLossWPerM,
                Insulation.HeatLossWithInsulation(8.0, 24.0, k, d, c.SelectedM, hi, ho), 1e-12);
            InsulationSizing h = DrawingInsulation.Size(new Round(d), DrawingInsulation.HeatLoss, "mineral_wool",
                60.0, 20.0, 0.0, 25.0);
            double reqH = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 25.0, k, d, hi, ho);
            Check("drawinsul.heat_required", h.RequiredM, reqH, 1e-12);
            CheckTrue("drawinsul.heat_meets_target", Math.Abs(h.HeatLossWPerM) <= 25.0);
            InsulationSizing none = DrawingInsulation.Size(new Round(d), DrawingInsulation.Condensation, "mineral_wool",
                16.0, 24.0, 15.0, 0.0);
            CheckTrue("drawinsul.none_needed", none.RequiredM == 0.0 && none.SelectedM == 0.0);
            ExpectError(true, "drawinsul.rect_err", delegate
            {
                DrawingInsulation.Size(new Rectangular(0.4, 0.2), DrawingInsulation.Condensation, "mineral_wool", 8.0, 24.0, 15.8, 0.0);
            });
            ExpectError(true, "drawinsul.material_err", delegate
            {
                DrawingInsulation.Size(new Round(d), DrawingInsulation.Condensation, "straw", 8.0, 24.0, 15.8, 0.0);
            });
            ExpectError(true, "drawinsul.purpose_err", delegate
            {
                DrawingInsulation.Size(new Round(d), "fire", "mineral_wool", 8.0, 24.0, 15.8, 0.0);
            });
        }

        private static void RunPerformance()
        {
            Network net = BenchNetwork(100, 10);
            CheckTrue("perf.network_size", net.Components.Count >= 1000);
            net.Solve();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int iters = 10;
            for (int i = 0; i < iters; i++) net.Solve();
            sw.Stop();
            double msPerSolve = sw.Elapsed.TotalMilliseconds / iters;
            Console.WriteLine("  [perf] " + net.Components.Count + " components, re-solve "
                + msPerSolve.ToString("F3", C) + " ms");
            CheckTrue("perf.resolve_under_200ms", msPerSolve < 200.0);

            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            Network fresh = BenchNetwork(100, 10);
            fresh.Solve();
            sw2.Stop();
            Console.WriteLine("  [perf] build + first solve "
                + sw2.Elapsed.TotalMilliseconds.ToString("F3", C) + " ms");
            CheckTrue("perf.cold_under_1s", sw2.Elapsed.TotalMilliseconds < 1000.0);
            Check("perf.same_answer", fresh.Solve(), net.Solve(), 1e-12);
        }
    }
}

