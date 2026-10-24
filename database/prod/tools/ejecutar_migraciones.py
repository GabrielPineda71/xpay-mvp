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
import re
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

# ---- Modo --atomico: PROTOTIPO EXPERIMENTAL LOCAL ----
# BLOQUEADO PARA PROD: solo se permite contra un servidor local (127.0.0.1,
# localhost o ::1) hasta superar las pruebas locales. Cambiar esta constante
# exige revision y autorizacion expresa.
ATOMICO_PERMITIDO_FUERA_DE_LOCAL = False
ATOMICO_BLOQUE_PERMITIDO = ("001", "002", "003", "004", "005", "006", "007", "009")
ATOMICO_HIPOTESIS_NO_PROBADAS = (
    "go-sqlcmd ejecuta un archivo -i en una sola sesion",
    "go-sqlcmd -b sale en el lote que falla y no envia los siguientes",
    "go-sqlcmd interpreta los archivos como UTF-8",
    "go-sqlcmd acepta -v VAR=valor para la verificacion",
    "el formato 'Msg n, Level m' de los errores en la salida de go-sqlcmd",
    "TRY/CATCH envuelve el DDL de B1 sin efectos laterales",
    "SET NOEXEC ON impide ejecutar los lotes posteriores",
    "sp_set_session_context no se revierte con ROLLBACK",
    "un error de resolucion diferida aborta el lote antes del testigo",
    "la reversion al cerrar la sesion es completa",
    "CURRENT_TRANSACTION_ID coincide con sys.dm_tran_active_transactions",
    "los valores esperados de B1 y la huella de esquema",
)
# Estado esperado tras B1 completo (calculado leyendo los scripts; pendiente de T1).
ATOMICO_ESPERADO_B1 = {
    "XPAY_V_TABLAS": "24", "XPAY_V_UNIDADES": "1", "XPAY_V_ROLES": "18",
    "XPAY_V_PERMISOS": "27", "XPAY_V_CUENTAS": "25", "XPAY_V_USUARIOS": "0",
    "XPAY_V_COL_KYC": "30", "XPAY_V_ACENTOS": "1",
}
ATOMICO_CODIGOS = {
    "EXITO_CONFIRMADO": 0,
    "EXITO_CONFIRMADO_POR_VERIFICACION": 0,
    "FALLO_CONFIRMADO_CON_REVERSION": 7,
    "ESTADO_INDETERMINADO": 8,
}


