using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public enum ResultadoPin
{
    Correcto,
    FormatoInvalido,
    NoConfigurado,
    Bloqueado,
    Incorrecto,
    CambiadoConcurrentemente,
}

public readonly record struct VerificacionPin(ResultadoPin Resultado, int IntentosRestantes = 0);

// PIN transaccional de 5 dígitos sobre la tabla existente pin_transaccional
// (una fila por usuario). Solo se guarda el hash BCrypt.
//
// Verificación resistente a concurrencia: cada intento RESERVA primero un
// intento con un UPDATE atómico condicionado a "ACTIVO, no bloqueado y con
// intentos < Max" (el mismo UPDATE bloquea al llegar a Max). Solo después se
// compara el hash; si acierta, otro UPDATE reinicia el contador. Así, ni con
// peticiones simultáneas se prueban más de MaxIntentos PIN antes del bloqueo.
// La reserva se confirma antes de verificar el hash. Un fallo posterior
// del cambio o de la operación de dinero no deshace el intento contado.
public sealed class PinTransaccionalService(XpayDbContext db)
{
    public const int Longitud = 5;
    public const int MaxIntentos = 5;
    private const int LongitudAnterior = 7;

    public async Task<EstadoPinDto> ObtenerEstadoAsync(long idUsuario, CancellationToken ct = default)
    {
        var pin = await db.PinesTransaccionales.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        var activo = pin is { Estado: EstadosPin.Activo };
        return new EstadoPinDto
        {
            Configurado       = activo,
            Bloqueado         = activo && pin!.Bloqueado,
            Restablecido      = pin is { Estado: EstadosPin.Restablecido },
            IntentosRestantes = activo && !pin!.Bloqueado ? Math.Max(0, MaxIntentos - pin.IntentosFallidos) : 0,
        };
    }

    public Task<VerificacionPin> VerificarAsync(long idUsuario, string? pin, CancellationToken ct = default) =>
        FormatoValido(pin)
            ? VerificarPinVigenteAsync(idUsuario, pin, ct)
            : Task.FromResult(new VerificacionPin(ResultadoPin.FormatoInvalido));

