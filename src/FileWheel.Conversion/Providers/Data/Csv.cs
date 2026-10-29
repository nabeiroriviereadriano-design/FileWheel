using System.Text;

namespace FileWheel.Conversion.Providers.Data;

/// <summary>Lecture/ecriture CSV conforme RFC 4180 (guillemets, retours a la ligne dans les champs).</summary>
public static class Csv
{
    /// <summary>Devine le separateur (virgule, point-virgule, tabulation, barre verticale) sur les premieres lignes.</summary>
    public static char DetectDelimiter(string text)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        var sample = text.Length > 20_000 ? text[..20_000] : text;
        var lines = sample.Split('\n').Take(20).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return ',';
        char best = ',';
        double bestScore = -1;
        foreach (var c in candidates)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, c)).ToList();
            if (counts[0] == 0) continue;
            // Prefere un nombre de colonnes constant et eleve
            double mean = counts.Average();
            double consistency = counts.Count(x => x == counts[0]) / (double)counts.Count;
            double score = mean * consistency;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char c)
    {
        int n = 0;
        bool q = false;
        foreach (var ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == c && !q) n++;
        }
        return n;
    }

    public static List<List<string>> Parse(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, fieldStarted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0 && !fieldStarted) { inQuotes = true; fieldStarted = true; continue; }
            if (c == delimiter) { row.Add(field.ToString()); field.Clear(); fieldStarted = false; continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                rows.Add(row);
                row = new List<string>();
                continue;
            }
            field.Append(c);
            fieldStarted = true;
        }
        if (inQuotes) throw new FormatException("Guillemet non fermé dans le fichier CSV.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        // Lignes completement vides retirees
        rows.RemoveAll(r => r.Count == 1 && r[0].Length == 0);
        return rows;
    }

    public static string Write(IEnumerable<IReadOnlyList<string>> rows, char delimiter, string newline = "\r\n")
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            for (int i = 0; i < r.Count; i++)
            {
                if (i > 0) sb.Append(delimiter);
                sb.Append(Escape(r[i] ?? "", delimiter));
            }
            sb.Append(newline);
        }
        return sb.ToString();
    }

    public static string Escape(string v, char delimiter)
    {
        bool needs = v.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0 || v.StartsWith(' ') || v.EndsWith(' ');
        return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>
    /// Protection contre l'injection de formules (=, +, -, @) quand le CSV sera ouvert dans un tableur.
    /// Appliquee seulement aux donnees issues d'autres formats, jamais aux nombres.
    /// </summary>
    public static string NeutralizeFormula(string v)
    {
        if (v.Length == 0) return v;
        if (v[0] is '=' or '@' or '\t' or '\r' || ((v[0] is '+' or '-') && !double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            return "'" + v;
        return v;
    }
}
