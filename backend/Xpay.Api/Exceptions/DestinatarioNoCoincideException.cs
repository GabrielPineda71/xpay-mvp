namespace Xpay.Api.Exceptions;

// El titular actual de la wallet destino no es el usuario que el ordenante
// confirmó (UsuarioDestinoEsperado), o la wallet dejó de ser un destino
// válido entre la confirmación y la transferencia. No se mueve dinero. No
// hereda de InvalidOperationException para no caer en el 400 genérico: se
// mapea a 409 en el controller.
public class DestinatarioNoCoincideException()
    : Exception("El destinatario cambió o no coincide. Vuelve a confirmarlo.");
