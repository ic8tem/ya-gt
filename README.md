<p align="center"><img src="assets/ya-gt-256.png" width="128" alt="YA-GT"></p>

# YA-GT

**Italiano** · [English](#english)

YA-GT mostra su una mappa del mondo chi riceve il tuo nominativo e chi ricevi tu, con un **fumetto ben visibile con il valore SNR** per ogni stazione, insieme ai tuoi QSO di LoTW e di QLog, alle tue decodifiche UDP, alla greyline, alla propagazione KC2G e all'aurora del NOAA. **Ogni funzione è un livello della mappa** e si accende o si spegne a scelta.

A cura di **Costantino Cerrotta • IC8TEM** — ccerrotta@gmail.com · ic8tem@gmail.com · [qrz.com/db/ic8tem](https://www.qrz.com/db/ic8tem) · [ko-fi.com/ic8tem](https://ko-fi.com/ic8tem)

## Avvio

Alla prima apertura si inseriscono **nominativo** e **locatore a 6 caratteri** (es. `JN70cn`). Il locatore fa da centro della mappa azimutale e serve a calcolare distanza e azimut di ogni spot.

## La mappa

- **Mercatore** oppure **azimutale equidistante** centrata sul tuo locatore (menu *Proiezione*).
- Rotella = zoom sul punto, trascinamento = spostamento, doppio clic = vista intera. Passando sopra un fumetto si leggono i dettagli (SNR, banda, modo, fonte, ora, distanza, azimut).
- **Aggiornamento dei dati** con il menu a tendina: 1, 3, 5, 10, 15 o 30 minuti, più il pulsante *Aggiorna ora*.
- Filtri: banda e durata degli spot (3 min - 6 h).

## Livelli

| Livello | Che cosa mostra |
|---|---|
| Chi mi sente | Stazioni che ricevono il tuo segnale: fumetto rettangolare con l'SNR (bordo blu) |
| Chi sento io | Stazioni che ricevi tu (PSKReporter, tuo skimmer): fumetto rettangolare (bordo viola) |
| Le mie decodifiche (UDP) | Decodifiche di WSJT-X, JTDX o MSHV ricevute via UDP: **nuvoletta ovale con le bollicine** (forma diversa) |
| Percorsi | Cerchio massimo dalla tua stazione a ogni stazione |
| Griglia Maidenhead | Campi e, ingrandendo, quadrati |
| Greyline | Notte e crepuscoli, terminatore e punto subsolare (ricalcolati ogni minuto) |
| Propagazione KC2G | Curve della MUF(3000) o della foF2 di [prop.kc2g.com](https://prop.kc2g.com) e valori delle ionosonde, con opacità regolabile |
| Aurora NOAA | Probabilità di aurora del modello OVATION del NOAA SWPC, ridisegnata nelle due proiezioni, con opacità regolabile |
| Quadrati LoTW | Quadrati lavorati e confermati (vedi sotto) |
| QSO del log QLog | Stazioni del tuo log di QLog: verde = LoTW, blu = QSL, arancio = lavorato |

La scala dei colori dell'SNR va dal rosso (forte) al blu (debole); per l'RBN in CW e RTTY l'SNR è in dB sopra il rumore. Un fumetto tratteggiato vuol dire che la posizione è solo quella del paese (cty.dat).

## Fonti degli spot

- **PSKReporter**: flusso MQTT `mqtt.pskreporter.info` filtrato sul tuo nominativo, più lo storico dell'ultima ora via HTTP (al massimo una richiesta ogni 5 minuti).
- **Reverse Beacon Network**: `telnet.reversebeacon.net` porte 7000 (CW/RTTY) e 7001 (FT8/FT4).
- **DM4X**: cluster telnet `dm4x.ddns.net:8500` (indirizzo fisso).
- **DXMAPS**: secondo cluster telnet `dxmaps.com:7300` (indirizzo fisso) con la **rete skimmer attivabile** (`set/skimmer`).
- **UDP**: protocollo WSJT-X (porta predefinita 2237, indirizzo configurabile anche multicast) con **ritrasmissione** dei pacchetti verso uno o più indirizzi `host:porta` (es. QLog, GridTracker, JTAlert).

## Posizione delle stazioni: CALL3.TXT

La base è il **CALL3.TXT da circa 18 MB** incluso, che fonde quello di LIVETROPOVHF con i database locatori di MSHV-IC8TEM (oltre un milione di nominativi). Le regole sono le stesse di LIVETROPOVHF e MSHV-IC8TEM:

- un **unico CALL3.TXT** universale (leggibile da WSJT-X, JTDX, MSHV), nessun file a parte e nulla scritto nel codice;
- le **correzioni dal web** entrano dentro CALL3.TXT: locatori a 6 caratteri imparati da PSKReporter, locatori dichiarati dalle stazioni negli spot DM4X/RBN, risposte di G7VRD;
- le **ricerche automatiche** dei nominativi senza locatore (G7VRD) partono **ogni 6 ore**; ogni 6 ore si controlla anche se riscrivere CALL3.TXT dalle fonti (WSJT-X Improved, W7GJ, JTDX: al massimo una volta alla settimana, il vecchio resta come CALL3.bak);
- se un nominativo non c'è, la posizione è quella del paese da **cty.dat** (AD1C).

## LoTW (da LoTW Grid Map v2)

*Scarica da LoTW…* scarica il report ADIF con le conferme (la **password non viene mai salvata**); *Apri ADIF…* unisce il file del tuo log. Colori confermati:

- **arancione** = lavorato non confermato, **verde** = confermato via LoTW, **blu** = confermato solo con QSL;
- **grigio** = confermato su **ognuna** delle bande 80, 40, 30, 20, 17, 15, 12 e 10 m (**esclusi i 60 m**); il grigio compare in *Tutte le bande*, in *HF 80-10 m* e nelle singole bande da 80 a 10 m.

Gruppi: Tutte le bande, **HF 80-10 m** (senza 160 m e 6 m), VHF 6 m-1,25 m, UHF e oltre, e ogni singola banda presente. Opzioni *Solo confermati* e *Solo EME*.

## QLog

YA-GT legge il database di [QLog](https://github.com/foldynl/QLog) (`qlog.db`, tabella `contacts`): su Windows è in `%LOCALAPPDATA%\hamradio\QLog\qlog.db`, oppure si sceglie in *Impostazioni*. Il file di QLog non viene mai modificato: si copia (con i file `-wal` e `-shm`) nella cartella `Dati` e si legge la copia.

## Installazione

- **Windows 10 e 11**: `YA-GT-…-windows-x64-setup.exe` (installa in `C:\YA-GT`) oppure lo zip portatile.
- **Linux** x86_64 e aarch64: archivio `tar.gz` con .NET incluso; si estrae e si avvia `YA-GT/YA-GT`. Il database di QLog si cerca in `~/.local/share/hamradio/QLog/qlog.db`.
- **Windows 7 SP1 e 8.1**: pacchetti `-windows7-x64-` o `-windows7-x86-` (setup o zip), per Microsoft .NET Framework 4.8; la mappa e le funzioni sono le stesse, la finestra è in WinForms.

Tutti i dati restano accanto al programma (`YA-GT.ini`, `CALL3.TXT`, cartella `Dati`): **niente in AppData**.

## Licenza

GNU GPL versione 3 (come LIVETROPOVHF e YA-NEC, da cui riprende codice). Copyright © 2026 Costantino Cerrotta IC8TEM.

## Riconoscimenti

PSKReporter (Philip Gladstone N1DQ) · Reverse Beacon Network · cluster DM4X · DXMAPS (EA6VQ) · prop.kc2g.com (Andrew Rodland KC2G) · NOAA SWPC (OVATION Prime) · ARRL LoTW · QLog (Ladislav Foldyna OK1MLG) · protocollo UDP di WSJT-X (K1JT e WSJT Development Group), JTDX, MSHV (LZ2HV) · CALL3.TXT: WSJT-X Improved (DG2YCB), W7GJ, JTDX, MSHV-IC8TEM, LIVETROPOVHF · G7VRD · cty.dat (AD1C) · Natural Earth · Avalonia UI, MQTTnet, Microsoft.Data.Sqlite. Codice ripreso da LIVETROPOVHF e LoTW Grid Map v2 (IC8TEM).

---

<a id="english"></a>
## English

YA-GT shows on a world map who hears your callsign and who you hear, with a **clearly visible SNR bubble** for every station, together with your LoTW and QLog QSOs, your UDP decodes, the greyline, KC2G propagation and NOAA aurora. **Every feature is a map layer** that can be switched on or off.

By **Costantino Cerrotta • IC8TEM** — ccerrotta@gmail.com · ic8tem@gmail.com · [qrz.com/db/ic8tem](https://www.qrz.com/db/ic8tem) · [ko-fi.com/ic8tem](https://ko-fi.com/ic8tem)

- **Start window**: callsign and 6-character locator (centre of the azimuthal map, distance and bearing of every spot).
- **Map**: Mercator or azimuthal equidistant centred on your locator; wheel zoom, drag to pan, double click for the whole view; hover a bubble for details. **Data refresh** drop-down: 1, 3, 5, 10, 15 or 30 minutes.
- **Layers**: heard me (rectangular SNR bubbles, blue border) · I heard (purple border) · my UDP decodes (oval thought bubble, a different shape) · great-circle paths · Maidenhead grid · greyline · KC2G MUF/foF2 · NOAA aurora (OVATION) · LoTW squares · QLog QSOs.
- **Sources**: PSKReporter MQTT (plus last-hour HTTP history), Reverse Beacon Network (ports 7000/7001), DM4X telnet cluster `dm4x.ddns.net:8500`, second telnet cluster DXMAPS `dxmaps.com:7300` with switchable skimmer network, WSJT-X UDP protocol with **UDP re-transmission** to one or more `host:port` targets.
- **Station positions**: the bundled ~18 MB **CALL3.TXT** (LIVETROPOVHF merged with the MSHV-IC8TEM locator databases), one universal file; web corrections are written into it; automatic look-ups (G7VRD) every 6 hours; cty.dat country position as a last resort.
- **LoTW** (from LoTW Grid Map v2): orange = worked, green = LoTW confirmed, blue = QSL only, grey = confirmed on **each** of 80, 40, 30, 20, 17, 15, 12 and 10 m (60 m excluded); groups All, HF 80-10 m, VHF, UHF and single bands. The password is never stored.
- **QLog**: reads a copy of `qlog.db` (table `contacts`), never modifies QLog's file.
- **Install**: Windows 10/11 setup or portable zip; Linux x86_64/aarch64 tar.gz; Windows 7 SP1/8.1 packages for .NET Framework 4.8. All data stays next to the program, nothing in AppData.

Credits as listed above.
