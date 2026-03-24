using Avalonia.Media;
using System;

namespace PavamanDroneConfigurator.UI.Helpers
{
    public static class TelemetryFormatters
    {
        public static string FormatElapsedTime(long? seconds)
        {
            if (!seconds.HasValue) return "—";
            var m = seconds.Value / 60;
            var s = seconds.Value % 60;
            return string.Format("{0:D2}:{1:D2}", m, s);
        }

        public static string FormatDistance(float? meters)
        {
            if (!meters.HasValue) return "N/A";
            if (meters.Value < 1000f) return string.Format("{0:0} m", meters.Value);
            return string.Format("{0:0.00} km", meters.Value / 1000f);
        }

        public static string FormatCoordinate(double? value)
        {
            return value.HasValue ? string.Format("{0:F6}", value.Value) : "N/A";
        }

        public static string FormatFloat(float? value, string unit = "")
        {
            if (!value.HasValue) return "N/A";
            return string.Format("{0:0.0}{1}", value.Value, string.IsNullOrEmpty(unit) ? "" : " " + unit);
        }

        public static string FormatInt(int? value, string unit = "")
        {
            if (!value.HasValue) return "N/A";
            return value.Value + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
        }
    }

    public static class TelemetryColors
    {
        public static Color Green  => Color.Parse("#22C55E");
        public static Color Yellow => Color.Parse("#F59E0B");
        public static Color Red    => Color.Parse("#EF4444");
        public static Color Gray   => Color.Parse("#9CA3AF");
        public static Color Blue   => Color.Parse("#3B82F6");
        public static Color Teal   => Color.Parse("#06B6D4");
        public static Color Orange => Color.Parse("#F59E0B");
        public static Color Purple => Color.Parse("#9C27B0");

        public static Color BatteryColor(int? percent)
        {
            if (!percent.HasValue) return Gray;
            if (percent.Value > 50) return Green;
            if (percent.Value > 20) return Yellow;
            return Red;
        }

        public static Color VoltageColor(float? voltage)
        {
            if (!voltage.HasValue) return Gray;
            if (voltage.Value > 21.0f) return Green;
            if (voltage.Value > 19.0f) return Yellow;
            return Red;
        }

        public static Color SatelliteColor(int? sats)
        {
            if (!sats.HasValue) return Gray;
            if (sats.Value > 8) return Green;
            if (sats.Value >= 4) return Yellow;
            return Red;
        }

        public static Color HdopColor(float? hdop)
        {
            if (!hdop.HasValue) return Gray;
            if (hdop.Value < 1.5f) return Green;
            if (hdop.Value < 2.5f) return Yellow;
            return Red;
        }

        public static Color ModeColor(string? mode)
        {
            if (mode == null) return Gray;
            switch (mode.ToUpperInvariant())
            {
                case "AUTO": return Blue;
                case "STABILIZE": return Green;
                case "LOITER": return Teal;
                case "RTL": return Orange;
                case "LAND": return Yellow;
                case "GUIDED": return Purple;
                default: return Gray;
            }
        }

        public static Color TankLevelColor(int? percent)
        {
            if (!percent.HasValue) return Gray;
            if (percent.Value > 30) return Green;
            if (percent.Value > 15) return Yellow;
            return Red;
        }

        public static Color ConnectionColor(bool connected) => connected ? Green : Red;
    }
}
