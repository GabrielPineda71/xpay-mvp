using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Common;

// Bloqueo financiero, fase visual — reglas de arquitectura para que ningún
// endpoint ni escritura nueva se salte el interruptor sin una decisión
// explícita:
//   · todo endpoint de escritura lleva [OperacionFinanciera] o figura aquí
//     como no financiero (con su motivo);
//   · ninguna escritura en las tablas de dinero esquiva el change tracker
//     (BloqueoFinancieroInterceptor solo ve SaveChanges).
public sealed class BloqueoFinancieroArquitecturaTests
{
    // Escrituras que NO mueven dinero ni crean obligaciones. Un endpoint nuevo
    // que no esté aquí ni lleve [OperacionFinanciera] hace fallar la prueba.
    private static readonly HashSet<string> NoFinancieros =
    [
        // Identidad, sesión, perfil y KYC (navegación y visualización).
        "AuthController.Login", "AuthController.CambiarClave", "AuthController.CambiarClaveObligatoria",
        "RecuperacionClaveController.SolicitarRecuperacion", "RecuperacionClaveController.RestablecerClave",
        "RegistroPublicoController.Registrar", "RegistroPublicoController.VerificarEmail",
        "RegistroPublicoController.ReenviarVerificacion",
        "UsuariosController.RegistrarInicial", "UsuariosController.RegistrarUsuarioFinal",
        "UsuariosController.ActualizarMiPerfil",
        "KycController.VeriffSession", "KycController.VeriffWebhook", "KycController.ReconciliarVeriff",
        "KycController.SimularEstadoQa",
        // Administración de usuarios (el alta del piloto crea una wallet con saldo 0).
        "UsuariosAdminController.AltaUsuarioPiloto", "UsuariosAdminController.Activar",
        "UsuariosAdminController.Inactivar", "UsuariosAdminController.Desbloquear",
        "UsuariosAdminController.RestablecerClave", "UsuariosAdminController.AsignarRol",
        "UsuariosAdminController.RevocarRol", "UsuariosAdminController.RestablecerPin",
        // PIN transaccional propio: crear/cambiar no mueve dinero (se crea antes de operar).
        "PinController.Crear", "PinController.Cambiar",
        // Solo ambientes no productivos (404 en productivo).
        "QaClientePruebaController.Crear", "QaIdentidadPruebaController.Preparar",
        "ComerciosAliadosController.BackfillDemoContexto",
        // Configuración y catálogos: no escriben ledger, saldos ni cupos.
        "CarteraOrdinariaController.UpsertParametro", "CarteraOrdinariaController.UpsertPolitica",
        "CarteraOrdinariaController.CreateGastoCobranza", "CarteraOrdinariaController.UpdateGastoCobranza",
        "CarteraOrdinariaController.SimularUtilizacion", "CarteraOrdinariaController.EjecutarLotePurgaB4",
        "LibranzaController.CrearConvenio", "LibranzaController.ActualizarConvenio",
        "LibranzaController.CrearParametros", "LibranzaController.ActualizarParametros",
        "LibranzaController.CrearRango", "LibranzaController.ActualizarRango",
        "LibranzaController.AsociarUsuarioEmpresa", "LibranzaController.AsociarEmpleadoUsuario",
        "LibranzaEmpresaController.ImportarEmpleados", "LibranzaEmpresaController.CancelarLote",
        "ComerciosAliadosController.Crear", "ComerciosAliadosController.Actualizar",
        "ComerciosAliadosController.Activar", "ComerciosAliadosController.Inactivar",
        "ComerciosAliadosController.CrearRepresentante", "ComerciosAliadosController.ActualizarRepresentante",
        "ComerciosAliadosController.CrearEstablecimiento", "ComerciosAliadosController.ActualizarEstablecimiento",
        "ComerciosAliadosController.CrearUsuarioSolicitado", "ComerciosAliadosController.ActualizarUsuarioSolicitado",
        "ComerciosAliadosController.SubirDocumento", "ComerciosAliadosController.EliminarDocumento",
        "ComerciosAliadosController.VincularOperativo", "ComerciosAliadosController.CrearCondicion",
        "ComerciosAliadosController.ActualizarCondicion", "ComerciosAliadosController.CrearParametro",
        "ComerciosAliadosController.ActualizarParametro", "ComerciosAliadosController.CrearUsuarioOperativo",
        "ComerciosAliadosController.ActualizarUsuarioOperativo", "ComerciosAliadosController.ImportarParametros",
        "ComerciosAliadosController.CopiarGlobalAComercio",
        // Cajas y cierres de comercio: arqueos y estados, sin asientos.
        "WalletCajaComercioController.Abrir", "WalletCajaComercioController.CorregirFondoInicial",
        "WalletCajaComercioController.IniciarCuadre", "WalletCajaComercioController.Cerrar",
        "WalletCajaComercioController.Revisar", "WalletCierreDiarioComercioController.Generar",
        "AdminWalletCierresDiariosComercioController.Revisar", "AdminWalletCierresDiariosComercioController.Cerrar",
        // Webhook de Passport: SIN atributo a propósito. El evento se registra y
        // LibranzaRecaudoAplicacionService exige el interruptor antes de aplicar:
        // queda sin procesar y Passport reintenta (nunca un 200 sin procesar).
        "PassportWebhookController.Webhook",
    ];

