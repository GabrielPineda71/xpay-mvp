using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class RegistroUsuarioFinalService
{
    private readonly XpayDbContext _db;
    private readonly IAmbienteXpay _ambiente;
    public RegistroUsuarioFinalService(XpayDbContext db, IAmbienteXpay ambiente)
    {
        _db       = db;
        _ambiente = ambiente;
    }

    // XPAY-717 - mensaje genérico: no revela reserva ni ambiente.
    internal const string MensajeUsuarioNoDisponible = "El nombre de usuario no está disponible.";

    public async Task<long> RegistrarAsync(RegistroUsuarioFinalRequest request)
    {
        // XPAY-717 - nombres "qa.*" reservados en ambiente productivo. Antes de
        // cualquier transacción o lectura: sin efectos en BD.
        if (!NombreUsuarioReservado.PermiteCrear(request.Usuario, _ambiente))
            throw new InvalidOperationException(MensajeUsuarioNoDisponible);

        // XPAY-702 - si ya hay una transaccion abierta (alta QA atomica), se une
        // a ella y no hace commit/rollback propios; si no, comportamiento original.
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        try
        {
            var tipoDocumento = request.TipoDocumento.Trim().ToUpper();
            var numeroDocumento = request.NumeroDocumento.Trim();
            var nombreUsuario = request.Usuario.Trim().ToLower();

            if (await _db.Personas.AnyAsync(p => p.IdUnidadNegocio == request.IdUnidadNegocio && p.TipoDocumento == tipoDocumento && p.NumeroDocumento == numeroDocumento))
                throw new InvalidOperationException("Ya existe una persona registrada con este documento.");
            if (await _db.Usuarios.AnyAsync(u => u.NombreUsuario == nombreUsuario))
                throw new InvalidOperationException("El nombre de usuario ya existe.");

            var persona = new Persona
            {
                IdUnidadNegocio = request.IdUnidadNegocio,
                TipoDocumento = tipoDocumento,
                NumeroDocumento = numeroDocumento,
                PrimerNombre = request.PrimerNombre.Trim(),
                SegundoNombre = request.SegundoNombre?.Trim(),
                PrimerApellido = request.PrimerApellido.Trim(),
                SegundoApellido = request.SegundoApellido?.Trim(),
                FechaNacimiento = request.FechaNacimiento,
                Celular = request.Celular.Trim(),
                Email = request.Email?.Trim().ToLower(),
                Pais = "Colombia",
                Estado = "ACTIVA",
                FechaCreacion = DateTime.UtcNow
            };
            _db.Personas.Add(persona);
            await _db.SaveChangesAsync();

            var usuario = new Usuario
            {
                IdPersona = persona.IdPersona,
                NombreUsuario = nombreUsuario,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Estado = "ACTIVO",
                FechaCreacion = DateTime.UtcNow
            };
            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync();

            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("No existe el rol USUARIO_FINAL.");

            _db.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario = usuario.IdUsuario,
                IdRol = rol.IdRol,
                Estado = "ACTIVO",
                FechaAsignacion = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            var wallet = new Wallet
            {
                IdUnidadNegocio = request.IdUnidadNegocio,
                TipoWallet = "PERSONA",
                IdPersona = persona.IdPersona,
                NombreWallet = $"Wallet {persona.PrimerNombre} {persona.PrimerApellido}",
                Estado = "ACTIVA",
                FechaCreacion = DateTime.UtcNow
            };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync();

            _db.WalletSaldos.Add(new WalletSaldo
            {
                IdWallet = wallet.IdWallet,
                FechaActualizacion = DateTime.UtcNow
            });

            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario = usuario.IdUsuario,
                IdPersona = persona.IdPersona,
                Modulo = "USUARIOS",
                Accion = "REGISTRO_USUARIO_FINAL",
                Entidad = "usuarios",
                IdEntidad = usuario.IdUsuario.ToString(),
                Resultado = "EXITOSO",
                Observacion = "Registro inicial de usuario final con wallet.",
                FechaEvento = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return usuario.IdUsuario;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync();
            throw;
        }
    }
}
