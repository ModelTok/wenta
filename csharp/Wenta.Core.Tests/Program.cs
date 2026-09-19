using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Wenta;

namespace Wenta.Core.Tests
{
    /// <summary>Console test runner: replays the frozen CSV parity vectors
    /// (generated from the Python/Mojo oracle before it was removed — see the
    /// `legacy-py-mojo-rust` tag) and the closed-form regression tests
    /// transcribed from the former venti unit tests, against Wenta.Core.
    /// No xUnit/NuGet — runs with the same bare-csc toolchain as Wenta.Core.
    /// Exit code 0 = all green.</summary>
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
            RunPerformance();

            Console.WriteLine();
            Console.WriteLine("==== " + _pass + " passed, " + _fail + " failed ====");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------

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
                string header = sr.ReadLine(); // skip
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

        // ------------------------------------------------------------------

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
                // arg is "<d>,<True|False>"; expected is c[3]
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

        // ---- solver networks (mirror of gen_vectors.py mirror_*_net) ----

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

        // ---- Catalog (open ζ-catalog JSON) — Phase-1 “new” module ----
        // Self-contained: parses the shipped example catalogue inline and
        // asserts documented behaviours (ById, size-window Match, ζ, and
        // the correlation fallback for types the catalogue does not carry).
        // The fallback values are the same Vettora.FittingsLibrary already
        // vector-tested in fittings.csv, so they stay independent of the
        // catalogue data itself.
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

            // ById
            ZetaCatalog.CatalogEntry vav = cat.ById("vav-box");
            Check("cat_byid_zeta", vav.Zeta, 0.35, 1e-12);
            Check("cat_byid_source",
                vav.Source == "generic VAV box fully open" ? 1.0 : 0.0, 1.0, 1e-12);
            Check("cat_byid_knr", vav.Knr.Length > 0 ? 1.0 : 0.0, 1.0, 1e-12);
            Check("cat_byid_missing", cat.ById("nope") == null ? 1.0 : 0.0, 1.0, 1e-12);

            // Match: first windowed entry wins for a size inside the window.
            Check("cat_match_rect", cat.Match("rect_elbow", new[]{ 400.0, 200.0 }).Zeta, 0.17, 1e-12);
            // Outside every window → no match.
            Check("cat_match_outside", cat.Match("rect_elbow", new[]{ 2000.0, 3000.0 }) == null ? 1.0 : 0.0, 1.0, 1e-12);
            // No size window → matches any size of that type.
            Check("cat_match_nowindow", cat.Match("round_elbow", new[]{ 315.0 }).Zeta, 0.24, 1e-12);

