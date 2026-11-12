namespace Xpay.Api.DTOs;

// PIN transaccional — cuerpo de los endpoints que mueven dinero. El filtro
// RequierePinTransaccional verifica Pin y lo borra del objeto antes de la
// action: ningún servicio posterior lo recibe ni puede registrarlo.
public interface IConPinTransaccional
{
    string? Pin { get; set; }
}

public class EstadoPinDto
{
    public bool Configurado       { get; set; }
    public bool Bloqueado         { get; set; }
    public bool Restablecido      { get; set; }
    public int  IntentosRestantes { get; set; }
}

public class CrearPinRequest
{
    // Contraseña de ingreso del propio usuario: confirma que quien crea el PIN
    // es el titular de la sesión.
    public string? ClaveActual     { get; set; }
    public string? Pin             { get; set; }
    public string? ConfirmacionPin { get; set; }
}

public class CambiarPinRequest
{
    public string? PinActual            { get; set; }
    public string? PinNuevo             { get; set; }
    public string? ConfirmacionPinNuevo { get; set; }
}
