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
}

public readonly record struct VerificacionPin(ResultadoPin Resultado, int IntentosRestantes = 0);

// PIN transaccional de 7 dígitos sobre la tabla existente pin_transaccional
// (una fila por usuario). Solo se guarda el hash BCrypt.
//
// Verificación resistente a concurrencia: cada intento RESERVA primero un
// intento con un UPDATE atómico condicionado a "ACTIVO, no bloqueado y con
// intentos < Max" (el mismo UPDATE bloquea al llegar a Max). Solo después se
// compara el hash; si acierta, otro UPDATE reinicia el contador. Así, ni con
// peticiones simultáneas se prueban más de MaxIntentos PIN antes del bloqueo.
// Los UPDATE se ejecutan fuera de cualquier transacción del llamador: un
// fallo posterior de la operación de dinero no deshace el intento contado.
public sealed class PinTransaccionalService(XpayDbContext db)
{
    public const int Longitud    = 7;
    public const int MaxIntentos = 5;

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

    public async Task<VerificacionPin> VerificarAsync(long idUsuario, string? pin, CancellationToken ct = default)
    {
        if (!FormatoValido(pin)) return new(ResultadoPin.FormatoInvalido);

        var registro = await db.PinesTransaccionales.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        if (registro is null || registro.Estado != EstadosPin.Activo) return new(ResultadoPin.NoConfigurado);
        if (registro.Bloqueado) return new(ResultadoPin.Bloqueado);

        var ahora = DateTime.UtcNow;
        var reservado = await db.PinesTransaccionales
            .Where(p => p.IdPin == registro.IdPin && p.Estado == EstadosPin.Activo
                     && !p.Bloqueado && p.IntentosFallidos < MaxIntentos)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IntentosFallidos, p => p.IntentosFallidos + 1)
                .SetProperty(p => p.Bloqueado, p => p.IntentosFallidos + 1 >= MaxIntentos)
                .SetProperty(p => p.FechaBloqueo, p => p.IntentosFallidos + 1 >= MaxIntentos ? ahora : p.FechaBloqueo)
                .SetProperty(p => p.FechaActualizacion, ahora), ct);
        if (reservado == 0) return new(ResultadoPin.Bloqueado);

        if (BCrypt.Net.BCrypt.Verify(pin, registro.PinHash))
        {
            await db.PinesTransaccionales
                .Where(p => p.IdPin == registro.IdPin && p.PinHash == registro.PinHash)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.IntentosFallidos, 0)
                    .SetProperty(p => p.Bloqueado, false)
                    .SetProperty(p => p.FechaBloqueo, (DateTime?)null), ct);
            return new(ResultadoPin.Correcto);
        }

        var intentos = await db.PinesTransaccionales.AsNoTracking()
            .Where(p => p.IdPin == registro.IdPin)
            .Select(p => p.IntentosFallidos)
            .FirstAsync(ct);
        var restantes = MaxIntentos - intentos;
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

    // Cambia el PIN validando el actual con las mismas reglas de intentos.
    public async Task<VerificacionPin> CambiarAsync(long idUsuario, CambiarPinRequest req, CancellationToken ct = default)
    {
        ValidarNuevo(req.PinNuevo, req.ConfirmacionPinNuevo);
        if (req.PinNuevo == req.PinActual)
            throw new InvalidOperationException("El PIN nuevo debe ser distinto del actual.");

        var verificacion = await VerificarAsync(idUsuario, req.PinActual, ct);
        if (verificacion.Resultado != ResultadoPin.Correcto) return verificacion;

        var registro = await db.PinesTransaccionales.FirstAsync(p => p.IdUsuario == idUsuario, ct);
        var ahora = DateTime.UtcNow;
        registro.PinHash            = BCrypt.Net.BCrypt.HashPassword(req.PinNuevo);
        registro.IntentosFallidos   = 0;
        registro.FechaActualizacion = ahora;
        Auditar(idUsuario, idUsuario, "PIN_CAMBIADO", ahora);
        await db.SaveChangesAsync(ct);
        return verificacion;
    }

    public static bool FormatoValido(string? pin) =>
        pin is { Length: Longitud } && pin.All(char.IsAsciiDigit);

    // Rechaza PIN triviales: todos los dígitos iguales o una secuencia
    // consecutiva ascendente/descendente (1234567, 7654321, 0123456...).
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