def ahora():
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def sha256(ruta):
    with open(ruta, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


# Unico destino permitido para el prototipo --atomico: el contenedor local de pruebas.
ATOMICO_PUERTO_LOCAL_PRUEBAS = 14350
_RE_SERVIDOR_LOCAL = re.compile(r"(?:tcp:)?(127\.0\.0\.1|localhost),(\d{1,5})", re.IGNORECASE)


def es_servidor_local(servidor):
    """True solo si el servidor es exactamente el contenedor local de pruebas.

    Formatos aceptados, completos, sin espacios, instancia ni parametros:
      tcp:127.0.0.1,14350   127.0.0.1,14350   tcp:localhost,14350   localhost,14350
    Cualquier otro formato, host o puerto devuelve False.
    """
    if not isinstance(servidor, str):
        return False
    m = _RE_SERVIDOR_LOCAL.fullmatch(servidor)
    if m is None:
        return False
    return int(m.group(2)) == ATOMICO_PUERTO_LOCAL_PRUEBAS


def _escanear_linea(linea, profundidad, cierre):
    """Recorre una linea de T-SQL y devuelve (codigo, profundidad, cierre).

    codigo:      la linea sin comentarios y sin el contenido de los literales
                 '...' (quedan como ''); los identificadores delimitados
                 ("..." y [...]) se conservan completos.
    profundidad: nivel de comentario de bloque abierto al terminar la linea
                 (T-SQL permite anidar /* */).
    cierre:      caracter que cierra el literal o identificador abierto al
                 terminar la linea (' " ]), o None.

    Escapes reconocidos dentro de un literal o identificador: el caracter de
    cierre duplicado ('' "" ]]). Fuera de ellos, -- termina la linea y /*
    abre un comentario de bloque.
    """
    salida = []
    i = 0
    n = len(linea)
    while i < n:
        c = linea[i]
        if profundidad > 0:
            if linea.startswith("/*", i):
                profundidad += 1
                i += 2
            elif linea.startswith("*/", i):
                profundidad -= 1
                i += 2
            else:
                i += 1
            continue
        if cierre is not None:
            literal = cierre == "'"
            if c == cierre and i + 1 < n and linea[i + 1] == cierre:
                if not literal:
                    salida.append(c + c)
                i += 2
                continue
            if c == cierre:
                cierre = None
                salida.append(c)
            elif not literal:
                salida.append(c)
            i += 1
            continue
        if linea.startswith("--", i):
            break
        if linea.startswith("/*", i):
            salida.append(" ")
            profundidad = 1
            i += 2
            continue
        if c in ("'", '"'):
            cierre = c
        elif c == "[":
            cierre = "]"
        salida.append(c)
        i += 1
    return "".join(salida), profundidad, cierre


_RE_GO_SEPARADOR = re.compile(r"\s*GO\s*", re.IGNORECASE)
_RE_EMPIEZA_POR_GO = re.compile(r"\s*GO\b", re.IGNORECASE)


def escanear_sql(texto):
    """Divide un script en lotes y devuelve (codigo, lotes, problemas).

    codigo:    el script sin comentarios (una linea por linea original).
    lotes:     texto original exacto de cada lote, entre separadores GO.
    problemas: lista de motivos de rechazo; vacia si la division es fiable.

    Solo se acepta como separador una linea que sea exactamente GO (con
    espacios opcionales, sin distinguir mayusculas) y que este fuera de
    comentarios, literales e identificadores delimitados. Cualquier otra
    linea que empiece por la palabra GO (GO 5, GO;, GO -- nota) o un GO
    dentro de un comentario o literal se rechaza. Asi, en todo lo que se
    acepta, la division coincide con la de sqlcmd sin depender de como
    trate los casos no estandar.
    """
    problemas = []
    lotes = []
    actual = []
    codigo = []
    profundidad = 0
    cierre = None
    for num, linea in enumerate(texto.split("\n"), start=1):
        en_estado_normal = profundidad == 0 and cierre is None
        if _RE_EMPIEZA_POR_GO.match(linea):
            if not en_estado_normal:
                problemas.append("linea %d: GO dentro de comentario o literal" % num)
            elif _RE_GO_SEPARADOR.fullmatch(linea) is None:
                problemas.append("linea %d: separador GO no estandar" % num)
            else:
                lotes.append("\n".join(actual))
                actual = []
                codigo.append("")
                continue
        actual.append(linea)
        codigo_linea, profundidad, cierre = _escanear_linea(linea, profundidad, cierre)
        codigo.append(codigo_linea)
    if "\n".join(actual).strip():
        lotes.append("\n".join(actual))
    if profundidad > 0 or cierre is not None:
        problemas.append("comentario o literal sin cerrar al final del archivo")
    lotes = [lote for lote in lotes if lote.strip()]
    return "\n".join(codigo), lotes, problemas


# Instrucciones que impiden incluir un script en el modo --atomico. Se buscan
# (sin distinguir mayusculas) en el codigo sin comentarios y sin el contenido
# de los literales '...'. La lista es deliberadamente amplia: un falso
# positivo solo rechaza el script; un falso negativo seria peligroso.
_PROHIBIDOS_ATOMICO = (
    # Control de transacciones y opciones de sesion que lo alteran
    (r"\bTRAN(SACTION)?\b", "control de transacciones (BEGIN/SAVE/COMMIT/ROLLBACK TRAN)"),
    (r"\bCOMMIT\b", "COMMIT"),
    (r"\bROLLBACK\b", "ROLLBACK"),
    (r"\b(XACT_ABORT|IMPLICIT_TRANSACTIONS|NOEXEC|PARSEONLY|FMTONLY"
     r"|CURSOR_CLOSE_ON_COMMIT|REMOTE_PROC_TRANSACTIONS|CONTEXT_INFO)\b",
     "opcion de sesion reservada"),
    # Control de errores, de flujo y esperas
    (r"\bBEGIN\s+(TRY|CATCH)\b", "TRY/CATCH"),
    (r"\b(THROW|RAISERROR|RETURN|GOTO|WAITFOR)\b", "control de errores, de flujo o espera"),
    # SQL dinamico, procedimientos y datos externos
    (r"\bEXEC(UTE)?\b", "EXEC/EXECUTE"),
    (r"\b(sp|xp)_\w*", "procedimiento del sistema"),
    (r"\bOPEN(ROWSET|QUERY|DATASOURCE)\b", "acceso a datos externos"),
    (r"\bBULK\s+INSERT\b", "BULK INSERT"),
    # Objetos que exigen lote propio o fuera de alcance
    (r"\bCREATE\s+(OR\s+ALTER\s+)?(PROC|PROCEDURE|FUNCTION|TRIGGER|VIEW|SCHEMA"
     r"|DEFAULT|RULE|TYPE|SYNONYM)\b", "objeto que exige lote propio o fuera de alcance"),
    (r"\bALTER\s+(PROC|PROCEDURE|FUNCTION|TRIGGER|VIEW|SCHEMA)\b", "ALTER de modulo"),
    # Administracion, seguridad y base de datos
    (r"\b(CREATE|ALTER|DROP)\s+DATABASE\b", "DDL de base de datos"),
    (r"\b(BACKUP|RESTORE|RECONFIGURE|DBCC|KILL|SHUTDOWN|CHECKPOINT)\b",
     "instruccion administrativa"),
    (r"\b(LOGIN|USER|ROLE|GRANT|REVOKE|DENY|AUTHORIZATION|CERTIFICATE|CREDENTIAL"
     r"|ENDPOINT)\b", "seguridad"),
    (r"\b(MASTER\s+KEY|SYMMETRIC|ASYMMETRIC|EXTERNAL|EVENT\s+SESSION)\b",
     "objeto de servidor o de cifrado"),
    (r"\bFULLTEXT\b", "texto completo"),
    (r"^\s*USE\b", "USE"),
    # Fuera del alcance de B1 (solo estructura y catalogo)
    (r"\b(DROP|TRUNCATE|DELETE|UPDATE|MERGE)\b", "modificacion o borrado fuera del alcance de B1"),
    # Nombres reservados por el prototipo
    (r"\bSESSION_CONTEXT\b", "contexto de sesion reservado"),
    (r"@xpay_", "variable reservada @xpay_"),
)

# Marcas que el prototipo escribe en la salida; un script no puede contenerlas
# (ni siquiera dentro de un literal), para que no pueda falsificar un resultado.
_MARCAS_RESERVADAS = ("XPAY_ATOMICO", "XPAY_LOTE_", "XPAY_CONTROL_", "XPAY_INICIO",
                      "XPAY_SESION", "XPAY_TRANCOUNT", "XPAY_FIN", "XPAY_V_", "XPAY:")
# Una llamada a procedimiento sin EXEC solo es posible como primera sentencia
# de un lote: la primera palabra de cada lote debe estar en esta lista.
_PRIMERAS_PALABRAS_PERMITIDAS = ("SET", "CREATE", "ALTER", "INSERT", "IF", "PRINT", "SELECT")
_RE_PRIMERA_PALABRA = re.compile(r"\s*([A-Za-z_]\w*)")
# Ordenes de sqlcmd reconocidas sin dos puntos al inicio de linea.
_RE_ORDEN_SQLCMD_SIN_DOS_PUNTOS = re.compile(r"\s*(EXIT|QUIT|RESET|ED)\b", re.IGNORECASE)


def analizar_compatibilidad(texto):
    """Lista de motivos por los que un script NO puede ir en el modo --atomico.

    Lista vacia = apto. Cualquier duda produce un motivo (fallo seguro).
    El codigo analizado ya llega sin comentarios y sin el contenido de los
    literales '...' (lo hace _escanear_linea con su contexto).
    """
    codigo, lotes, problemas = escanear_sql(texto)
    if texto.startswith(chr(0xFEFF)):
        problemas.append("empieza con marca de orden de bytes (BOM)")
    if "\r" in texto:
        problemas.append("contiene retornos de carro (CR)")
    if "$(" in texto:
        problemas.append("contiene '$(' (variables de sqlcmd)")
    for num, linea in enumerate(texto.split("\n"), start=1):
        inicio = linea.lstrip()
        if (inicio.startswith(":") or inicio.startswith("!!")
                or _RE_ORDEN_SQLCMD_SIN_DOS_PUNTOS.match(linea)):
            problemas.append("linea %d: orden de sqlcmd" % num)
    texto_mayusculas = texto.upper()
    for marca in _MARCAS_RESERVADAS:
        if marca in texto_mayusculas:
            problemas.append("contiene la marca reservada %s" % marca)
    for patron, descripcion in _PROHIBIDOS_ATOMICO:
        if re.search(patron, codigo, re.IGNORECASE | re.MULTILINE):
            problemas.append("instruccion incompatible: %s" % descripcion)
    if not lotes:
        problemas.append("sin lotes ejecutables")
    for num_lote, lote in enumerate(lotes, start=1):
        codigo_lote = escanear_sql(lote)[0].strip()
        if not codigo_lote:
            continue
        m = _RE_PRIMERA_PALABRA.match(codigo_lote)
        if m is None or m.group(1).upper() not in _PRIMERAS_PALABRAS_PERMITIDAS:
            problemas.append("lote %d: primera sentencia no permitida (%s)"
                             % (num_lote, codigo_lote.split(None, 1)[0][:30]))
    return problemas


# ---- Modo --atomico: construccion del SQL combinado (sin conexion) ----

# Condicion que debe cumplirse despues del lote n (y antes del COMMIT con n = total):
# testigo del ultimo lote completado, sin error marcado, una sola transaccion
# valida y que sea la misma que se abrio al principio.
_CONDICION_OK = "\n".join([
    "ISNULL(CAST(SESSION_CONTEXT(N'xpay_lote') AS INT), -1) = <<N>>",
    "   AND ISNULL(CAST(SESSION_CONTEXT(N'xpay_error') AS INT), 1) = 0",
    "   AND @@TRANCOUNT = 1",
    "   AND XACT_STATE() = 1",
    "   AND ISNULL(CAST(SESSION_CONTEXT(N'xpay_tx') AS BIGINT), -1)",
    "       = ISNULL(CURRENT_TRANSACTION_ID(), -2)",
])


def _sql_preambulo(on_error_exit):
    """Lotes iniciales: opciones, comprobacion previa y apertura de la transaccion."""
    lineas = []
    if on_error_exit:
        lineas.append(":on error exit")
    lineas += [
        "-- XPAY MODO ATOMICO - PROTOTIPO EXPERIMENTAL LOCAL - BLOQUEADO PARA PROD",
        "SET XACT_ABORT ON;",
        "SET ANSI_NULLS ON;",
        "SET QUOTED_IDENTIFIER ON;",
        "GO",
        "IF @@TRANCOUNT <> 0",
        "BEGIN",
        "    RAISERROR('XPAY: transaccion previa abierta; no se inicia', 16, 1);",
        "    SET NOEXEC ON;",
        "END",
        "GO",
        "BEGIN TRANSACTION;",
        "DECLARE @xpay_tx BIGINT = CURRENT_TRANSACTION_ID();",
        "DECLARE @xpay_login DATETIME = (",
        "    SELECT login_time FROM sys.dm_exec_sessions WHERE session_id = @@SPID);",
        "EXEC sp_set_session_context N'xpay_tx', @xpay_tx;",
        "EXEC sp_set_session_context N'xpay_lote', 0;",
        "EXEC sp_set_session_context N'xpay_error', 0;",
        "PRINT CONCAT('XPAY_SESION spid=', @@SPID, ' tx=', @xpay_tx,",
        "             ' login=', CONVERT(VARCHAR(23), @xpay_login, 126));",
        "PRINT 'XPAY_INICIO_OK';",
        "GO",
    ]
    return "\n".join(lineas)


def _sql_lote(n, cid, k, csha, contenido):
    """Un lote original envuelto en TRY/CATCH, con su testigo al final."""
    sn = str(n)
    sk = str(k)
    cabecera = [
        "-- >>> lote " + sn + ": " + cid + " parte " + sk + " sha256 " + csha,
        "BEGIN TRY",
        "PRINT 'XPAY_LOTE_" + sn + "_INICIO " + cid + ":" + sk + "';",
    ]
    pie = [
        ";EXEC sp_set_session_context N'xpay_lote', " + sn + ";",
        "END TRY",
        "BEGIN CATCH",
        "    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;",
        "    EXEC sp_set_session_context N'xpay_error', 1;",
        "    DECLARE @xpay_num INT = ERROR_NUMBER();",
        "    DECLARE @xpay_msg NVARCHAR(2048) = ERROR_MESSAGE();",
        "    RAISERROR('XPAY: error en lote %d (%s:%d): error %d: %s', 16, 1,",
        "              " + sn + ", '" + cid + "', " + sk + ", @xpay_num, @xpay_msg);",
        "    SET NOEXEC ON;",
        "END CATCH",
        "GO",
    ]
    return "\n".join(cabecera + [contenido.rstrip("\n")] + pie)


def _sql_control(n):
    """Lote de control tras el lote n: revierte y detiene si algo no cuadra."""
    sn = str(n)
    lineas = [
        "IF NOT (",
        _CONDICION_OK.replace("<<N>>", sn),
        ")",
        "BEGIN",
        "    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;",
        "    EXEC sp_set_session_context N'xpay_error', 1;",
        "    RAISERROR('XPAY: control tras lote %d fallido; transaccion revertida',",
        "              16, 1, " + sn + ");",
        "    SET NOEXEC ON;",
        "END",
        "ELSE",
        "    PRINT 'XPAY_CONTROL_" + sn + "_OK';",
        "GO",
    ]
    return "\n".join(lineas)


def _sql_final(total):
    """Lote del COMMIT protegido y marcas finales."""
    lineas = [
        "IF (",
        _CONDICION_OK.replace("<<N>>", str(total)),
        ")",
        "BEGIN",
        "    COMMIT TRANSACTION;",
        "    PRINT 'XPAY_ATOMICO_COMMIT_OK';",
        "END",
        "ELSE",
        "BEGIN",
        "    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;",
        "    EXEC sp_set_session_context N'xpay_error', 1;",
        "    RAISERROR(",
        "        'XPAY: COMMIT bloqueado por condiciones no cumplidas',",
        "        16, 1);",
        "    SET NOEXEC ON;",
        "END",
        "GO",
        "PRINT CONCAT('XPAY_TRANCOUNT_FINAL=', @@TRANCOUNT);",
        "PRINT 'XPAY_FIN';",
        "GO",
    ]
    return "\n".join(lineas)


_RE_ID_COMPONENTE = re.compile(r"[0-9]{3}[a-z]?")
_RE_SHA256 = re.compile(r"[0-9a-f]{64}")


def _validar_componente(cid, csha, texto):
    """ValueError si el componente no sirve para --atomico."""
    if not isinstance(cid, str):
        raise ValueError("id de componente no valido")
    if _RE_ID_COMPONENTE.fullmatch(cid) is None:
        raise ValueError("id de componente no valido: " + repr(cid))
    if not isinstance(csha, str):
        raise ValueError("sha256 no valido en " + cid)
    if _RE_SHA256.fullmatch(csha) is None:
        raise ValueError("sha256 no valido en " + cid)
    if not isinstance(texto, str):
        raise ValueError("contenido no valido en " + cid)
    calculado = hashlib.sha256(texto.encode("utf-8")).hexdigest()
    if calculado != csha:
        raise ValueError("sha256 distinto del contenido de " + cid)
    problemas = analizar_compatibilidad(texto)
    if problemas:
        raise ValueError(cid + ": " + "; ".join(problemas))


def construir_combinado(componentes, on_error_exit=False):
    """Construye el SQL combinado; devuelve (sql, total_lotes).

    componentes: lista de (cid, csha, texto) en orden de ejecucion.
    texto: contenido exacto del archivo, leido como UTF-8.
    Lanza ValueError si algun componente no es valido.
    No se conecta a nada.
    """
    partes = [_sql_preambulo(on_error_exit)]
    n = 0
    for cid, csha, texto in componentes:
        _validar_componente(cid, csha, texto)
        lotes = escanear_sql(texto)[1]
        for k, contenido in enumerate(lotes, start=1):
            n += 1
            lote = _sql_lote(n, cid, k, csha, contenido)
            partes.append(lote)
            partes.append(_sql_control(n))
    if n == 0:
        raise ValueError("sin lotes que ejecutar")
    partes.append(_sql_final(n))
    sql = "\n".join(partes) + "\n"
    return sql, n


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--desde", required=True, help="id inicial del bloque (inclusive)")
    p.add_argument("--hasta", required=True, help="id final del bloque (inclusive)")
    p.add_argument("--modo", choices=["plan", "ejecutar"], default="plan")
    p.add_argument("--omitir-bloqueados", action="store_true")
    p.add_argument("--sqlcmd", default=os.path.expanduser("~/xpay-tools/go-sqlcmd-1.10.0/sqlcmd"))
    p.add_argument("--log-dir", default=os.path.expanduser("~/xpay-tools/xpay-prod-migraciones/logs"))
    p.add_argument("--limite-inicio-utc",
                   help="YYYY-MM-DDTHH:MM:SSZ; a partir de esta hora no se inicia ningun script nuevo")
    a = p.parse_args()
    if a.limite_inicio_utc and not re.match(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", a.limite_inicio_utc):
        print("DETENIDO: --limite-inicio-utc debe tener el formato YYYY-MM-DDTHH:MM:SSZ")
        return 2
    # Archivo de detencion: si existe, no se inicia ningun script nuevo.
    detener = os.path.join(os.path.dirname(os.path.abspath(a.log_dir)), "DETENER")

    print("== Validador local ==", flush=True)
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
    print("Limite de inicio: %s | Archivo DETENER: %s (%s)"
          % (a.limite_inicio_utc or "sin limite", detener,
             "PRESENTE" if os.path.exists(detener) else "ausente"))

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
        for posicion, e in enumerate(a_ejecutar):
            motivo = None
            if a.limite_inicio_utc and ahora() >= a.limite_inicio_utc:
                motivo, codigo_salida = "NO_INICIADO_LIMITE", 5
            elif os.path.exists(detener):
                motivo, codigo_salida = "NO_INICIADO_DETENER", 6
            if motivo:
                pendientes = a_ejecutar[posicion:]
                for x in pendientes:
                    reg.write("%s\t-\t%s\t%s\t-\t%s\t-\n"
                              % (ahora(), x["id"], x["archivo"], motivo))
                ids = ", ".join(x["id"] for x in pendientes)
                print("DETENIDO (%s): no se inician %s." % (motivo, ids))
                print("Registro: %s" % registro)
                return codigo_salida
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
