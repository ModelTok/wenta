using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>Results for a single component: one row per component,
    /// aggregating its ports' flow, velocity and pressure drop.
    /// Port of `venti::results` (SPEC FR-16).</summary>
    public sealed class ComponentResult
    {
        public string ComponentId;
        public string Name;
        public string ComponentType;
        /// <summary>Volumetric flow entering the component [m^3/s].</summary>
        public double? FlowrateIn;
        /// <summary>Volumetric flow leaving the component [m^3/s].</summary>
        public double? FlowrateOut;
        /// <summary>Velocity at the inlet port [m/s].</summary>
        public double? VelocityIn;
        /// <summary>Velocity at the outlet port [m/s].</summary>
        public double? VelocityOut;
        /// <summary>Total pressure drop across all ports [Pa].</summary>
        public double PressureDrop;

        /// <summary>Field names, in order, for a CSV header (matches ToDict()).</summary>
        public static readonly string[] Fields =
        {
            "component_id",
            "name",
            "component_type",
            "flowrate_in",
            "flowrate_out",
            "velocity_in",
            "velocity_out",
            "pressure_drop",
        };

        /// <summary>Ordered (key, value) string pairs, mirroring the Rust
        /// `to_dict` (missing options render as an empty string).</summary>
        public List<KeyValuePair<string, string>> ToDict()
        {
            var d = new List<KeyValuePair<string, string>>();
            d.Add(new KeyValuePair<string, string>("component_id", ComponentId));
            d.Add(new KeyValuePair<string, string>("name", Name));
            d.Add(new KeyValuePair<string, string>("component_type", ComponentType));
            d.Add(new KeyValuePair<string, string>("flowrate_in", OptFmt(FlowrateIn)));
            d.Add(new KeyValuePair<string, string>("flowrate_out", OptFmt(FlowrateOut)));
            d.Add(new KeyValuePair<string, string>("velocity_in", OptFmt(VelocityIn)));
            d.Add(new KeyValuePair<string, string>("velocity_out", OptFmt(VelocityOut)));
            d.Add(new KeyValuePair<string, string>(
                "pressure_drop", PressureDrop.ToString(CultureInfo.InvariantCulture)));
            return d;
        }

        private static string OptFmt(double? v)
        {
            return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "";
        }
    }

    /// <summary>Extract results from a solved network into structured formats:
    /// one row per component, aggregating flow/velocity/pressure-drop. Port of
    /// `venti::results` (SPEC FR-16). Building blocks for schedules, CSVs and
    /// reports. (The Rust `cli`-feature JSON helpers are omitted here — no JSON
    /// library is available in this bare-csc build; `ExtractResults` /
    /// `ComponentResult.ToDict()` give the same rows for a caller to serialize.)</summary>
    public static class Results
    {
        /// <summary>Extract results from a solved network into a list, one row
        /// per component. The network should have been solved
        /// (<see cref="Network.Solve"/>) so flowrates and velocities are
        /// populated.</summary>
        public static List<ComponentResult> ExtractResults(Network network)
        {
            var results = new List<ComponentResult>();
            foreach (var kv in network.Components)
            {
                string cid = kv.Key;
                Component c = kv.Value;
                var inPorts = new List<Port>(c.Inlets);
                var outPorts = new List<Port>(c.Outlets);

                // A port's velocity is considered "set" once it has a flowrate
                // (i.e. it went through solve); matches the Rust/Python reference
                // semantics.
                double? flowrateIn = FirstOpt(inPorts, p => p.Flowrate);
                double? flowrateOut = FirstOpt(outPorts, p => p.Flowrate);
                double? velocityIn = FirstOpt(inPorts, p => p.Flowrate.HasValue ? p.Velocity : null);
                double? velocityOut = FirstOpt(outPorts, p => p.Flowrate.HasValue ? p.Velocity : null);

                double pressureDrop = 0.0;
                foreach (Port p in c.Ports) pressureDrop += p.PressureDrop;

                results.Add(new ComponentResult
                {
                    ComponentId = cid,
                    Name = c.Name,
                    ComponentType = c.GetType().Name,
                    FlowrateIn = flowrateIn,
                    FlowrateOut = flowrateOut,
                    VelocityIn = velocityIn,
                    VelocityOut = velocityOut,
                    PressureDrop = pressureDrop,
                });
            }
            return results;
        }

        /// <summary>Isolate the first matching value from a list of ports.</summary>
        private static double? FirstOpt(IEnumerable<Port> ports, Func<Port, double?> f)
        {
            foreach (Port p in ports)
            {
                double? v = f(p);
                if (v.HasValue) return v;
            }
            return null;
        }

        /// <summary>Export results as a CSV string with a header row.</summary>
        public static string ResultsAsCsv(Network network, char delimiter)
        {
            List<ComponentResult> results = ExtractResults(network);
            if (results.Count == 0) return "";

            string sep = delimiter.ToString();
            var lines = new List<string> { string.Join(sep, ComponentResult.Fields) };
            foreach (ComponentResult r in results)
            {
                var values = new List<string>();
                foreach (KeyValuePair<string, string> kv in r.ToDict()) values.Add(kv.Value);
                lines.Add(string.Join(sep, values.ToArray()));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Format results as a human-readable table.</summary>
        public static string ResultsSummary(Network network)
        {
            List<ComponentResult> results = ExtractResults(network);
            if (results.Count == 0) return "(no components)";

            // Column widths mirroring the Rust/Python reference.
            string[] headerLabels = { "ID", "Name", "Type", "Q_in [m³/s]", "V_in [m/s]", "ΔP [Pa]" };
            int[] headerWidths = { 12, 20, 18, 13, 12, 11 };

            var headerParts = new string[headerLabels.Length];
            for (int i = 0; i < headerLabels.Length; i++)
                headerParts[i] = headerLabels[i].PadRight(headerWidths[i]);
            string header = string.Join(" | ", headerParts);

            int sepLen = 0;
            foreach (int w in headerWidths) sepLen += w;
            sepLen += headerWidths.Length * 3 - 3;
            string sepLine = new string('-', sepLen);

            var lines = new List<string> { sepLine, header, sepLine };
            foreach (ComponentResult r in results)
            {
                string qIn = r.FlowrateIn.HasValue
                    ? r.FlowrateIn.Value.ToString("0.000", CultureInfo.InvariantCulture)
                    : "—";
                string vIn = r.VelocityIn.HasValue
                    ? r.VelocityIn.Value.ToString("0.00", CultureInfo.InvariantCulture)
                    : "—";
                string dp = r.PressureDrop.ToString("0.00", CultureInfo.InvariantCulture);
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} | {1,-20} | {2,-18} | {3,12} | {4,11} | {5,10}",
                    r.ComponentId, r.Name, r.ComponentType, qIn, vIn, dp));
            }
            lines.Add(sepLine);
            return string.Join("\n", lines.ToArray());
        }
    }
}
