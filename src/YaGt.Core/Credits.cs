namespace YaGt.Core;

/// <summary>Riconoscimenti mostrati nella finestra About (uguali nelle due versioni) e nel README.</summary>
public static class Credits
{
    public static readonly (string text, string url)[] List =
    {
        ("PSKReporter — Philip Gladstone N1DQ: flusso MQTT (mqtt.pskreporter.info) e interfaccia HTTP", "https://pskreporter.info"),
        ("Reverse Beacon Network (telnet.reversebeacon.net)", "https://www.reversebeacon.net"),
        ("Cluster DM4X (dm4x.ddns.net:8500): spot PSKReporter in forma di cluster", ""),
        ("DXMAPS — Gabriel Sampol EA6VQ: secondo cluster con rete skimmer (dxmaps.com:7300)", "https://www.dxmaps.com"),
        ("prop.kc2g.com — Andrew Rodland KC2G: mappe di propagazione MUF/foF2 (GeoJSON)", "https://prop.kc2g.com"),
        ("NOAA Space Weather Prediction Center: modello OVATION Prime dell'ovale aurorale", "https://www.swpc.noaa.gov"),
        ("ARRL Logbook of The World (LoTW): report ADIF delle conferme", "https://lotw.arrl.org"),
        ("QLog — Ladislav Foldyna OK1MLG: struttura del database qlog.db", "https://github.com/foldynl/QLog"),
        ("Protocollo UDP di WSJT-X (K1JT e WSJT Development Group); JTDX; MSHV (LZ2HV)", "https://wsjt.sourceforge.io"),
        ("CALL3.TXT: WSJT-X Improved (DG2YCB), CALL3 EME di W7GJ, grid_data.bin di JTDX, database di MSHV-IC8TEM e LIVETROPOVHF", ""),
        ("G7VRD: ricerca dei locatori per nominativo (api.g7vrd.co.uk)", "https://api.g7vrd.co.uk"),
        ("cty.dat — Jim Reisert AD1C (country-files.com): prefissi e paesi DXCC", "https://www.country-files.com"),
        ("Natural Earth: coste e confini (pubblico dominio)", "https://www.naturalearthdata.com"),
        ("Avalonia UI, MQTTnet, Microsoft.Data.Sqlite / SQLite", "https://avaloniaui.net"),
        ("Codice ripreso da LIVETROPOVHF e LoTW Grid Map v2 (IC8TEM)", ""),
    };
}
