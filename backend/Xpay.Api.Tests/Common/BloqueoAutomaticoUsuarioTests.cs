using Xpay.Api.Common;
using Xpay.Api.Models;
using Xunit;

namespace Xpay.Api.Tests.Common;

// P0-B2 — fuente única del bloqueo automático: solo BLOQUEADO + motivo exacto
// + fecha de bloqueo es reconocible; cualquier variación no lo es.
public class BloqueoAutomaticoUsuarioTests
{
    private static readonly DateTime Fecha = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Motivo_ConservaElLiteralHistorico()
        => Assert.Equal("Bloqueo automático por intentos fallidos.", BloqueoAutomaticoUsuario.Motivo);

    [Fact]
    public void BloqueadoConMotivoExactoYFecha_EsBloqueoAutomatico()
    {
        Assert.True(BloqueoAutomaticoUsuario.Es(EstadosUsuario.Bloqueado, BloqueoAutomaticoUsuario.Motivo, Fecha));
        Assert.True(BloqueoAutomaticoUsuario.Es(new Usuario
        {
            Estado = EstadosUsuario.Bloqueado, MotivoBloqueo = BloqueoAutomaticoUsuario.Motivo, FechaBloqueo = Fecha,
        }));
    }

    [Theory]
    [InlineData("BLOQUEADO", "Bloqueo administrativo.", true)]
    [InlineData("BLOQUEADO", "bloqueo automático por intentos fallidos.", true)]
    [InlineData("BLOQUEADO", "Bloqueo automático por intentos fallidos. ", true)]
    [InlineData("BLOQUEADO", "Bloqueo automatico por intentos fallidos.", true)]
    [InlineData("BLOQUEADO", null, true)]
    [InlineData("BLOQUEADO", "Bloqueo automático por intentos fallidos.", false)]
    [InlineData("bloqueado", "Bloqueo automático por intentos fallidos.", true)]
    [InlineData("ACTIVO", "Bloqueo automático por intentos fallidos.", true)]
    [InlineData("INACTIVO", "Bloqueo automático por intentos fallidos.", true)]
    [InlineData(null, "Bloqueo automático por intentos fallidos.", true)]
    public void CualquierVariacion_NoEsBloqueoAutomatico(string? estado, string? motivo, bool conFecha)
        => Assert.False(BloqueoAutomaticoUsuario.Es(estado, motivo, conFecha ? Fecha : null));

    [Fact]
    public void UsuarioNulo_Lanza()
        => Assert.Throws<ArgumentNullException>(() => BloqueoAutomaticoUsuario.Es((Usuario)null!));
}
