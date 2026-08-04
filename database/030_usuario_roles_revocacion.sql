/* ================================================================
   Migración 030: Revocación de usuario_roles (Fase USUARIOS-ADMIN-4)

   Aditiva e idempotente — no borra datos, no altera columnas
   existentes, no toca ninguna otra tabla. Agrega a usuario_roles las
   3 columnas necesarias para revocar una asignación de rol sin
   borrar la fila (se conserva el historial: cuándo se asignó y,
   ahora también, cuándo y por quién se revocó):

     - fecha_revocacion DATETIME2 NULL
     - revocado_por      BIGINT   NULL  (FK -> usuarios.id_usuario)
     - observacion       VARCHAR(500) NULL

   No crea ni modifica ninguna otra tabla. No toca la migración 028
   (Fase 70.4, ajena a este módulo).
   ================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '=== INICIO MIGRACIÓN 030: Revocación de usuario_roles ===';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('usuario_roles') AND name = 'fecha_revocacion'
)
BEGIN
    ALTER TABLE usuario_roles ADD fecha_revocacion DATETIME2 NULL;
    PRINT '  OK: columna fecha_revocacion agregada a usuario_roles.';
END
ELSE PRINT '  INFO: usuario_roles.fecha_revocacion ya existe — omitida.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('usuario_roles') AND name = 'revocado_por'
)
BEGIN
    ALTER TABLE usuario_roles ADD revocado_por BIGINT NULL;
    PRINT '  OK: columna revocado_por agregada a usuario_roles.';
END
ELSE PRINT '  INFO: usuario_roles.revocado_por ya existe — omitida.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('usuario_roles') AND name = 'observacion'
)
BEGIN
    ALTER TABLE usuario_roles ADD observacion VARCHAR(500) NULL;
    PRINT '  OK: columna observacion agregada a usuario_roles.';
END
ELSE PRINT '  INFO: usuario_roles.observacion ya existe — omitida.';
GO

-- FK revocado_por -> usuarios.id_usuario, solo si no existe todavia.
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_usuario_roles_revocado_por'
)
BEGIN
    ALTER TABLE usuario_roles
        ADD CONSTRAINT FK_usuario_roles_revocado_por
        FOREIGN KEY (revocado_por) REFERENCES usuarios(id_usuario);
    PRINT '  OK: FK_usuario_roles_revocado_por agregada.';
END
ELSE PRINT '  INFO: FK_usuario_roles_revocado_por ya existe — omitida.';
GO

PRINT '=== FIN MIGRACIÓN 030 (idempotente, aditiva) ===';
GO
