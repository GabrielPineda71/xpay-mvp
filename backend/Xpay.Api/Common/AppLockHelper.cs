using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;

namespace Xpay.Api.Common;

// Wrapper compartido para sp_getapplock — Fase 70.4-B, sincronización
// bilateral entre WalletCajaComercioService.AbrirAsync y
// WalletCierreDiarioComercioService.GenerarCierreAsync (diseño Fase 70.4,
// sección 10). Sin DI, sin registro en Program.cs — son métodos estáticos que
// operan sobre la conexión y transacción ADO.NET ya abiertas por el
// XpayDbContext recibido, dentro de la transacción EF Core activa del
// llamador. Nunca abre una conexión independiente.
public static class AppLockHelper
{
    public const int LockTimeoutMs = 5000;

    // Misma cadena exacta en ambos servicios para el mismo comercio+fecha.
    // idEstablecimiento NO participa a propósito — GenerarCierreAsync opera
    // sobre todo el comercio (todos los establecimientos), no por sede.
    public static string ClaveCajaCierre(long idComercio, DateOnly fechaOperativa) =>
        $"XPAY:CAJA_CIERRE:{idComercio}:{fechaOperativa:yyyy-MM-dd}";

    // Debe llamarse después de BeginTransactionAsync() del propio db, antes de
    // cualquier otra consulta de la sección atómica. @Resource/@LockMode/
    // @LockOwner/@LockTimeout van como SqlParameter tipados — nunca
    // interpolados en el texto del comando.
    public static async Task<int> AdquirirAsync(XpayDbContext db, string clave, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("AppLockHelper.AdquirirAsync requiere una transacción EF Core activa.");

        var connection = db.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = clave });
        command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.VarChar, 32) { Value = "Exclusive" });
        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Transaction" });
        command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = LockTimeoutMs });

        var returnValue = new SqlParameter { Direction = ParameterDirection.ReturnValue, SqlDbType = SqlDbType.Int };
        command.Parameters.Add(returnValue);

        await command.ExecuteNonQueryAsync(ct);

        return (int)returnValue.Value;
    }

    // Interpreta el código de retorno de sp_getapplock (diseño Fase 70.4-B):
    // 0/1 → adquirido (sincrónicamente o tras esperar), no hace nada. -1/-2/-3
    // → timeout/cancelación/deadlock — contención transitoria de otra
    // operación concurrente sobre el mismo comercio+fecha, nunca un conflicto
    // de datos de negocio; se traduce a OperacionCajaCierreConcurrenteException
    // (compartida por ambos servicios), mapeada a 409. Cualquier otro código
    // (p. ej. -999, error de parámetros o de la llamada) es un error técnico
    // inesperado — nunca se traduce a una excepción de negocio; se relanza
    // como Exception genérica (no InvalidOperationException, para que ningún
    // catch de dominio existente la intercepte) y el middleware de errores
    // global responde 500, igual que cualquier otra falla no controlada.
    public static void ValidarResultado(int resultado, string clave)
    {
        switch (resultado)
        {
            case 0:
            case 1:
                return;
            case -1:
                throw new OperacionCajaCierreConcurrenteException(
                    $"Tiempo de espera agotado esperando otra operación sobre este comercio y fecha ({clave}). Intenta de nuevo.");
            case -2:
                throw new OperacionCajaCierreConcurrenteException(
                    $"La solicitud de sincronización fue cancelada para este comercio y fecha ({clave}). Intenta de nuevo.");
            case -3:
                throw new OperacionCajaCierreConcurrenteException(
                    $"Se detectó un interbloqueo con otra operación concurrente sobre este comercio y fecha ({clave}). Intenta de nuevo.");
            default:
                throw new Exception(
                    $"sp_getapplock devolvió un código no reconocido ({resultado}) para la clave {clave} — error técnico de la llamada, no una condición de negocio.");
        }
    }
}
