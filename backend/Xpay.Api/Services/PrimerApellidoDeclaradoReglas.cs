using System.Text;
using System.Text.RegularExpressions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Una declaración del titular, nunca un apellido extraído de lastName ni
// un nuevo atributo verificado por Veriff. No contiene llamadas externas.
internal static class PrimerApellidoDeclaradoReglas
{
    internal const int LongitudMaxima = 100;

    internal static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException("El primer apellido es obligatorio.");
        if (valor.Any(char.IsControl))
            throw new InvalidOperationException("El primer apellido no puede contener caracteres de control.");
        string apellido;
        try { apellido = valor.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("El primer apellido no tiene un formato válido.");
        }
        if (apellido.Length > LongitudMaxima)
            throw new InvalidOperationException("El primer apellido no puede superar 100 caracteres.");
        return apellido;
    }

    internal static bool CoincideConCompleto(string declarado, string? completo, string? segundoDeclarado = null)
    {
        if (string.IsNullOrWhiteSpace(completo)) return false;
        var apellido = Comparable(declarado);
        var verificado = Comparable(completo);
        if (!string.IsNullOrWhiteSpace(segundoDeclarado))
            return Comparable(declarado + " " + segundoDeclarado) == verificado;
        return verificado == apellido || verificado.StartsWith(apellido + " ", StringComparison.Ordinal);
    }

    internal static bool Equivalentes(string a, string b) => Comparable(a) == Comparable(b);

    private static string Comparable(string valor) => Regex.Replace(
        valor.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ").ToUpperInvariant();

    internal static bool TieneIdentidadVigente(
        Usuario usuario, Persona persona, IReadOnlyList<KycVerificacion> actuales) =>
        usuario.IdUsuario > 0 && usuario.IdPersona == persona.IdPersona
        && usuario.Estado == "ACTIVO" && persona.Estado == "ACTIVA"
        && usuario.EmailVerificado && !string.IsNullOrWhiteSpace(persona.Email)
        && !usuario.RequiereCambioClave && usuario.EstadoKycActual == "APROBADO"
        && persona.IdentidadVerificada && persona.IdentidadVerificadaProveedor == "VERIFF"
        && !string.IsNullOrWhiteSpace(persona.ApellidoVerificadoCompleto)
        && actuales.Count == 1 && actuales[0].EsActual
        && actuales[0].IdUsuario == usuario.IdUsuario && actuales[0].IdPersona == persona.IdPersona
        && actuales[0].Proveedor == "VERIFF" && actuales[0].EstadoKyc == "APROBADO";
}

public sealed class PrimerApellidoYaRegistradoException : InvalidOperationException
{
    public PrimerApellidoYaRegistradoException()
        : base("Tu primer apellido ya está registrado. Si necesitas corregirlo, contacta a XPAY.") { }
}
