using System.Globalization;
using System.Reflection;

namespace YaGt.Core;

/// <summary>
/// Lingue dell'interfaccia (dalla 1.1.0, armonizzazione del 27/11/2026): le stesse 37 di YA-NEC, YA-MS, YAEC, E-LAYER ANALYZER,
/// LIVETROPOVHF, IP-DESK-CTRL e IP-TRX-CTRL. La chiave di ogni testo è la frase italiana: i file Lang/&lt;codice&gt;.txt (UTF-8,
/// incorporati nell'eseguibile) hanno una voce per riga, &lt;chiave italiana&gt;&lt;TAB&gt;&lt;traduzione&gt;; \n vale a capo.
/// Se una voce manca si usa la lingua madre (pt-br → pt, zh-tw → zh), poi l'inglese, poi l'italiano.
/// Lingua all'avvio: quella salvata in YA-GT.ini (chiave "lingua"); al primo avvio quella del sistema, se prevista, altrimenti l'inglese.
/// </summary>
public static class L
{
    public static readonly string[][] All =
    {
        new[] { "it", "Italiano" }, new[] { "en", "English" }, new[] { "de", "Deutsch" }, new[] { "fr", "Français" },
        new[] { "es", "Español" }, new[] { "pt", "Português" }, new[] { "pt-br", "Português (Brasil)" }, new[] { "nl", "Nederlands" },
        new[] { "sv", "Svenska" }, new[] { "nb", "Norsk (bokmål)" }, new[] { "da", "Dansk" }, new[] { "is", "Íslenska" },
        new[] { "et", "Eesti" }, new[] { "lv", "Latviešu" }, new[] { "lt", "Lietuvių" }, new[] { "pl", "Polski" },
        new[] { "cs", "Čeština" }, new[] { "sk", "Slovenčina" }, new[] { "hu", "Magyar" }, new[] { "ro", "Română" },
        new[] { "el", "Ελληνικά" }, new[] { "ru", "Русский" }, new[] { "uk", "Українська" }, new[] { "tr", "Türkçe" },
        new[] { "af", "Afrikaans" }, new[] { "ar", "العربية" }, new[] { "he", "עברית" }, new[] { "ur", "اردو" },
        new[] { "hi", "हिन्दी" }, new[] { "bn", "বাংলা" }, new[] { "id", "Bahasa Indonesia" }, new[] { "tl", "Tagalog" },
        new[] { "zh", "中文 (简体)" }, new[] { "zh-tw", "中文 (繁體)" }, new[] { "th", "ไทย" }, new[] { "ja", "日本語" },
        new[] { "ko", "한국어" },
    };

    public static string Code { get; private set; } = "it";
    public static bool IsRtl => Code == "ar" || Code == "he" || Code == "ur";

    static readonly Dictionary<string, Dictionary<string, string>> Cache = new Dictionary<string, Dictionary<string, string>>();
    static readonly object Lock = new object();

    public static int IndexOf(string code)
    {
        for (int i = 0; i < All.Length; i++) if (string.Equals(All[i][0], code, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Lingua salvata (o vuota = quella del sistema).</summary>
    public static void Init(string saved)
    {
        int i = IndexOf((saved ?? "").Trim());
        Code = i >= 0 ? All[i][0] : FromSystem();
        try { CultureInfo.CurrentUICulture = Culture; } catch { }
    }

    public static string FromSystem()
    {
        try
        {
            var ui = CultureInfo.CurrentUICulture;
            string name = ui.Name.Replace('_', '-').ToLowerInvariant();
            if (name.StartsWith("pt-br")) return "pt-br";
            if (name.StartsWith("zh") && (name.Contains("tw") || name.Contains("hk") || name.Contains("mo") || name.Contains("hant"))) return "zh-tw";
            string two = name.Split('-')[0];
            if (two == "no" || two == "nn") two = "nb";
            if (two == "fil") two = "tl";
            if (two == "iw") two = "he";
            if (two == "in") two = "id";
            if (IndexOf(two) >= 0) return two;
        }
        catch { }
        return "en";
    }

    static Dictionary<string, string> Load(string code)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(code, out var d)) return d;
            d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (code != "it")
            {
                try
                {
                    var asm = typeof(L).Assembly;
                    using (var s = asm.GetManifestResourceStream("YaGt.Lang." + code + ".txt"))
                    {
                        if (s != null)
                            using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                            {
                                string line;
                                while ((line = r.ReadLine()) != null)
                                {
                                    if (line.Length == 0 || line[0] == '#') continue;
                                    int t = line.IndexOf('\t');
                                    if (t <= 0) continue;
                                    var k = line.Substring(0, t).Replace("\\n", "\n");
                                    var v = line.Substring(t + 1).Replace("\\n", "\n");
                                    if (v.Length > 0) d[k] = v;
                                }
                            }
                    }
                }
                catch { }
            }
            Cache[code] = d;
            return d;
        }
    }

    /// <summary>Traduzione della frase italiana (testo invariato se non è una chiave).</summary>
    public static string T(string it)
    {
        if (Code == "it" || string.IsNullOrEmpty(it)) return it;
        if (Load(Code).TryGetValue(it, out var v)) return v;
        int dash = Code.IndexOf('-');
        if (dash > 0 && Load(Code.Substring(0, dash)).TryGetValue(it, out v)) return v;
        if (Load("en").TryGetValue(it, out v)) return v;
        return it;
    }

    /// <summary>Traduzione con segnaposto {0}, {1}…</summary>
    public static string F(string it, params object[] args)
    {
        var fmt = T(it);
        try { return string.Format(Culture, fmt, args); }
        catch (FormatException)
        {
            try { return string.Format(CultureInfo.InvariantCulture, it, args); } catch { return it; }
        }
    }

    /// <summary>Vero se il testo è una chiave (serve a non tradurre due volte).</summary>
    public static bool Has(string it) => Code != "it" && !string.IsNullOrEmpty(it) && (Load(Code).ContainsKey(it) || Load("en").ContainsKey(it));

    public static CultureInfo Culture
    {
        get
        {
            try
            {
                string c = Code == "nb" ? "nb-NO" : Code == "tl" ? "fil-PH" : Code == "pt-br" ? "pt-BR" : Code == "zh-tw" ? "zh-TW" : Code == "zh" ? "zh-CN" : Code;
                return CultureInfo.GetCultureInfo(c);
            }
            catch { return CultureInfo.InvariantCulture; }
        }
    }
}
