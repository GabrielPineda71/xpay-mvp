using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-B — una cuenta pública nace PENDIENTE_VERIFICACION, que nunca es ACTIVO
// (AuthService.LoginAsync solo admite ACTIVO).
public class EstadosUsuarioTests
{
    [Fact]
    public void PendienteVerificacion_TieneElValorExacto()
        => Assert.Equal("PENDIENTE_VERIFICACION", EstadosUsuario.PendienteVerificacion);

    [Fact]
    public void PendienteVerificacion_NoEsActivo()
        => Assert.False(string.Equals(
            EstadosUsuario.PendienteVerificacion, EstadosUsuario.Activo, StringComparison.Ordinal));
}
