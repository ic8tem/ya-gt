using System.Globalization;

namespace YaGt.Core;

/// <summary>Una stazione di QLog da mostrare sulla mappa.</summary>
public sealed class QLogPoint
{
    public string Call = "";
    public LatLon Pos;
    public bool Approx;
    public bool Lotw, Qsl;
    public int Qsos;
    public string Bands = "";
}

/// <summary>Fotografia dei dati per il disegno: si rifà a ogni aggiornamento (1-30 minuti) o su richiesta.</summary>
public sealed class Frame
{
    public DateTime Utc = DateTime.UtcNow;
    public List<SpotItem> HeardMe = new List<SpotItem>();
    public List<SpotItem> IHeard = new List<SpotItem>();
    public List<Decode> Decodes = new List<Decode>();
    public Dictionary<string, SquareInfo> Squares = new Dictionary<string, SquareInfo>();
    public bool SquaresGrey;
    public string SquaresTitle = "";
    public List<QLogPoint> QLog = new List<QLogPoint>();
    public FrameStats Stats = new FrameStats();
}

/// <summary>Statistiche del riquadro in alto a destra.</summary>
public sealed class DirStats
{
    public int Stations, Squares, Countries;
    public double MaxKm; public string MaxCall = "";
    public int? BestSnr; public string BestCall = "";
}

public sealed class FrameStats
{
    public string Band = "";
    public DirStats HeardMe = new DirStats(), IHeard = new DirStats(), Decodes = new DirStats();
    public Dictionary<string, int> BySource = new Dictionary<string, int>();
    public int SqWorked, SqConfirmed, SqLotw, SqAllHf;
    public bool HasSquares;
}

/// <summary>Stato del programma: impostazioni, fonti, archivi, livelli della mappa e aggiornamento periodico.</summary>
public sealed class AppState
{
    public readonly Settings S = new Settings();
    public readonly CallDb CallDb = new CallDb();
    public readonly Cty Cty = new Cty();
    public readonly SpotStore Spots = new SpotStore();
    public readonly DecodeStore Decodes = new DecodeStore();
    public readonly PskMqtt Mqtt = new PskMqtt();
    public readonly WsjtxUdp Udp = new WsjtxUdp();
    public readonly Kc2gData Kc2g = new Kc2gData();
    public readonly AuroraData Aurora = new AuroraData();
    public readonly LotwLog Lotw = new LotwLog();
    public readonly QLogDb QLog = new QLogDb();
    public readonly Dictionary<string, TelnetFeed> Feeds = new Dictionary<string, TelnetFeed>();
    public readonly HttpClient Http;

    // --- stazione
    public string MyCall = "";
    public string MyLoc = "";
    public LatLon Home;

    // --- livelli (tutti attivabili a scelta)
    public bool LayerStats, LayerGrid, LayerGreyline, LayerKc2g, LayerAurora, LayerHeardMe, LayerIHeard, LayerDecodes, LayerLotw, LayerQLog, LayerPaths;
    public string Kc2gMetric = "mufd";
    public int Kc2gAlpha = 85, AuroraAlpha = 70;   // percento
    public string LotwTag = "BAND";   // BAND = la banda scelta in alto
    public bool LotwOnlyConfirmed, LotwOnlyEme;
    public bool BubbleCalls = true;

    // --- fonti
    public bool SrcPsk, SrcRbn, SrcDm4x, SrcCl2, Cl2Skimmer, UdpEnabled;

    // --- filtri e aggiornamento
    public string BandFilter = "";
    public int AgeMin = 30;
    public int RefreshMin = 3;
    public static readonly int[] RefreshChoices = { 1, 3, 5, 10, 15, 30 };
    public static readonly int[] AgeChoices = { 3, 5, 15, 30, 60, 120, 360 };
    public ProjKind Proj = ProjKind.Mercator;
    public string ThemeKey = "chiaro";
    public string QLogPath = "";

    public Frame Current = new Frame();
    public DateTime LastRefresh = DateTime.MinValue;
    public DateTime NextRefresh = DateTime.MinValue;
    public string Status = "";
    public string Call3Status = "";
    /// <summary>"aggiornato da … a …" se un aggiornamento automatico è appena entrato in funzione.</summary>
    public string UpdateMessage;

