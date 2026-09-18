using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wenta
{
    /// <summary>Nameplate electrical data for a single equipment item.
    /// Port of `venti/src/electrical.rs` — "zestawienie danych elektrycznych"
    /// (electrical data schedule) equipment record.</summary>
    public class ElectricalData
    {
        /// <summary>Identifier of the component in the wider model (e.g. "F-01").</summary>
        public string ComponentId;

        /// <summary>Equipment type (e.g. "supply fan", "circulation pump").</summary>
        public string DeviceType;

        /// <summary>Installed electrical power in watts.</summary>
        public double PowerW;

        /// <summary>Supply voltage in volts, when known.</summary>
        public double? VoltageV;

        /// <summary>Operating current in amperes, when known or computable.</summary>
        public double? CurrentA;

        /// <summary>Power factor (cos phi), when known.</summary>
        public double? PowerFactor;

        /// <summary>Supply frequency in hertz, when known.</summary>
        public double? FrequencyHz;

        /// <summary>Create a new electrical-data record with the given installed power.
        /// Throws <see cref="WentaException"/> when `powerW` is negative or not a finite
        /// value.</summary>
        public ElectricalData(string componentId, string deviceType, double powerW)
        {
            if (double.IsNaN(powerW) || double.IsInfinity(powerW) || powerW < 0.0)
                throw new WentaException("power_w must be a non-negative finite value");
            ComponentId = componentId;
            DeviceType = deviceType;
            PowerW = powerW;
        }

        /// <summary>Compute the operating current `I = P / (U * cos phi)` from the installed
        /// power, voltage and power factor, and store it in <see cref="CurrentA"/>.
        /// Returns null (and clears <see cref="CurrentA"/>) when either the voltage or the
        /// power factor is missing or non-positive.</summary>
        public double? Current()
        {
            double? amps = ComputedCurrent();
            CurrentA = amps;
            return amps;
        }

        /// <summary>Installed power expressed in kilowatts.</summary>
        public double PowerKw()
        {
            return PowerW / 1000.0;
        }

        /// <summary>Current in amperes derived from voltage and power factor alone
        /// (does not touch the stored <see cref="CurrentA"/> field).</summary>
        internal double? ComputedCurrent()
        {
            if (VoltageV.HasValue && PowerFactor.HasValue
                && VoltageV.Value > 0.0 && PowerFactor.Value > 0.0)
                return PowerW / (VoltageV.Value * PowerFactor.Value);
            return null;
        }
    }

    /// <summary>A tabular schedule ("zestawienie") of electrical equipment records.
    /// Port of `venti/src/electrical.rs::ElectricalSchedule`.</summary>
    public class ElectricalSchedule
    {
        private readonly List<ElectricalData> _entries = new List<ElectricalData>();

        /// <summary>Append one electrical-data record to the schedule.</summary>
        public void Add(ElectricalData entry)
        {
            _entries.Add(entry);
        }

        /// <summary>Total installed electrical power across all entries, in watts.</summary>
        public double TotalPowerW()
        {
            double total = 0.0;
            foreach (ElectricalData e in _entries) total += e.PowerW;
            return total;
        }

        /// <summary>Sum of the per-entry operating currents (amperes).
        /// Returns null as soon as any entry is missing the voltage or power
        /// factor needed to derive its current.</summary>
        public double? TotalCurrentA()
        {
            double total = 0.0;
            foreach (ElectricalData e in _entries)
            {
                double? c = e.ComputedCurrent();
                if (!c.HasValue) return null;
                total += c.Value;
            }
            return total;
        }

        /// <summary>Number of records in the schedule.</summary>
        public int Len()
        {
            return _entries.Count;
        }

        /// <summary>Whether the schedule contains no records.</summary>
        public bool IsEmpty()
        {
            return _entries.Count == 0;
        }

        /// <summary>Iterate over the schedule's records.</summary>
        public IEnumerable<ElectricalData> Iter()
        {
            return _entries;
        }
    }

    /// <summary>CSV rendering for an <see cref="ElectricalSchedule"/> — the
    /// "zestawienie danych elektrycznych" handover sheet. Port of
    /// `venti::electrical_as_csv`.</summary>
    public static class Electrical
    {
        /// <summary>Render an <see cref="ElectricalSchedule"/> as a CSV report.
        ///
        /// The header line is
        /// `component_id,device_type,power_w,power_kw,voltage_v,current_a,power_factor,frequency_hz`.
        /// Missing optional values (voltage, current, power factor, frequency) are
        /// rendered as empty fields; the `current_a` column shows the stored value
        /// when present and otherwise falls back to the value computable from
        /// power / (voltage * power factor).</summary>
        public static string AsCsv(ElectricalSchedule schedule)
        {
            var sb = new StringBuilder();
            sb.Append("component_id,device_type,power_w,power_kw,voltage_v,current_a,power_factor,frequency_hz");
            foreach (ElectricalData e in schedule.Iter())
            {
                // stored current when present, else the computable one (or empty)
                double? current = e.CurrentA ?? e.ComputedCurrent();

                sb.Append('\n');
                sb.Append(e.ComponentId).Append(',')
                  .Append(e.DeviceType).Append(',')
                  .Append(FmtNum(e.PowerW)).Append(',')
                  .Append(FmtNum(e.PowerKw())).Append(',')
                  .Append(Results.FmtOpt(e.VoltageV)).Append(',')
                  .Append(Results.FmtOpt(current)).Append(',')
                  .Append(Results.FmtOpt(e.PowerFactor)).Append(',')
                  .Append(Results.FmtOpt(e.FrequencyHz));
            }
            return sb.ToString();
        }

        /// <summary>Format a double the way Rust's `f64::to_string()` would: plain
        /// decimal notation, no forced trailing zeros (e.g. 5500.0 -&gt; "5500",
        /// 5.5 -&gt; "5.5").</summary>
        private static string FmtNum(double v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
