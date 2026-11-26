using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace YaGt.Core;

/// <summary>
/// Aggiornamento automatico silenzioso dal GitHub, come in YA-NEC (richiesta di IC8TEM del 26/11/2026):
///  - 10 secondi dopo l'avvio e poi ogni 3 minuti si legge l'ultima versione pubblicata (api.github.com/repos/ic8tem/ya-gt/releases/latest,
///    con If-None-Match: la risposta 304 non conta nel limite di GitHub);
///  - se è più nuova si scarica il pacchetto portatile della piattaforma (YA-GT-x.y.z-windows-x64-portatile.zip, -windows7-x64/x86-portatile.zip,
///    -linux-x64.tar.gz, -linux-arm64.tar.gz) con la sua impronta .sha256 e la sua firma .sig (ECDSA P-256 / SHA-256, formato r||s in base64,
///    fatta nella release con la chiave privata che sta nei segreti di GitHub). Senza impronta o firma giusta non si installa niente;
///  - un file in uso non si sovrascrive ma si rinomina: il vecchio diventa "*.old", il nuovo prende il suo posto; la versione nuova parte al
///    prossimo avvio, che cancella i .old. Copia della versione precedente in Dati/aggiornamenti/precedente-x.y.z;
///  - non si toccano YA-GT.ini, CALL3.TXT (con le correzioni dal web dell'utente) e la cartella Dati;
///  - spento nelle prove automatiche e quando la cartella del programma non è scrivibile.
/// </summary>
public static class Updater
{
    public const string Repo = "ic8tem/ya-gt";
    public const int IntervalMinutes = 3;
    /// <summary>Chiave pubblica della firma dei pacchetti (P-256): coordinate X e Y in base64.</summary>
    const string KeyX = "m+I9ZeBn6jeqbjMFvtrdM7eZYLl0o3SiJ9pVEPs/51Y=";
    const string KeyY = "fPvsImUDR7YedhC5jCoiPVvx2DVByT1yVTBvq9xcpnk=";
    const long MaxPackage = 400L * 1024 * 1024;

    public static string Status { get; private set; } = "non ancora controllato";
    public static event Action<string> Changed;
    /// <summary>Versione nuova installata: entra in funzione al prossimo avvio.</summary>
    public static event Action<string> Ready;
    public static bool Enabled = true;

    static Timer _timer;
    static Mutex _single;
    static int _busy;
    static string _etag, _lastJson, _announced = "";

    static string UpdDir => Path.Combine(Settings.DataDir, "aggiornamenti");

