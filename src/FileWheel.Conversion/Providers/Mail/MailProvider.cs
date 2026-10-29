using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FileWheel.Conversion.Core;
using FileWheel.Conversion.Providers.Data;
using FileWheel.Conversion.Providers.Text;
using FileWheel.Core.FileOperations;

namespace FileWheel.Conversion.Providers.Mail;

/// <summary>
/// E-mails, contacts et agendas (integre) :
/// EML -&gt; TXT/HTML, MBOX -&gt; dossier d'EML, VCF -&gt; CSV, ICS -&gt; CSV.
/// Les pieces jointes sont listees, jamais extraites ni ouvertes. Dans la version HTML,
/// scripts et images distantes sont retires (pas de « pixel espion » a l'ouverture).
/// </summary>
public sealed class MailProvider : ProviderBase
{
    public override string Id => "mail";
    public override string DisplayName => "E-mails, contacts, agendas (intégré)";

    public override IReadOnlyList<ConversionCapability> Capabilities { get; } =
    [
        Cap("mail", ["eml"], "txt", loss: LossKind.Structure, lossNote: "Texte du message et en-têtes ; pièces jointes seulement listées.", priority: 10),
        Cap("mail", ["eml"], "html", loss: LossKind.Structure, lossNote: "Scripts et images distantes retirés ; pièces jointes seulement listées.", priority: 10),
        Cap("mail", ["mbox"], "eml", ConversionKind.Split, folder: true, priority: 10, label: "Séparer en messages EML (un fichier par message)"),
        Cap("mail", ["vcf"], "csv", options: [OptionDef.Choice("delimiter", "Séparateur", ";", (";", "Point-virgule"), (",", "Virgule"))], priority: 10,
            lossNote: "Champs courants (nom, e-mails, téléphones, adresse, organisation…) ; photos non conservées."),
        Cap("mail", ["ics"], "csv", options: [OptionDef.Choice("delimiter", "Séparateur", ";", (";", "Point-virgule"), (",", "Virgule"))], priority: 10,
            lossNote: "Un événement par ligne ; rappels et invités non conservés, règles de répétition recopiées telles quelles."),
    ];

