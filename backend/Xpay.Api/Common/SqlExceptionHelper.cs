using Microsoft.Data.SqlClient;

namespace Xpay.Api.Common;

// Fase 71.2-E-G: helper compartido para clasificar errores de SQL Server por
// número (nunca por texto del mensaje, que varía por idioma/versión del
// servidor) — mismo criterio ya usado en
// WalletCajaComercioService.EsViolacionUniqueUsuarioComercioFecha (Fase
// 70.4), generalizado aquí porque dos servicios distintos
// (WalletOperacionService, PagoQrService) necesitan la misma clasificación.
//
// Fase 71.2-E-G.1: IMPORTANTE — IsUniqueViolation() por sí solo NO identifica
// cuál restricción UNIQUE falló (a diferencia de
// EsViolacionUniqueUsuarioComercioFecha, que además exige que el mensaje
// mencione el nombre exacto del índice). Aquí se optó deliberadamente por no
// inspeccionar el nombre de la restricción en el mensaje, porque el mensaje
// de SQL Server varía por idioma/versión y el proyecto ya evita depender de
// texto. La consecuencia práctica es que IsUniqueViolation() es verdadero
// ante CUALQUIER violación UNIQUE, sin importar la tabla — por eso su uso
// para interpretar un resultado como "replay de idempotencia" debe estar
// SIEMPRE acotado, en el código que lo invoca, exclusivamente al primer
// SaveChangesAsync que inserta la fila de reserva en wallet_idempotencia
// (ver WalletOperacionService.TransferirWalletAsync/PagoQrService.PagarQrAsync,
// catch interno anidado). Nunca debe envolver ledger, movimientos, ventas QR,
// auditoría ni el SaveChangesAsync final — una violación UNIQUE ahí sería un
// problema de integridad real, no una colisión de Idempotency-Key.
public static class SqlExceptionHelper
{
    private const int UniqueConstraintViolation = 2627; // CONSTRAINT ... UNIQUE con nombre
    private const int UniqueIndexViolation       = 2601; // CREATE UNIQUE INDEX sin restricción nombrada
    private const int DeadlockVictim             = 1205;
    private const int LockRequestTimeout         = 1222;

    // Límite defensivo de profundidad — nunca un bucle sin cota. En la
    // práctica de este proyecto un SqlException aparece a 0 o 1 niveles de
    // profundidad (directo, o como InnerException de un DbUpdateException),
    // pero se recorre la cadena completa por si algún camino futuro agrega
    // una capa adicional de envoltura.
    private const int MaxInnerExceptionDepth = 10;

    // Recorre ex, ex.InnerException, ex.InnerException.InnerException, ...
    // hasta encontrar la primera SqlException o hasta agotar el límite de
    // profundidad — nunca asume un nivel de anidamiento fijo.
    public static bool TryGetSqlException(Exception? ex, out SqlException? sqlException)
    {
        var current = ex;
        for (var depth = 0; current != null && depth < MaxInnerExceptionDepth; depth++)
        {
            if (current is SqlException found)
            {
                sqlException = found;
                return true;
            }
            current = current.InnerException;
        }
        sqlException = null;
        return false;
    }

    // Un SqlException puede agrupar más de un SqlError en .Errors — no basta
    // con mirar únicamente el .Number del nivel superior.
    private static bool HasErrorNumber(SqlException sqlEx, int number)
    {
        if (sqlEx.Number == number) return true;
        foreach (SqlError error in sqlEx.Errors)
        {
            if (error.Number == number) return true;
        }
        return false;
    }

    public static bool IsUniqueViolation(Exception ex) =>
        TryGetSqlException(ex, out var sqlEx) && sqlEx != null &&
        (HasErrorNumber(sqlEx, UniqueConstraintViolation) || HasErrorNumber(sqlEx, UniqueIndexViolation));

    public static bool IsTransient(Exception ex) =>
        TryGetSqlException(ex, out var sqlEx) && sqlEx != null &&
        (HasErrorNumber(sqlEx, DeadlockVictim) || HasErrorNumber(sqlEx, LockRequestTimeout));
}