    /// <summary>Dati pronti per un nuovo disegno (chiamato da un thread qualsiasi).</summary>
    public event Action Updated;
    /// <summary>Cambio di stato delle connessioni (barra di stato).</summary>
    public event Action ConnectionsChanged;

    Timer _timer;
    int _refreshBusy;
    readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public AppState()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("YA-GT/" + Version + " (+IC8TEM; ic8tem@gmail.com)");
        LoadSettings();
    }

    public static string Version
    {
        get
        {
            var v = typeof(AppState).Assembly.GetName().Version;
            return v == null ? "1.0.0" : v.Major + "." + v.Minor + "." + v.Build;
        }
    }

    // ------------------------------------------------------------------ impostazioni

    void LoadSettings()
    {
        MyCall = S.Get("call", "").ToUpperInvariant();
        MyLoc = S.Get("loc", "");
        Locator.TryToLatLon(MyLoc, out Home);
        LayerGrid = S.GetBool("layer_grid", true);
        LayerGreyline = S.GetBool("layer_greyline", true);
        LayerKc2g = S.GetBool("layer_kc2g", false);
        LayerAurora = S.GetBool("layer_aurora", false);
        LayerHeardMe = S.GetBool("layer_heardme", true);
        LayerIHeard = S.GetBool("layer_iheard", true);
        LayerDecodes = S.GetBool("layer_decodes", true);
        LayerLotw = S.GetBool("layer_lotw", false);
        LayerQLog = S.GetBool("layer_qlog", false);
        LayerPaths = S.GetBool("layer_paths", false);
        Kc2gMetric = S.Get("kc2g_metric", "mufd");
        Kc2gAlpha = S.GetInt("kc2g_alpha", 85);
        AuroraAlpha = S.GetInt("aurora_alpha", 70);
        LotwTag = S.GetBool("lotw_tag_v2", false) ? S.Get("lotw_tag", "BAND") : "BAND";   // 1.0.2: i quadrati seguono la banda scelta in alto
        LayerStats = S.GetBool("layer_stats", true);
        LotwOnlyConfirmed = S.GetBool("lotw_only_confirmed", false);
        LotwOnlyEme = S.GetBool("lotw_only_eme", false);
        BubbleCalls = S.GetBool("bubble_calls", true);
        SrcPsk = S.GetBool("src_psk", true);
        SrcRbn = S.GetBool("src_rbn", true);
        SrcDm4x = S.GetBool("src_dm4x", true);
        SrcCl2 = S.GetBool("src_cl2", false);
        Cl2Skimmer = S.GetBool("cl2_skimmer", true);
        UdpEnabled = S.GetBool("udp", true);
        Udp.ListenAddress = S.Get("udp_addr", "127.0.0.1");
        Udp.ListenPort = S.GetInt("udp_port", 2237);
        Udp.Forward = S.GetBool("udp_forward", false);
        Udp.ForwardTo = S.Get("udp_forward_to", "127.0.0.1:2238");
        BandFilter = S.Get("band", "");
        AgeMin = S.GetInt("age_min", 30);
        RefreshMin = S.GetInt("refresh_min", 3);
        if (Array.IndexOf(RefreshChoices, RefreshMin) < 0) RefreshMin = 3;
        Proj = S.Get("proj", "merc") == "azi" ? ProjKind.Azimuthal : ProjKind.Mercator;
        ThemeKey = S.Get("theme", "chiaro");
        QLogPath = S.Get("qlog_path", "");
        Updater.Enabled = S.GetBool("auto_update", true);
    }

    public void SaveSettings()
    {
        S.Set("call", MyCall); S.Set("loc", MyLoc);
        S.SetBool("layer_grid", LayerGrid); S.SetBool("layer_greyline", LayerGreyline); S.SetBool("layer_kc2g", LayerKc2g);
        S.SetBool("layer_aurora", LayerAurora); S.SetBool("layer_heardme", LayerHeardMe); S.SetBool("layer_iheard", LayerIHeard);
        S.SetBool("layer_decodes", LayerDecodes); S.SetBool("layer_lotw", LayerLotw); S.SetBool("layer_qlog", LayerQLog); S.SetBool("layer_paths", LayerPaths);
        S.Set("kc2g_metric", Kc2gMetric); S.SetInt("kc2g_alpha", Kc2gAlpha); S.SetInt("aurora_alpha", AuroraAlpha);
        S.Set("lotw_tag", LotwTag); S.SetBool("lotw_tag_v2", true); S.SetBool("layer_stats", LayerStats); S.SetBool("lotw_only_confirmed", LotwOnlyConfirmed); S.SetBool("lotw_only_eme", LotwOnlyEme);
        S.SetBool("bubble_calls", BubbleCalls);
        S.SetBool("src_psk", SrcPsk); S.SetBool("src_rbn", SrcRbn); S.SetBool("src_dm4x", SrcDm4x); S.SetBool("src_cl2", SrcCl2); S.SetBool("cl2_skimmer", Cl2Skimmer);
        S.SetBool("udp", UdpEnabled); S.Set("udp_addr", Udp.ListenAddress); S.SetInt("udp_port", Udp.ListenPort);
        S.SetBool("udp_forward", Udp.Forward); S.Set("udp_forward_to", Udp.ForwardTo);
        S.Set("band", BandFilter); S.SetInt("age_min", AgeMin); S.SetInt("refresh_min", RefreshMin);
        S.Set("proj", Proj == ProjKind.Azimuthal ? "azi" : "merc"); S.Set("theme", ThemeKey); S.Set("qlog_path", QLogPath); S.SetBool("auto_update", Updater.Enabled);
        S.Save();
    }

    // ------------------------------------------------------------------ avvio

    /// <summary>Imposta nominativo e locatore scelti nella finestra di avvio.</summary>
    public void SetStation(string call, string loc)
    {
        MyCall = (call ?? "").Trim().ToUpperInvariant();
        MyLoc = (loc ?? "").Trim();
        if (MyLoc.Length == 6) MyLoc = MyLoc.Substring(0, 4).ToUpperInvariant() + MyLoc.Substring(4).ToLowerInvariant();
        Locator.TryToLatLon(MyLoc, out Home);
        SaveSettings();
    }

    public void Start()
    {
        BaseMap.Load();
        Task.Run(() =>
        {
            try
            {
                var dirs = new[] { Settings.AppDir, Settings.DataDir, Directory.GetCurrentDirectory() };
                CallDb.Load(dirs.Select(d => Path.Combine(d, "CALL3.TXT")));
                Cty.Load(dirs.Select(d => Path.Combine(d, "cty.dat")).FirstOrDefault(File.Exists) ?? "");
                G7Lookup.Found = (c, l) => CallDb.Learn(c, l);
                _ = G7Lookup.RunAsync(Http, () => { }, _cts.Token);
                _ = Call3LoopAsync(_cts.Token);
                Lotw.LoadCached();
            }
            catch (Exception ex) { ErrorLog.Write("avvio", ex); }
            RefreshNow(true);
        });
        Mqtt.MyCall = MyCall;
        Mqtt.Spot += OnSpot;
        Mqtt.StateChanged += () => ConnectionsChanged?.Invoke();
        Udp.Decoded += d => { Decodes.Add(d); };
        Udp.StateChanged += () => ConnectionsChanged?.Invoke();
        // cluster telnet: indirizzi fissi (DM4X come in LIVETROPOVHF; secondo cluster DXMAPS con la rete skimmer attivabile)
        AddFeed("rbn", "RBN", "telnet.reversebeacon.net", 7000, "");
        AddFeed("rbn2", "RBN", "telnet.reversebeacon.net", 7001, "");
        AddFeed("dm4x", "DM4X", "dm4x.ddns.net", 8500, "");
        AddFeed("cl2", "CL2", "dxmaps.com", 7300, Cl2Skimmer ? "set/skimmer" : "set/noskimmer");
        if (Environment.GetEnvironmentVariable("YAGT_DEMO") == "1") InjectDemo();
        ApplySources();
        ScheduleTimer();
        UpdateMessage = Updater.CleanAfterStart();
        Updater.Start();
    }

    void AddFeed(string id, string src, string host, int port, string cmds)
    {
        var f = new TelnetFeed { Id = id, Src = src, Host = host, Port = port, Cmds = cmds, Reconnect = true };
        f.LineReceived += line => OnClusterLine(src, line);
        f.StateChanged += () => ConnectionsChanged?.Invoke();
        Feeds[id] = f;
    }

    /// <summary>Accende o spegne le fonti secondo le caselle.</summary>
    public void ApplySources()
    {
        if (SrcPsk && !string.IsNullOrEmpty(MyCall)) Mqtt.Connect(); else if (Mqtt.Wanted) Mqtt.Disconnect();
        void F(string id, bool on) { var f = Feeds[id]; if (on && !string.IsNullOrEmpty(MyCall)) f.Connect(MyCall); else if (f.Wanted) f.Disconnect(); }
        F("rbn", SrcRbn); F("rbn2", SrcRbn); F("dm4x", SrcDm4x); F("cl2", SrcCl2);
        if (UdpEnabled) { if (!Udp.Running) Udp.Start(); } else Udp.Stop();
        ConnectionsChanged?.Invoke();
    }

    public void SetSkimmer(bool on)
    {
        Cl2Skimmer = on;
        var f = Feeds["cl2"];
        f.Cmds = on ? "set/skimmer" : "set/noskimmer";
        if (f.Connected) f.Send(f.Cmds);
        SaveSettings();
    }

    public void RestartUdp() { Udp.Stop(); if (UdpEnabled) Udp.Start(); ConnectionsChanged?.Invoke(); }

    void OnClusterLine(string src, string line)
    {
        try
        {
            var s = ClusterParser.Parse(line, src, MyCall, DateTime.UtcNow);
            if (s == null) return;
            // DM4X/RBN: il locatore di chi spotta, a 6 caratteri, è dichiarato dalla stazione stessa → correzione in CALL3.TXT
            if (s.RxGrid.Length == 6 && (src == "DM4X" || src == "RBN")) CallDb.SetCluster(ClusterParser.SkimmerBase(s.Rx), s.RxGrid);
            Spots.Add(s);
        }
        catch { }
    }

    void OnSpot(SpotItem s)
    {
        // locatori a 6 caratteri imparati dal vivo entrano in CALL3.TXT (solo se il nominativo non c'è o ha 4 caratteri)
        if (s.TxGrid.Length == 6) CallDb.Learn(s.Tx, s.TxGrid);
        if (s.RxGrid.Length == 6) CallDb.Learn(ClusterParser.SkimmerBase(s.Rx), s.RxGrid);
        Spots.Add(s);
    }

    // ------------------------------------------------------------------ aggiornamento periodico

    public void SetRefresh(int minutes)
    {
        if (Array.IndexOf(RefreshChoices, minutes) < 0) return;
        RefreshMin = minutes;
        SaveSettings();
        ScheduleTimer();
    }

    void ScheduleTimer()
    {
        var period = TimeSpan.FromMinutes(RefreshMin);
        NextRefresh = DateTime.UtcNow + period;
        if (_timer == null) _timer = new Timer(_ => RefreshNow(false), null, period, period);
        else _timer.Change(period, period);
    }

    /// <summary>Aggiorna subito i dati (scarica ciò che serve) e rifà la fotografia per la mappa.</summary>
    public void RefreshNow(bool first = false)
    {
        if (Interlocked.Exchange(ref _refreshBusy, 1) == 1) return;
        Task.Run(async () =>
        {
            try
            {
                var ct = _cts.Token;
                var jobs = new List<Task>();
                if (SrcPsk && !string.IsNullOrEmpty(MyCall))
                    jobs.Add(Task.Run(async () => { foreach (var s in await PskHttp.FetchAsync(Http, MyCall, Math.Max(15, Math.Min(AgeMin, 120)), ct)) OnSpot(s); }));
                if (LayerKc2g) jobs.Add(Kc2g.LoadAsync(Http, Kc2gMetric, ct));
                if (LayerAurora) jobs.Add(Aurora.LoadAsync(Http, ct));
                if (LayerQLog || LayerLotw) jobs.Add(Task.Run(() => QLog.Load(QLogPath)));
                try { await Task.WhenAll(jobs); } catch { }
                Spots.Prune(DateTime.UtcNow, Math.Max(AgeMin, 120));
                Decodes.Prune(DateTime.UtcNow, Math.Max(AgeMin, 120));
                BuildFrame();
                _ = Task.Run(() => CallDb.Save());
                LastRefresh = DateTime.UtcNow;
                NextRefresh = LastRefresh + TimeSpan.FromMinutes(RefreshMin);
                Status = StatusText();
            }
            catch (Exception ex) { ErrorLog.Write("aggiornamento", ex); }
            finally { Interlocked.Exchange(ref _refreshBusy, 0); }
            Updated?.Invoke();
        });
    }

    /// <summary>Rifà solo la fotografia (cambio di filtro o di livello), senza scaricare.</summary>
    public void Rebuild()
    {
        Task.Run(() =>
        {
            try { BuildFrame(); } catch (Exception ex) { ErrorLog.Write("disegno", ex); }
            Updated?.Invoke();
        });
    }

    /// <summary>Posizione di una stazione: locatore ricevuto (6 caratteri), CALL3.TXT, locatore a 4 caratteri, cty.dat.</summary>
    public bool Resolve(string call, string grid, out LatLon pos, out bool approx, out string src)
    {
        approx = false; src = "";
        pos = default(LatLon);
        grid = (grid ?? "").Trim();
        if (grid.Length >= 6 && Locator.TryToLatLon(grid.Substring(0, 6), out pos)) { src = "spot"; return true; }
        var db = CallDb.Lookup(call);
        if (db == "") db = CallDb.Lookup(ClusterParser.SkimmerBase(call));
        if (db.Length >= 6 && (grid.Length < 4 || db.StartsWith(grid.Substring(0, 4), StringComparison.OrdinalIgnoreCase)) && Locator.TryToLatLon(db, out pos)) { src = "call3"; return true; }
        if (db.Length < 6 && !string.IsNullOrEmpty(call)) G7Lookup.Request(ClusterParser.SkimmerBase(call));
        if (grid.Length >= 4 && Locator.TryToLatLon(grid.Substring(0, 4), out pos)) { src = "spot"; return true; }
        if (db.Length >= 4 && Locator.TryToLatLon(db, out pos)) { src = "call3"; return true; }
        if (Cty.TryLookup(call, out pos, out _)) { approx = true; src = "cty"; return true; }
        return false;
    }

    void Place(SpotItem s)
    {
        if (Resolve(s.Other, s.OtherGrid, out var p, out var approx, out var src))
        {
            s.Pos = p; s.HasPos = true; s.PosApprox = approx; s.PosSource = src;
            s.Km = Geo.DistKm(Home, p); s.Az = Geo.Azimuth(Home, p);
        }
    }

    public void BuildFrame()
    {
        var now = DateTime.UtcNow;
        var f = new Frame { Utc = now };
        var all = Spots.Snapshot(now, AgeMin);
        if (BandFilter != "") all = all.Where(s => s.Band == BandFilter).ToList();
        // stessa stazione da più fonti: si tiene il rapporto più recente (a parità, quello con l'SNR)
        foreach (var g in all.GroupBy(s => (s.Dir == SpotDir.HeardMe ? "M|" : "I|") + s.Other + "|" + s.Band))
        {
            var best = g.OrderByDescending(s => s.Time).ThenByDescending(s => s.Snr.HasValue).First();
            Place(best);
            if (!best.HasPos) continue;
            if (best.Dir == SpotDir.HeardMe) f.HeardMe.Add(best); else f.IHeard.Add(best);
        }
        foreach (var d in Decodes.Snapshot(now, AgeMin))
        {
            if (BandFilter != "" && d.Band != BandFilter) continue;
            if (Resolve(d.Call, d.Grid, out var p, out var approx, out _)) { d.Pos = p; d.HasPos = true; d.PosApprox = approx; d.Km = Geo.DistKm(Home, p); d.Az = Geo.Azimuth(Home, p); f.Decodes.Add(d); }
        }
        if (LayerLotw)
        {
            var tag = EffectiveLotwTag;
            // i quadrati si colorano con LoTW/ADIF e, se c'è, anche con il log di QLog
            f.Squares = Lotw.BuildSquares(tag, LotwOnlyConfirmed, LotwOnlyEme, QLog.Qsos.Count > 0 ? QLog.Qsos : null);
            f.SquaresGrey = LotwLog.GreyTab(tag);
            f.SquaresTitle = tag == "ALL" ? "tutte le bande" : tag == "G:1" ? "HF 80-10 m" : tag == "G:2" ? "VHF" : tag == "G:3" ? "UHF e oltre" : tag.Substring(2);
        }
        f.Stats = ComputeStats(f);
        if (LayerQLog)
        {
            var qs = QLog.Qsos;
            if (BandFilter != "") qs = qs.Where(q => q.Band == BandFilter).ToList();
            foreach (var g in qs.GroupBy(q => q.Call))
            {
                var grid = g.Select(q => q.Grid).OrderByDescending(x => x.Length).FirstOrDefault() ?? "";
                if (!Resolve(g.Key, grid, out var p, out var approx, out _)) continue;
                f.QLog.Add(new QLogPoint
                {
                    Call = g.Key, Pos = p, Approx = approx, Lotw = g.Any(q => q.Lotw), Qsl = g.Any(q => q.Qsl), Qsos = g.Count(),
                    Bands = string.Join(" ", g.Select(q => q.Band).Distinct().OrderBy(Bands.Order)),
                });
            }
        }
        Current = f;
    }

    /// <summary>Gruppo dei quadrati: "BAND" segue la banda scelta in alto (tutte le bande se non è scelta).</summary>
    public string EffectiveLotwTag => LotwTag == "BAND" ? (BandFilter == "" ? "ALL" : "B:" + BandFilter) : LotwTag;

    DirStats Dir(IEnumerable<(string call, LatLon pos, bool approx, double km, int? snr)> items)
    {
        var d = new DirStats();
        var sq = new HashSet<string>(); var cty = new HashSet<string>();
        foreach (var it in items)
        {
            d.Stations++;
            var g4 = Locator.FromLatLon(it.pos.Lat, it.pos.Lon); if (!it.approx && g4.Length >= 4) sq.Add(g4.Substring(0, 4).ToUpperInvariant());
            if (Cty.TryLookup(it.call, out _, out var c)) cty.Add(c);
            if (it.km > d.MaxKm) { d.MaxKm = it.km; d.MaxCall = it.call; }
            if (it.snr.HasValue && (!d.BestSnr.HasValue || it.snr.Value > d.BestSnr.Value)) { d.BestSnr = it.snr; d.BestCall = it.call; }
        }
        d.Squares = sq.Count; d.Countries = cty.Count;
        return d;
    }

    FrameStats ComputeStats(Frame f)
    {
        var s = new FrameStats { Band = BandFilter == "" ? "tutte le bande" : BandFilter };
        s.HeardMe = Dir(f.HeardMe.Select(x => (x.Other, x.Pos, x.PosApprox, x.Km, x.Snr)));
        s.IHeard = Dir(f.IHeard.Select(x => (x.Other, x.Pos, x.PosApprox, x.Km, x.Snr)));
        s.Decodes = Dir(f.Decodes.Select(x => (x.Call, x.Pos, x.PosApprox, x.Km, (int?)x.Snr)));
        foreach (var x in f.HeardMe.Concat(f.IHeard))
        {
            var k = x.Source == "PSK" ? "PSKReporter" : x.Source == "CL2" ? "DXMAPS" : x.Source;
            s.BySource[k] = s.BySource.TryGetValue(k, out var n) ? n + 1 : 1;
        }
        if (LayerLotw && f.Squares.Count > 0)
        {
            s.HasSquares = true;
            s.SqWorked = f.Squares.Count;
            s.SqConfirmed = f.Squares.Values.Count(q => q.Lotw || q.Qsl);
            s.SqLotw = f.Squares.Values.Count(q => q.Lotw);
            s.SqAllHf = f.SquaresGrey ? f.Squares.Values.Count(q => q.AllHf) : 0;
        }
        return s;
    }

    public string StatusText()
    {
        var parts = new List<string>();
        parts.Add($"{Current.HeardMe.Count} mi sentono · {Current.IHeard.Count} sento · {Current.Decodes.Count} decodifiche");
        if (CallDb.Ready) parts.Add($"CALL3.TXT {CallDb.Count:N0}");
        if (LayerKc2g && Kc2g.Status != "") parts.Add(Kc2g.Status);
        if (LayerAurora && Aurora.Status != "") parts.Add(Aurora.Status);
        if (LayerQLog && QLog.Status != "") parts.Add(QLog.Status);
        if (LayerLotw && Lotw.Status != "") parts.Add(Lotw.Status);
        if (G7Lookup.Pending > 0) parts.Add($"G7VRD in coda {G7Lookup.Pending}");
        return string.Join("  ·  ", parts);
    }

    /// <summary>Stato delle connessioni, per la barra di stato.</summary>
    public string ConnectionsText()
    {
        string On(bool wanted, bool ok, string name) => !wanted ? "" : name + (ok ? " ●" : " ○");
        var l = new List<string>
        {
            On(Mqtt.Wanted, Mqtt.Connected, "PSKReporter"),
            On(Feeds["rbn"].Wanted, Feeds["rbn"].Connected || Feeds["rbn2"].Connected, "RBN"),
            On(Feeds["dm4x"].Wanted, Feeds["dm4x"].Connected, "DM4X"),
            On(Feeds["cl2"].Wanted, Feeds["cl2"].Connected, "DXMAPS" + (Cl2Skimmer ? "+skimmer" : "")),
            UdpEnabled ? (Udp.Running ? $"UDP {Udp.ListenPort} ●" + (Udp.Forward ? " → inoltro" : "") : "UDP ○ " + Udp.LastError) : "",
        };
        return string.Join("   ", l.Where(x => x != ""));
    }

    // ------------------------------------------------------------------ CALL3.TXT dalle fonti (controllo ogni 6 ore)

    DateTime Call3Last
    {
        get => DateTime.TryParse(S.Get("call3_last", ""), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
        set => S.Set("call3_last", value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    async Task Call3LoopAsync(CancellationToken ct)
    {
        // il CALL3.TXT del pacchetto è appena fuso: la prima volta si conta da adesso
        if (Call3Last == DateTime.MinValue) { Call3Last = DateTime.UtcNow; S.Save(); }
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch { return; }
            if (DateTime.UtcNow - Call3Last < TimeSpan.FromDays(7)) continue;
            await UpdateCall3Async();
        }
    }

    int _call3Busy;

    /// <summary>
    /// Riscrive CALL3.TXT dalle fonti (WSJT-X Improved, W7GJ, JTDX), come LIVETROPOVHF e MSHV-IC8TEM: salva il vecchio come
    /// CALL3.bak; voci a mano e correzioni dal cluster restano in cima.
    /// </summary>
    public async Task<bool> UpdateCall3Async()
    {
        if (Interlocked.Exchange(ref _call3Busy, 1) == 1) return false;
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YA-GT/" + Version);
                var built = await Call3Sources.BuildAsync(http, CallDb.BodySnapshot(), m => Call3Status = m);
                var dest = CallDb.TargetPath;
                if (File.Exists(dest))
                {
                    File.SetAttributes(dest, FileAttributes.Normal);
                    var bak = Path.ChangeExtension(dest, ".bak");
                    if (File.Exists(bak)) File.SetAttributes(bak, FileAttributes.Normal);
                    File.Copy(dest, bak, true);
                }
                CallDb.ReplaceBody(built.header, built.body);
                if (!await Task.Run(() => CallDb.Save(true))) throw new IOException(CallDb.Error);
                Call3Last = DateTime.UtcNow;
                S.Save();
                Call3Status = $"CALL3.TXT riscritto: {CallDb.Count:N0} nominativi — {built.summary}";
                return true;
            }
        }
        catch (Exception ex) { Call3Status = "Aggiornamento di CALL3.TXT non riuscito: " + ex.Message; return false; }
        finally { Interlocked.Exchange(ref _call3Busy, 0); }
    }

    /// <summary>
    /// Solo per la prova automatica su GitHub (variabile d'ambiente YAGT_DEMO=1): qualche spot e decodifica di esempio, per
    /// fotografare fumetti e nuvolette anche quando nessuno sta ascoltando il nominativo di prova.
    /// </summary>
    public void InjectDemo()
    {
        var now = DateTime.UtcNow;
        var heard = new[] { ("W1AW", "FN31pr", -12, "20m"), ("JA1YXP", "PM95vq", -18, "20m"), ("VK2IO", "QF56od", -20, "20m"), ("PY2SEX", "GG66qn", -8, "15m"),
                            ("ZS6KN", "KG44df", -15, "20m"), ("G4ABC", "IO91wm", 3, "40m"), ("DL1KJ", "JO62qm", -3, "40m"), ("UA9CGL", "MO06ab", -10, "20m"),
                            ("K6XX", "CM87um", -22, "20m"), ("LU1AA", "GF05tj", -16, "15m"), ("OH2BH", "KP20le", -6, "20m"), ("EA8AH", "", 21, "20m") };
        int i = 0;
        foreach (var h in heard)
            Spots.Add(new SpotItem { Time = now.AddMinutes(-i++), Source = h.Item1 == "EA8AH" ? "RBN" : "PSK", Dir = SpotDir.HeardMe, Tx = MyCall, Rx = h.Item1, RxGrid = h.Item2, Snr = h.Item3, Band = h.Item4, Mode = h.Item1 == "EA8AH" ? "CW" : "FT8", FreqKHz = 14074 });
        var iheard = new[] { ("VE3NEA", "FN03ai", -14), ("4X1MK", "KM72jb", -9), ("RA3AUU", "KO85ts", -1), ("5B4AHJ", "KM64lr", -19) };
        foreach (var h in iheard)
            Spots.Add(new SpotItem { Time = now.AddMinutes(-i++), Source = "PSK", Dir = SpotDir.IHeard, Tx = h.Item1, Rx = MyCall, TxGrid = h.Item2, Snr = h.Item3, Band = "20m", Mode = "FT8", FreqKHz = 14074 });
        // qualche QSO di esempio per i quadrati colorati (lavorato / LoTW / QSL)
        lock (Lotw.Lock)
        {
            int k = 0;
            foreach (var g in new[] { "JN70", "JN61", "JN63", "JN54", "JO62", "IO91", "IN80", "KM18", "KN04", "FN31", "PM95", "QF56", "KP20", "JN95", "JN88", "IM58", "KO85", "GG66" })
            {
                var q = new QsoRecord { Call = "DEMO" + k, Band = k % 3 == 0 ? "40m" : "20m", Date = "20261126", Time = (1000 + k).ToString(), Grid = g, Lotw = k % 3 == 1, Qsl = k % 3 == 2 };
                q.AddGrid(g); Lotw.Qsos.Add(q); k++;
            }
        }
        foreach (var d in new[] { ("SV1ABC", "KM18", -7), ("9A2AA", "JN95", -13), ("OE1XYZ", "JN88", 2), ("CT1AAA", "IM58", -17) })
            Decodes.Add(new Decode { Time = now, Band = "20m", Mode = "FT8", Snr = d.Item3, Call = d.Item1, Grid = d.Item2, Message = "CQ " + d.Item1 + " " + d.Item2 });
    }

    public void Shutdown()
    {
        try { _cts.Cancel(); } catch { }
        try { _timer?.Dispose(); } catch { }
        try { Mqtt.Disconnect(); } catch { }
        foreach (var f in Feeds.Values) try { f.Disconnect(); } catch { }
        try { Udp.Stop(); } catch { }
        try { CallDb.Save(true); } catch { }
        try { Updater.Stop(); } catch { }
        SaveSettings();
    }
}
