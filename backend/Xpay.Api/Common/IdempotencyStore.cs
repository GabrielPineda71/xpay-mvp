using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Common;

// Fase 71.2-E-G: helpers compartidos para el flujo de idempotencia de una
// sola transacción (diseño aprobado en
// docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md §16). Estático y sin
// estado propio — recibe el XpayDbContext ya abierto del servicio que lo
// invoca, para no requerir registro nuevo en el contenedor de DI (Program.cs
// no se modifica en esta etapa).
public static class IdempotencyStore
{
    // 3 lecturas con backoff creciente — nunca un bucle sin límite. En
    // operación normal esta ruta no debería ejecutarse más que la primera
    // iteración (la fila COMPLETADA ya está confirmada por la transacción
    // que ganó la carrera antes de que esta se desbloqueara) — el resto es
    // una salvaguarda defensiva ante condiciones transitorias de
    // infraestructura, no un mecanismo esperado en el camino normal.
    private static readonly TimeSpan[] Backoffs =
    {
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
    };

    public static WalletIdempotencia NuevaReserva(long idUsuario, string endpoint, Guid idempotencyKey, byte[] requestHash)
    {
        var ahora = DateTime.UtcNow;
        return new WalletIdempotencia
        {
            IdUsuario       = idUsuario,
            Endpoint        = endpoint,
            IdempotencyKey  = idempotencyKey,
            RequestHash     = requestHash,
            Estado          = "EN_PROCESO",
            FechaCreacion   = ahora,
            FechaExpiracion = ahora.AddDays(7), // ver justificación en el documento de seguridad §16.10
        };
    }

    public static void MarcarCompletada(WalletIdempotencia idem, int httpStatus, long? idRecurso, long? idTransaccionLedger, string respuestaDataJson)
    {
        idem.Estado              = "COMPLETADA";
        idem.HttpStatus           = (short)httpStatus;
        idem.IdRecurso             = idRecurso;
        idem.IdTransaccionLedger     = idTransaccionLedger;
        idem.RespuestaDataJson         = respuestaDataJson;
        idem.FechaCompletado             = DateTime.UtcNow;
    }

    // Invocado solo tras una violación UNIQUE en el INSERT de reserva (ver
    // SqlExceptionHelper.IsUniqueViolation) — la transacción que intentaba
    // insertar ya se revirtió y el ChangeTracker ya se limpió antes de
    // llamar aquí (responsabilidad del llamador). Lectura AsNoTracking — no
    // se modifica esta fila desde este camino.
    public static async Task<IdempotentOperationResult<TData>> ResolverReplayAsync<TData>(
        XpayDbContext db, long idUsuario, string endpoint, Guid idempotencyKey, byte[] requestHash)
    {
        foreach (var backoff in Backoffs)
        {
            await Task.Delay(backoff);

            var fila = await db.WalletIdempotencias.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario && x.Endpoint == endpoint && x.IdempotencyKey == idempotencyKey);

            // Caso B (fila inexistente): la transacción ganadora hizo
            // rollback y liberó la clave — no hay nada que reproducir, pero
            // tampoco es este método quien reintenta la inserción (eso lo
            // decide el llamador); aquí simplemente no hay resultado que
            // devolver todavía en esta iteración.
            if (fila == null) continue;

            // EN_PROCESO no debería observarse desde fuera de la
            // transacción bajo este diseño (nunca se confirma por
            // separado) — si ocurre, se trata como condición transitoria y
            // se reintenta la lectura.
            if (fila.Estado != "COMPLETADA") continue;

            if (!CryptographicOperations.FixedTimeEquals(fila.RequestHash, requestHash))
                throw new IdempotencyConflictException("La clave de idempotencia ya fue utilizada para una operación diferente.");

            if (string.IsNullOrEmpty(fila.RespuestaDataJson))
                throw new InvalidOperationException("No fue posible reconstruir la respuesta de idempotencia.");

            var data = JsonSerializer.Deserialize<TData>(fila.RespuestaDataJson)
                ?? throw new InvalidOperationException("No fue posible reconstruir la respuesta de idempotencia.");
            return new IdempotentOperationResult<TData>(data, Replayed: true);
        }

        throw new IdempotencyUnavailableException("La operación con esta clave de idempotencia está en curso. Intenta de nuevo en unos segundos.");
    }
}
