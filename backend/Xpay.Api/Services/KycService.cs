using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class KycService
{
    private readonly XpayDbContext _db;

    private static readonly HashSet<string> EstadosValidos = new(StringComparer.Ordinal)
    {
        "NO_INICIADO", "PENDIENTE", "EN_REVISION", "APROBADO", "RECHAZADO", "EXPIRADO", "ERROR"
    };

    // Only QA demo wallet users are eligible for simulation — not XPAY staff accounts
    private static readonly HashSet<string> UsuariosQaPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "qa.usuario1", "qa.usuario2"
    };

    public KycService(XpayDbContext db) => _db = db;

    public async Task<MiEstadoKycResponse> GetMiEstadoAsync(long idUsuario)
    {
        var datos = await _db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.EstadoKycActual, u.FechaKycActualizacion })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        return new MiEstadoKycResponse
        {
            EstadoKyc          = datos.EstadoKycActual,
            FechaActualizacion = datos.FechaKycActualizacion,
            Nota               = "QA/Demo — sin verificación real de identidad en esta fase.",
        };
    }

    public async Task<string> SimularEstadoQaAsync(SimularEstadoKycRequest request)
    {
        var nombreUsuario = (request.Usuario ?? string.Empty).Trim().ToLower();

        if (!UsuariosQaPermitidos.Contains(nombreUsuario))
            throw new InvalidOperationException(
                $"Usuario '{request.Usuario}' no permitido para simulación. " +
                $"Usuarios QA válidos: {string.Join(", ", UsuariosQaPermitidos)}.");

        var estadoKyc = (request.EstadoKyc ?? string.Empty).Trim().ToUpper();
        if (!EstadosValidos.Contains(estadoKyc))
            throw new InvalidOperationException(
                $"EstadoKyc '{request.EstadoKyc}' inválido. " +
                $"Valores permitidos: {string.Join(", ", EstadosValidos)}.");

        var usuario = await _db.Usuarios
            .Where(u => u.NombreUsuario == nombreUsuario)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"Usuario '{request.Usuario}' no encontrado en la base de datos.");

        // Deactivate previous KYC records so only one is es_actual=true
        var anteriores = await _db.KycVerificaciones
            .Where(k => k.IdUsuario == usuario.IdUsuario && k.EsActual)
            .ToListAsync();
        foreach (var anterior in anteriores)
        {
            anterior.EsActual          = false;
            anterior.FechaActualizacion = DateTime.UtcNow;
        }

        _db.KycVerificaciones.Add(new KycVerificacion
        {
            IdUsuario          = usuario.IdUsuario,
            IdPersona          = usuario.IdPersona,
            Proveedor          = "SIMULACION_QA",
            EstadoKyc          = estadoKyc,
            EsActual           = true,
            FechaCreacion      = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow,
        });

        usuario.EstadoKycActual        = estadoKyc;
        usuario.FechaKycActualizacion  = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return $"Estado KYC de '{nombreUsuario}' actualizado a '{estadoKyc}' (SIMULACION_QA).";
    }
}
