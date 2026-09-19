namespace Xpay.Api.DTOs;

// XPAY-399 — GET /api/usuarios/mi-perfil. Construido explícitamente campo
// por campo (NUNCA serializa la entidad EF Persona/Usuario completa) — ver
// XPAY-398 PASO 2: nunca expone payloads crudos de Veriff, session_id,
// vendor_data ni ningún identificador interno innecesario. IdentidadVerificada
// y EstadoKycActual se exponen como resumen booleano/textual únicamente —
// nada de kyc_verificaciones (historial) viaja aquí.
public class MiPerfilResponseDto
{
    public string Usuario { get; set; } = string.Empty;

    // Datos declarados (pueden venir de registro-final, o quedar null si el
    // usuario entró por registro-inicial y todavía no los completa/verifica).
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? TipoDocumento { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateTime? FechaNacimiento { get; set; }

    // Datos de contacto declarados — editables por este endpoint (ver
    // ActualizarMiPerfilRequest). *Verificado* indica si XPAY ya confirmó el
    // dato (hoy nunca true — no existe mecanismo real de verificación, ver
    // XPAY-398 PASO 4) — el valor declarado y el valor verificado NUNCA deben
    // presentarse como equivalentes en la UI.
    public string Celular { get; set; } = string.Empty;
    public string? Email { get; set; }

    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Departamento { get; set; }
    public string Pais { get; set; } = string.Empty;

    // Resumen de identidad — nunca el detalle crudo de Veriff.
    public bool IdentidadVerificada { get; set; }
    public string EstadoKycActual { get; set; } = string.Empty;

    public bool EmailVerificado { get; set; }
    public bool CelularVerificado { get; set; }
}

// XPAY-399 — PATCH /api/usuarios/mi-perfil. ÚNICAMENTE los 5 campos
// autorizados por decisión de producto (XPAY-399 PASO 3) — cualquier otro
// campo que el cliente envíe es ignorado por el model binder (nunca se
// mapea a Persona/Usuario completos, cero riesgo de overposting).
//
// Semántica NULL/ausente/vacío (aplicada igual a los 5 campos, decisión
// explícita de este ticket):
//   - propiedad ausente en el JSON, o enviada como null  -> NO modificar.
//   - cadena vacía ("" o solo espacios)                  -> LIMPIAR el
//     campo (solo posible en Email/Direccion/Ciudad/Departamento, que son
//     nullable en la base de datos). Celular es NOT NULL — un valor vacío
//     para Celular se RECHAZA explícitamente (ver PerfilService), nunca se
//     limpia en silencio.
//   - cualquier otro valor                                -> se valida
//     (longitud/formato) y, si es distinto al actual, se aplica.
// Esta fase NO permite limpiar Celular ni editar identidad legal — ver
// PerfilService para la aplicación exacta de esta regla.
public record ActualizarMiPerfilRequest(
    string? Celular,
    string? Email,
    string? Direccion,
    string? Ciudad,
    string? Departamento
);
