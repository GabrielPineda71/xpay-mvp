using Microsoft.EntityFrameworkCore;
using Xpay.Api.Models;

namespace Xpay.Api.Data;

public class XpayDbContext : DbContext
{
    public XpayDbContext(DbContextOptions<XpayDbContext> options) : base(options) { }

    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletSaldo> WalletSaldos => Set<WalletSaldo>();
    public DbSet<WalletMovimiento> WalletMovimientos => Set<WalletMovimiento>();
    public DbSet<LedgerCuenta> LedgerCuentas => Set<LedgerCuenta>();
    public DbSet<LedgerTransaccion> LedgerTransacciones => Set<LedgerTransaccion>();
    public DbSet<LedgerMovimiento> LedgerMovimientos => Set<LedgerMovimiento>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<ComercioTienda> ComercioTiendas => Set<ComercioTienda>();
    public DbSet<QrComercio> QrComercios => Set<QrComercio>();
    public DbSet<VentaQr> VentasQr => Set<VentaQr>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Persona>(e => { e.ToTable("personas"); e.HasKey(x => x.IdPersona); MapPersona(e); });
        modelBuilder.Entity<Usuario>(e => { e.ToTable("usuarios"); e.HasKey(x => x.IdUsuario); MapUsuario(e); });
        modelBuilder.Entity<Rol>(e => { e.ToTable("roles"); e.HasKey(x => x.IdRol); MapRol(e); });
        modelBuilder.Entity<UsuarioRol>(e => { e.ToTable("usuario_roles"); e.HasKey(x => new { x.IdUsuario, x.IdRol }); MapUsuarioRol(e); });
        modelBuilder.Entity<Wallet>(e => { e.ToTable("wallets"); e.HasKey(x => x.IdWallet); MapWallet(e); });
        modelBuilder.Entity<WalletSaldo>(e => { e.ToTable("wallet_saldos"); e.HasKey(x => x.IdWallet); MapWalletSaldo(e); });
        modelBuilder.Entity<WalletMovimiento>(e => { e.ToTable("wallet_movimientos"); e.HasKey(x => x.IdMovimientoWallet); MapWalletMovimiento(e); });
        modelBuilder.Entity<LedgerCuenta>(e => { e.ToTable("ledger_cuentas"); e.HasKey(x => x.IdCuenta); MapLedgerCuenta(e); });
        modelBuilder.Entity<LedgerTransaccion>(e => { e.ToTable("ledger_transacciones"); e.HasKey(x => x.IdTransaccionLedger); MapLedgerTransaccion(e); });
        modelBuilder.Entity<LedgerMovimiento>(e => { e.ToTable("ledger_movimientos"); e.HasKey(x => x.IdMovimientoLedger); MapLedgerMovimiento(e); });
        modelBuilder.Entity<Auditoria>(e => { e.ToTable("auditoria"); e.HasKey(x => x.IdAuditoria); MapAuditoria(e); });
        modelBuilder.Entity<Comercio>(e => { e.ToTable("comercios"); e.HasKey(x => x.IdComercio); MapComercio(e); });
        modelBuilder.Entity<ComercioTienda>(e => { e.ToTable("comercio_tiendas"); e.HasKey(x => x.IdTienda); MapComercioTienda(e); });
        modelBuilder.Entity<QrComercio>(e => { e.ToTable("qr_comercios"); e.HasKey(x => x.IdQr); MapQrComercio(e); });
        modelBuilder.Entity<VentaQr>(e => { e.ToTable("ventas_qr"); e.HasKey(x => x.IdVentaQr); MapVentaQr(e); });
    }

    private static void MapPersona(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Persona> e)
    {
        e.Property(x => x.IdPersona).HasColumnName("id_persona"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.TipoDocumento).HasColumnName("tipo_documento"); e.Property(x => x.NumeroDocumento).HasColumnName("numero_documento"); e.Property(x => x.PrimerNombre).HasColumnName("primer_nombre"); e.Property(x => x.SegundoNombre).HasColumnName("segundo_nombre"); e.Property(x => x.PrimerApellido).HasColumnName("primer_apellido"); e.Property(x => x.SegundoApellido).HasColumnName("segundo_apellido"); e.Property(x => x.FechaNacimiento).HasColumnName("fecha_nacimiento"); e.Property(x => x.Celular).HasColumnName("celular"); e.Property(x => x.Email).HasColumnName("email"); e.Property(x => x.Direccion).HasColumnName("direccion"); e.Property(x => x.Ciudad).HasColumnName("ciudad"); e.Property(x => x.Departamento).HasColumnName("departamento"); e.Property(x => x.Pais).HasColumnName("pais"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion");
    }
    private static void MapUsuario(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Usuario> e)
    {
        e.Property(x => x.IdUsuario).HasColumnName("id_usuario"); e.Property(x => x.IdPersona).HasColumnName("id_persona"); e.Property(x => x.NombreUsuario).HasColumnName("usuario"); e.Property(x => x.PasswordHash).HasColumnName("password_hash"); e.Property(x => x.EmailVerificado).HasColumnName("email_verificado"); e.Property(x => x.CelularVerificado).HasColumnName("celular_verificado"); e.Property(x => x.RequiereCambioClave).HasColumnName("requiere_cambio_clave"); e.Property(x => x.IntentosFallidos).HasColumnName("intentos_fallidos"); e.Property(x => x.UltimoIngreso).HasColumnName("ultimo_ingreso"); e.Property(x => x.FechaBloqueo).HasColumnName("fecha_bloqueo"); e.Property(x => x.MotivoBloqueo).HasColumnName("motivo_bloqueo"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion");
    }
    private static void MapRol(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Rol> e)
    { e.Property(x => x.IdRol).HasColumnName("id_rol"); e.Property(x => x.Codigo).HasColumnName("codigo"); e.Property(x => x.Nombre).HasColumnName("nombre"); e.Property(x => x.Descripcion).HasColumnName("descripcion"); e.Property(x => x.TipoRol).HasColumnName("tipo_rol"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); }
    private static void MapUsuarioRol(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<UsuarioRol> e)
    { e.Property(x => x.IdUsuario).HasColumnName("id_usuario"); e.Property(x => x.IdRol).HasColumnName("id_rol"); e.Property(x => x.FechaAsignacion).HasColumnName("fecha_asignacion"); e.Property(x => x.AsignadoPor).HasColumnName("asignado_por"); e.Property(x => x.Estado).HasColumnName("estado"); }
    private static void MapWallet(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Wallet> e)
    { e.Property(x => x.IdWallet).HasColumnName("id_wallet"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.TipoWallet).HasColumnName("tipo_wallet"); e.Property(x => x.IdPersona).HasColumnName("id_persona"); e.Property(x => x.IdComercio).HasColumnName("id_comercio"); e.Property(x => x.NombreWallet).HasColumnName("nombre_wallet"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion"); }
    private static void MapWalletSaldo(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WalletSaldo> e)
    { e.Property(x => x.IdWallet).HasColumnName("id_wallet"); e.Property(x => x.SaldoDisponible).HasColumnName("saldo_disponible"); e.Property(x => x.SaldoRetenido).HasColumnName("saldo_retenido"); e.Property(x => x.SaldoTransito).HasColumnName("saldo_transito"); e.Property(x => x.SaldoContingencia).HasColumnName("saldo_contingencia"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion"); }
    private static void MapWalletMovimiento(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WalletMovimiento> e)
    { e.Property(x => x.IdMovimientoWallet).HasColumnName("id_movimiento_wallet"); e.Property(x => x.IdWallet).HasColumnName("id_wallet"); e.Property(x => x.IdTransaccionLedger).HasColumnName("id_transaccion_ledger"); e.Property(x => x.TipoMovimiento).HasColumnName("tipo_movimiento"); e.Property(x => x.Naturaleza).HasColumnName("naturaleza"); e.Property(x => x.Valor).HasColumnName("valor"); e.Property(x => x.SaldoAntes).HasColumnName("saldo_antes"); e.Property(x => x.SaldoDespues).HasColumnName("saldo_despues"); e.Property(x => x.Descripcion).HasColumnName("descripcion"); e.Property(x => x.ReferenciaTipo).HasColumnName("referencia_tipo"); e.Property(x => x.ReferenciaId).HasColumnName("referencia_id"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.CreadoPor).HasColumnName("creado_por"); e.Property(x => x.FechaMovimiento).HasColumnName("fecha_movimiento"); }
    private static void MapLedgerCuenta(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<LedgerCuenta> e)
    { e.Property(x => x.IdCuenta).HasColumnName("id_cuenta"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.Codigo).HasColumnName("codigo"); e.Property(x => x.Nombre).HasColumnName("nombre"); e.Property(x => x.TipoCuenta).HasColumnName("tipo_cuenta"); e.Property(x => x.SubtipoCuenta).HasColumnName("subtipo_cuenta"); e.Property(x => x.Naturaleza).HasColumnName("naturaleza"); e.Property(x => x.PermiteMovimiento).HasColumnName("permite_movimiento"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion"); }
    private static void MapLedgerTransaccion(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<LedgerTransaccion> e)
    { e.Property(x => x.IdTransaccionLedger).HasColumnName("id_transaccion_ledger"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.TipoTransaccion).HasColumnName("tipo_transaccion"); e.Property(x => x.ReferenciaTipo).HasColumnName("referencia_tipo"); e.Property(x => x.ReferenciaId).HasColumnName("referencia_id"); e.Property(x => x.Descripcion).HasColumnName("descripcion"); e.Property(x => x.ValorTotal).HasColumnName("valor_total"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.CreadoPor).HasColumnName("creado_por"); e.Property(x => x.FechaTransaccion).HasColumnName("fecha_transaccion"); e.Property(x => x.FechaActualizacion).HasColumnName("fecha_actualizacion"); }
    private static void MapLedgerMovimiento(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<LedgerMovimiento> e)
    { e.Property(x => x.IdMovimientoLedger).HasColumnName("id_movimiento_ledger"); e.Property(x => x.IdTransaccionLedger).HasColumnName("id_transaccion_ledger"); e.Property(x => x.IdCuenta).HasColumnName("id_cuenta"); e.Property(x => x.Naturaleza).HasColumnName("naturaleza"); e.Property(x => x.Valor).HasColumnName("valor"); e.Property(x => x.Concepto).HasColumnName("concepto"); e.Property(x => x.ReferenciaTipo).HasColumnName("referencia_tipo"); e.Property(x => x.ReferenciaId).HasColumnName("referencia_id"); e.Property(x => x.Descripcion).HasColumnName("descripcion"); e.Property(x => x.FechaMovimiento).HasColumnName("fecha_movimiento"); }
    private static void MapAuditoria(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Auditoria> e)
    { e.Property(x => x.IdAuditoria).HasColumnName("id_auditoria"); e.Property(x => x.IdUsuario).HasColumnName("id_usuario"); e.Property(x => x.IdPersona).HasColumnName("id_persona"); e.Property(x => x.Modulo).HasColumnName("modulo"); e.Property(x => x.Accion).HasColumnName("accion"); e.Property(x => x.Entidad).HasColumnName("entidad"); e.Property(x => x.IdEntidad).HasColumnName("id_entidad"); e.Property(x => x.ValorAnterior).HasColumnName("valor_anterior"); e.Property(x => x.ValorNuevo).HasColumnName("valor_nuevo"); e.Property(x => x.Ip).HasColumnName("ip"); e.Property(x => x.Dispositivo).HasColumnName("dispositivo"); e.Property(x => x.Resultado).HasColumnName("resultado"); e.Property(x => x.Observacion).HasColumnName("observacion"); e.Property(x => x.FechaEvento).HasColumnName("fecha_evento"); }
    private static void MapComercio(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Comercio> e)
    { e.Property(x => x.IdComercio).HasColumnName("id_comercio"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.NombreComercial).HasColumnName("nombre_comercial"); e.Property(x => x.RazonSocial).HasColumnName("razon_social"); e.Property(x => x.Nit).HasColumnName("nit"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); }
    private static void MapComercioTienda(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ComercioTienda> e)
    { e.Property(x => x.IdTienda).HasColumnName("id_tienda"); e.Property(x => x.IdComercio).HasColumnName("id_comercio"); e.Property(x => x.NombreTienda).HasColumnName("nombre_tienda"); e.Property(x => x.Ciudad).HasColumnName("ciudad"); e.Property(x => x.Direccion).HasColumnName("direccion"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); }
    private static void MapQrComercio(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<QrComercio> e)
    { e.Property(x => x.IdQr).HasColumnName("id_qr"); e.Property(x => x.IdComercio).HasColumnName("id_comercio"); e.Property(x => x.IdTienda).HasColumnName("id_tienda"); e.Property(x => x.CodigoQr).HasColumnName("codigo_qr"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion"); }
    private static void MapVentaQr(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<VentaQr> e)
    { e.Property(x => x.IdVentaQr).HasColumnName("id_venta_qr"); e.Property(x => x.IdUnidadNegocio).HasColumnName("id_unidad_negocio"); e.Property(x => x.IdComercio).HasColumnName("id_comercio"); e.Property(x => x.IdTienda).HasColumnName("id_tienda"); e.Property(x => x.IdQr).HasColumnName("id_qr"); e.Property(x => x.IdWalletUsuario).HasColumnName("id_wallet_usuario"); e.Property(x => x.IdTransaccionLedger).HasColumnName("id_transaccion_ledger"); e.Property(x => x.ValorBruto).HasColumnName("valor_bruto"); e.Property(x => x.ValorComision).HasColumnName("valor_comision"); e.Property(x => x.ValorIvaComision).HasColumnName("valor_iva_comision"); e.Property(x => x.ValorNetoComercio).HasColumnName("valor_neto_comercio"); e.Property(x => x.Estado).HasColumnName("estado"); e.Property(x => x.Referencia).HasColumnName("referencia"); e.Property(x => x.Descripcion).HasColumnName("descripcion"); e.Property(x => x.FechaVenta).HasColumnName("fecha_venta"); }
}
