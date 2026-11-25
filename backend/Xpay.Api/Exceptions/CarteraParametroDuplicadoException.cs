namespace Xpay.Api.Exceptions;

public sealed class CarteraParametroDuplicadoException() : InvalidOperationException(
    "Esta modalidad ya tiene parámetros. Actualiza la lista para editarlos.");
