#!/usr/bin/env python3
"""Ejecutor controlado de un bloque del manifiesto XPAY PROD.

POR DEFECTO SOLO MUESTRA EL PLAN (modo 'plan'): no invoca sqlcmd ni se conecta a nada.
El modo 'ejecutar' exige escribir una frase de confirmacion exacta y solo debe usarse
con autorizacion expresa, siguiendo database/prod/PROCEDIMIENTO_EJECUCION_PROD.md.

Garantias:
- Antes de nada ejecuta el validador local; si falla, se detiene.
- Solo ejecuta entradas con ejecutable=true. Las bloqueadas se rechazan, salvo que se
  pida --omitir-bloqueados, en cuyo caso se listan y se saltan explicitamente.
- Recalcula el SHA-256 de cada archivo inmediatamente antes de ejecutarlo.
- Usa go-sqlcmd con -b (sale ante error) y -N mandatory (cifrado obligatorio, sin -C).
- Se detiene ante el primer codigo de salida distinto de 0.
- Registra cada paso (hora UTC, id, archivo, SHA, codigo) y la salida completa.
- No recibe ni guarda credenciales: la autenticacion es interactiva de Entra.
- Los valores del entorno (servidor, base y administrador) se leen de variables de
  entorno y nunca se guardan en el repositorio (ver ENTORNO).

Uso (desde la raiz del repositorio):
    python3 database/prod/tools/ejecutar_migraciones.py --desde 001 --hasta 007
    python3 database/prod/tools/ejecutar_migraciones.py --desde 010 --hasta 033 --omitir-bloqueados
    python3 database/prod/tools/ejecutar_migraciones.py --desde 001 --hasta 007 --modo ejecutar
"""

import argparse
import datetime
import hashlib
import json
import os
import subprocess
import sys

RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
MANIFIESTO = os.path.join(RAIZ, "database", "prod", "MANIFIESTO_PROD.json")
VALIDADOR = os.path.join(RAIZ, "database", "prod", "tools", "validar_paquete.py")

# Valores del entorno: se leen de variables, nunca se guardan en el repositorio.
#   XPAY_PROD_SQL_SERVIDOR  p. ej. tcp:<servidor>.database.windows.net,1433
#   XPAY_PROD_SQL_BASE      nombre de la base PROD
#   XPAY_PROD_SQL_ADMIN     administrador de Entra del servidor
ENTORNO = ("XPAY_PROD_SQL_SERVIDOR", "XPAY_PROD_SQL_BASE", "XPAY_PROD_SQL_ADMIN")
MARCADORES = {"XPAY_PROD_SQL_SERVIDOR": "<servidor>", "XPAY_PROD_SQL_BASE": "<base>",
              "XPAY_PROD_SQL_ADMIN": "<admin>"}


