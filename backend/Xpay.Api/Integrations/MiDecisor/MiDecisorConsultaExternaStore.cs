using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xpay.Api.Common;
using Xpay.Api.Data;

namespace Xpay.Api.Integrations.MiDecisor;

public interface IMiDecisorConsultaExternaStore
{
    Task<DateTime?> UltimaReferenciaAsync(string tipoDocumento, string numeroDocumento, CancellationToken ct = default);
    Task<bool> RegistrarAsync(MiDecisorConsultaExternaRegistro registro, CancellationToken ct = default);
}

// Administrative import only, no controller/public route. Contains no risk
// result and can never be consumed as a Cartera credit decision.
public sealed class MiDecisorConsultaExternaRegistro
{
    public required long IdPersonaOrigen { get; init; }
    public required long RegistradoPorUsuario { get; init; }
    public required string TipoDocumento { get; init; }
    public required string NumeroDocumento { get; init; }
    public required DateTime FechaReferenciaUtc { get; init; }
    public required bool FechaExacta { get; init; }
    public required string Origen { get; init; }
    public required string EvidenciaSha256 { get; init; }
}

public static class MiDecisorSujeto
{
    public static string Huella(string tipoDocumento, string numeroDocumento)
    {
        var tipo = tipoDocumento.Trim().ToUpperInvariant();
        if (tipo == "PASAPORTE") tipo = "PAS";
        var numero = TipoDocumentoVeriffMapper.NumeroComparable(numeroDocumento);
        if (!TipoDocumentoMiDecisorMapper.TryMapPersonaNatural(tipo, out var codigo) || numero is not { Length: >= 3 and <= 13 } || !numero.All(char.IsAsciiDigit))
            throw new ArgumentException("Identidad documental no válida para MiDecisor.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("XPAY.MIDECISOR.SUBJECT.V1|" + codigo + "|" + numero))).ToLowerInvariant();
    }
}

public sealed class MiDecisorConsultaExternaStore(XpayDbContext db, TimeProvider reloj) : IMiDecisorConsultaExternaStore
{
    public const string ReciboDiagnostico = "RECIBO_DIAGNOSTICO_TITULAR";
    public const string ConfirmacionManual = "CONFIRMACION_MANUAL_TITULAR";

    public async Task<DateTime?> UltimaReferenciaAsync(string tipoDocumento, string numeroDocumento, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = "SELECT MAX(fecha_referencia_utc) FROM dbo.midecisor_consultas_externas WHERE huella_sujeto=@subject;";
            Parameter(command, "@subject", DbType.AnsiStringFixedLength, MiDecisorSujeto.Huella(tipoDocumento, numeroDocumento), 64);
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
        }
        finally { if (opened) await connection.CloseAsync().ConfigureAwait(false); }
    }