    static void Set(string s)
    {
        Status = s;
        try { File.AppendAllText(Path.Combine(Settings.DataDir, "aggiornamenti.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + "\r\n"); } catch { }
        try { Changed?.Invoke(s); } catch { }
    }

    /// <summary>All'avvio: cancella i .old e restituisce "aggiornato da … a …" se un aggiornamento è appena entrato in funzione.</summary>
    public static string CleanAfterStart()
    {
        string msg = null;
        try
        {
            foreach (var f in Directory.GetFiles(Settings.AppDir, "*.old", SearchOption.AllDirectories)) try { File.Delete(f); } catch { }
            var done = Path.Combine(UpdDir, "applicato.txt");
            if (File.Exists(done)) { msg = File.ReadAllText(done).Trim(); File.Delete(done); }
        }
        catch { }
        return msg;
    }

    static bool Writable()
    {
        try
        {
            var p = Path.Combine(Settings.AppDir, ".prova_scrittura_" + Process.GetCurrentProcess().Id);
            File.WriteAllText(p, ""); File.Delete(p);
            return true;
        }
        catch { return false; }
    }

    static bool InTest => Environment.GetEnvironmentVariable("YAGT_DEMO") == "1" || Environment.GetEnvironmentVariable("YAGT_SKIPSTART") == "1";

    public static void Start()
    {
        if (InTest) { Set("spento durante le prove automatiche"); return; }
        if (!Enabled) { Set("spento nelle Impostazioni"); return; }
        if (!Writable()) { Set("spento: la cartella del programma non è scrivibile (aggiornare a mano)"); return; }
        try
        {
            _single = new Mutex(true, "YA-GT-aggiornamento", out bool first);
            if (!first) { Set("gestito da un'altra finestra di YA-GT già aperta"); return; }
        }
        catch { }
        _timer = new Timer(_ => { var t = CheckAsync(false); }, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(IntervalMinutes));
    }

    public static void Stop() { try { _timer?.Dispose(); } catch { } _timer = null; }

    static string PackageName(Version v)
    {
        var p = "YA-GT-" + v.ToString(3) + "-";
        bool x86 = RuntimeInformation.ProcessArchitecture == Architecture.X86;
#if NETFRAMEWORK
        return p + (x86 ? "windows7-x86-portatile.zip" : "windows7-x64-portatile.zip");
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? p + "windows-x64-portatile.zip" : null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64) return p + "linux-x64.tar.gz";
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return p + "linux-arm64.tar.gz";
        }
        return null;
#endif
    }

    static Version Ver(string s)
    {
        s = (s ?? "").Trim().TrimStart('v', 'V');
        int i = s.IndexOfAny(new[] { '-', '+' });
        if (i > 0) s = s.Substring(0, i);
        return Version.TryParse(s, out var v) ? v : null;
    }

    static string Hex(byte[] b)
    {
        var c = new char[b.Length * 2];
        for (int i = 0; i < b.Length; i++) { var h = b[i].ToString("x2", CultureInfo.InvariantCulture); c[2 * i] = h[0]; c[2 * i + 1] = h[1]; }
        return new string(c);
    }

    /// <summary>Firma r||s (64 byte, base64) dei dati con la chiave di YA-GT.</summary>
    public static bool SignatureValid(byte[] data, string sigB64)
    {
        try
        {
            var sig = Convert.FromBase64String((sigB64 ?? "").Trim());
            var prm = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = Convert.FromBase64String(KeyX), Y = Convert.FromBase64String(KeyY) } };
            using (var ec = ECDsa.Create(prm)) return ec.VerifyData(data, sig, HashAlgorithmName.SHA256);
        }
        catch { return false; }
    }

    /// <summary>Un controllo (dal timer o dal pulsante "Controlla adesso").</summary>
    public static async Task CheckAsync(bool manual)
    {
        if (InTest || (!Enabled && !manual)) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YA-GT/" + AppState.Version);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json;
                using (var rq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repo + "/releases/latest"))
                {
                    if (_etag != null && _lastJson != null) rq.Headers.TryAddWithoutValidation("If-None-Match", _etag);
                    using (var rs = await http.SendAsync(rq))
                    {
                        if (rs.StatusCode == System.Net.HttpStatusCode.NotModified) json = _lastJson;
                        else
                        {
                            if (rs.StatusCode == System.Net.HttpStatusCode.NotFound) { Set("nessuna versione pubblicata su github.com/" + Repo); return; }
                            rs.EnsureSuccessStatusCode();
                            json = await Compat.ReadString(rs.Content, CancellationToken.None);
                            _etag = rs.Headers.ETag?.ToString();
                            _lastJson = json;
                        }
                    }
                }
                string tag;
                var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var d = JsonDocument.Parse(json))
                {
                    tag = d.RootElement.GetProperty("tag_name").GetString();
                    foreach (var a in d.RootElement.GetProperty("assets").EnumerateArray())
                        assets[a.GetProperty("name").GetString()] = a.GetProperty("browser_download_url").GetString();
                }
                Version latest = Ver(tag), current = Ver(AppState.Version);
                if (latest == null || current == null) { Set("versione pubblicata non leggibile: " + tag); return; }
                if (latest <= current) { Set((manual ? "hai già l'ultima versione (" : "aggiornato: l'ultima versione è la ") + latest.ToString(3) + (manual ? ")" : " (" + DateTime.Now.ToString("HH:mm") + ")")); return; }
                var name = PackageName(latest);
                if (name == null) { Set("versione " + latest.ToString(3) + " disponibile su github.com/" + Repo + "/releases"); return; }
                if (!assets.TryGetValue(name, out var urlPkg)) { Set("versione " + latest.ToString(3) + " pubblicata, ma senza il pacchetto " + name); return; }
                if (!assets.TryGetValue(name + ".sha256", out var urlSha) || !assets.TryGetValue(name + ".sig", out var urlSig))
                { Set("versione " + latest.ToString(3) + ": mancano impronta o firma del pacchetto, non si installa"); return; }
                Directory.CreateDirectory(UpdDir);
                var pkg = Path.Combine(UpdDir, name);
                Set("scarico la versione " + latest.ToString(3) + " (" + name + ")");
                byte[] data;
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using (var rs = await http.GetAsync(urlPkg, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                {
                    rs.EnsureSuccessStatusCode();
                    if (rs.Content.Headers.ContentLength > MaxPackage) { Set("pacchetto troppo grande, non si scarica"); return; }
                    using (var s = await rs.Content.ReadAsStreamAsync())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[81920]; int n;
                        while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0)
                        {
                            ms.Write(buf, 0, n);
                            if (ms.Length > MaxPackage) throw new IOException("pacchetto troppo grande");
                        }
                        data = ms.ToArray();
                    }
                }
                var expected = (await Compat.ReadString((await http.GetAsync(urlSha)).EnsureSuccessStatusCode().Content, CancellationToken.None)).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                string actual;
                using (var sha = SHA256.Create()) actual = Hex(sha.ComputeHash(data));
                if (actual != expected) { Set("versione " + latest.ToString(3) + ": impronta SHA-256 diversa, pacchetto scartato"); return; }
                var sig = await Compat.ReadString((await http.GetAsync(urlSig)).EnsureSuccessStatusCode().Content, CancellationToken.None);
                if (!SignatureValid(data, sig)) { Set("versione " + latest.ToString(3) + ": FIRMA NON VALIDA, pacchetto scartato"); return; }
                File.WriteAllBytes(pkg, data);
                Install(pkg, current.ToString(3), latest.ToString(3));
            }
        }
        catch (Exception ex) { Set("controllo non riuscito: " + ex.Message + " (riprovo fra " + IntervalMinutes + " minuti)"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    static bool DoNotTouch(string rel)
    {
        var r = rel.Replace('\\', '/').ToLowerInvariant();
        return r == "ya-gt.ini" || r == "call3.txt" || r.StartsWith("dati/") || r == "errore-avvio.txt";
    }

    static string Rel(string root, string full)
    {
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full);
    }

    static void Install(string pkg, string oldV, string newV)
    {
        var ext = Path.Combine(UpdDir, newV);
        if (Directory.Exists(ext)) Directory.Delete(ext, true);
        Directory.CreateDirectory(ext);
        if (pkg.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
#if NETFRAMEWORK
            Set("pacchetto tar.gz non gestito su questa piattaforma"); return;
#else
            using (var fs = File.OpenRead(pkg))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                System.Formats.Tar.TarFile.ExtractToDirectory(gz, ext, true);
#endif
        }
        else ZipFile.ExtractToDirectory(pkg, ext);
        var root = ext;
        if (Directory.GetFiles(ext).Length == 0 && Directory.GetDirectories(ext).Length == 1) root = Directory.GetDirectories(ext)[0];
        var prev = Path.Combine(UpdDir, "precedente-" + oldV);
        var renamed = new List<string>(); var added = new List<string>();
        try
        {
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Rel(root, f);
                if (DoNotTouch(rel)) continue;
                var dest = Path.Combine(Settings.AppDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (File.Exists(dest))
                {
                    var bk = Path.Combine(prev, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bk));
                    File.Copy(dest, bk, true);
                    var old = dest + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(dest, old);
                    renamed.Add(dest);
                }
                else added.Add(dest);
                File.Copy(f, dest, false);
            }
            File.WriteAllText(Path.Combine(UpdDir, "applicato.txt"), "aggiornato da " + oldV + " a " + newV + " il " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            Set("versione " + newV + " installata: entra in funzione al prossimo avvio di YA-GT");
            Stop();
            if (_announced != newV) { _announced = newV; try { Ready?.Invoke(newV); } catch { } }
        }
        catch (Exception ex)
        {
            foreach (var d in added) try { File.Delete(d); } catch { }
            foreach (var d in renamed) try { if (File.Exists(d)) File.Delete(d); File.Move(d + ".old", d); } catch { }
            Set("installazione della " + newV + " non riuscita, rimessa la " + oldV + ": " + ex.Message);
        }
        finally { try { Directory.Delete(ext, true); } catch { } try { File.Delete(pkg); } catch { } }
    }
}
