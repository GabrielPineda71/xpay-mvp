using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class RegistroUsuarioFinalService
{
    private readonly XpayDbContext _db;
    public RegistroUsuarioFinalService(XpayDbContext db) => _db = db;

    public async Task<long> RegistrarAsync(RegistroUsuarioFinalRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
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
            await transaction.CommitAsync();
            return usuario.IdUsuario;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