    public async Task<bool> RegistrarAsync(MiDecisorConsultaExternaRegistro registro, CancellationToken ct = default)
    {
        Validar(registro, reloj.GetUtcNow().UtcDateTime);
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("La importación requiere una transacción administrativa propia.");
        var subject = MiDecisorSujeto.Huella(registro.TipoDocumento, registro.NumeroDocumento);
        var evidence = registro.EvidenciaSha256.ToLowerInvariant();
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = tx; command.CommandTimeout = 15;
            command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@lock,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @result;";
            Parameter(command, "@lock", DbType.String, "XPAY:MIDECISOR:" + subject, 255);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) < 0)
                throw new InvalidOperationException("No se pudo serializar la importación por titular.");
            command.Parameters.Clear();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.usuarios u
                    JOIN dbo.usuario_roles ur ON ur.id_usuario=u.id_usuario AND ur.estado='ACTIVO'
                    JOIN dbo.roles r ON r.id_rol=ur.id_rol AND r.estado='ACTIVO'
                    WHERE u.id_usuario=@admin AND u.estado='ACTIVO' AND r.codigo IN ('ADMIN_XPAY','SUPERUSUARIO')
                ) THEN 1 ELSE 0 END;
                """;
            Parameter(command, "@admin", DbType.Int64, registro.RegistradoPorUsuario);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)) != 1)
                throw new UnauthorizedAccessException("La importación de consultas externas requiere un administrador activo.");

            command.CommandText = "SELECT tipo_documento,numero_documento,tipo_documento_veriff_raw,numero_documento_verificado FROM dbo.personas WHERE id_persona=@person;";
            Parameter(command, "@person", DbType.Int64, registro.IdPersonaOrigen);
            await using (var person = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await person.ReadAsync(ct).ConfigureAwait(false) || !VinculoDocumental(registro,
                    person.IsDBNull(0) ? null : person.GetString(0), person.IsDBNull(1) ? null : person.GetString(1),
                    person.IsDBNull(2) ? null : person.GetString(2), person.IsDBNull(3) ? null : person.GetString(3)))
                    throw new InvalidOperationException("La evidencia externa no corresponde al documento de la persona de origen.");
            }

            command.CommandText = """
                SELECT huella_sujeto,id_persona_origen,fecha_referencia_utc,fecha_exacta,origen
                FROM dbo.midecisor_consultas_externas WITH (UPDLOCK,HOLDLOCK) WHERE evidencia_sha256=@evidence;
                """;
            Parameter(command, "@evidence", DbType.AnsiStringFixedLength, evidence, 64);
            var existente = false;
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (reader.GetString(0) != subject || reader.GetInt64(1) != registro.IdPersonaOrigen || reader.GetDateTime(2) != registro.FechaReferenciaUtc ||
                        reader.GetBoolean(3) != registro.FechaExacta || reader.GetString(4) != registro.Origen)
                        throw new InvalidOperationException("La evidencia externa ya existe con metadatos diferentes.");
                    existente = true;
                }
            }
            if (existente)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return false;
            }
            command.CommandText = """
                INSERT INTO dbo.midecisor_consultas_externas
                    (huella_sujeto,id_persona_origen,fecha_referencia_utc,fecha_exacta,origen,evidencia_sha256,registrado_por_usuario,registrado_utc,ambito)
                VALUES (@subject,@person,@reference,@exact,@origin,@evidence,@admin,@now,'CONSULTA_SIN_DECISION');
                """;
            Parameter(command, "@subject", DbType.AnsiStringFixedLength, subject, 64);
            Parameter(command, "@reference", DbType.DateTime2, registro.FechaReferenciaUtc);
            Parameter(command, "@exact", DbType.Boolean, registro.FechaExacta);
            Parameter(command, "@origin", DbType.AnsiString, registro.Origen, 40);
            Parameter(command, "@now", DbType.DateTime2, reloj.GetUtcNow().UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally { if (opened) await connection.CloseAsync().ConfigureAwait(false); }
    }

    internal static void Validar(MiDecisorConsultaExternaRegistro r, DateTime ahoraUtc)
    {
        if (r.IdPersonaOrigen <= 0 || r.RegistradoPorUsuario <= 0 || r.FechaReferenciaUtc.Kind != DateTimeKind.Utc || r.FechaReferenciaUtc > ahoraUtc ||
            r.Origen is not (ReciboDiagnostico or ConfirmacionManual) || (r.Origen == ConfirmacionManual && r.FechaExacta) ||
            r.EvidenciaSha256.Length != 64 || !r.EvidenciaSha256.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Los metadatos de la consulta externa no son válidos.");
        _ = MiDecisorSujeto.Huella(r.TipoDocumento, r.NumeroDocumento);
    }

    internal static bool VinculoDocumental(MiDecisorConsultaExternaRegistro r, string? tipo, string? numero, string? tipoVeriff, string? numeroVerificado)
    {
        var esperado = r.TipoDocumento.Trim().ToUpperInvariant();
        if (esperado == "PASAPORTE") esperado = "PAS";
        var legal = tipo?.Trim().ToUpperInvariant();
        if (legal == "PASAPORTE") legal = "PAS";
        var comparable = TipoDocumentoVeriffMapper.NumeroComparable(r.NumeroDocumento);
        var tipoRaw = esperado switch { "CC" => "ID_CARD", "PAS" => "PASSPORT", _ => null };
        return (legal == esperado && TipoDocumentoVeriffMapper.NumeroComparable(numero) == comparable)
            || (tipoRaw is not null && string.Equals(tipoVeriff?.Trim(), tipoRaw, StringComparison.OrdinalIgnoreCase)
                && TipoDocumentoVeriffMapper.NumeroComparable(numeroVerificado) == comparable);
    }

    private static void Parameter(DbCommand command, string name, DbType type, object value, int size = 0)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.DbType = type; parameter.Value = value;
        if (size != 0) parameter.Size = size;
        command.Parameters.Add(parameter);
    }
}
