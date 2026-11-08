#!/usr/bin/env python3
"""Construye el paquete de migraciones XPAY PROD a partir de la especificacion.

Solo trabaja con archivos locales: no se conecta a ninguna base de datos ni a Azure.

- Lee database/prod/especificacion_paquete.json.
- Para cada entrada VARIANTE_PROD o PENDIENTE_DATOS genera el archivo derivado
  copiando exactamente los rangos de lineas conservados del original, precedidos
  por un encabezado ASCII que termina en MARCA_FIN_ENCABEZADO.
- Escribe database/prod/MANIFIESTO_PROD.json con hashes SHA-256 y estados.

Los originales de database/ nunca se modifican.

Uso (desde la raiz del repositorio):
    python3 database/prod/tools/construir_paquete.py
"""

import hashlib
import json
import os
import subprocess
import sys

MARCA_FIN_ENCABEZADO = "-- @@FIN_ENCABEZADO_VARIANTE_PROD@@"
RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DIR_PROD = os.path.join(RAIZ, "database", "prod")
ESPECIFICACION = os.path.join(DIR_PROD, "especificacion_paquete.json")
MANIFIESTO = os.path.join(DIR_PROD, "MANIFIESTO_PROD.json")


def sha256_archivo(ruta):
    h = hashlib.sha256()
    with open(ruta, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def leer_lineas(ruta):
    with open(ruta, "rb") as f:
        return f.read().split(b"\n")


def cuerpo_desde_rangos(lineas, rangos):
    # lineas viene de split(b"\n"); la linea N (1-based) es lineas[N-1].
    partes = []
    for inicio, fin in rangos:
        if inicio < 1 or fin < inicio or fin > len(lineas):
            raise ValueError("rango fuera del archivo: %s-%s" % (inicio, fin))
        partes.extend(lineas[inicio - 1:fin])
    return b"\n".join(partes) + b"\n"


def ruta_derivada(entrada):
    base = os.path.basename(entrada["origen"])
    nombre = base.split("_", 1)[1][:-len(".sql")]
    if entrada["tipo"] == "VARIANTE_PROD":
        return "database/prod/variantes/%s_%s.prod.sql" % (entrada["id"], nombre)
    return "database/prod/pendientes/%s_%s.pendiente.sql" % (entrada["id"], nombre)


def encabezado(entrada, sha_origen):
    rangos = ", ".join("%d-%d" % (a, b) for a, b in entrada["rangos_conservados"])
    lineas = [
        "-- =====================================================================",
        "-- XPAY PROD - %s %s" % (entrada["tipo"], entrada["id"]),
        "-- Origen: %s" % entrada["origen"],
        "-- SHA-256 del origen: %s" % sha_origen,
        "-- Lineas conservadas del origen: %s" % rangos,
        "-- Estado en el manifiesto: %s" % entrada["estado"],
        "-- Generado por database/prod/tools/construir_paquete.py.",
        "-- NO EDITAR A MANO: modificar la especificacion y regenerar.",
        "-- El contenido despues de la marca es copia exacta de esas lineas.",
        "-- =====================================================================",
        MARCA_FIN_ENCABEZADO,
    ]
    return ("\n".join(lineas) + "\n").encode("ascii")


def commit_originales():
    # Ultimo commit que modifico los scripts originales de database/.
    # No depende de los commits del propio paquete (database/prod/).
    try:
        salida = subprocess.check_output(
            ["git", "-C", RAIZ, "log", "-1", "--format=%H", "--",
             ":(glob)database/[0-9][0-9][0-9]_*.sql"], text=True).strip()
        return salida or "desconocido"
    except Exception:
        return "desconocido"


def main():
    with open(ESPECIFICACION, "r", encoding="utf-8") as f:
        espec = json.load(f)

    entradas_manifiesto = []
    for orden, entrada in enumerate(espec["scripts"], start=1):
        ruta_origen = os.path.join(RAIZ, entrada["origen"])
        sha_origen = sha256_archivo(ruta_origen)

        if entrada["tipo"] == "ORIGINAL":
            archivo = entrada["origen"]
        else:
            archivo = ruta_derivada(entrada)
            destino = os.path.join(RAIZ, archivo)
            os.makedirs(os.path.dirname(destino), exist_ok=True)
            contenido = encabezado(entrada, sha_origen) + cuerpo_desde_rangos(
                leer_lineas(ruta_origen), entrada["rangos_conservados"])
            with open(destino, "wb") as f:
                f.write(contenido)

        entradas_manifiesto.append({
            "orden": orden,
            "id": entrada["id"],
            "tipo": entrada["tipo"],
            "clase": entrada["clase"],
            "origen": entrada["origen"],
            "sha256_origen": sha_origen,
            "archivo": archivo,
            "sha256_archivo": sha256_archivo(os.path.join(RAIZ, archivo)),
            "rangos_conservados": entrada.get("rangos_conservados"),
            "depende_de": entrada["depende_de"],
            "estado": entrada["estado"],
            "ejecutable": entrada["estado"] == "LISTO",
            "autorizacion_ejecucion": "NO_OTORGADA",
            "eliminado": entrada.get("eliminado"),
            "notas": entrada.get("notas"),
        })

    excluidos = []
    for ex in espec["excluidos"]:
        excluidos.append(dict(ex, sha256=sha256_archivo(os.path.join(RAIZ, ex["ruta"]))))

    manifiesto = {
        "paquete": espec["paquete"],
        "base_datos_objetivo": espec["base_datos_objetivo"],
        "commit_origen": commit_originales(),
        "commit_origen_significado": "ultimo commit que modifico los scripts originales de database/",
        "sha256_especificacion": sha256_archivo(ESPECIFICACION),
        "nota": "ejecutable=true solo indica preparacion tecnica. Ninguna ejecucion esta autorizada (autorizacion_ejecucion=NO_OTORGADA).",
        "scripts": entradas_manifiesto,
        "excluidos": excluidos,
    }
    with open(MANIFIESTO, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifiesto, f, ensure_ascii=True, indent=2)
        f.write("\n")

    listos = sum(1 for e in entradas_manifiesto if e["ejecutable"])
    print("Manifiesto escrito: %s" % os.path.relpath(MANIFIESTO, RAIZ))
    print("Entradas: %d (ejecutables tecnicamente: %d, bloqueadas: %d). Excluidos: %d."
          % (len(entradas_manifiesto), listos, len(entradas_manifiesto) - listos, len(excluidos)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