def ahora():
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def sha256(ruta):
    with open(ruta, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--desde", required=True, help="id inicial del bloque (inclusive)")
    p.add_argument("--hasta", required=True, help="id final del bloque (inclusive)")
    p.add_argument("--modo", choices=["plan", "ejecutar"], default="plan")
    p.add_argument("--omitir-bloqueados", action="store_true")
    p.add_argument("--sqlcmd", default=os.path.expanduser("~/xpay-tools/go-sqlcmd-1.10.0/sqlcmd"))
    p.add_argument("--log-dir", default=os.path.expanduser("~/xpay-tools/xpay-prod-migraciones/logs"))
    a = p.parse_args()

    print("== Validador local ==")
    if subprocess.call([sys.executable, VALIDADOR]) != 0:
        print("DETENIDO: el validador local fallo.")
        return 2

    with open(MANIFIESTO, "r", encoding="utf-8") as f:
        man = json.load(f)
    por_id = {e["id"]: e for e in man["scripts"]}
    for i in (a.desde, a.hasta):
        if i not in por_id:
            print("DETENIDO: id inexistente en el manifiesto: %s" % i)
            return 2
    o1, o2 = por_id[a.desde]["orden"], por_id[a.hasta]["orden"]
    if o1 > o2:
        print("DETENIDO: --desde va despues de --hasta")
        return 2
    bloque = [e for e in man["scripts"] if o1 <= e["orden"] <= o2]
    bloqueados = [e for e in bloque if not e["ejecutable"]]
    if bloqueados and not a.omitir_bloqueados:
        print("DETENIDO: el bloque contiene entradas bloqueadas: "
              + ", ".join("%s=%s" % (e["id"], e["estado"]) for e in bloqueados))
        return 2
    a_ejecutar = [e for e in bloque if e["ejecutable"]]

    print("")
    print("== Plan: %s -> %s (%d scripts, %d omitidos) ==" % (a.desde, a.hasta, len(a_ejecutar), len(bloqueados)))
    for e in bloque:
        marca = "EJECUTAR" if e["ejecutable"] else "OMITIR (%s)" % e["estado"]
        print("  %3d  %-5s %-9s %s  sha256=%s" % (e["orden"], e["id"], marca, e["archivo"], e["sha256_archivo"]))
    faltan = [v for v in ENTORNO if not os.environ.get(v, "").strip()]
    valores = {v: os.environ.get(v, "").strip() or MARCADORES[v] for v in ENTORNO}
    SERVIDOR = valores["XPAY_PROD_SQL_SERVIDOR"]
    BASE = valores["XPAY_PROD_SQL_BASE"]
    USUARIO = valores["XPAY_PROD_SQL_ADMIN"]

    print("")
    print("Comando por script:")
    print("  %s -S %s -d %s --authentication-method ActiveDirectoryInteractive -U %s -N mandatory -b -i <archivo>"
          % (a.sqlcmd, SERVIDOR, BASE, USUARIO))
    if faltan:
        print("AVISO: variables de entorno no definidas: %s" % ", ".join(faltan))

    if a.modo == "plan":
        print("")
        print("Modo plan: no se invoco sqlcmd ni hubo conexion alguna.")
        return 0

    # ---- Modo ejecutar ----
    if faltan:
        print("DETENIDO: faltan variables de entorno: %s. No se ejecuto nada." % ", ".join(faltan))
        return 2
    if not os.path.isfile(a.sqlcmd):
        print("DETENIDO: no existe %s" % a.sqlcmd)
        return 2
    frase = "EJECUTAR %s-%s EN %s" % (a.desde, a.hasta, BASE)
    print("")
    print("Para continuar escriba exactamente: %s" % frase)
    if input("> ").strip() != frase:
        print("DETENIDO: confirmacion incorrecta. No se ejecuto nada.")
        return 2

    os.makedirs(a.log_dir, mode=0o700, exist_ok=True)
    sello = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    registro = os.path.join(a.log_dir, "ejecucion_%s_%s-%s.tsv" % (sello, a.desde, a.hasta))
    with open(registro, "w", encoding="utf-8") as reg:
        reg.write("inicio_utc\tfin_utc\tid\tarchivo\tsha256\tcodigo_salida\tlog\n")
        for e in a_ejecutar:
            ruta = os.path.join(RAIZ, e["archivo"])
            actual = sha256(ruta)
            if actual != e["sha256_archivo"]:
                reg.write("%s\t%s\t%s\t%s\t%s\tSHA_DISTINTO\t-\n" % (ahora(), ahora(), e["id"], e["archivo"], actual))
                print("DETENIDO: SHA-256 distinto en %s. No se ejecuto." % e["id"])
                return 3
            salida = os.path.join(a.log_dir, "%s_%s.log" % (sello, e["id"]))
            inicio = ahora()
            print("[%s] %s ..." % (inicio, e["id"]))
            with open(salida, "wb") as out:
                codigo = subprocess.call(
                    [a.sqlcmd, "-S", SERVIDOR, "-d", BASE,
                     "--authentication-method", "ActiveDirectoryInteractive",
                     "-U", USUARIO, "-N", "mandatory", "-b", "-i", ruta],
                    stdout=out, stderr=subprocess.STDOUT)
            reg.write("%s\t%s\t%s\t%s\t%s\t%d\t%s\n" % (inicio, ahora(), e["id"], e["archivo"], actual, codigo, salida))
            reg.flush()
            print("    codigo de salida %d (log: %s)" % (codigo, salida))
            if codigo != 0:
                print("DETENIDO: %s fallo. No se ejecutan los siguientes. Revisar el log antes de cualquier accion." % e["id"])
                return 4
    print("Bloque completado. Registro: %s" % registro)
    return 0


if __name__ == "__main__":
    sys.exit(main())
