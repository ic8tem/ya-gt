#!/usr/bin/env python3
"""Impronta .sha256 e firma .sig dei pacchetti di aggiornamento di YA-GT.
Firma ECDSA P-256 / SHA-256 nel formato r||s (64 byte) in base64, che il programma verifica con la chiave pubblica scritta in
src/YaGt.Core/Updater.cs. La chiave privata arriva dal segreto UPDATE_SIGNING_KEY del repository (variabile d'ambiente)."""
import base64, hashlib, os, subprocess, sys, tempfile

key = os.environ.get("UPDATE_SIGNING_KEY", "").strip()
for f in sys.argv[1:]:
    data = open(f, "rb").read()
    open(f + ".sha256", "w").write(hashlib.sha256(data).hexdigest() + "  " + os.path.basename(f) + "\n")
    if not key:
        print("manca UPDATE_SIGNING_KEY: niente firma per", f)
        continue
    with tempfile.NamedTemporaryFile("w", delete=False, suffix=".pem") as k:
        k.write(key + "\n")
    der = subprocess.run(["openssl", "dgst", "-sha256", "-sign", k.name, f], capture_output=True, check=True).stdout
    os.unlink(k.name)
    # DER SEQUENCE { INTEGER r, INTEGER s } -> r||s di 32 byte ciascuno
    i = 2 if der[1] < 0x80 else 3
    def integer(p):
        assert der[p] == 2
        n = der[p + 1]
        return int.from_bytes(der[p + 2:p + 2 + n], "big"), p + 2 + n
    r, p = integer(i)
    s, _ = integer(p)
    open(f + ".sig", "w").write(base64.b64encode(r.to_bytes(32, "big") + s.to_bytes(32, "big")).decode() + "\n")
    print("firmato", f)
