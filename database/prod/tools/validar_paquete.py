#!/usr/bin/env python3
"""Validador local del paquete de migraciones XPAY PROD.

Solo lee archivos locales y consulta git en modo lectura. NO se conecta a ninguna
base de datos ni a Azure, y NO ejecuta SQL. Superarlo no sustituye una prueba real
de los scripts contra un motor SQL.

Comprueba:
  1. Especificacion y manifiesto coherentes (hash de la especificacion).
  2. Los 49 originales de database/ siguen intactos (git y SHA-256).
  3. Cada original esta incluido (original o derivado) o excluido explicitamente.
  4. Hashes de todos los archivos del manifiesto.
  5. Cada variante/pendiente es copia exacta de los rangos declarados del original.
  6. Formato: sin BOM, sin CR, salto de linea final.
  7. Estados: solo LISTO es ejecutable; ninguna ejecucion autorizada.
  8. Dependencias: existen, y las de un ejecutable son ejecutables y anteriores.
  9. Referencias QA/demo en codigo (error) o comentarios (aviso) de ejecutables.
 10. Sentencias prohibidas y USE en ejecutables.
 11. Inventario informativo de DROP e id_unidad_negocio = 1 fijo.

Uso (desde la raiz del repositorio):
    python3 database/prod/tools/validar_paquete.py
Codigo de salida 0 si no hay errores.
"""

import glob
import hashlib
import json
import os
import re
import subprocess
import sys

MARCA_FIN_ENCABEZADO = b"-- @@FIN_ENCABEZADO_VARIANTE_PROD@@"
RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DIR_PROD = os.path.join(RAIZ, "database", "prod")
ESPECIFICACION = os.path.join(DIR_PROD, "especificacion_paquete.json")
MANIFIESTO = os.path.join(DIR_PROD, "MANIFIESTO_PROD.json")

errores = []
avisos = []
info = []


def error(msg):
    errores.append(msg)


def aviso(msg):
    avisos.append(msg)


def sha256_bytes(datos):
    return hashlib.sha256(datos).hexdigest()


def leer(ruta_rel):
    with open(os.path.join(RAIZ, ruta_rel), "rb") as f:
        return f.read()


def separar_codigo_y_comentarios(texto):
    """Devuelve (codigo, comentarios) quitando comentarios -- y /* */ fuera de literales."""
    codigo, comentarios = [], []
    i, n = 0, len(texto)
    while i < n:
        c = texto[i]
        if c == "'":
            j = i + 1
            while j < n:
                if texto[j] == "'":
                    if j + 1 < n and texto[j + 1] == "'":
                        j += 2
                        continue
                    break
                j += 1
            codigo.append(texto[i:j + 1])
            i = j + 1
        elif texto.startswith("--", i):
            j = texto.find("\n", i)
            j = n if j == -1 else j
            comentarios.append(texto[i:j])
            codigo.append("\n")
            i = j + 1
        elif texto.startswith("/*", i):
            j = texto.find("*/", i + 2)
            j = n if j == -1 else j + 2
            comentarios.append(texto[i:j])
            codigo.append("\n" * texto[i:j].count("\n"))
            i = j
        else:
            codigo.append(c)
            i += 1
    return "".join(codigo), "\n".join(comentarios)


def comprobar_formato(ruta_rel, datos):
    if datos.startswith(b"\xef\xbb\xbf"):
        error("%s: tiene BOM" % ruta_rel)
    if b"\r" in datos:
        error("%s: contiene CR (fin de linea Windows)" % ruta_rel)
    if not datos.endswith(b"\n"):
        error("%s: no termina en salto de linea" % ruta_rel)


def originales_en_disco():
    rutas = sorted(glob.glob(os.path.join(RAIZ, "database", "[0-9][0-9][0-9]_*.sql")))
    return [os.path.relpath(r, RAIZ) for r in rutas]


