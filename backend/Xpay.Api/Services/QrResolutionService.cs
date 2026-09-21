using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-451 — resolución READ-ONLY compartida de un CodigoQr a su
// comercio/tienda ACTIVOS. Extraída, sin ningún cambio de comportamiento,
// desde las 3 consultas que PagoQrService.PagarQrAsync ya hacía inline
// (mismo orden, mismas condiciones ACTIVO, mismos mensajes de excepción
// exactos) para que el preview de pago (QrController.Resolver, solo
// lectura) y el pago real (PagoQrService) nunca puedan divergir en qué
// CodigoQr es válido y a qué comercio/tienda resuelve — la autoridad sobre
// el receptor sigue siendo, en ambos casos, exclusivamente CodigoQr →
// QrComercio → Comercio/Tienda, nunca un IdComercio recibido del cliente.
//
// El núcleo es un método ESTÁTICO que recibe el XpayDbContext ya inyectado
// de quien lo llama. Esto permite que PagoQrService.PagarQrAsync lo use
// directamente (QrResolutionService.ResolverQrActivoAsync(_db, codigoQr))
// SIN agregar QrResolutionService al constructor de PagoQrService — evita
// tocar los dos sitios que instancian PagoQrService directamente con su
// constructor actual bajo el freeze de XPAY-419
// (CarteraAsignarCupoConcurrencyTests.cs,
// CarteraOrdinariaEstadosActivosTests.cs). El wrapper de instancia existe
// únicamente para que QrController pueda inyectarlo por DI de forma normal.
public class QrResolutionService
{
    private readonly XpayDbContext _db;

    public QrResolutionService(XpayDbContext db)
    {
        _db = db;
    }

    public Task<QrResolutionResult> ResolverQrActivoAsync(string codigoQr) =>
        ResolverQrActivoAsync(_db, codigoQr);

    public static async Task<QrResolutionResult> ResolverQrActivoAsync(XpayDbContext db, string codigoQr)
    {
        var qr = await db.QrComercios.FirstOrDefaultAsync(q => q.CodigoQr == codigoQr && q.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("El QR no existe o no está activo.");

        var comercio = await db.Comercios.FirstOrDefaultAsync(c => c.IdComercio == qr.IdComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("El comercio no existe o no está activo.");

        var tienda = await db.ComercioTiendas.FirstOrDefaultAsync(t => t.IdTienda == qr.IdTienda && t.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("La tienda no existe o no está activa.");

        return new QrResolutionResult(qr, comercio, tienda);
    }
}

public record QrResolutionResult(QrComercio Qr, Comercio Comercio, ComercioTienda Tienda);
