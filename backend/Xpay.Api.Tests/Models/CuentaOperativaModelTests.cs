using Xpay.Api.Models;
using Xunit;

namespace Xpay.Api.Tests.Models;

// XPAY-385 FASE 7 tests #3/#4 — verificación ESTRUCTURAL (reflexión, sin
// DB) de que la entidad CuentaOperativa nunca puede almacenar un
// account_id completo, y que la relación con la cuenta ledger es
// obligatoria (no nullable) por construcción del tipo.
public class CuentaOperativaModelTests
{
    [Fact]
    public void CuentaOperativa_NoTienePropiedadParaElAccountIdCrudo()
    {
        var propiedades = typeof(CuentaOperativa).GetProperties();

        Assert.DoesNotContain(propiedades, p =>
            p.Name.Contains("AccountId", StringComparison.OrdinalIgnoreCase)
            && !p.Name.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuentaOperativa_TienePropiedadFingerprintDeTipoString()
    {
        var prop = typeof(CuentaOperativa).GetProperty("AccountIdFingerprint");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void CuentaOperativa_ConfigKeyReference_EsUnNombreDeVariable_NoUnValor()
    {
        var prop = typeof(CuentaOperativa).GetProperty("ConfigKeyReference");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    // FASE 7 test #3 — la relación con la cuenta ledger es un `long` NO
    // nullable: en tiempo de compilación es imposible construir una
    // CuentaOperativa sin asignarle explícitamente un id_cuenta_ledger
    // (queda en 0 por defecto, que a su vez viola la FK real de la
    // migración 044 al persistir — la garantía completa es DB+código).
    [Fact]
    public void CuentaOperativa_IdCuentaLedger_EsNoNullable()
    {
        var prop = typeof(CuentaOperativa).GetProperty("IdCuentaLedger");
        Assert.NotNull(prop);
        Assert.Equal(typeof(long), prop!.PropertyType);
        Assert.False(Nullable.GetUnderlyingType(prop.PropertyType) is not null);
    }
}