    public override Task<ConversionResult> ConvertAsync(ConversionRequest req, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var warnings = new List<string>();
            switch ((req.SourceFormat.Id, req.TargetFormat))
            {
                case ("eml", "txt"):
                {
                    var msg = Mime.Parse(File.ReadAllBytes(req.Input));
                    TextIO.WriteUtf8(req.TempOutput, ToText(msg));
                    break;
                }
                case ("eml", "html"):
                {
                    var msg = Mime.Parse(File.ReadAllBytes(req.Input));
                    TextIO.WriteUtf8(req.TempOutput, ToHtml(msg, out bool blocked));
                    if (blocked) warnings.Add("Des images distantes ont été retirées (elles permettraient à l'expéditeur de savoir que le message a été ouvert).");
                    break;
                }
                case ("mbox", "eml"):
                    SplitMbox(req.Input, req.TempOutput, ct, progress);
                    break;
                case ("vcf", "csv"):
                    WriteCsv(req, VcfTable(TextIO.Read(req.Input).text));
                    break;
                case ("ics", "csv"):
                    WriteCsv(req, IcsTable(TextIO.Read(req.Input).text));
                    break;
                default:
                    throw new ConversionException(ConversionErrorKind.NotSupported, "Conversion non prise en charge.");
            }
            return new ConversionResult([req.TempOutput], warnings);
        }, ct);

    private static void WriteCsv(ConversionRequest req, Table t)
    {
        if (t.Rows.Count == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Aucune entrée trouvée dans ce fichier.");
        char d = req.Opt("delimiter") == "," ? ',' : ';';
        var rows = t.AllRows(true).Select(r => (IReadOnlyList<string>)r.Select(Csv.NeutralizeFormula).ToList());
        TextIO.WriteUtf8(req.TempOutput, Csv.Write(rows, d), bom: true);
    }

    // ------------------------------------------------------------------ EML

    private static string ToText(Mime.Part msg)
    {
        var sb = new StringBuilder();
        foreach (var (label, h) in new[] { ("De", "From"), ("À", "To"), ("Cc", "Cc"), ("Date", "Date"), ("Objet", "Subject") })
            if (msg.Header(h) is { Length: > 0 } v) sb.Append(label).Append(" : ").Append(Mime.DecodeWords(v)).Append('\n');
        sb.Append('\n');
        var (plain, html) = Mime.Bodies(msg);
        sb.Append(plain ?? (html is not null ? HtmlText.ToText(html) : "(message sans texte)"));
        var att = Mime.Attachments(msg);
        if (att.Count > 0)
        {
            sb.Append("\n\nPièces jointes (non extraites) :\n");
            foreach (var a in att) sb.Append(" - ").Append(a).Append('\n');
        }
        return sb.ToString();
    }

    private static string ToHtml(Mime.Part msg, out bool blocked)
    {
        blocked = false;
        var head = new StringBuilder("<table class=\"entete\">");
        foreach (var (label, h) in new[] { ("De", "From"), ("À", "To"), ("Cc", "Cc"), ("Date", "Date"), ("Objet", "Subject") })
            if (msg.Header(h) is { Length: > 0 } v)
                head.Append($"<tr><th>{label}</th><td>{WebUtility.HtmlEncode(Mime.DecodeWords(v))}</td></tr>");
        head.Append("</table><hr>");
        var (plain, html) = Mime.Bodies(msg);
        string body;
        if (html is not null)
        {
            var m = Regex.Match(html, @"<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            body = m.Success ? m.Groups[1].Value : html;
            body = Regex.Replace(body, @"<(script|iframe|object|embed|form|style|noscript)\b[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            body = Regex.Replace(body, @"<(script|iframe|object|embed|form|meta|link|base)\b[^>]*/?>", "", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, @"\son\w+\s*=\s*(""[^""]*""|'[^']*'|\S+)", "", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, @"(href|src)\s*=\s*([""'])\s*javascript:[^""']*\2", "$1=\"#\"", RegexOptions.IgnoreCase);
            int before = body.Length;
            body = Regex.Replace(body, @"<img\b[^>]*\bsrc\s*=\s*[""']https?://[^""']*[""'][^>]*>", "[image distante retirée]", RegexOptions.IgnoreCase);
            blocked = body.Length != before;
        }
        else body = "<pre>" + WebUtility.HtmlEncode(plain ?? "") + "</pre>";
        var att = Mime.Attachments(msg);
        if (att.Count > 0)
            body += "<hr><p><strong>Pièces jointes (non extraites) :</strong></p><ul>" + string.Concat(att.Select(a => $"<li>{WebUtility.HtmlEncode(a)}</li>")) + "</ul>";
        var subject = Mime.DecodeWords(msg.Header("Subject") ?? "Message");
        return Markdown.HtmlDocument(WebUtility.HtmlEncode(subject), head + body);
    }

    private static void SplitMbox(string path, string outDir, CancellationToken ct, IProgress<ConversionProgress> progress)
    {
        using var r = new StreamReader(path, Encoding.Latin1); // octets preserves tels quels
        var current = new StringBuilder();
        int n = 0;
        string? line;
        bool started = false;
        void Flush()
        {
            if (!started || current.Length == 0) return;
            n++;
            var bytes = Encoding.Latin1.GetBytes(current.ToString());
            string subject = "";
            try { subject = Mime.DecodeWords(Mime.Parse(bytes).Header("Subject") ?? ""); } catch { }
            var name = PathSafety.SanitizeFileName($"{n:00000} {subject}".Trim());
            if (name.Length > 100) name = name[..100].TrimEnd('.', ' ');
            File.WriteAllBytes(Path.Combine(outDir, name + ".eml"), bytes);
            current.Clear();
            if (n % 50 == 0) progress.Report(new ConversionProgress(null, $"{n} message(s)…"));
        }
        while ((line = r.ReadLine()) is not null)
        {
            if (line.StartsWith("From ")) { Flush(); started = true; ct.ThrowIfCancellationRequested(); continue; }
            if (!started) continue;
            if (Regex.IsMatch(line, @"^>+From ")) line = line[1..];
            current.Append(line).Append("\r\n");
        }
        Flush();
        if (n == 0) throw new ConversionException(ConversionErrorKind.InvalidInput, "Aucun message trouvé dans ce fichier MBOX.");
    }

    // ------------------------------------------------------------------ vCard / iCalendar

    private static List<(string name, Dictionary<string, string> prms, string value)> ContentLines(string text)
    {
        // Lignes repliees : une ligne commencant par espace/tabulation continue la precedente
        var unfolded = Regex.Replace(text.Replace("\r\n", "\n"), @"\n[ \t]", "");
        // vCard 2.1 : quoted-printable avec « = » en fin de ligne
        unfolded = Regex.Replace(unfolded, @"=\n", "");
        var list = new List<(string, Dictionary<string, string>, string)>();
        foreach (var line in unfolded.Split('\n'))
        {
            int colon = IndexOfUnquoted(line, ':');
            if (colon <= 0) continue;
            var left = line[..colon];
            var value = line[(colon + 1)..];
            var parts = left.Split(';');
            var name = parts[0].ToUpperInvariant();
            if (name.Contains('.')) name = name[(name.IndexOf('.') + 1)..]; // groupes « item1.EMAIL »
            var prms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts.Skip(1))
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2) prms[kv[0]] = kv[1].Trim('"');
                else prms["TYPE"] = (prms.GetValueOrDefault("TYPE") is { } t ? t + "," : "") + kv[0];
            }
            if (prms.TryGetValue("ENCODING", out var enc) && enc.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
                value = Mime.QuotedPrintable(value, prms.GetValueOrDefault("CHARSET") ?? "utf-8", header: false);
            list.Add((name, prms, value));
        }
        return list;
    }

    private static int IndexOfUnquoted(string s, char c)
    {
        bool q = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') q = !q;
            else if (s[i] == c && !q) return i;
        }
        return -1;
    }

    private static string Unescape(string v) => v.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    private static Table VcfTable(string text)
    {
        var t = new Table();
        t.Headers.AddRange(["Nom complet", "Nom", "Prénom", "E-mails", "Téléphones", "Organisation", "Fonction", "Adresse", "Anniversaire", "Site web", "Note"]);
        Dictionary<string, List<string>>? cur = null;
        foreach (var (name, prms, value) in ContentLines(text))
        {
            if (name == "BEGIN" && value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase)) { cur = new(); continue; }
            if (name == "END" && cur is not null)
            {
                string J(string k) => string.Join(" | ", cur.GetValueOrDefault(k) ?? []);
                var n = (cur.GetValueOrDefault("N")?.FirstOrDefault() ?? "").Split(';');
                t.Rows.Add([J("FN"), n.ElementAtOrDefault(0) ?? "", n.ElementAtOrDefault(1) ?? "", J("EMAIL"), J("TEL"), J("ORG").Replace(";", " "), J("TITLE"), J("ADR"), J("BDAY"), J("URL"), J("NOTE")]);
                cur = null;
                continue;
            }
            if (cur is null) continue;
            var v = name == "N" ? value : Unescape(value);
            if (name == "ADR") v = string.Join(", ", value.Split(';').Select(Unescape).Where(x => x.Trim().Length > 0));
            if (name is "TEL" or "EMAIL" && prms.TryGetValue("TYPE", out var type)) v += $" ({type.ToLowerInvariant()})";
            if (!cur.TryGetValue(name, out var l)) cur[name] = l = new();
            l.Add(v.Trim());
        }
        return t;
    }

    private static Table IcsTable(string text)
    {
        var t = new Table();
        t.Headers.AddRange(["Titre", "Début", "Fin", "Toute la journée", "Lieu", "Description", "Répétition", "Fuseau", "Identifiant"]);
        Dictionary<string, (Dictionary<string, string> p, string v)>? ev = null;
        int depth = 0;
        foreach (var (name, prms, value) in ContentLines(text))
        {
            if (name == "BEGIN")
            {
                if (value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(); depth = 0; }
                else if (ev is not null) depth++; // VALARM imbrique : ignore
                continue;
            }
            if (name == "END")
            {
                if (ev is not null && depth > 0) { depth--; continue; }
                if (ev is not null && value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    var start = ev.GetValueOrDefault("DTSTART");
                    var end = ev.GetValueOrDefault("DTEND");
                    bool allDay = start.p?.GetValueOrDefault("VALUE")?.Equals("DATE", StringComparison.OrdinalIgnoreCase) == true || (start.v?.Length == 8);
                    t.Rows.Add([
                        Unescape(ev.GetValueOrDefault("SUMMARY").v ?? ""), IcsDate(start.v), IcsDate(end.v), allDay ? "oui" : "non",
                        Unescape(ev.GetValueOrDefault("LOCATION").v ?? ""), Unescape(ev.GetValueOrDefault("DESCRIPTION").v ?? ""),
                        ev.GetValueOrDefault("RRULE").v ?? "", start.p?.GetValueOrDefault("TZID") ?? (start.v?.EndsWith('Z') == true ? "UTC" : ""),
                        ev.GetValueOrDefault("UID").v ?? "",
                    ]);
                    ev = null;
                }
                continue;
            }
            if (ev is not null && depth == 0) ev[name] = (prms, value);
        }
        return t;
    }

    private static string IcsDate(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var s = v.Trim();
        if (DateTime.TryParseExact(s, ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return s.Length == 8 ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm:ss") + (s.EndsWith('Z') ? " UTC" : "");
        return s;
    }
}
