**YA-GT** — spot di un nominativo, LoTW, QLog e decodifiche UDP su una mappa a livelli. A cura di Costantino Cerrotta IC8TEM.
*Spots of a callsign, LoTW, QLog and UDP decodes on a layered map. By Costantino Cerrotta IC8TEM.*

- **Windows 10 e 11**: `YA-GT-…-windows-x64-setup.exe` (installa in `C:\YA-GT`, senza permessi di amministratore) oppure lo zip portatile `-windows-x64-portatile.zip`.
- **Linux** (x86_64 e aarch64): `YA-GT-…-linux-x64.tar.gz` o `-linux-arm64.tar.gz`; si estrae e si avvia `YA-GT/YA-GT` (nessuna installazione, .NET incluso).
- **Windows 7 SP1 e 8.1**: `YA-GT-…-windows7-x64-…` o `-windows7-x86-…` (setup o zip portatile). Richiede Microsoft .NET Framework 4.8: se manca, l'installatore apre la pagina di Microsoft.

Tutti i dati restano accanto al programma (`YA-GT.ini`, `CALL3.TXT`, cartella `Dati`): niente in AppData. La password LoTW non viene mai salvata.
*All data stays next to the program: nothing in AppData. The LoTW password is never stored.*

**1.1.0**: interfaccia in 37 lingue, le stesse di tutti i programmi IC8TEM (YA-NEC, YA-MS, YAEC, E-LAYER ANALYZER, LIVETROPOVHF, IP-DESK-CTRL); al primo avvio la lingua del sistema, poi si sceglie in Impostazioni > Lingua (vale dal riavvio). / *Interface in 37 languages, the same as all IC8TEM programs; on first start the system language, then choose it in Settings > Language (applies after a restart).*

**1.0.3**: rimbalzo UDP nei due sensi con porta di ricezione e porta di ritrasmissione scelte a parte (i QSO arrivano a QLog, le risposte tornano a WSJT-X/JTDX/MSHV), avviso chiaro se la porta è già occupata, i QSO appena registrati colorano subito i quadrati. / *Two-way UDP relay with separately selectable receive and re-transmit ports (QSOs reach QLog, replies go back to WSJT-X/JTDX/MSHV), clear warning when the port is busy, freshly logged QSOs colour the squares at once.*

**1.0.2**: aggiornamento automatico dal GitHub (ogni 3 minuti, pacchetti con impronta e firma, come YA-NEC); riquadro delle statistiche in alto a destra (stazioni, quadrati, paesi, più lontana, miglior SNR, fonti, quadrati confermati); i quadrati si colorano per la banda scelta in alto e uniscono LoTW, ADIF e QLog. / *Automatic updates from GitHub (every 3 minutes, signed packages, as in YA-NEC); statistics box at top right; squares coloured for the band selected at the top, merging LoTW, ADIF and QLog.*

**1.0.1**: durata degli spot anche di 3 e 5 minuti; prima versione per Linux. / *Spot window also 3 and 5 minutes; first Linux build.*
