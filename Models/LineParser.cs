using System.Globalization;

namespace PcsHmi.Models;

/// <summary>
/// Parser linjer på formatet:
/// T1:22.3,LDR:858,SET:22.0,HEAT:0,MODE:AUTO,DIM:158,LIGHT:0
/// Returnerer null for halve linjer, WARN-linjer og annen støy.
/// </summary>
public static class LineParser
{
    private static readonly string[] RequiredKeys = { "T1", "LDR", "SET", "HEAT", "MODE", "DIM", "LIGHT" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Measurement? TryParse(string? line, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        line = line.Trim();
        if (!line.StartsWith("T1:")) return null;

        var fields = new Dictionary<string, string>();
        foreach (var part in line.Split(','))
        {
            int idx = part.IndexOf(':');
            if (idx <= 0) return null;
            fields[part[..idx]] = part[(idx + 1)..];
        }
        if (!RequiredKeys.All(fields.ContainsKey)) return null;

        // InvariantCulture: Arduino sender punktum, norsk språkoppsett bruker komma
        if (!double.TryParse(fields["T1"], NumberStyles.Float, Inv, out var t1)) return null;
        if (!int.TryParse(fields["LDR"], NumberStyles.Integer, Inv, out var ldr)) return null;
        if (!double.TryParse(fields["SET"], NumberStyles.Float, Inv, out var sp)) return null;
        if (!int.TryParse(fields["HEAT"], NumberStyles.Integer, Inv, out var heat)) return null;
        if (!int.TryParse(fields["DIM"], NumberStyles.Integer, Inv, out var dim)) return null;
        if (!int.TryParse(fields["LIGHT"], NumberStyles.Integer, Inv, out var light)) return null;

        var mode = fields["MODE"];
        if (mode != "AUTO" && mode != "MAN") return null;

        return new Measurement(timestampUtc, t1, ldr, sp, heat == 1, mode, dim, light);
    }
}
