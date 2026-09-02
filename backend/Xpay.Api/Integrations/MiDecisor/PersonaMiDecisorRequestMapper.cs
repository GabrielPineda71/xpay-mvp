using Xpay.Api.Models;

namespace Xpay.Api.Integrations.MiDecisor;

// M2.3a — mapeo PURO Persona → MiDecisorConsultaRequest, aislado para que la
// "autoridad de identidad" (declarada vs. verificada por KYC) pueda cambiarse
// más adelante sin tocar el orquestador.
//
// DECISIÓN ESTRUCTURAL M2.3a: usa los campos DECLARADOS
//   Persona.TipoDocumento / NumeroDocumento / PrimerApellido.
// NO usa NumeroDocumentoVerificado / ApellidoVerificadoCompleto /
// TipoDocumentoVeriffRaw — `ApellidoVerificadoCompleto` es un apellido
// COMPLETO y no hay regla segura para extraer "primer apellido" de él.
// Esto NO declara que estos campos sean la autoridad definitiva para UAT
// (MAPPING_AUTHORITY sigue UNRESOLVED).
//
// Falla cerrado: si los datos no permiten una consulta PN válida devuelve
// false + un motivo saneado (NO incluye el valor recibido). El llamador
// debe abortar ANTES de cualquier transición durable o llamada al proveedor.
public static class PersonaMiDecisorRequestMapper
{
    public static bool TryMapear(
        Persona persona,
        out MiDecisorConsultaRequest request,
        out string motivoRechazo)
    {
        request = null!;
        motivoRechazo = string.Empty;

        var tipo     = (persona.TipoDocumento ?? string.Empty).Trim();
        var numero   = (persona.NumeroDocumento ?? string.Empty).Trim();
        var apellido = (persona.PrimerApellido ?? string.Empty).Trim();

        if (!TipoDocumentoMiDecisorMapper.TryMapPersonaNatural(tipo, out var tipoCodigo))
        {
            motivoRechazo = "Tipo de documento no soportado para consulta de Persona Natural.";
            return false;
        }

        if (numero.Length is < 3 or > 13 || !EsSoloDigitosAscii(numero))
        {
            motivoRechazo = "Número de documento ausente o con formato no válido.";
            return false;
        }

        if (apellido.Length == 0)
        {
            motivoRechazo = "Primer apellido ausente.";
            return false;
        }

        request = new MiDecisorConsultaRequest(tipoCodigo, numero, apellido);
        return true;
    }

    private static bool EsSoloDigitosAscii(string value)
    {
        foreach (var c in value)
            if (!char.IsAsciiDigit(c))
                return false;
        return value.Length > 0;
    }
}