def main():
    with open(ESPECIFICACION, "rb") as f:
        datos_espec = f.read()
    espec = json.loads(datos_espec.decode("utf-8"))
    with open(MANIFIESTO, "r", encoding="utf-8") as f:
        man = json.load(f)

    # 1. Coherencia especificacion / manifiesto
    if man.get("sha256_especificacion") != sha256_bytes(datos_espec):
        error("El manifiesto no corresponde a la especificacion actual: regenerar con construir_paquete.py")
    ids_espec = [e["id"] for e in espec["scripts"]]
    ids_man = [e["id"] for e in man["scripts"]]
    if ids_espec != ids_man:
        error("Los ids/orden del manifiesto no coinciden con la especificacion")
    if len(set(ids_man)) != len(ids_man):
        error("Hay ids duplicados en el manifiesto")

    # 2. Originales intactos
    originales = originales_en_disco()
    if len(originales) != 49:
        error("Se esperaban 49 scripts originales en database/, hay %d" % len(originales))
    try:
        estado_git = subprocess.check_output(
            ["git", "-C", RAIZ, "status", "--porcelain", "--"] + originales, text=True).strip()
        if estado_git:
            error("Originales de database/ con cambios respecto a git:\n    " + estado_git.replace("\n", "\n    "))
        else:
            info.append("git: los 49 originales no tienen cambios respecto al commit actual")
    except Exception as exc:
        aviso("No se pudo consultar git (%s); se comprueban solo los SHA-256" % exc)

    # 2b. commit_origen existe en el historial y contiene exactamente los originales
    commit = man.get("commit_origen", "")
    try:
        subprocess.check_call(["git", "-C", RAIZ, "merge-base", "--is-ancestor", commit, "HEAD"],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        distintos = []
        for e in man["scripts"]:
            datos_c = subprocess.check_output(["git", "-C", RAIZ, "show", "%s:%s" % (commit, e["origen"])])
            if sha256_bytes(datos_c) != e["sha256_origen"]:
                distintos.append(e["id"])
        if distintos:
            error("Originales distintos en commit_origen %s: %s" % (commit, distintos))
        else:
            info.append("commit_origen %s esta en el historial y contiene los originales del manifiesto" % commit[:12])
    except Exception:
        error("commit_origen %s no existe o no es antecesor de HEAD" % commit)

    # 3. Cobertura de originales
    origenes = {e["origen"] for e in man["scripts"]}
    excluidos = {x["ruta"] for x in man["excluidos"]}
    for ruta in originales:
        if ruta not in origenes and ruta not in excluidos:
            error("%s no esta incluido ni excluido en el manifiesto" % ruta)
        if ruta in origenes and ruta in excluidos:
            error("%s aparece incluido y excluido a la vez" % ruta)
    for x in man["excluidos"]:
        ruta_abs = os.path.join(RAIZ, x["ruta"])
        if not os.path.exists(ruta_abs):
            error("Excluido inexistente: %s" % x["ruta"])
        elif sha256_bytes(leer(x["ruta"])) != x["sha256"]:
            error("SHA-256 distinto en excluido %s" % x["ruta"])
        nombre = os.path.basename(x["ruta"])[:3]
        for derivado in glob.glob(os.path.join(DIR_PROD, "*", nombre + "*")):
            error("Existe un derivado de un script excluido: %s" % os.path.relpath(derivado, RAIZ))

    # 4-8. Entradas del manifiesto
    por_id = {e["id"]: e for e in man["scripts"]}
    archivos_derivados = set()
    for e in man["scripts"]:
        etiqueta = "%s (%s)" % (e["id"], e["archivo"])
        datos_origen = leer(e["origen"])
        if sha256_bytes(datos_origen) != e["sha256_origen"]:
            error("%s: el original cambio respecto al manifiesto" % etiqueta)
        if not os.path.exists(os.path.join(RAIZ, e["archivo"])):
            error("%s: archivo inexistente" % etiqueta)
            continue
        datos = leer(e["archivo"])
        if sha256_bytes(datos) != e["sha256_archivo"]:
            error("%s: SHA-256 del archivo no coincide con el manifiesto" % etiqueta)
        comprobar_formato(e["archivo"], datos)

        if e["tipo"] == "ORIGINAL":
            if e["archivo"] != e["origen"]:
                error("%s: una entrada ORIGINAL debe apuntar a su origen" % etiqueta)
        else:
            archivos_derivados.add(os.path.normpath(e["archivo"]))
            if datos.count(MARCA_FIN_ENCABEZADO) != 1:
                error("%s: la marca de fin de encabezado debe aparecer exactamente una vez" % etiqueta)
            else:
                cabecera, cuerpo = datos.split(MARCA_FIN_ENCABEZADO + b"\n", 1)
                try:
                    cabecera.decode("ascii")
                except UnicodeDecodeError:
                    error("%s: el encabezado no es ASCII" % etiqueta)
                if e["sha256_origen"].encode() not in cabecera:
                    error("%s: el encabezado no declara el SHA-256 del origen" % etiqueta)
                lineas = datos_origen.split(b"\n")
                esperado = b"\n".join(
                    l for a, b in e["rangos_conservados"] for l in lineas[a - 1:b]) + b"\n"
                if cuerpo != esperado:
                    error("%s: el cuerpo no es copia exacta de los rangos %s del original"
                          % (etiqueta, e["rangos_conservados"]))

        if e["ejecutable"] != (e["estado"] == "LISTO"):
            error("%s: 'ejecutable' no es coherente con el estado %s" % (etiqueta, e["estado"]))
        if e["autorizacion_ejecucion"] != "NO_OTORGADA":
            error("%s: el paquete no debe registrar autorizaciones de ejecucion" % etiqueta)

        bloqueadas = []
        for dep in e["depende_de"]:
            if dep not in por_id:
                error("%s: dependencia inexistente %s" % (etiqueta, dep))
                continue
            d = por_id[dep]
            if d["orden"] >= e["orden"]:
                error("%s: depende de %s, que va despues en el orden" % (etiqueta, dep))
            if not d["ejecutable"]:
                bloqueadas.append(dep)
        if e["ejecutable"] and bloqueadas:
            error("%s: es ejecutable pero depende de scripts bloqueados %s" % (etiqueta, bloqueadas))
        if e["estado"] == "BLOQUEADO_DEPENDENCIA" and not bloqueadas:
            aviso("%s: marcado BLOQUEADO_DEPENDENCIA pero sus dependencias estan listas" % etiqueta)

    # Derivados huerfanos
    for ruta in glob.glob(os.path.join(DIR_PROD, "variantes", "*.sql")) + glob.glob(os.path.join(DIR_PROD, "pendientes", "*.sql")):
        rel = os.path.normpath(os.path.relpath(ruta, RAIZ))
        if rel not in archivos_derivados:
            error("Archivo derivado no declarado en el manifiesto: %s" % rel)

    # 9-11. Contenido
    patrones = espec["patrones_qa_demo"]
    prohibidas = espec["sentencias_prohibidas"]
    for e in man["scripts"]:
        datos = leer(e["archivo"])
        if e["tipo"] != "ORIGINAL":
            datos = datos.split(MARCA_FIN_ENCABEZADO + b"\n", 1)[-1]
        texto = datos.decode("utf-8", errors="replace")
        codigo, comentarios = separar_codigo_y_comentarios(texto)
        codigo_l, comentarios_l = codigo.lower(), comentarios.lower()
        etiqueta = "%s (%s)" % (e["id"], e["estado"])
        for p in patrones:
            if p.lower() in codigo_l:
                msg = "%s: referencia QA/demo en codigo: '%s'" % (etiqueta, p)
                (error if e["ejecutable"] else aviso)(msg)
            elif p.lower() in comentarios_l:
                aviso("%s: referencia QA/demo solo en comentarios: '%s'" % (etiqueta, p))
        for s in prohibidas:
            if re.search(r"\b" + re.escape(s.strip()).replace(r"\ ", r"\s+") + r"\b", codigo, re.IGNORECASE):
                msg = "%s: sentencia prohibida en codigo: '%s'" % (etiqueta, s.strip())
                (error if e["ejecutable"] else aviso)(msg)
        if re.search(r"^\s*USE\s+\[?\w", codigo, re.IGNORECASE | re.MULTILINE):
            error("%s: contiene USE" % etiqueta)
        for m in re.finditer(r"\bDROP\s+(CONSTRAINT|INDEX)\s+\[?(\w+)", codigo, re.IGNORECASE):
            info.append("%s: DROP %s %s" % (e["id"], m.group(1).upper(), m.group(2)))
        if re.search(r"id_unidad_negocio\s*=\s*1\b|VALUES\s*\(\s*1\s*,\s*'\d{6}'", codigo, re.IGNORECASE):
            info.append("%s: usa id_unidad_negocio = 1 fijo" % e["id"])

    # Informe
    listos = [e["id"] for e in man["scripts"] if e["ejecutable"]]
    bloqueados = [(e["id"], e["estado"]) for e in man["scripts"] if not e["ejecutable"]]
    print("VALIDADOR LOCAL DEL PAQUETE XPAY PROD (no ejecuta SQL ni se conecta a nada)")
    print("Commit de origen del manifiesto: %s" % man.get("commit_origen"))
    print("Entradas: %d | tecnicamente listas: %d | bloqueadas: %d | excluidos: %d"
          % (len(man["scripts"]), len(listos), len(bloqueados), len(man["excluidos"])))
    print("Bloqueadas: " + ", ".join("%s=%s" % b for b in bloqueados))
    print("")
    print("INFO (%d):" % len(info))
    for m in info:
        print("  - " + m)
    print("AVISOS (%d):" % len(avisos))
    for m in avisos:
        print("  - " + m)
    print("ERRORES (%d):" % len(errores))
    for m in errores:
        print("  - " + m)
    print("")
    print("RESULTADO: %s" % ("OK" if not errores else "FALLO"))
    return 0 if not errores else 1


if __name__ == "__main__":
    sys.exit(main())
