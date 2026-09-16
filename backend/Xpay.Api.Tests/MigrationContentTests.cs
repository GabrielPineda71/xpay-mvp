using System.Text.RegularExpressions;
using Xunit;

namespace Xpay.Api.Tests;

// XPAY-385 FASE 7 — valida el CONTENIDO REAL de la migración 044 (el
// archivo .sql que efectivamente se ejecuta contra la DB, no una copia en
// memoria) y del código de bootstrap, para las garantías que no pueden
// verificarse sin una base de datos real (ningún test de este repositorio
// construye un XpayDbContext real — mismo criterio ya documentado en
// BrebKeyResolutionRequestBuilderTests.cs).
public class MigrationContentTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string LeerMigracion044()
    {
        var path = Path.Combine(RepoRoot(), "database", "044_cuenta_operativa_real.sql");
        Assert.True(File.Exists(path), $"No se encontró la migración esperada en: {path}");
        return File.ReadAllText(path);
    }

    // FASE 7 test #1 — cuenta 110103 definida correctamente.
    [Fact]
    public void Migracion044_Define110103_ConNombreTipoYNaturalezaCorrectos()
    {
        var sql = LeerMigracion044();
        Assert.Contains("'110103'", sql);
        Assert.Contains("Banco Coopcentral XPAY REAL (Passport)", sql);
        Assert.Contains("'ACTIVO'", sql);
        Assert.Contains("'BANCO'", sql);
    }

    // FASE 7 test #2 — 110102 permanece intacta: la migración 044 solo
    // puede MENCIONAR 110102 en comentarios (para documentar que queda
    // legacy), nunca en una sentencia INSERT/UPDATE/DELETE que la toque.
    [Fact]
    public void Migracion044_NuncaEjecutaDmlSobre110102()
    {
        var sql = LeerMigracion044();

        // Quita bloques /* ... */ (p.ej. el encabezado del archivo) antes
        // de revisar línea por línea los comentarios de una sola línea (--).
        var sinBloques = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var lineas = sinBloques.Split('\n');

        foreach (var linea in lineas)
        {
            var sinComentario = linea.Contains("--") ? linea[..linea.IndexOf("--", StringComparison.Ordinal)] : linea;
            if (sinComentario.Contains("110102"))
            {
                Assert.Fail($"Migración 044 referencia '110102' fuera de un comentario: '{linea.Trim()}' — 110102 debe permanecer intacta.");
            }
        }
    }

    // FASE 7 test #3 — la entidad cuenta_operativa exige una cuenta ledger
    // real (FK), no un código de texto duplicado sin integridad referencial.
    [Fact]
    public void Migracion044_CuentasOperativas_TieneForeignKeyRealHaciaLedgerCuentas()
    {
        var sql = LeerMigracion044();
        Assert.Contains("FK_cuenta_operativa_ledger", sql);
        Assert.Contains("REFERENCES ledger_cuentas(id_cuenta)", sql);
    }

    // Constraint de unicidad que sustenta la idempotencia del bootstrap.
    [Fact]
    public void Migracion044_CuentasOperativas_TieneUniqueSobreCombinacion()
    {
        var sql = LeerMigracion044();
        Assert.Contains("UQ_cuenta_operativa_combinacion", sql);
        Assert.Contains("UNIQUE (proveedor, institucion, moneda, ambiente)", sql);
    }

    [Fact]
    public void Migracion044_CuentasOperativas_RestringeAmbienteASandboxOProduccion()
    {
        var sql = LeerMigracion044();
        Assert.Contains("CHK_cuenta_operativa_ambiente", sql);
        Assert.Contains("ambiente IN ('SANDBOX', 'PRODUCCION')", sql);
    }

    // FASE 7 test #8 — el bootstrap nunca crea ledger_transaccion/
    // ledger_movimiento: sólo referencia la lectura de LedgerCuentas
    // existente y la escritura de CuentasOperativas — ningún movimiento
    // financiero nuevo.
    [Fact]
    public void OperationalAccountBootstrapper_NuncaReferenciaTiposDeMovimientoFinanciero()
    {
        var path = Path.Combine(RepoRoot(), "backend", "Xpay.Api", "Services", "OperationalAccountBootstrapper.cs");
        Assert.True(File.Exists(path), $"No se encontró el archivo esperado en: {path}");
        var codigo = File.ReadAllText(path);

        Assert.DoesNotContain("LedgerTransaccion", codigo);
        Assert.DoesNotContain("LedgerMovimiento", codigo);
        Assert.DoesNotContain("WalletMovimiento", codigo);
        Assert.DoesNotContain("WalletSaldo", codigo);
    }

    // El bootstrap es una mejora OPCIONAL de arranque — un fallo ahí
    // (p. ej. migración 044 todavía no aplicada) nunca debe tumbar el
    // arranque completo de la API.
    [Fact]
    public void OperationalAccountBootstrapper_NuncaPropagaExcepcionesAlLlamador()
    {
        var path = Path.Combine(RepoRoot(), "backend", "Xpay.Api", "Services", "OperationalAccountBootstrapper.cs");
        var codigo = File.ReadAllText(path);

        Assert.Contains("catch (Exception ex)", codigo);
        Assert.Contains("EnsureSeededCoreAsync", codigo);
    }

    // Confirma que Program.cs efectivamente invoca el bootstrap al
    // arrancar (y no quedó código muerto sin conectar).
    [Fact]
    public void ProgramCs_InvocaElBootstrapDeCuentaOperativa()
    {
        var path = Path.Combine(RepoRoot(), "backend", "Xpay.Api", "Program.cs");
        var codigo = File.ReadAllText(path);

        Assert.Contains("OperationalAccountBootstrapper.EnsureSeededAsync", codigo);
    }
}
