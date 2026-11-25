using System.Globalization;

namespace YaGt.Core;

/// <summary>Punto geografico in gradi.</summary>
public readonly struct LatLon
{
    public readonly double Lat;
    public readonly double Lon;
    public LatLon(double lat, double lon) { Lat = lat; Lon = lon; }
    public override string ToString() => Lat.ToString("0.00", CultureInfo.InvariantCulture) + "," + Lon.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Locatore Maidenhead (4, 6, 8 o 10 caratteri) e conversioni (come in LIVETROPOVHF).</summary>
public static class Locator
{
    public static bool TryToLatLon(string loc, out LatLon p)
    {
        p = default(LatLon);
        if (string.IsNullOrWhiteSpace(loc)) return false;
        var l = loc.Trim().ToUpperInvariant();
        if (l.Length != 4 && l.Length != 6 && l.Length != 8 && l.Length != 10) return false;
        if (l[0] < 'A' || l[0] > 'R' || l[1] < 'A' || l[1] > 'R') return false;
        if (l[2] < '0' || l[2] > '9' || l[3] < '0' || l[3] > '9') return false;
        double lon = (l[0] - 'A') * 20 - 180 + (l[2] - '0') * 2;
        double lat = (l[1] - 'A') * 10 - 90 + (l[3] - '0');
        double w = 2, h = 1;
        if (l.Length >= 6)
        {
            if (l[4] < 'A' || l[4] > 'X' || l[5] < 'A' || l[5] > 'X') return false;
            w /= 24; h /= 24; lon += (l[4] - 'A') * w; lat += (l[5] - 'A') * h;
        }
        if (l.Length >= 8)
        {
            if (l[6] < '0' || l[6] > '9' || l[7] < '0' || l[7] > '9') return false;
            w /= 10; h /= 10; lon += (l[6] - '0') * w; lat += (l[7] - '0') * h;
        }
        if (l.Length >= 10)
        {
            if (l[8] < 'A' || l[8] > 'X' || l[9] < 'A' || l[9] > 'X') return false;
            w /= 24; h /= 24; lon += (l[8] - 'A') * w; lat += (l[9] - 'A') * h;
        }
        p = new LatLon(lat + h / 2, lon + w / 2);
        return true;
    }

    public static bool IsValid(string loc) => TryToLatLon(loc, out _);

    /// <summary>Locatore a 6 caratteri valido (per la finestra di avvio).</summary>
    public static bool IsValid6(string loc) => loc != null && loc.Trim().Length == 6 && IsValid(loc);

    public static string FromLatLon(double lat, double lon)
    {
        double x = lon + 180, y = lat + 90;
        if (x < 0 || x >= 360 || y < 0 || y >= 180) return "";
        const string A = "ABCDEFGHIJKLMNOPQR";
        const string a = "abcdefghijklmnopqrstuvwx";
        var c = new char[6];
        c[0] = A[(int)(x / 20)];
        c[1] = A[(int)(y / 10)];
        c[2] = (char)('0' + (int)((x % 20) / 2));
        c[3] = (char)('0' + (int)(y % 10));
        c[4] = a[Math.Min(23, (int)((x % 2) * 12))];
        c[5] = a[Math.Min(23, (int)((y % 1) * 24))];
        return new string(c);
    }

    /// <summary>Quadrato a 4 caratteri (maiuscolo) o null.</summary>
    public static string Square4(string g)
    {
        if (g == null) return null;
        var s = g.Trim().ToUpperInvariant();
        if (s.Length < 4) return null;
        if (s[0] < 'A' || s[0] > 'R' || s[1] < 'A' || s[1] > 'R' || s[2] < '0' || s[2] > '9' || s[3] < '0' || s[3] > '9') return null;
        return s.Substring(0, 4);
    }

    /// <summary>Angolo sud-ovest di un quadrato a 4 caratteri (2° × 1°).</summary>
    public static bool SquareOrigin(string g, out double lon, out double lat)
    {
        lon = lat = 0;
        if (g == null || g.Length < 4) return false;
        lon = (g[0] - 'A') * 20 - 180 + (g[2] - '0') * 2;
        lat = (g[1] - 'A') * 10 - 90 + (g[3] - '0');
        return true;
    }
}

public static class Geo
{
    public const double EarthKm = 6371.0;
    public const double D2R = Math.PI / 180.0;

    public static double DistKm(LatLon a, LatLon b)
    {
        double dLat = (b.Lat - a.Lat) * D2R, dLon = (b.Lon - a.Lon) * D2R;
        double h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(a.Lat * D2R) * Math.Cos(b.Lat * D2R) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static double Azimuth(LatLon a, LatLon b)
    {
        double dLon = (b.Lon - a.Lon) * D2R;
        double y = Math.Sin(dLon) * Math.Cos(b.Lat * D2R);
        double x = Math.Cos(a.Lat * D2R) * Math.Sin(b.Lat * D2R) - Math.Sin(a.Lat * D2R) * Math.Cos(b.Lat * D2R) * Math.Cos(dLon);
        return (Math.Atan2(y, x) / D2R + 360) % 360;
    }

    /// <summary>Punto a una distanza (km) e un azimut (gradi) da un punto di partenza, sul cerchio massimo.</summary>
    public static LatLon Destination(LatLon from, double azDeg, double km)
    {
        double d = km / EarthKm, th = azDeg * D2R;
        double f1 = from.Lat * D2R, l1 = from.Lon * D2R;
        double f2 = Math.Asin(Math.Sin(f1) * Math.Cos(d) + Math.Cos(f1) * Math.Sin(d) * Math.Cos(th));
        double l2 = l1 + Math.Atan2(Math.Sin(th) * Math.Sin(d) * Math.Cos(f1), Math.Cos(d) - Math.Sin(f1) * Math.Sin(f2));
        return new LatLon(f2 / D2R, ((l2 / D2R + 540) % 360) - 180);
    }

    public static List<LatLon> GreatCircle(LatLon a, LatLon b, int n)
    {
        var res = new List<LatLon>(n + 1);
        double la1 = a.Lat * D2R, lo1 = a.Lon * D2R, la2 = b.Lat * D2R, lo2 = b.Lon * D2R;
        double d = DistKm(a, b) / EarthKm;
        if (d < 1e-9) { res.Add(a); res.Add(b); return res; }
        for (int i = 0; i <= n; i++)
        {
            double f = (double)i / n;
            double A = Math.Sin((1 - f) * d) / Math.Sin(d), B = Math.Sin(f * d) / Math.Sin(d);
            double x = A * Math.Cos(la1) * Math.Cos(lo1) + B * Math.Cos(la2) * Math.Cos(lo2);
            double y = A * Math.Cos(la1) * Math.Sin(lo1) + B * Math.Cos(la2) * Math.Sin(lo2);
            double z = A * Math.Sin(la1) + B * Math.Sin(la2);
            res.Add(new LatLon(Math.Atan2(z, Math.Sqrt(x * x + y * y)) / D2R, Math.Atan2(y, x) / D2R));
        }
        return res;
    }
}

/// <summary>Posizione del Sole (punto subsolare) e altezza del Sole su un punto: serve alla greyline.</summary>
public static class Sun
{
    /// <summary>Punto subsolare all'istante dato (UTC), formule NOAA semplificate (errore di qualche decimo di grado).</summary>
    public static LatLon Subsolar(DateTime utc)
    {
        double jd = utc.ToOADate() + 2415018.5;
        double n = jd - 2451545.0;
        double L = (280.460 + 0.9856474 * n) % 360;
        double g = ((357.528 + 0.9856003 * n) % 360) * Geo.D2R;
        double lambda = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Geo.D2R;
        double eps = (23.439 - 0.0000004 * n) * Geo.D2R;
        double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
        double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
        // tempo siderale di Greenwich
        double gmst = (280.46061837 + 360.98564736629 * n) % 360;
        double lon = (ra / Geo.D2R - gmst) % 360;
        if (lon > 180) lon -= 360;
        if (lon < -180) lon += 360;
        return new LatLon(dec / Geo.D2R, lon);
    }

    /// <summary>Altezza del Sole in gradi sopra l'orizzonte di un punto, dato il punto subsolare.</summary>
    public static double Elevation(LatLon sub, double lat, double lon)
    {
        double f = lat * Geo.D2R, fs = sub.Lat * Geo.D2R, dl = (lon - sub.Lon) * Geo.D2R;
        double c = Math.Sin(f) * Math.Sin(fs) + Math.Cos(f) * Math.Cos(fs) * Math.Cos(dl);
        if (c > 1) c = 1; if (c < -1) c = -1;
        return 90 - Math.Acos(c) / Geo.D2R;
    }
}

/// <summary>Una banda radioamatoriale (MHz) e il suo gruppo (come in LoTW Grid Map v2).</summary>
public sealed class BandDef
{
    public string Name;
    public double Lo, Hi;   // MHz
    public int Group;       // 0 = LF/MF e 160 m, 1 = HF 80-10 m, 2 = VHF 8 m-1,25 m, 3 = UHF e oltre
    public BandDef(string name, double lo, double hi, int group) { Name = name; Lo = lo; Hi = hi; Group = group; }
}

public static class Bands
{
    public static readonly BandDef[] All =
    {
        new BandDef("2190m", 0.1357, 0.1378, 0), new BandDef("630m", 0.472, 0.479, 0), new BandDef("160m", 1.8, 2.0, 0),
        new BandDef("80m", 3.5, 4.0, 1), new BandDef("60m", 5.06, 5.45, 1), new BandDef("40m", 7.0, 7.3, 1),
        new BandDef("30m", 10.1, 10.15, 1), new BandDef("20m", 14.0, 14.35, 1), new BandDef("17m", 18.068, 18.168, 1),
        new BandDef("15m", 21.0, 21.45, 1), new BandDef("12m", 24.89, 24.99, 1), new BandDef("10m", 28.0, 29.7, 1),
        new BandDef("8m", 40.0, 45.0, 2), new BandDef("6m", 50.0, 54.0, 2), new BandDef("5m", 54.000001, 69.9, 2),
        new BandDef("4m", 70.0, 71.0, 2), new BandDef("2m", 144.0, 148.0, 2), new BandDef("1.25m", 222.0, 225.0, 2),
        new BandDef("70cm", 420.0, 450.0, 3), new BandDef("33cm", 902.0, 928.0, 3), new BandDef("23cm", 1240.0, 1300.0, 3),
        new BandDef("13cm", 2300.0, 2450.0, 3), new BandDef("9cm", 3300.0, 3500.0, 3), new BandDef("6cm", 5650.0, 5925.0, 3),
        new BandDef("3cm", 10000.0, 10500.0, 3), new BandDef("1.25cm", 24000.0, 24250.0, 3), new BandDef("6mm", 47000.0, 47200.0, 3),
        new BandDef("4mm", 75500.0, 81000.0, 3), new BandDef("2.5mm", 119980.0, 123000.0, 3), new BandDef("2mm", 134000.0, 149000.0, 3),
        new BandDef("1mm", 241000.0, 250000.0, 3),
    };

    /// <summary>Bande da confermare tutte perché un quadrato diventi grigio (80-10 m, esclusi i 60 m).</summary>
    public static readonly string[] GreyBands = { "80m", "40m", "30m", "20m", "17m", "15m", "12m", "10m" };

    /// <summary>Bande proposte nel filtro degli spot.</summary>
    public static readonly string[] SpotFilter = { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "4m", "2m", "70cm", "23cm" };

    public static bool IsGreyBand(string name) => Array.IndexOf(GreyBands, name) >= 0;

    public static string FromMHz(double mhz)
    {
        foreach (var d in All) if (mhz >= d.Lo && mhz <= d.Hi) return d.Name;
        return "";
    }

    public static string FromKHz(double khz) => FromMHz(khz / 1000.0);

    /// <summary>Nome di banda da ADIF/PSKReporter/QLog ("20M", "2m", "70CM") o dalla frequenza in MHz.</summary>
    public static string Normalize(string band, string freqMHz)
    {
        string b = (band ?? "").Trim().ToLowerInvariant().Replace(',', '.').Replace(" ", "");
        foreach (var d in All) if (d.Name == b) return d.Name;
        double f;
        if (double.TryParse((freqMHz ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f > 0)
        {
            var r = FromMHz(f);
            if (r != "") return r;
        }
        return b.Length > 0 ? b : "?";
    }

    public static int Order(string name)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].Name == name) return i;
        return 999;
    }

    public static int GroupOf(string name)
    {
        foreach (var d in All) if (d.Name == name) return d.Group;
        return -1;
    }
}
