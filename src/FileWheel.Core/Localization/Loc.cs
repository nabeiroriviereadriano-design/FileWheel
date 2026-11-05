using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileWheel.Core.Settings;

namespace FileWheel.Core.Localization;

/// <summary>
/// Traduction de l'interface. Le francais est la langue source du code : chaque texte affiche
/// passe par <see cref="T"/> au moment de l'affichage (constructeurs d'interface, roue, messages,
/// notifications). En anglais, le texte francais est cherche dans le dictionnaire <c>en.json</c>
/// (texte francais → texte anglais) :
/// - correspondance exacte ;
/// - modeles avec trous pour les textes composes (« {0} fichiers » → « {0} files ») ;
/// - decoupage par lignes et par phrases pour les messages assembles ;
/// - majuscules conservees (« RECADRER » → « CROP »).
/// Un texte absent du dictionnaire reste en francais : rien ne casse.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _exactInsensitive = new(StringComparer.OrdinalIgnoreCase);
    private static List<Pattern> _patterns = new();
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static bool _loaded;

    /// <summary>Interface en anglais.</summary>
    public static bool English { get; private set; }

    /// <summary>Code de langue (« fr » / « en »).</summary>
    public static string Code => English ? "en" : "fr";

    /// <summary>Langue effective pour un reglage donne (Automatique = langue de Windows).</summary>
    public static bool IsEnglishFor(UiLanguage lang) => lang switch
    {
        UiLanguage.English => true,
        UiLanguage.French => false,
        _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase),
    };

    public static void Set(UiLanguage lang) => SetEnglish(IsEnglishFor(lang));

    public static void SetEnglish(bool english)
    {
        English = english;
        Cache.Clear();
        if (english && !_loaded) Load(ReadEmbedded());
    }

    /// <summary>Nombre d'entrees du dictionnaire (tests).</summary>
    public static int Count => _exact.Count + _patterns.Count;

    /// <summary>Releve des textes sans traduction (verification automatique de l'interface en anglais).</summary>
    public static bool RecordMisses { get; set; }
    public static readonly ConcurrentDictionary<string, byte> Misses = new(StringComparer.Ordinal);

    /// <summary>Traduit un texte francais dans la langue de l'interface.</summary>
    public static string T(string? french)
    {
        if (string.IsNullOrEmpty(french) || !English) return french ?? "";
        return Cache.GetOrAdd(french, s =>
        {
            var t = Translate(s);
            if (RecordMisses && ReferenceEquals(t, s) && LooksFrench(s)) Misses.TryAdd(s, 0);
            return t;
        });
    }

    /// <summary>Texte probablement francais : lettres accentuees ou mots-outils courants.</summary>
    public static bool LooksFrench(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s, @"[éèêàâùûôîçœÉÈÀÇ]|\b(le|la|les|des|du|un|une|et|ou|pour|avec|dans|sur|pas|est|sont|vos|votre|aucun|aucune)\b", RegexOptions.IgnoreCase);

    /// <summary>Modele + arguments (equivalent de string.Format apres traduction du modele).</summary>
    public static string F(string frenchTemplate, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(frenchTemplate), args);

    /// <summary>Le texte est-il traduit tel quel (tests de couverture) ?</summary>
    public static bool Has(string french) => _exact.ContainsKey(french) || _patterns.Any(p => p.TryApply(french, out _));

    // ------------------------------------------------------------------ chargement

    private static string ReadEmbedded()
    {
        var asm = typeof(Loc).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("en.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return "{}";
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Charge un dictionnaire JSON { "texte francais": "English text" } (public pour les tests).</summary>
    public static void Load(string json)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        }) ?? new();
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var insensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new List<Pattern>();
        foreach (var (fr, en) in map)
        {
            if (string.IsNullOrEmpty(fr) || en is null) continue;
            if (Regex.IsMatch(fr, @"\{\d+\}")) patterns.Add(new Pattern(fr, en));
            else
            {
                exact[fr] = en;
                insensitive.TryAdd(fr, en);
            }
        }
        // Les modeles les plus precis (le plus de texte fixe) d'abord
        patterns.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
        _exact = exact;
        _exactInsensitive = insensitive;
        _patterns = patterns;
        _loaded = true;
        Cache.Clear();
    }

    // ------------------------------------------------------------------ traduction

    private static string Translate(string s) => TranslateCore(s, 0) ?? s;

    private static string? TranslateCore(string s, int depth)
    {
        if (depth > 4 || s.Length == 0) return null;
        if (_exact.TryGetValue(s, out var hit)) return hit;

        // Espaces autour : traduit le coeur, garde les espaces
        var trimmed = s.Trim();
        if (trimmed.Length == 0) return s;
        if (trimmed.Length != s.Length)
        {
            var core = TranslateCore(trimmed, depth + 1);
            if (core is not null)
            {
                int lead = s.Length - s.TrimStart().Length;
                return s[..lead] + core + s[(lead + trimmed.Length)..];
            }
            return null;
        }

        // Majuscules : « RECADRER » → T(« Recadrer ») en majuscules
        if (IsUpper(s))
        {
            var lower = CultureInfo.InvariantCulture.TextInfo.ToLower(s);
            var cap = char.ToUpperInvariant(lower[0]) + lower[1..];
            var t = Lookup(cap) ?? Lookup(lower);
            if (t is not null) return t.ToUpperInvariant();
        }

        foreach (var p in _patterns)
            if (p.TryApply(s, out var applied)) return applied;

        // Plusieurs lignes : chaque ligne
        if (s.Contains('\n'))
        {
            var lines = s.Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var t = line.Length == 0 ? null : TranslateCore(line, depth + 1);
                if (t is not null) { lines[i] = lines[i].EndsWith('\r') ? t + "\r" : t; any = true; }
            }
            return any ? string.Join('\n', lines) : null;
        }

        // Prefixes et suffixes courants : « ⚠ texte », « texte (3) », « texte… », « texte : »
        var m = Affix.Match(s);
        if (m.Success && m.Groups["core"].Value.Length > 0 && m.Groups["core"].Value.Length < s.Length)
        {
            var core = TranslateCore(m.Groups["core"].Value, depth + 1);
            if (core is not null) return m.Groups["pre"].Value + core + m.Groups["post"].Value;
        }

        // Majuscule initiale differente : « copier » / « Copier »
        if (_exactInsensitive.TryGetValue(s, out var ci)) return MatchCase(s, ci);

        // Phrases assemblees : « Phrase un. Phrase deux. »
        var sentences = SentenceSplit.Split(s);
        if (sentences.Length > 1)
        {
            bool any = false;
            var sb = new StringBuilder();
            foreach (var part in sentences)
            {
                var t = part.Trim().Length == 0 ? null : TranslateCore(part.Trim(), depth + 1);
                if (t is not null)
                {
                    any = true;
                    int lead = part.Length - part.TrimStart().Length;
                    sb.Append(part[..lead]).Append(t).Append(part[(lead + part.Trim().Length)..]);
                }
                else sb.Append(part);
            }
            return any ? sb.ToString() : null;
        }
        return null;
    }

    private static string? Lookup(string s) =>
        _exact.TryGetValue(s, out var e) ? e : _exactInsensitive.TryGetValue(s, out var i) ? i : null;

    private static bool IsUpper(string s)
    {
        bool letter = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c)) { letter = true; if (char.IsLower(c)) return false; }
        }
        return letter && s.Length > 1;
    }

    private static string MatchCase(string source, string translated)
    {
        if (translated.Length == 0) return translated;
        if (IsUpper(source)) return translated.ToUpperInvariant();
        if (char.IsUpper(source[0])) return char.ToUpperInvariant(translated[0]) + translated[1..];
        if (char.IsLower(source[0])) return char.ToLowerInvariant(translated[0]) + translated[1..];
        return translated;
    }

    private static readonly Regex Affix = new(
        @"^(?<pre>(?:[⚠✓✗•→←›»«\-–—]\s*|\d+\s*[.)]\s+)?)(?<core>.*?)(?<post>(?:\s*\(\d+\)|\s*…|\s*\.\.\.|\s*:|\s*\?|\s*!|\s*\.)*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Coupe apres « . », « ! », « ? » ou « : » suivis d'un espace et d'une majuscule (le separateur reste avec la phrase precedente)
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?:;])(?=\s+[A-ZÀÂÉÈÊÎÔÛÇ«(])", RegexOptions.Compiled);

    private sealed class Pattern
    {
        private readonly Regex _regex;
        private readonly string _english;
        private readonly int[] _groupToIndex;
        private readonly string _prefix;
        public int LiteralLength { get; }

        public Pattern(string french, string english)
        {
            _english = english;
            var parts = Regex.Split(french, @"\{(\d+)\}");
            var sb = new StringBuilder("^");
            var map = new List<int>();
            int literal = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    sb.Append(Regex.Escape(parts[i]));
                    literal += parts[i].Length;
                }
                else
                {
                    sb.Append("(.+?)");
                    map.Add(int.Parse(parts[i], CultureInfo.InvariantCulture));
                }
            }
            sb.Append('$');
            _regex = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            _groupToIndex = map.ToArray();
            _prefix = parts[0];
            LiteralLength = literal;
        }

        public bool TryApply(string s, out string result)
        {
            result = s;
            if (_prefix.Length > 0 && !s.StartsWith(_prefix, StringComparison.Ordinal)) return false;
            var m = _regex.Match(s);
            if (!m.Success) return false;
            var values = new Dictionary<int, string>();
            for (int g = 0; g < _groupToIndex.Length; g++)
            {
                var v = m.Groups[g + 1].Value;
                // Les morceaux variables sont traduits s'ils sont connus (« Moteurs », « fichiers »...)
                values[_groupToIndex[g]] = _exact.TryGetValue(v, out var tv) ? tv : v;
            }
            result = Regex.Replace(_english, @"\{(\d+)\}", mm =>
                values.TryGetValue(int.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture), out var v) ? v : mm.Value);
            return true;
        }
    }
}