            // ZetaFor: catalogue wins over correlation when present.
            Check("cat_zeta_catalog", cat.ZetaFor("tee", new double[0]), 0.45, 1e-12);
            // ZetaFor fallback: type not in catalogue → correlation library.
            // GrilleReturn(0.15) = 0.2875 (see fittings.csv grl0.15).
            Check("cat_zeta_fallback", cat.ZetaFor("grille", new[]{ 300.0 }), 0.2875, 1e-12);
            // No entry and no correlation → error.
            ExpectError(true, "cat_zeta_unknown", delegate { cat.ZetaFor("nonsense", new double[0]); });
        }

        // ---- Bom (bill of materials + KNR rows) — Phase-1 “new” module ----
        // Mirrors the net_tee network from RunSolver and asserts the parts list:
        // one row per component, straight-duct length + π·D·L sheet area, and
        // the KNR codes. Expected geometry is computed independently from thin-air.
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

            // Straight-duct paths carry sheet area A = π·(D/2)²·L, in m².
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

            // Non-duct items carry zero length/area; source has no KNR code.
            Check("bom_source_len", FindBom(bom, "ahu").Length, 0.0, 1e-12);
            Check("bom_source_knr_empty", FindBom(bom, "ahu").KnrCode.Length == 0 ? 1.0 : 0.0, 1.0, 1e-12);
            Check("bom_terminal_kind", FindBom(bom, "t1").Kind == "terminal" ? 1.0 : 0.0, 1.0, 1e-12);
            Check("bom_fitting_kind", FindBom(bom, "tee").Kind == "fitting" ? 1.0 : 0.0, 1.0, 1e-12);

            double len = cs.Area * 20.0 + Math.PI * (0.2 / 2.0) * (0.2 / 2.0) * 5.0
                       + Math.PI * (0.125 / 2.0) * (0.125 / 2.0) * 3.0;
            Check("bom_total_length", bom.TotalLength, 28.0, 1e-12);
            Check("bom_total_area", bom.TotalArea, len, 1e-12);

            // CSV: header + 7 data rows (8 lines each terminated by \r\n).
            string csv = bom.ToCsv();
            int lines = 0;
            for (int i = 0; i < csv.Length; i++)
                if (csv[i] == '\n') lines++;
            Check("bom_csv_lines", lines == 8 ? 1.0 : 0.0, 1.0, 1e-12);
        }

        // ---- Balancing (damper ζ + setting) — Phase-4 “balancing” vertical ----
        // Mirrors venti/src/balancing.rs closed-form tests: required ζ from the
        // dynamic-pressure relation, the surplus-Δ logic, and the open-%
        // round-trip through the (already vector-tested) DamperButterfly.
        private static void RunBalancing()
        {
            // required_zeta: ζ = 2·dp/(ρ v²). ρ=1.204, v=4 → dynamic=9.632.
            Check("bal_zeta_closed", Balancing.RequiredZeta(9.632, 4.0, 1.204), 1.0, 1e-12);
            // The induced drop round-trips through LocalPressureDrop.
            Check("bal_roundtrip",
                Losses.LocalPressureDrop(Balancing.RequiredZeta(9.632, 4.0, 1.204), 4.0, 1.204),
                9.632, 1e-12);
            // Zero dynamic pressure guard.
            Check("bal_zero_dynamic", Balancing.RequiredZeta(5.0, 0.0, 1.204), 0.0, 1e-12);

            // balancing_zeta: short by 19.264 Pa at dynamic 9.632 → ζ = 2.
            Check("bal_delta", Balancing.BalancingZeta(30.0, 10.736, 4.0, 1.204), 2.0, 1e-12);
            // Branch already meets / exactly met → 0 (fully open).
            Check("bal_already_met", Balancing.BalancingZeta(10.0, 20.0, 4.0, 1.204), 0.0, 1e-12);
            Check("bal_exact", Balancing.BalancingZeta(10.0, 10.0, 4.0, 1.204), 0.0, 1e-12);
            // The added ζ reproduces the missing drop.
            double z = Balancing.BalancingZeta(30.0, 10.736, 4.0, 1.204);
            Check("bal_added_dp",
                Losses.LocalPressureDrop(z, 4.0, 1.204), 30.0 - 10.736, 1e-9);

            // damper_open_percentage round-trips through DamperButterfly.
            double[] roundTripZetas = new double[] { 0.1, 0.5, 1.0, 2.5, 5.0, 8.0, 10.0 };
            for (int i = 0; i < roundTripZetas.Length; i++)
            {
                double zeta = roundTripZetas[i];
                double open = Balancing.DamperOpenPercentage(zeta);
                double back = FittingsLibrary.DamperButterfly(open);
                Check("bal_open_rt" + i, back, zeta, 1e-9);
            }
            // Below the fully-open floor → fully open → still ζ = 0.1.
            Check("bal_floor", Balancing.DamperOpenPercentage(0.05), 100.0, 1e-12);
            Check("bal_floor_rt",
                FittingsLibrary.DamperButterfly(Balancing.DamperOpenPercentage(0.1)), 0.1, 1e-12);
        }

        // ---- Room ventilation balance — Phase-4 “balancing” family ----
        // Mirrors venti/src/room.rs closed-form tests: net sign, tolerance,
        // ACH, imbalance fraction, totals, and CSV rendering.
        private static void RunRoom()
        {
            // Net sign.
            Check("room_net_over", new RoomBalance(0.3, 0.1).NetM3s(), 0.2, 1e-12);
            Check("room_net_under", new RoomBalance(0.1, 0.3).NetM3s(), -0.2, 1e-12);
            Check("room_net_equal", new RoomBalance(0.2, 0.2).NetM3s(), 0.0, 1e-12);

            // is_balanced tolerance (monotonic).
            RoomBalance bal = new RoomBalance(0.5, 0.25); // net 0.25
            Check("room_tol_low", bal.IsBalanced(0.249) ? 1.0 : 0.0, 0.0, 1e-12);
            Check("room_tol_eq", bal.IsBalanced(0.25) ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_tol_hi", bal.IsBalanced(0.251) ? 1.0 : 0.0, 1.0, 1e-12);

            // ACH via Units: 0.1 m³/s in 100 m³ = 3.6.
            Check("room_ach", Units.AirChangesPerHour(0.1, 100.0), 3.6, 1e-12);
            Check("room_ach_zero_flow", Units.AirChangesPerHour(0.0, 100.0), 0.0, 1e-12);
            ExpectError(true, "room_ach_bad_volume", delegate { Units.AirChangesPerHour(0.1, 0.0); });

            // Validation.
            ExpectError(true, "room_neg_supply", delegate { new RoomBalance(-0.1, 0.2); });
            ExpectError(true, "room_neg_exhaust", delegate { new RoomBalance(0.1, -0.2); });
            new RoomBalance(0.0, 0.0); // ok, no assert
            Check("room_zero_ok", 1.0, 1.0, 1e-12);

            // Imbalance fraction.
            Check("room_imb_pure_supply", new RoomBalance(0.3, 0.0).ImbalanceFraction() ?? 0.0, 1.0, 1e-12);
            Check("room_imb_pure_exhaust", new RoomBalance(0.0, 0.3).ImbalanceFraction() ?? 0.0, -1.0, 1e-12);
            Check("room_imb_balanced", new RoomBalance(0.2, 0.2).ImbalanceFraction() ?? 99.0, 0.0, 1e-12);
            Check("room_imb_half", new RoomBalance(0.5, 0.25).ImbalanceFraction() ?? 0.0, 0.5, 1e-12);
            Check("room_imb_none", new RoomBalance(0.0, 0.0).ImbalanceFraction() == null ? 1.0 : 0.0, 1.0, 1e-12);

            // Totals across rooms (binary-fraction-exact).
            RoomBalanceSet set = new RoomBalanceSet();
            set.Add("bathroom", new RoomBalance(0.0625, 0.125));
            set.Add("living", new RoomBalance(0.25, 0.125));
            set.Add("kitchen", new RoomBalance(0.0625, 0.1875));
            Check("room_tot_supply", set.TotalSupplyM3s(), 0.375, 1e-12);
            Check("room_tot_exhaust", set.TotalExhaustM3s(), 0.4375, 1e-12);
            Check("room_tot_net", set.OverallNetM3s(), -0.0625, 1e-12);
            Check("room_tot_bal_eq", set.IsBalanced(0.0625) ? 1.0 : 0.0, 1.0, 1e-12);
            Check("room_tot_bal_low", set.IsBalanced(0.0624) ? 1.0 : 0.0, 0.0, 1e-12);

            // CSV content (compact format, trailing zeros trimmed).
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

        // ==================================================================
        // Regression coverage carried over from the Rust `#[cfg(test)]`
        // blocks of venti/src/*.rs (closed-form assertions, no CSV vectors).
        // ==================================================================

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

        // ---- ReCorrections (venti/src/re.rs) ----
        private static void RunReCorrections()
        {
            // re_correction_trend_and_clamp
            Check("re.corr_at_ref", ReCorrections.ReCorrection(ReCorrections.ReRef, 0.2), 1.0, 1e-12);
            CheckTrue("re.corr_higher_re_lt1", ReCorrections.ReCorrection(ReCorrections.ReRef * 10.0, 0.2) < 1.0);
            Check("re.corr_clamp_hi", ReCorrections.ReCorrection(ReCorrections.ReRef * 100.0, 1.5), 0.75, 1e-9);
            Check("re.corr_clamp_lo", ReCorrections.ReCorrection(ReCorrections.ReRef / 100.0, 1.5), 1.5, 1e-9);
            ExpectError(true, "re.corr_zero_re_err", delegate { ReCorrections.ReCorrection(0.0, 0.2); });

            // size_correction_trend_and_clamp
            Check("re.size_at_ref", ReCorrections.SizeCorrection(ReCorrections.DRefM, 0.15), 1.0, 1e-12);
            CheckTrue("re.size_smaller_gt1", ReCorrections.SizeCorrection(0.1, 0.15) > 1.0);
            Check("re.size_clamp", ReCorrections.SizeCorrection(0.01, 1.0), 1.3, 1e-9);
            ExpectError(true, "re.size_zero_d_err", delegate { ReCorrections.SizeCorrection(0.0, 0.1); });

            // corrected_elbow_within_bounds_and_monotonic_re
            // Rust elbow_round_loss(0.2, 0.2, 90, v, rho, mu) has no direct C# port; it is
            // elbow_round(0.2, 0.2, 90) = 0.21 (R/D = 1) x re_correction(Re, 0.2) x
            // size_correction(D, 0.15), i.e. CorrectedZeta(0.21, v, 0.2, 0.2, 0.15) under
            // standard air (rho = 1.204, mu = 1.825e-5 — the Rust test constants).
            Fluid air = Fluid.StandardAir();
            Check("re.std_air_rho", air.Density, 1.204, 1e-12);
            Check("re.std_air_mu", air.DynamicViscosity, 1.825e-5, 1e-12);
            double z = ReCorrections.CorrectedZeta(0.21, 4.0, 0.2, 0.2, 0.15, air);
            CheckTrue("re.elbow_within_bounds", z > 0.21 * 0.75 && z < 0.21 * 1.5);
            double reLo = ReCorrections.CorrectedZeta(0.21, 0.5, 0.2, 0.2, 0.15, air);
            double reHi = ReCorrections.CorrectedZeta(0.21, 15.0, 0.2, 0.2, 0.15, air);
            CheckTrue("re.elbow_monotonic_re", reHi <= reLo);

            // corrected_zeta_convenience
            double zc = ReCorrections.CorrectedZeta(0.3, 6.0, 0.3, 0.2, 0.15, air);
            CheckTrue("re.corrected_zeta_range", zc > 0.2 && zc < 0.5);
        }

        // ---- Standards (venti/src/standards.rs) ----
        private static void RunStandards()
        {
            // every_table_is_nonempty_and_sorted
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

            // en_table_shares_canonical_constants
            CheckTrue("standards.en_round_canonical",
                SameInts(Standards.StandardRoundSizes(Standard.En1505_1506), StandardSizes.RoundDuctSizes));
            CheckTrue("standards.en_rect_canonical",
                SamePairs(Standards.StandardRectangularSizes(Standard.En1505_1506), StandardSizes.RectangularDuctSizes));

            // nearest_round_up_and_closest
            CheckInt("standards.en_300_up", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, true), 300);
            CheckInt("standards.en_300_closest", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, false), 300);
            CheckInt("standards.en_120_up", Standards.NearestRoundSizeFor(Standard.En1505_1506, 120.0, true), 125);
            CheckInt("standards.en_110_closest", Standards.NearestRoundSizeFor(Standard.En1505_1506, 110.0, false), 100);
            CheckInt("standards.en_clamp_low", Standards.NearestRoundSizeFor(Standard.En1505_1506, 10.0, true), 63);
            CheckInt("standards.en_clamp_high", Standards.NearestRoundSizeFor(Standard.En1505_1506, 9999.0, true), 1250);

            // ashrae_vs_en_expected_diameters
            CheckInt("standards.ashrae_300", Standards.NearestRoundSizeFor(Standard.AsHrae, 300.0, true), 305);
            CheckInt("standards.en_300", Standards.NearestRoundSizeFor(Standard.En1505_1506, 300.0, true), 300);
            CheckInt("standards.ashrae_228", Standards.NearestRoundSizeFor(Standard.AsHrae, 228.0, true), 229);
            CheckInt("standards.en_228", Standards.NearestRoundSizeFor(Standard.En1505_1506, 228.0, true), 250);
            CheckInt("standards.ashrae_150", Standards.NearestRoundSizeFor(Standard.AsHrae, 150.0, true), 152);
            CheckInt("standards.en_150", Standards.NearestRoundSizeFor(Standard.En1505_1506, 150.0, true), 150);

            // din_round_renard_series
            CheckInt("standards.din_250", Standards.NearestRoundSizeFor(Standard.Din, 250.0, true), 250);
            CheckInt("standards.din_300_up", Standards.NearestRoundSizeFor(Standard.Din, 300.0, true), 315);
            CheckInt("standards.din_300_closest", Standards.NearestRoundSizeFor(Standard.Din, 300.0, false), 315);
        }

        // ---- Settings (venti/src/settings.rs; cli-gated JSON tests skipped) ----
        private static void RunSettings()
        {
            // defaults_are_valid
            ExpectOk("settings.defaults_valid", delegate { new ProjectSettings().Validate(); });

            // validate_rejects_non_positive_values
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

            // units_default_is_si
            CheckTrue("settings.units_default_si", new ProjectSettings().UnitSystem == UnitSystem.Si);
            CheckTrue("settings.units_default_not_ip", new ProjectSettings().UnitSystem != UnitSystem.Ip);

            // standard_default_is_en
            CheckTrue("settings.standard_default_en", new ProjectSettings().Standard == Standard.En1505_1506);

            // standard_round_trips
            Standard[] all = { Standard.En1505_1506, Standard.AsHrae, Standard.Din };
            foreach (Standard st in all)
            {
                ProjectSettings s = new ProjectSettings();
                s.Standard = st;
                CheckTrue("settings.standard_roundtrip_" + st, s.Standard == st);
            }
        }

        // ---- Results (venti/src/results.rs; cli-gated JSON tests skipped) ----
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
            // extract_one_row_per_component_with_types
            List<ComponentResult> res = Results.ExtractResults(SolvedChain());
            CheckInt("results.row_count", res.Count, 4);
            CheckStr("results.type_duct", FindResult(res, "duct").ComponentType, "RigidDuct");
            CheckStr("results.type_fit", FindResult(res, "fit").ComponentType, "TwoPortFitting");
            CheckStr("results.type_term", FindResult(res, "term").ComponentType, "Terminal");
            CheckStr("results.type_ahu", FindResult(res, "ahu").ComponentType, "Source");

            // terminal_flow_propagates_to_duct
            ComponentResult duct = FindResult(Results.ExtractResults(SolvedChain()), "duct");
            Check("results.duct_flow_in", duct.FlowrateIn ?? double.NaN, 0.1, 1e-12);
            CheckTrue("results.duct_velocity_positive", (duct.VelocityIn ?? -1.0) > 0.0);
            CheckTrue("results.duct_dp_positive", duct.PressureDrop > 0.0);

            // csv_has_header_and_rows
            string csv = Results.ResultsAsCsv(SolvedChain(), ',');
            string[] lines = csv.Split('\n');
            CheckInt("results.csv_lines", lines.Length, 5);
            CheckTrue("results.csv_header", lines[0].StartsWith("component_id,"));

            // summary_is_non_empty_table
            string summary = Results.ResultsSummary(SolvedChain());
            CheckTrue("results.summary_has_id", Contains(summary, "ID"));
            CheckTrue("results.summary_has_type", Contains(summary, "RigidDuct"));

            // unsolved_network_yields_no_flow
            var lone = new Network { Name = "chain" };
            lone.Add("term", new Terminal("term", 0.1, new Round(0.2), 1.0));
            List<ComponentResult> loneRes = Results.ExtractResults(lone);
            CheckInt("results.unsolved_rows", loneRes.Count, 1);
            CheckTrue("results.unsolved_terminal_seeded",
                loneRes[0].FlowrateIn.HasValue && loneRes[0].FlowrateIn.Value == 0.1);
        }

        // ---- Analysis (venti/src/analysis.rs) — Analyze() solves in place, so
        // every Rust #[test] gets its own freshly built network. ----
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

            // chain_analysis_has_duct_branch_with_velocity
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

            // regenerated_noise_in_plausible_range
            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckTrue("analysis.noise_present", s.Branches[0].RegeneratedNoiseDb.HasValue);
            double db = s.Branches[0].RegeneratedNoiseDb ?? -1.0;
            CheckTrue("analysis.noise_plausible", db > 0.0 && db < 90.0);

            // balancing_zeta_non_negative
            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            bool allNonNeg = true;
            foreach (BranchInfo b in s.Branches)
                if (b.BalancingZeta.HasValue && b.BalancingZeta.Value < 0.0) allNonNeg = false;
            CheckTrue("analysis.balancing_zeta_non_negative", allNonNeg);

            // n_branches_counts_ducts_only
            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckInt("analysis.n_branches_ducts_only", s.NBranches, 1);
            CheckInt("analysis.branches_len_matches", s.Branches.Count, s.NBranches);
            CheckStr("analysis.branch_kind", s.Branches[0].Kind, "RigidDuct");

            // critical_dp_is_positive
            s = Analysis.Analyze(BuildChain(0.1, 10.0), air);
            CheckTrue("analysis.critical_dp_positive", s.CriticalDpPa > 0.0);

            // flex_duct_branch_reports_round_diameter
            s = Analysis.Analyze(BuildFlexChain(0.1, 10.0), air);
            CheckInt("analysis.flex_n_branches", s.NBranches, 1);
            CheckStr("analysis.flex_kind", s.Branches[0].Kind, "FlexDuct");
            CheckTrue("analysis.flex_velocity_positive", s.Branches[0].VelocityMs > 0.0);
            CheckTrue("analysis.flex_noise_some", s.Branches[0].RegeneratedNoiseDb.HasValue);
            CheckTrue("analysis.flex_zeta_some", s.Branches[0].BalancingZeta.HasValue);

            // solve_error_propagates_on_cyclic_network
            Round r = new Round(0.2);
            var cyc = new Network { Name = "cyclic" };
            cyc.Add("s", new Source("AHU"));
            cyc.Add("d0", new RigidDuct("d0", r, 5.0, 0.0001));
            cyc.Add("d1", new RigidDuct("d1", r, 5.0, 0.0001));
            cyc.Add("t", new Terminal("t", 0.1, r, 1.0));
            cyc.Connect("s", "d0");
            cyc.Connect("d0", "d1");
            cyc.Connect("d1", "t");
            cyc.Connect("d1", "d0"); // cycle
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

        // ---- Marking (venti/src/marking.rs) ----
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

            // simple_chain_gives_consecutive_branch_numbers
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

            // tee_split_gives_distinct_downstream_branch_numbers
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

            // size_mm_is_some_for_ducts
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

            // flow_is_some_after_solve_else_none
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

            // marks_as_csv_has_header_and_content
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

            // empty_network_returns_empty_marks
            List<Mark> empty = Marking.AssignBranchMarks(new Network { Name = "empty" });
            CheckInt("marking.empty_count", empty.Count, 0);
            CheckStr("marking.empty_csv", Marking.MarksAsCsv(empty), "branch_no,component_id,kind,size_mm,flow_m3s");
        }

        // ---- Fabrication (venti/src/fabrication.rs) ----
        private static void RunFabrication()
        {
            // round_surface_area_is_pi_d_times_len
            Check("fabrication.round_area", Fabrication.DuctSurfaceAreaM2(new Round(0.2), 10.0), Math.PI * 0.2 * 10.0, 1e-12);
            // rectangular_surface_area_is_twice_w_plus_h_times_len
            Check("fabrication.rect_area", Fabrication.DuctSurfaceAreaM2(new Rectangular(0.3, 0.2), 5.0), 2.0 * (0.3 + 0.2) * 5.0, 1e-12);
            // zero_length_surface_area_is_zero
            CheckTrue("fabrication.zero_length_area", Fabrication.DuctSurfaceAreaM2(new Round(0.2), 0.0) == 0.0);
            // surface_area_rejects_negative_length
            ExpectError(true, "fabrication.negative_length_err", delegate { Fabrication.DuctSurfaceAreaM2(new Round(0.2), -1.0); });

            // weight_formula_and_default_density
            Check("fabrication.weight_default_density", Fabrication.DuctWeightKg(1.0, 1.0, null), 7.85, 1e-9);
            double w = Fabrication.DuctWeightKg(2.0, 0.5, 8000.0);
            Check("fabrication.weight_custom_formula", w, 2.0 * 0.0005 * 8000.0, 1e-9);
            Check("fabrication.weight_custom_value", w, 8.0, 1e-9);

            // weight_requires_gauge
            ExpectError(true, "fabrication.weight_no_gauge", delegate { Fabrication.DuctWeightKg(1.0, null, null); });
            ExpectError(true, "fabrication.weight_zero_gauge", delegate { Fabrication.DuctWeightKg(1.0, 0.0, null); });
            ExpectError(true, "fabrication.weight_negative_area", delegate { Fabrication.DuctWeightKg(-1.0, 1.0, null); });
            ExpectError(true, "fabrication.weight_zero_density", delegate { Fabrication.DuctWeightKg(1.0, 1.0, 0.0); });

            // breakout_totals
            Fabrication.FabricationBreakout b = new Fabrication.FabricationBreakout(20.0, 3.5);
            CheckTrue("fabrication.breakout_straight", b.StraightM == 20.0);
            CheckTrue("fabrication.breakout_fittings", b.FittingsM == 3.5);
            Check("fabrication.breakout_total", b.TotalM(), 23.5, 1e-12);

            // breakout_rejects_negative
            ExpectError(true, "fabrication.breakout_neg_straight", delegate { new Fabrication.FabricationBreakout(-1.0, 2.0); });
            ExpectError(true, "fabrication.breakout_neg_fittings", delegate { new Fabrication.FabricationBreakout(1.0, -2.0); });
            ExpectError(true, "fabrication.breakout_neg_both", delegate { new Fabrication.FabricationBreakout(-1.0, -2.0); });
            ExpectOk("fabrication.breakout_zero_ok", delegate { new Fabrication.FabricationBreakout(0.0, 0.0); });

            // cutting_schedule_sums_and_sorts
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

            // cutting_schedule_empty_input
            CheckInt("fabrication.schedule_empty", Fabrication.CuttingSchedule(new List<KeyValuePair<string, double>>()).Count, 0);
        }

        // ---- Development (venti/src/development.rs) ----
        private static void RunDevelopment()
        {
            // duct_circumference_area_exact
            FlatPiece p = Development.RoundDuctDevelopment(0.25, 4.0);
            double circ = Math.PI * 0.25;
            Check("development.duct_width", p.WidthM, circ, 1e-12);
            CheckTrue("development.duct_length", p.LengthM == 4.0);
            Check("development.duct_area", p.AreaM2, circ * 4.0, 1e-12);

            // elbow_area_is_width_times_arclength
            p = Development.RoundElbowDevelopment(0.3, 0.2, 45.0, 5);
            double arc = 45.0 * Math.PI / 180.0 * 0.3;
            double width = Math.PI * 0.2;
            Check("development.elbow_length", p.LengthM, arc, 1e-12);
            Check("development.elbow_area", p.AreaM2, width * arc, 1e-12);

            // reducer_trrapezoid_area
            p = Development.ReducerConeDevelopment(0.1, 0.3, 0.5);
            double slant = Math.Sqrt(0.5 * 0.5 + Math.Pow((0.3 - 0.1) / 2.0, 2));
            double rw = Math.PI * (0.1 + 0.3) / 2.0;
            Check("development.reducer_length", p.LengthM, slant, 1e-12);
            Check("development.reducer_width", p.WidthM, rw, 1e-12);
            Check("development.reducer_area", p.AreaM2, rw * slant, 1e-12);

            // angle_180_is_full_turn_half_circumference
            p = Development.RoundElbowDevelopment(0.3, 0.2, 180.0, 7);
            Check("development.elbow180_length", p.LengthM, Math.PI * 0.3, 1e-12);
            Check("development.elbow180_area", p.AreaM2, Math.PI * 0.2 * Math.PI * 0.3, 1e-12);

            // segment_count_does_not_change_area
            FlatPiece a = Development.RoundElbowDevelopment(0.3, 0.2, 90.0, 3);
            FlatPiece bb = Development.RoundElbowDevelopment(0.3, 0.2, 90.0, 12);
            CheckTrue("development.segments_same_area", a.AreaM2 == bb.AreaM2);

            // straight_reducer_is_rectangle
            p = Development.ReducerConeDevelopment(0.2, 0.2, 1.0);
            Check("development.straight_reducer_length", p.LengthM, 1.0, 1e-12);
            Check("development.straight_reducer_width", p.WidthM, Math.PI * 0.2, 1e-12);

            // rejects_non_positive_inputs
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

        // ---- Clash (venti/src/clash.rs) ----
        private static DuctSegment Seg(string id, double x0, double y0, double x1, double y1, double d)
        {
            return new DuctSegment(id, new Point2(x0, y0), new Point2(x1, y1), d);
        }

        private static void RunClash()
        {
            // crossing_segments_clash
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

            // far_parallel_segments_do_not_clash
            CheckInt("clash.far_parallel", ClashDetection.ClashCount(ClashDetection.FindClashes(new List<DuctSegment>
            {
                Seg("x", 0.0, 0.0, 10.0, 0.0, 0.1),
                Seg("y", 0.0, 5.0, 10.0, 5.0, 0.1),
            }, 0.0)), 0);

            // clearance_margin_shifts_threshold
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

            // identical_and_overlapping_segments_clash
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

            // collinear_overlap
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

            // clashes_as_csv_content
            var manual = new List<Clash> { new Clash("A", "B", 0.0), new Clash("C", "D", 0.05) };
            CheckStr("clash.csv_rows", ClashDetection.ClashesAsCsv(manual), "a,b,distance_m\nA,B,0\nC,D,0.05\n");
            CheckStr("clash.csv_empty", ClashDetection.ClashesAsCsv(new List<Clash>()), "a,b,distance_m\n");

            // negative_clearance_is_rejected
            ExpectError(true, "clash.negative_clearance_err", delegate
            {
                ClashDetection.FindClashes(new List<DuctSegment> { Seg("a", 0.0, 0.0, 1.0, 0.0, 0.1) }, -1.0);
            });

            // degenerate_zero_length_segment
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

        // ---- Topology (venti/src/topology.rs) ----
        private static Polyline Pl(params double[] xy)
        {
            var pts = new List<Point2>();
            for (int i = 0; i + 1 < xy.Length; i += 2)
                pts.Add(new Point2(xy[i], xy[i + 1]));
            return new Polyline(pts);
        }

        private static void RunTopology()
        {
            // single_straight_run
            TracedSystem sys = Topology.Trace(new List<Polyline> { Pl(0.0, 0.0, 5.0, 0.0) }, new TraceOptions());
            CheckInt("topology.single_chains", sys.Chains.Count, 1);
            Check("topology.single_length", sys.TotalLengthM(), 5.0, 1e-9);
            CheckInt("topology.single_network_len", sys.Network.Components.Count, 3);
            CheckInt("topology.single_flatten_len", sys.Flatten().Count, 1);
            Check("topology.single_default_diameter", sys.Flatten()[0].Diameter, 0.2, 1e-12);

            // two_collinear_polylines_make_one_chain
            sys = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 0.0, 1.0, 0.0),
                Pl(1.0, 0.0, 3.0, 0.0),
            }, new TraceOptions());
            CheckInt("topology.collinear_chains", sys.Chains.Count, 1);
            Check("topology.collinear_length", sys.TotalLengthM(), 3.0, 1e-9);

            // tee_split_creates_three_ducts_and_two_terminals
            var teeOpts = new TraceOptions();
            teeOpts.Diameters["duct1"] = 0.3;
            teeOpts.Flows["term0"] = 0.06;
            teeOpts.Flows["term1"] = 0.04;
            sys = Topology.Trace(new List<Polyline>
            {
                Pl(0.0, 1.0, 1.0, 1.0, 2.0, 1.0), // trunk, tee at (2,1)
                Pl(2.0, 1.0, 3.0, 1.0),           // branch to term0
                Pl(2.0, 1.0, 2.0, 0.0),           // branch to term1
            }, teeOpts);
            CheckInt("topology.tee_chains", sys.Chains.Count, 3);
            CheckInt("topology.tee_network_len", sys.Network.Components.Count, 7);
            CheckTrue("topology.tee_total_length", sys.TotalLengthM() == 4.0);
            double dp = sys.Network.Solve(new Fluid(1.204, 1.825e-5));
            CheckTrue("topology.tee_solve_dp_positive", dp > 0.0);
            CheckInt("topology.tee_flatten_len", sys.Flatten().Count, 3);

            // rejects_degree4_junction
            ExpectError(true, "topology.rejects_degree4", delegate
            {
                Topology.Trace(new List<Polyline>
                {
                    Pl(-1.0, 0.0, 0.0, 0.0, 1.0, 0.0),
                    Pl(0.0, -1.0, 0.0, 0.0, 0.0, 1.0),
                }, new TraceOptions());
            });

            // assigns_terminal_flowrates
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

        // ---- Fan (venti/src/fan.rs) ----
        private static FanPoint FP(double flow, double pressure)
        {
            return new FanPoint(flow, pressure);
        }

        /// <summary>(0, 300) -> (0.1, 250) -> (0.25, 100) -> (0.4, 0) Pa.</summary>
        private static FanCurve SampleCurve()
        {
            return new FanCurve("sample", new[] { FP(0.0, 300.0), FP(0.1, 250.0), FP(0.25, 100.0), FP(0.4, 0.0) });
        }

        private static void RunFan()
        {
            FanCurve fan = SampleCurve();

            // interpolation_is_exact_at_knots
            for (int i = 0; i < fan.Points.Length; i++)
                Check("fan.knot" + i, fan.StaticPressureAt(fan.Points[i].FlowM3s), fan.Points[i].StaticPressurePa, 1e-12);

            // interpolation_is_linear_at_midpoints
            Check("fan.mid_seg1", fan.StaticPressureAt(0.05), 275.0, 1e-9);
            Check("fan.quarter_seg2", fan.StaticPressureAt(0.1375), 212.5, 1e-9);
            Check("fan.mid_seg3", fan.StaticPressureAt(0.325), 50.0, 1e-9);

            // interpolation_outside_range_is_error
            ExpectError(true, "fan.below_range_err", delegate { fan.StaticPressureAt(-0.01); });
            ExpectError(true, "fan.above_range_err", delegate { fan.StaticPressureAt(0.41); });
            ExpectError(true, "fan.nan_err", delegate { fan.StaticPressureAt(double.NaN); });
            ExpectOk("fan.lower_boundary_ok", delegate { fan.StaticPressureAt(0.0); });
            ExpectOk("fan.upper_boundary_ok", delegate { fan.StaticPressureAt(0.4); });

            // fan_power_closed_form
            Check("fan.power_basic", Fan.Power(1.5, 800.0, 0.4), 3000.0, 1e-9);
            Check("fan.power_zero_flow", Fan.Power(0.0, 800.0, 0.4), 0.0, 1e-12);
            Check("fan.power_unit_efficiency", Fan.Power(1.5, 800.0, 1.0), 1200.0, 1e-9);

            // fan_power_rejects_invalid_inputs
            ExpectError(true, "fan.power_eta_zero", delegate { Fan.Power(1.0, 100.0, 0.0); });
            ExpectError(true, "fan.power_eta_negative", delegate { Fan.Power(1.0, 100.0, -0.1); });
            ExpectError(true, "fan.power_eta_gt1", delegate { Fan.Power(1.0, 100.0, 1.5); });
            ExpectError(true, "fan.power_negative_flow", delegate { Fan.Power(-1.0, 100.0, 0.5); });
            ExpectError(true, "fan.power_negative_pressure", delegate { Fan.Power(1.0, -100.0, 0.5); });

            // pick_fan_selects_first_adequate_fan
            FanCurve weak = new FanCurve("weak", new[] { FP(0.0, 300.0), FP(0.5, 150.0) });
            FanCurve strong = new FanCurve("strong", new[] { FP(0.0, 500.0), FP(0.5, 400.0) });
            FanCurve mid = new FanCurve("mid", new[] { FP(0.0, 450.0), FP(0.5, 300.0) });
            FanCurve[] curves = { weak, mid, strong };
            CheckTrue("fan.pick_250", Fan.PickFan(curves, 0.3, 250.0) == 1);
            CheckTrue("fan.pick_200", Fan.PickFan(curves, 0.3, 200.0) == 0);
            CheckTrue("fan.pick_exact_330", Fan.PickFan(curves, 0.3, 330.0) == 1);

            // pick_fan_returns_none_when_nothing_meets_duty
            FanCurve small = new FanCurve("small", new[] { FP(0.0, 200.0), FP(0.3, 100.0) });
            CheckTrue("fan.pick_none_too_high", Fan.PickFan(new[] { small }, 0.15, 5000.0) == null);
            CheckTrue("fan.pick_none_empty", Fan.PickFan(new FanCurve[0], 0.15, 100.0) == null);

            // pick_fan_skips_fans_outside_their_curve_range
            FanCurve narrow = new FanCurve("narrow", new[] { FP(0.0, 400.0), FP(0.2, 100.0) });
            FanCurve wide = new FanCurve("wide", new[] { FP(0.0, 400.0), FP(0.6, 250.0) });
            CheckTrue("fan.pick_skips_out_of_range", Fan.PickFan(new[] { narrow, wide }, 0.4, 300.0) == 1);

            // margin_sign_and_undefined_range
            Check("fan.curve_at_0_3", fan.StaticPressureAt(0.3), 200.0 / 3.0, 1e-9);
            Check("fan.margin_positive", Fan.Margin(fan, 0.3, 40.0) ?? double.NaN, 26.66666666, 1e-7);
            CheckTrue("fan.margin_negative", (Fan.Margin(fan, 0.3, 100.0) ?? 1.0) < 0.0);
            CheckTrue("fan.margin_zero", Math.Abs(Fan.Margin(fan, 0.3, 200.0 / 3.0) ?? 1.0) < 1e-9);
            CheckTrue("fan.margin_undefined_out_of_range", Fan.Margin(fan, 0.9, 40.0) == null);
            ExpectError(true, "fan.margin_negative_flow_err", delegate { Fan.Margin(fan, -0.1, 40.0); });
            ExpectError(true, "fan.margin_negative_pressure_err", delegate { Fan.Margin(fan, 0.3, -1.0); });

            // fan_curve_new_validation
            ExpectError(true, "fan.curve_one_point", delegate { new FanCurve("one", new[] { FP(0.1, 100.0) }); });
            ExpectError(true, "fan.curve_equal_flows", delegate { new FanCurve("dup", new[] { FP(0.1, 100.0), FP(0.1, 90.0) }); });
            ExpectError(true, "fan.curve_decreasing_flow", delegate { new FanCurve("down", new[] { FP(0.2, 100.0), FP(0.1, 90.0) }); });
            ExpectError(true, "fan.curve_nan", delegate { new FanCurve("nan", new[] { FP(0.1, double.NaN), FP(0.2, 90.0) }); });
            ExpectError(true, "fan.curve_inf", delegate { new FanCurve("inf", new[] { FP(double.PositiveInfinity, 100.0), FP(0.2, 90.0) }); });
            ExpectOk("fan.curve_valid", delegate { new FanCurve("ok", new[] { FP(0.0, 0.0), FP(0.5, 100.0) }); });
        }

        // ---- Insulation (venti/src/insulation.rs) ----
        private static void RunInsulation()
        {
            const double MW = 0.035, D = 0.2, HI = 10.0, HE = 8.0;

            // no_insulation_when_no_condensation_risk
            CheckTrue("insulation.no_risk_zero",
                Insulation.RequiredThicknessCondensation(40.0, 5.0, 15.0, MW, D, HI, HE) == 0.0);

            // condensation_thickness_grows_with_humidity
            double dry = Insulation.RequiredThicknessCondensation(8.0, 12.0, 24.0, MW, D, HI, HE);
            double humid = Insulation.RequiredThicknessCondensation(8.0, 17.0, 24.0, MW, D, HI, HE);
            CheckTrue("insulation.humid_ge_dry", humid >= dry);

            // condensation_meets_criterion — surface_temp is private in both ports;
            // T_s = T_amb + q * R_out with q from the public HeatLossWithInsulation.
            double t = Insulation.RequiredThicknessCondensation(8.0, 15.8, 24.0, MW, D, HI, HE);
            double q = Insulation.HeatLossWithInsulation(8.0, 24.0, MW, D, t, HI, HE);
            double rOut = 1.0 / (HE * Math.PI * (D + 2.0 * t));
            double ts = 24.0 + q * rOut;
            CheckTrue("insulation.cond_surface_above_dew", ts >= 15.8 - 1e-6);
            CheckTrue("insulation.cond_thickness_range", t > 0.0 && t < 0.1);

            // heat_loss_thickness_monotonic_with_target
            double strict = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 10.0, MW, D, HI, HE);
            double loose = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 50.0, MW, D, HI, HE);
            CheckTrue("insulation.strict_ge_loose", strict >= loose);
            ExpectError(true, "insulation.zero_target_err", delegate
            {
                Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 0.0, MW, D, HI, HE);
            });

            // heat_loss_meets_target
            double t25 = Insulation.RequiredThicknessHeatLoss(60.0, 20.0, 25.0, MW, D, HI, HE);
            double q25 = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, t25, HI, HE));
            CheckTrue("insulation.heat_loss_meets_target", q25 <= 25.0 + 1e-9);

            // heat_loss_with_insulation_decreases_with_thickness
            double thin = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, 0.02, HI, HE));
            double thick = Math.Abs(Insulation.HeatLossWithInsulation(60.0, 20.0, MW, D, 0.06, HI, HE));
            CheckTrue("insulation.thick_lt_thin", thick < thin);

            // select_thickness_rounds_up
            CheckTrue("insulation.select_20", Insulation.SelectThickness(0.020) == 0.02);
            CheckTrue("insulation.select_45", Insulation.SelectThickness(0.045) == 0.05);
            CheckTrue("insulation.select_61", Insulation.SelectThickness(0.061) == 0.08);
            CheckTrue("insulation.select_1mm", Insulation.SelectThickness(0.001) == 0.02);
            ExpectError(true, "insulation.select_above_max_err", delegate { Insulation.SelectThickness(0.200); });

            // material_lookup
            double cond;
            CheckTrue("insulation.material_mineral_wool",
                Insulation.MaterialConductivity("mineral_wool", out cond) && cond == 0.035);
            CheckTrue("insulation.material_pir",
                Insulation.MaterialConductivity("PIR", out cond) && cond == 0.024);
            CheckTrue("insulation.material_bogus", !Insulation.MaterialConductivity("bogus", out cond));
            CheckInt("insulation.materials_count", Insulation.Materials.Length, 5);

            // validation
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

        // ---- Sound (venti/src/sound.rs) ----
        private static void RunSound()
        {
            // regenerated_noise_grows_with_velocity
            double lb = Sound.RegeneratedNoiseRound(2.0, 0.2);
            double hi = Sound.RegeneratedNoiseRound(6.0, 0.2);
            CheckTrue("sound.faster_is_louder", hi > lb);
            Check("sound.v6_scaling", hi - lb, 60.0 * Math.Log10(3.0), 1e-9);

            // regenerated_noise_falls_with_diameter
            double small = Sound.RegeneratedNoiseRound(4.0, 0.1);
            double large = Sound.RegeneratedNoiseRound(4.0, 0.5);
            CheckTrue("sound.bigger_is_quieter", large < small);
            Check("sound.d2_scaling", large - small, -20.0 * Math.Log10(5.0), 1e-9);

            // regenerated_noise_closed_form
            double lw = Sound.RegeneratedNoiseRound(2.0, 0.5);
            Check("sound.closed_form", lw, 10.0 + 60.0 * Math.Log10(2.0) - 20.0 * Math.Log10(0.5), 1e-9);

            // regenerated_noise_higher_density_is_louder
            double thin = Sound.RegeneratedNoiseRound(4.0, 0.2, 1.0);
            double dense = Sound.RegeneratedNoiseRound(4.0, 0.2, 1.5);
            CheckTrue("sound.denser_is_louder", dense > thin);
            Check("sound.density_scaling", dense - thin, 10.0 * Math.Log10(1.5 / 1.0), 1e-9);

            // regenerated_noise_rejects_bad_inputs
            ExpectError(true, "sound.regen_zero_velocity", delegate { Sound.RegeneratedNoiseRound(0.0, 0.2); });
            ExpectError(true, "sound.regen_negative_velocity", delegate { Sound.RegeneratedNoiseRound(-3.0, 0.2); });
            ExpectError(true, "sound.regen_zero_diameter", delegate { Sound.RegeneratedNoiseRound(4.0, 0.0); });
            ExpectError(true, "sound.regen_negative_density", delegate { Sound.RegeneratedNoiseRound(4.0, 0.2, -1.0); });

            // duct_pressure_level_closed_form
            double lp = Sound.DuctPressureLevel(60.0, 100.0, 0.2);
            Check("sound.room_eq_closed_form", lp, 60.0 + 10.0 * Math.Log10(4.0 * (1.0 - 0.2) / (0.2 * 100.0)), 1e-9);

            // duct_pressure_level_more_absorption_is_quieter
            CheckTrue("sound.absorption_quieter",
                Sound.DuctPressureLevel(60.0, 100.0, 0.45) < Sound.DuctPressureLevel(60.0, 100.0, 0.05));

            // duct_pressure_level_rejects_bad_inputs
            ExpectError(true, "sound.room_zero_area", delegate { Sound.DuctPressureLevel(60.0, 0.0, 0.2); });
            ExpectError(true, "sound.room_zero_alpha", delegate { Sound.DuctPressureLevel(60.0, 100.0, 0.0); });
            ExpectError(true, "sound.room_unit_alpha", delegate { Sound.DuctPressureLevel(60.0, 100.0, 1.0); });
            ExpectError(true, "sound.room_negative_area", delegate { Sound.DuctPressureLevel(60.0, -5.0, 0.2); });

            // nc_ok_boundary
            CheckTrue("sound.nc_office_35", Sound.NcOk("office", 35.0));
            CheckTrue("sound.nc_office_34_9", Sound.NcOk("office", 34.9));
            CheckTrue("sound.nc_office_35_1", !Sound.NcOk("office", 35.1));
            CheckTrue("sound.nc_bedroom_26", !Sound.NcOk("bedroom", 26.0));

            // nc_ok_lookup_and_target (nc_limit is private in C#; the public
            // NoiseLimitsNc table is the same lookup)
            CheckTrue("sound.nc_limit_studio", Sound.NoiseLimitsNc["studio"] == 25.0);
            ExpectError(true, "sound.nc_bogus_space", delegate { Sound.NcOk("bogus", 10.0); });
            CheckTrue("sound.nc_target_eq", Sound.NcOkTarget(35.0, 35.0) == Sound.NcOk("office", 35.0));
            CheckTrue("sound.nc_target_under", Sound.NcOkTarget(35.0, 34.0) == Sound.NcOk("office", 34.0));
            CheckTrue("sound.nc_target_over", Sound.NcOkTarget(35.0, 36.0) == Sound.NcOk("office", 36.0));
        }

        // ---- Electrical (venti/src/electrical.rs) ----
        private static ElectricalData Pump()
        {
            return new ElectricalData("P-01", "centrifugal pump", 1500.0);
        }

        private static void RunElectrical()
        {
            // current_computation_from_voltage_and_power_factor
            ElectricalData p = Pump();
            p.VoltageV = 230.0;
            p.PowerFactor = 0.9;
            double expected = 1500.0 / (230.0 * 0.9);
            Check("electrical.current", p.Current() ?? double.NaN, expected, 1e-9);
            Check("electrical.current_stored", p.CurrentA ?? double.NaN, expected, 1e-9);

            // power_kw_conversion
            Check("electrical.power_kw", Pump().PowerKw(), 1.5, 1e-12);
            Check("electrical.power_kw_big", new ElectricalData("H-01", "heater", 12000.0).PowerKw(), 12.0, 1e-12);

            // schedule_totals_power
            var s = new ElectricalSchedule();
            s.Add(new ElectricalData("F-01", "supply fan", 5500.0));
            s.Add(new ElectricalData("P-01", "circulation pump", 1500.0));
            s.Add(new ElectricalData("H-01", "electric heater", 2000.0));
            Check("electrical.total_power", s.TotalPowerW(), 9000.0, 1e-9);

            // schedule_totals_current_when_all_present
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

            // missing_voltage_or_power_factor_gives_none
            p = Pump();
            CheckTrue("electrical.current_none_no_data", p.Current() == null);
            CheckTrue("electrical.current_a_none_no_data", p.CurrentA == null);
            p.PowerFactor = 0.9;
            CheckTrue("electrical.current_none_pf_only", p.Current() == null);
            ElectricalData m = new ElectricalData("M-01", "motor", 1000.0);
            m.VoltageV = 230.0;
            CheckTrue("electrical.current_none_voltage_only", m.Current() == null);

            // total_current_is_none_when_any_entry_is_missing_data
            s = new ElectricalSchedule();
            fan = new ElectricalData("F-01", "supply fan", 5500.0);
            fan.VoltageV = 400.0;
            fan.PowerFactor = 0.85;
            s.Add(fan);
            s.Add(new ElectricalData("H-01", "electric heater", 2000.0));
            CheckTrue("electrical.total_current_none_when_missing", s.TotalCurrentA() == null);

            // csv_header_and_rows
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

            // rejects_negative_and_non_finite_power
            ExpectError(true, "electrical.negative_power_err", delegate { new ElectricalData("X-01", "heater", -5.0); });
            ExpectError(true, "electrical.nan_power_err", delegate { new ElectricalData("X-02", "heater", double.NaN); });
            ExpectError(true, "electrical.inf_power_err", delegate { new ElectricalData("X-03", "heater", double.PositiveInfinity); });
            ExpectOk("electrical.zero_power_ok", delegate { new ElectricalData("X-04", "heater", 0.0); });

            // add_and_len
            s = new ElectricalSchedule();
            CheckInt("electrical.len_empty", s.Len(), 0);
            CheckTrue("electrical.is_empty", s.IsEmpty());
            s.Add(new ElectricalData("A-01", "AHU", 2000.0));
            s.Add(new ElectricalData("A-02", "AHU", 3000.0));
            CheckInt("electrical.len_two", s.Len(), 2);
            CheckTrue("electrical.not_empty", !s.IsEmpty());
            CheckInt("electrical.iter_count", new List<ElectricalData>(s.Iter()).Count, 2);
        }

        // ---- Solver idempotence: a second Solve must not accumulate flow ----
        private static void RunResolve()
        {
            Network tee = TeeNetwork();
            double dp1 = tee.Solve();
            double flow1 = tee.Components["duct"].Port_("inlet").Flowrate ?? 0.0;
            double dp2 = tee.Solve();
            Check("resolve.dp_identical", dp2, dp1, 0.0);
            Check("resolve.flow_identical", tee.Components["duct"].Port_("inlet").Flowrate ?? 0.0, flow1, 0.0);
            Check("resolve.flow_is_demand_sum", flow1, 0.1, 1e-12);

            // Analysis on an already-solved network equals analysis on a fresh one.
            Fluid air = Fluid.StandardAir();
            AnalysisSummary fresh = Analysis.Analyze(TeeNetwork(), air);
            AnalysisSummary again = Analysis.Analyze(tee, air);
            Check("resolve.analysis_critical_dp", again.CriticalDpPa, fresh.CriticalDpPa, 0.0);
            CheckInt("resolve.analysis_branches", again.NBranches, fresh.NBranches);
            for (int i = 0; i < fresh.Branches.Count; i++)
                Check("resolve.analysis_flow_" + fresh.Branches[i].ComponentId,
                    again.Branches[i].FlowM3s, fresh.Branches[i].FlowM3s, 0.0);
        }

        // ---- NetworkJson (issue #46) — versioned JSON round-trip of a Network ----
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
            // Round trip of the two parity networks: identical topology and
            // bit-identical critical path after serialize -> parse -> solve.
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

            // Malformed input is rejected, never silently ignored.
            ExpectError(true, "json.err_no_components", () => NetworkJson.Parse("{\"schema_version\":1,\"name\":\"x\"}"));
            ExpectError(true, "json.err_unknown_class", () => NetworkJson.Parse(
                "{\"schema_version\":1,\"components\":[{\"id\":\"a\",\"wenta_class\":\"Nozzle\"}],\"connections\":[]}"));
            ExpectError(true, "json.err_schema_version", () => NetworkJson.Parse(
                "{\"schema_version\":2,\"components\":[],\"connections\":[]}"));
            ExpectError(true, "json.err_not_json", () => NetworkJson.Parse("not json"));
        }

        // ---- Catalog merge + shipped example catalogs (issue #53) ----
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
            // Overrides replace in place; untouched ids keep the base value; vendor-only ids are appended.
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

        // ---- KNR estimate-code mapping, configurable per edition (issue #54) ----
        // The default map must reproduce the pre-#54 codes byte for byte; a file
        // swaps them without a rebuild; anything the file does not cover is
        // reported in Unmapped, never invented.
        private static void RunKnrMap()
        {
            // Same tee network as RunBom (round duct, tee, round d2, flex, 2 terminals, source).
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

            // Default map == the legacy Bom.KnrMap dictionary; no overrides; nothing unmapped.
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

            // Shipped example file: codes come from the file, overrides pick by shape/type.
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogs", "knr-example.json");
            KnrMap file = KnrMap.Load(path);
            CheckStr("knr.file_edition", file.Edition, "KNR 2-08 example (configure per your edition)");
            CheckInt("knr.file_version", file.Version, 1);
            CheckInt("knr.file_codes", file.Codes.Count, 5);        // "_comment" key skipped
            CheckInt("knr.file_overrides", file.Overrides.Count, 3);
            Bom fromFile = Bom.Build(net, file);
            CheckStr("knr.file_duct_round", FindBom(fromFile, "duct").KnrCode, "2-08 01xx-A (placeholder: round sheet-metal duct)");
            CheckStr("knr.file_tee_override", FindBom(fromFile, "tee").KnrCode, "2-08 03xx-T (placeholder: tee / branch piece)");
            CheckStr("knr.file_flex", FindBom(fromFile, "flex").KnrCode, "2-08 02xx-A (placeholder: flexible duct)");
            CheckStr("knr.file_terminal", FindBom(fromFile, "t1").KnrCode, "2-08 04xx-A (placeholder: air terminal)");
            CheckStr("knr.file_source_empty", FindBom(fromFile, "ahu").KnrCode, "");
            CheckInt("knr.file_unmapped", fromFile.Unmapped.Count, 0);
            CheckTrue("knr.file_csv_differs", fromFile.ToCsv() != legacy.ToCsv());

            // Rectangular duct + rectangular in-line fitting hit the shape overrides;
            // a round in-line fitting falls through to codes["fitting"].
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

            // Missing kinds: empty code in the row + reported once per distinct query.
            KnrMap partial = KnrMap.Parse(
                "{\"schema_version\":1,\"edition\":\"partial\",\"codes\":{\"duct\":\"D-1\",\"source\":\"\"}," +
                " \"overrides\":[{\"match\":{\"kind\":\"fitting\",\"type\":\"tee\"},\"code\":\"T-1\"}]}", "inline-partial");
            Bom partialBom = Bom.Build(net, partial);
            CheckStr("knr.partial_duct", FindBom(partialBom, "duct").KnrCode, "D-1");
            CheckStr("knr.partial_tee_via_override_only", FindBom(partialBom, "tee").KnrCode, "T-1");
            CheckStr("knr.partial_flex_empty", FindBom(partialBom, "flex").KnrCode, "");
            CheckStr("knr.partial_terminal_empty", FindBom(partialBom, "t2").KnrCode, "");
            CheckInt("knr.partial_unmapped_count", partialBom.Unmapped.Count, 2);   // flex, terminal (2 terminals -> 1 entry)
            CheckStr("knr.partial_unmapped_flex", partialBom.Unmapped[0], "flex shape=round");
            CheckStr("knr.partial_unmapped_terminal", partialBom.Unmapped[1], "terminal");
            CheckInt("knr.partial_map_unmapped_count", partial.Unmapped.Count, 2);
            // An override naming a shape never matches a call without one.
            CheckTrue("knr.partial_override_needs_type", partial.CodeFor("fitting") == null);
            CheckStr("knr.partial_unmapped_fitting", partial.Unmapped[2], "fitting");

            // Malformed input is rejected, never silently ignored.
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

        // ---- BomExport (issue #29) — JSON + dependency-free XLSX of a BOM ----
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

        // ---- PressureReport (issue #47) — critical-path ΔP rows over a solved network ----
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

        // ---- BatchSizing (issue #24) — five methods over a request list, EN snap ----
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
            CheckStr("batch.bad_error", res[5].Error, "flowrate must be positive, got -0.1");
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

        // ---- IfcExport (issue #61) — IFC4 SPF from a traced system ----
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

            // Every #n= id is unique and every #n reference resolves.
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

            // Deterministic apart from the FILE_NAME timestamp line.
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

        // ---- Multi-storey / multi-drawing networks (issue #55) ----
        // A riser drawing (Source + riser duct + riser tee) feeds two storey
        // drawings through cross-drawing links off the tee legs; the merged
        // network must solve exactly like the same components drawn on one sheet.
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

            // A drawing exported without guid/scope gets both on load.
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

            // Merge: scoped ids, rebuilt intra-drawing wiring, links off the tee legs.
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

            // The key check: identical to the same network drawn on one sheet.
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

            // The merged assembly round-trips as ordinary network JSON.
            string json = MultiDrawing.ToJson(merged, mergedMeta);
            Dictionary<string, ComponentMeta> backMeta;
            Network back = NetworkJson.Parse(json, out backMeta);
            CheckInt("multi.json_count", back.Components.Count, 7);
            Check("multi.json_dp", back.Solve(), dpMerged, 1e-12);
            CheckStr("multi.json_guid", backMeta["L2/d"].Guid, gL2);
            CheckStr("multi.json_scope", backMeta["riser/tee"].DrawingScope, "riser");

            // Validate: clean set, then one defect at a time.
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

        // ---- ReFit (issue #27) — re-size ducts on edit + balancing hints ----
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

            // Idempotent: re-fitting an already re-fitted network changes nothing.
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

            // Balancing hints over the (unmodified) parity network.
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

        // ---- QuickConnect (issue #57) — auto transitions, flexes and spacers ----
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

            // Same section with a gap: a plain spacer, no loss.
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

        // ---- Solver performance (issue #25: a live panel needs <200 ms recalc) ----
        // A branching supply system: trunk ducts with a tee per storey, each tee
        // feeding a run that ends in a terminal.
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

        private static void RunPerformance()
        {
            Network net = BenchNetwork(100, 10);
            CheckTrue("perf.network_size", net.Components.Count >= 1000);
            net.Solve();                                   // warm up the topo cache and the JIT

            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int iters = 10;
            for (int i = 0; i < iters; i++) net.Solve();
            sw.Stop();
            double msPerSolve = sw.Elapsed.TotalMilliseconds / iters;
            Console.WriteLine("  [perf] " + net.Components.Count + " components, re-solve "
                + msPerSolve.ToString("F3", C) + " ms");
            // The live-panel budget is 200 ms; measured ~2 ms on a dev box, so this
            // only fires on a real regression, not on a slow CI runner.
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

