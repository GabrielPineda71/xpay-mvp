using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// Integración relacional: catálogos y cuentas sintéticos, sin proveedores.
[Collection("SqlIntegration")]
public sealed class PerfilDaneSqlTests
{
    [Fact]
    public async Task Domicilio_ValidaJerarquiaActiva_PersisteCanonicos_InvalidaTextoLibre_YAdminBuscaFallback()
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection");
        if (string.IsNullOrWhiteSpace(cs))
        {
            Assert.False(Environment.GetEnvironmentVariable("CI") == "true", "Falta conexión SQL local para PerfilDaneSqlTests.");
            return;
        }
        await using var db = new XpayDbContext(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var paises = new List<CatalogoPais>();
        var departamentos = new List<CatalogoDepartamento>();
        var ciudades = new List<CatalogoCiudad>();
        Persona? persona = null;
        Usuario? usuario = null;
        try
        {
            // CO es una segunda clave ISO válida para Colombia, distinta del
            // COL sembrado por 031; todas estas filas pertenecen a la prueba.
            var pais = new CatalogoPais { Codigo = "CO", Nombre = "Colombia sintética", FechaCreacion = DateTime.UtcNow };
            var otroPais = new CatalogoPais { Codigo = "Z" + sufijo[..2], Nombre = "Otro país sintético", FechaCreacion = DateTime.UtcNow };
            paises.AddRange(new[] { pais, otroPais });
            db.CatalogoPaises.AddRange(paises);
            await db.SaveChangesAsync();
            var depto = new CatalogoDepartamento { IdPais = pais.IdPais, CodigoDivipola = "01", Nombre = "Departamento sintético", FechaCreacion = DateTime.UtcNow };
            var otroDepto = new CatalogoDepartamento { IdPais = pais.IdPais, CodigoDivipola = "02", Nombre = "Otro departamento", FechaCreacion = DateTime.UtcNow };
            var deptoExtranjero = new CatalogoDepartamento { IdPais = otroPais.IdPais, CodigoDivipola = "01", Nombre = "Departamento extranjero", FechaCreacion = DateTime.UtcNow };
            departamentos.AddRange(new[] { depto, otroDepto, deptoExtranjero });
            db.CatalogoDepartamentos.AddRange(departamentos);
            await db.SaveChangesAsync();
            var ciudad = new CatalogoCiudad { IdDepartamento = depto.IdDepartamento, CodigoDivipola = "01001", Nombre = "Ciudad sintética", Tipo = "MUNICIPIO", FechaCreacion = DateTime.UtcNow };
            var extranjera = new CatalogoCiudad { IdDepartamento = deptoExtranjero.IdDepartamento, CodigoDivipola = "01001", Nombre = "Ciudad extranjera", Tipo = "MUNICIPIO", FechaCreacion = DateTime.UtcNow };
            ciudades.AddRange(new[] { ciudad, extranjera });
            db.CatalogoCiudades.AddRange(ciudades);
            await db.SaveChangesAsync();
            var unidad = await db.Database.SqlQueryRaw<long>("SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
            persona = new Persona
            {
                IdUnidadNegocio = unidad, Celular = "3000000000", FechaCreacion = DateTime.UtcNow,
                IdentidadVerificada = true, IdentidadVerificadaProveedor = "VERIFF", IdentidadVerificadaFecha = DateTime.UtcNow,
                NombreVerificadoCompleto = "Persona" + sufijo, ApellidoVerificadoCompleto = "Sintética",
                TipoDocumentoVeriffRaw = "ID_CARD", NumeroDocumentoVerificado = "9" + sufijo
            };
            db.Personas.Add(persona);
            await db.SaveChangesAsync();
            usuario = new Usuario { IdPersona = persona.IdPersona, NombreUsuario = "dane_" + sufijo, PasswordHash = "hash-sintetico", EstadoKycActual = "APROBADO", EmailVerificado = true, FechaCreacion = DateTime.UtcNow };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            var servicio = new PerfilService(db);
            var request = new GuardarDomicilioDaneRequest("  Calle de prueba 12  ", depto.IdDepartamento, ciudad.IdCiudad);
            Assert.False(await servicio.DomicilioDaneCompletoAsync(persona.IdPersona));
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request with { IdDepartamentoDane = otroDepto.IdDepartamento }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request with { IdDepartamentoDane = deptoExtranjero.IdDepartamento, IdCiudadDane = extranjera.IdCiudad }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request with { Direccion = " " }));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => servicio.ActualizarDomicilioDaneAsync(-1, persona.IdPersona, request));
            var respuesta = await servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request);
            Assert.Equal("Calle de prueba 12", respuesta.Direccion);
            Assert.Equal(ciudad.Nombre, respuesta.Ciudad);
            Assert.Equal(depto.Nombre, respuesta.Departamento);
            Assert.Equal(ciudad.IdCiudad, respuesta.IdCiudadDane);
            Assert.Equal(depto.IdDepartamento, respuesta.IdDepartamentoDane);
            Assert.True(await servicio.DomicilioDaneCompletoAsync(persona.IdPersona));
            var auditorias = await db.Auditorias.CountAsync(a => a.IdUsuario == usuario.IdUsuario);
            await servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request);
            Assert.Equal(auditorias, await db.Auditorias.CountAsync(a => a.IdUsuario == usuario.IdUsuario));

            foreach (var nivel in new[] { "ciudad", "departamento", "pais" })
            {
                if (nivel == "ciudad") ciudad.Estado = "INACTIVO";
                if (nivel == "departamento") depto.Estado = "INACTIVO";
                if (nivel == "pais") pais.Estado = "INACTIVO";
                await db.SaveChangesAsync();
                Assert.False(await servicio.DomicilioDaneCompletoAsync(persona.IdPersona));
                await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarDomicilioDaneAsync(usuario.IdUsuario, persona.IdPersona, request));
                ciudad.Estado = depto.Estado = pais.Estado = "ACTIVO";
                await db.SaveChangesAsync();
            }
            await servicio.ActualizarAsync(usuario.IdUsuario, persona.IdPersona,
                new ActualizarMiPerfilRequest(null, null, "Calle 2", null, null));
            Assert.Equal(ciudad.IdCiudad, persona.IdCiudadDane);
            await servicio.ActualizarAsync(usuario.IdUsuario, persona.IdPersona,
                new ActualizarMiPerfilRequest(null, null, null, "Ciudad escrita libremente", null));
            Assert.Null(persona.IdCiudadDane);
            Assert.False(await servicio.DomicilioDaneCompletoAsync(persona.IdPersona));
            Assert.True(usuario.EmailVerificado);

            var admin = new UsuarioAdminService(db);
            var detalle = await admin.ObtenerDetalleAsync(usuario.IdUsuario);
            Assert.NotNull(detalle);
            Assert.Equal("VERIFF", detalle.DocumentoFuente);
            Assert.Equal("Documento de identidad", detalle.TipoDocumento);
            Assert.Equal(persona.NumeroDocumentoVerificado, detalle.NumeroDocumento);
            foreach (var texto in new[] { persona.NumeroDocumentoVerificado!, persona.NombreVerificadoCompleto!,
                         persona.NombreVerificadoCompleto + " " + persona.ApellidoVerificadoCompleto })
            {
                var json = JsonSerializer.SerializeToElement(await admin.ListarUsuariosAsync(texto, null, null, false, 1, 20));
                var item = Assert.Single(json.GetProperty("items").EnumerateArray());
                Assert.Equal(usuario.IdUsuario, item.GetProperty("IdUsuario").GetInt64());
                Assert.Equal("VERIFF", item.GetProperty("DocumentoFuente").GetString());
            }
            usuario.EstadoKycActual = "PENDIENTE";
            await db.SaveChangesAsync();
            var sinConfiar = JsonSerializer.SerializeToElement(await admin.ListarUsuariosAsync(persona.NumeroDocumentoVerificado, null, null, false, 1, 20));
            Assert.Empty(sinConfiar.GetProperty("items").EnumerateArray());
            Assert.Null(persona.NumeroDocumento);
            Assert.Null(persona.TipoDocumento);
            Assert.False(await db.Wallets.AnyAsync(w => w.IdPersona == persona.IdPersona));
        }
        finally
        {
            if (usuario?.IdUsuario > 0)
            {
                await db.Auditorias.Where(a => a.IdUsuario == usuario.IdUsuario).ExecuteDeleteAsync();
                await db.Usuarios.Where(u => u.IdUsuario == usuario.IdUsuario).ExecuteDeleteAsync();
            }
            if (persona?.IdPersona > 0) await db.Personas.Where(p => p.IdPersona == persona.IdPersona).ExecuteDeleteAsync();
            var idsCiudades = ciudades.Select(c => c.IdCiudad).ToList();
            var idsDepartamentos = departamentos.Select(d => d.IdDepartamento).ToList();
            var idsPaises = paises.Select(p => p.IdPais).ToList();
            await db.CatalogoCiudades.Where(c => idsCiudades.Contains(c.IdCiudad)).ExecuteDeleteAsync();
            await db.CatalogoDepartamentos.Where(d => idsDepartamentos.Contains(d.IdDepartamento)).ExecuteDeleteAsync();
            await db.CatalogoPaises.Where(p => idsPaises.Contains(p.IdPais)).ExecuteDeleteAsync();
        }
    }
}
