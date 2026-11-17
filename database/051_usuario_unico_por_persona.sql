-- Una Persona puede tener una cuenta de usuario. Los registros provisionales
-- pueden mantener su documento NULL hasta asociarlo en el alta o con Veriff.
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM dbo.usuarios WITH (TABLOCKX, HOLDLOCK)
        GROUP BY id_persona HAVING COUNT_BIG(*) > 1
    )
        THROW 55051, N'Existen Personas con varias cuentas de usuario. Revisa los registros antes de aplicar esta migración.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.usuarios') AND name = N'IX_usuarios_persona' AND is_unique = 1
    )
    BEGIN
        DROP INDEX IF EXISTS IX_usuarios_persona ON dbo.usuarios;
        CREATE UNIQUE INDEX IX_usuarios_persona ON dbo.usuarios(id_persona);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
