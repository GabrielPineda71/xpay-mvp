using Xpay.Api.Common;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// P0-B — validación y normalización del registro público, sin BD. Reutiliza
// las reglas existentes: nombre de usuario como registro-inicial y login
// (Trim + ToLower, máx. 100, "qa.*" reservado en productivo), la política
// AuthService.ValidarPoliticaClave y EmailNormalizador. Los errores son
// InvalidOperationException, que los controladores traducen a 400.
public static class RegistroPublicoValidacion
{
    public const int LongitudMaximaUsuario = 100;
    public const int LongitudMaximaCelular = 30;

    public static RegistroPublicoNormalizado Validar(RegistroPublicoRequest request, IAmbienteXpay ambiente)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ambiente);

        if (string.IsNullOrWhiteSpace(request.NombreUsuario))
            throw new InvalidOperationException("El nombre de usuario es obligatorio.");
        if (!NombreUsuarioReservado.PermiteCrear(request.NombreUsuario, ambiente))
            throw new InvalidOperationException(RegistroInicialService.MensajeUsuarioNoDisponible);
        var nombreUsuario = request.NombreUsuario.Trim().ToLower();
        if (nombreUsuario.Length > LongitudMaximaUsuario)
            throw new InvalidOperationException("El nombre de usuario no puede superar 100 caracteres.");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new InvalidOperationException("El email es obligatorio.");
        if (!EmailNormalizador.TryNormalizar(request.Email, out var email))
            throw new InvalidOperationException("El email no es válido.");

        AuthService.ValidarPoliticaClave(request.Password, nombreUsuario);

        if (string.IsNullOrWhiteSpace(request.Celular))
            throw new InvalidOperationException("El celular es obligatorio.");
        var celular = request.Celular.Trim();
        if (celular.Length > LongitudMaximaCelular)
            throw new InvalidOperationException("El celular no puede superar 30 caracteres.");

        return new RegistroPublicoNormalizado(email, nombreUsuario, celular);
    }
}
