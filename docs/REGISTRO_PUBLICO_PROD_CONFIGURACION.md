# Registro público de cuentas limitadas

El alta crea acceso a Perfil. Confirmar el correo activa el inicio de sesión,
sin crear Wallet, saldo ni permiso financiero. El piloto existente conserva
sus controles. Las cuentas con evidencia `ACEPTAR_TERMINOS_REGISTRO` tienen
una restricción del servidor que solo admite las acciones explícitas de
perfil, contraseña, contactos y lectura de estado KYC.

## Habilitación de Production

El interruptor queda cerrado hasta verificar la publicación y configuración.
El propietario de XPAY aprobó el18Nov2026 la actualización v1.2 para cuentas
limitadas y autorizó ampliar el piloto hasta50 usuarios. Los PDF aprobados y
sus huellas están en `frontend/xpay-admin/public/legal/v1.2/`.
La versión1.1 describe una pausa financiera global y no debe reutilizarse
para abrir este registro junto al piloto.

Requisitos acumulativos:

- Ambiente exacto `Production`.
- `XPAY_REGISTRO_PUBLICO_HABILITADO=true`.
- `XPAY_REGISTRO_PUBLICO_MODALIDAD=CUENTA_LIMITADA`.
- `Email__Modo=Acs`, sin ninguna sección SMTP QA.
- ACS: `XPAY_CONTACTOS_EMAIL_ACS_ENDPOINT` y
  `XPAY_CONTACTOS_EMAIL_REMITENTE`, con identidad administrada autorizada.
- `Identidad__UrlFrontend=https://app.xpaycol.com`.
- `Identidad__RutaVerificacionEmail=/verificar-email`.
- Metadatos completos de Términos y Política aprobados y publicados en el
  mismo origen de la app. Para cada prefijo `XPAY_REGISTRO_PUBLICO_TERMINOS`
  y `XPAY_REGISTRO_PUBLICO_POLITICA_DATOS`: `_VERSION`, `_FECHA` en formato
  `yyyy-MM-dd`, `_URL` HTTPS sin parámetros, `_SHA256` del PDF exacto.
- `XPAY_PILOTO_MAX_USUARIOS=50`: techo histórico de altas administrativas
  auditadas, con autorización individual del superusuario. No son50 altas
  nuevas ni cambia el acceso limitado de las cuentas de autorregistro.

Sin estos requisitos, las cuatro rutas de registro/configuración,
verificación y reenvío responden 404 en producción. El servicio también
comprueba esa frontera antes de cualquier escritura; nunca usa el alta
legacy ni crea una Wallet por omitir los consentimientos.

`registro-inicial` y `registro-final` siguen excluidos de producción. Los
restablecimientos de contraseña conservan su frontera anterior; seleccionar
ACS no abre rutas adicionales de recuperación.

## Evidencias y acceso

Las dos casillas se aceptan separadamente y se registran en la transacción
del alta, con versión, huella del documento, IP y fecha. No habilitan captura
biométrica. El correo se envía después del commit, con token de un solo uso
en el fragmento del enlace, sin cuerpo ni token en los registros.

El usuario pendiente no puede iniciar sesión. Tras confirmar el correo,
`requierePerfil` orienta la interfaz; la restricción real es el filtro global,
que consulta el marcador persistido en cada petición autenticada. Devuelve
403 `CUENTA_LIMITADA` para acciones no permitidas y 503 si no puede comprobar
la restricción en SQL. Cambiar de rol o aprobar KYC no elimina ese marcador.

Veriff sigue cerrado para estas cuentas hasta implementar y comprobar la
autorización biométrica separada. No se incorpora en esta entrega la
activación financiera de cuentas nuevas ni la PR36 completa.

## Comprobación antes del lanzamiento

1. Probar alta y consentimiento, rechazo de datos/versiones inválidos y
   ausencia de Wallet, saldos, movimientos y asientos.
2. Comprobar entrega del correo, confirmación única y posterior entrada a Perfil.
3. Probar la restricción usando un JWT real y llamadas directas a operaciones,
   incluso con el interruptor financiero del piloto activo.
4. Comprobar que un usuario del piloto conserva sus permisos y que las rutas
   legacy permanecen cerradas.
5. Verificar PDFs servidos, fechas/versiones/huellas de configuración y
   referencias de Key Vault, sin imprimir secretos.
6. Conectar los botones de la web privada al registro de producción cuando
   ese recorrido esté habilitado y comprobado. Mantener el sitio privado
   hasta la decisión posterior de lanzamiento público.

Reversión: cerrar primero `XPAY_REGISTRO_PUBLICO_HABILITADO`, conservar las
evidencias y cuentas pendientes y mantener la restricción por cuenta. No
revertir el servidor a una versión que carezca de esa restricción mientras
existan cuentas de autorregistro productivas.