    private static readonly Type[] AtributosEscritura =
        [typeof(HttpPostAttribute), typeof(HttpPutAttribute), typeof(HttpPatchAttribute), typeof(HttpDeleteAttribute)];

    private static IEnumerable<(string Nombre, MethodInfo Metodo)> EndpointsDeEscritura() =>
        typeof(OperacionFinancieraAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => AtributosEscritura.Contains(a.GetType())))
            .Select(m => ($"{m.DeclaringType!.Name}.{m.Name}", m));

    [Fact]
    public void Todo_endpoint_de_escritura_esta_clasificado()
    {
        var sinClasificar = EndpointsDeEscritura()
            .Where(e => e.Metodo.GetCustomAttribute<OperacionFinancieraAttribute>() is null && !NoFinancieros.Contains(e.Nombre))
            .Select(e => e.Nombre)
            .ToList();

        Assert.True(sinClasificar.Count == 0,
            "Endpoints de escritura sin [OperacionFinanciera] ni clasificación no financiera: " + string.Join(", ", sinClasificar));
    }

    [Fact]
    public void Ningun_no_financiero_lleva_ademas_el_atributo_y_la_lista_no_tiene_restos()
    {
        var endpoints = EndpointsDeEscritura().ToDictionary(e => e.Nombre, e => e.Metodo);
        var restos = NoFinancieros.Where(n => !endpoints.ContainsKey(n)).ToList();
        var dobles = NoFinancieros.Where(n => endpoints.TryGetValue(n, out var m)
            && m.GetCustomAttribute<OperacionFinancieraAttribute>() is not null).ToList();
        Assert.True(restos.Count == 0, "Clasificados que ya no existen: " + string.Join(", ", restos));
        Assert.True(dobles.Count == 0, "Marcados a la vez como no financieros: " + string.Join(", ", dobles));
    }

    [Theory]
    [InlineData("WalletsController.Transferir")]
    [InlineData("QrController.Pagar")]
    [InlineData("WalletRecargaComercioController.Recargar")]
    [InlineData("CarteraOrdinariaController.ConfirmarAvanceWallet")]
    [InlineData("CarteraOrdinariaController.SolicitarCupo")]
    [InlineData("BrebController.RegistrarMiLlave")]
    [InlineData("BrebController.SolicitarRetiroReal")]
    [InlineData("LibranzaClienteController.SolicitarAnticipo")]
    public void Operaciones_clave_llevan_el_atributo(string nombre)
    {
        var metodo = EndpointsDeEscritura().Where(e => e.Nombre == nombre).Select(e => e.Metodo).SingleOrDefault();
        Assert.NotNull(metodo);
        Assert.NotNull(metodo.GetCustomAttribute<OperacionFinancieraAttribute>());
    }

    // SQL directo o ExecuteUpdate/ExecuteDelete sobre las tablas de dinero
    // esquivaría el interceptor: no debe existir en el código de la API.
    [Fact]
    public void Ninguna_escritura_de_dinero_esquiva_el_change_tracker()
    {
        var raiz = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Xpay.Api"));
        Assert.True(Directory.Exists(raiz), $"No se encontró el código de la API en {raiz}.");

        var sqlDirecto = new Regex(
            @"\b(UPDATE|INSERT\s+INTO|DELETE\s+FROM|MERGE)\s+(dbo\.)?(wallet_saldos|wallet_movimientos|ledger_transacciones|ledger_movimientos|cartera_cupos_ordinarios|cartera_utilizaciones)\b",
            RegexOptions.IgnoreCase);
        var masivo = new Regex(
            @"\b(WalletSaldos|WalletMovimientos|LedgerTransacciones|LedgerMovimientos|CarteraCuposOrdinarios|CarteraUtilizaciones)\b[^;]*\.Execute(Update|Delete)",
            RegexOptions.Singleline);

        var hallazgos = Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => { var t = File.ReadAllText(f); return sqlDirecto.IsMatch(t) || masivo.IsMatch(t); })
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(hallazgos.Count == 0, "Escrituras de dinero fuera del change tracker en: " + string.Join(", ", hallazgos));
    }
}