    private async Task<VerificacionPin> VerificarPinVigenteAsync(long idUsuario, string pin, CancellationToken ct,
        string? pinNuevo = null)
    {
        var registro = await db.PinesTransaccionales.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        if (registro is null || registro.Estado != EstadosPin.Activo) return new(ResultadoPin.NoConfigurado);
        if (registro.Bloqueado) return new(ResultadoPin.Bloqueado);

        // Una reserva o confirmación del PIN anterior nunca modifica el PIN
        // que acaba de cambiarse o restablecerse en otra solicitud.
        var pinVigente = db.PinesTransaccionales
            .Where(p => p.IdPin == registro.IdPin && p.Estado == EstadosPin.Activo && p.PinHash == registro.PinHash);
        var ahora = DateTime.UtcNow;
        var reservado = await pinVigente
            .Where(p => !p.Bloqueado && p.IntentosFallidos < MaxIntentos)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IntentosFallidos, p => p.IntentosFallidos + 1)
                .SetProperty(p => p.Bloqueado, p => p.IntentosFallidos + 1 >= MaxIntentos)
                .SetProperty(p => p.FechaBloqueo, p => p.IntentosFallidos + 1 >= MaxIntentos ? ahora : p.FechaBloqueo)
                .SetProperty(p => p.FechaActualizacion, ahora), ct);
        if (reservado == 0)
            return new(await pinVigente.AnyAsync(ct) ? ResultadoPin.Bloqueado : ResultadoPin.CambiadoConcurrentemente);

        if (BCrypt.Net.BCrypt.Verify(pin, registro.PinHash))
        {
            if (pinNuevo is not null)
                return await GuardarCambioAsync(pinVigente, idUsuario, pinNuevo, ct);

            var confirmado = await pinVigente.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IntentosFallidos, 0)
                .SetProperty(p => p.Bloqueado, false)
                .SetProperty(p => p.FechaBloqueo, (DateTime?)null), ct);
            return new(confirmado == 1 ? ResultadoPin.Correcto : ResultadoPin.CambiadoConcurrentemente);
        }

        var intentos = await pinVigente.Select(p => (int?)p.IntentosFallidos).SingleOrDefaultAsync(ct);
        if (intentos is null) return new(ResultadoPin.CambiadoConcurrentemente);

        var restantes = MaxIntentos - intentos.Value;
        return restantes > 0 ? new(ResultadoPin.Incorrecto, restantes) : new(ResultadoPin.Bloqueado);
    }

    // Crea el PIN (primera vez o tras un restablecimiento administrativo).
    public async Task CrearAsync(long idUsuario, CrearPinRequest req, CancellationToken ct = default)
    {
        ValidarNuevo(req.Pin, req.ConfirmacionPin);

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");
        if (string.IsNullOrEmpty(req.ClaveActual) || !BCrypt.Net.BCrypt.Verify(req.ClaveActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña de ingreso no coincide.");

        var ahora = DateTime.UtcNow;
        var hash  = BCrypt.Net.BCrypt.HashPassword(req.Pin);
        var existente = await db.PinesTransaccionales.FirstOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        if (existente is { Estado: EstadosPin.Activo })
            throw new InvalidOperationException("Ya tienes un PIN transaccional. Usa la opción de cambiar PIN.");

        if (existente is null)
        {
            db.PinesTransaccionales.Add(new PinTransaccional
            {
                IdUsuario = idUsuario, PinHash = hash, IntentosFallidos = 0, Bloqueado = false,
                FechaCreacion = ahora, Estado = EstadosPin.Activo,
            });
        }
        else
        {
            existente.PinHash            = hash;
            existente.IntentosFallidos   = 0;
            existente.Bloqueado          = false;
            existente.FechaBloqueo       = null;
            existente.FechaActualizacion = ahora;
            existente.Estado             = EstadosPin.Activo;
        }
        Auditar(idUsuario, idUsuario, existente is null ? "PIN_CREADO" : "PIN_CREADO_TRAS_RESTABLECIMIENTO", ahora);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
        {
            // Dos creaciones simultáneas: la otra ya dejó el PIN.
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("Ya tienes un PIN transaccional. Usa la opción de cambiar PIN.");
        }
    }

    // El PIN anterior de 7 dígitos solo sirve para cambiarlo por uno de 5.
    public async Task<VerificacionPin> CambiarAsync(long idUsuario, CambiarPinRequest req, CancellationToken ct = default)
    {
        ValidarNuevo(req.PinNuevo, req.ConfirmacionPinNuevo);
        if (req.PinNuevo == req.PinActual)
            throw new InvalidOperationException("El PIN nuevo debe ser distinto del actual.");

        var pinActual = req.PinActual;
        if (!FormatoValido(pinActual) && !FormatoValido(pinActual, LongitudAnterior))
            return new(ResultadoPin.FormatoInvalido);

        return await VerificarPinVigenteAsync(idUsuario, pinActual, ct, req.PinNuevo);
    }

    private async Task<VerificacionPin> GuardarCambioAsync(IQueryable<PinTransaccional> pinVigente, long idUsuario,
        string pinNuevo, CancellationToken ct)
    {
        var hashNuevo = BCrypt.Net.BCrypt.HashPassword(pinNuevo);
        var ahora = DateTime.UtcNow;
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var actualizado = await pinVigente.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.PinHash, hashNuevo)
            .SetProperty(p => p.IntentosFallidos, 0)
            .SetProperty(p => p.Bloqueado, false)
            .SetProperty(p => p.FechaBloqueo, (DateTime?)null)
            .SetProperty(p => p.FechaActualizacion, ahora), ct);
        if (actualizado != 1) return new(ResultadoPin.CambiadoConcurrentemente);

        Auditar(idUsuario, idUsuario, "PIN_CAMBIADO", ahora);
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new(ResultadoPin.Correcto);
    }

    public static bool FormatoValido([NotNullWhen(true)] string? pin) => FormatoValido(pin, Longitud);

    private static bool FormatoValido([NotNullWhen(true)] string? pin, int longitud) =>
        pin is not null && pin.Length == longitud && pin.All(char.IsAsciiDigit);

    // Rechaza PIN triviales: todos los dígitos iguales o una secuencia
    // consecutiva ascendente/descendente (12345, 76543, 01234...).
    public static bool EsTrivial(string pin)
    {
        if (pin.Distinct().Count() == 1) return true;
        bool asc = true, desc = true;
        for (var i = 1; i < pin.Length; i++)
        {
            asc  &= pin[i] - pin[i - 1] == 1;
            desc &= pin[i - 1] - pin[i] == 1;
        }
        return asc || desc;
    }

    private static void ValidarNuevo(string? pin, string? confirmacion)
    {
        if (!FormatoValido(pin))
            throw new InvalidOperationException($"El PIN debe tener exactamente {Longitud} dígitos numéricos.");
        if (pin != confirmacion)
            throw new InvalidOperationException("El PIN y su confirmación no coinciden.");
        if (EsTrivial(pin!))
            throw new InvalidOperationException("El PIN es demasiado sencillo: no uses dígitos repetidos ni consecutivos.");
    }

    private void Auditar(long idActor, long idUsuarioObjetivo, string accion, DateTime ahora) =>
        db.Auditorias.Add(new Auditoria
        {
            IdUsuario   = idActor,
            Modulo      = "PIN_TRANSACCIONAL",
            Accion      = accion,
            Entidad     = "pin_transaccional",
            IdEntidad   = idUsuarioObjetivo.ToString(),
            Resultado   = "EXITOSO",
            FechaEvento = ahora,
        });
}
