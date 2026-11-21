# Registro público, correo e identidad

El usuario inicia el registro desde el botón **Crear cuenta** de xpaycol.com.
El formulario de app.xpaycol.com conserva las aceptaciones de términos y
tratamiento de datos y envía un enlace de confirmación al correo del titular.
La cuenta permanece pendiente hasta confirmar ese enlace; después accede a Perfil.

En Perfil puede leer el documento de autorizaciones aprobado v1.2 y marcar por
separado la autorización opcional de datos sensibles para verificar su identidad.
La casilla empieza desmarcada. La aceptación se registra para el usuario de la
sesión, con versión, hash del documento y del texto, fecha y canal, en la auditoría
existente. Aceptar términos o confirmar correo no sustituye esta autorización.

Con el correo confirmado y la autorización vigente puede iniciar Veriff. Si
interrumpe una sesión pendiente, continúa la misma sesión del mismo titular.
Una decisión posterior no genera otro intento automáticamente. El estado se
actualiza por el flujo de Veriff existente; la autorización por sí sola no aprueba
la identidad.

La verificación de WhatsApp queda pendiente y no condiciona estos pasos. Este
cambio no marca ningún celular como verificado ni conecta un proveedor de mensajes.

El autorregistro conserva la cuenta limitada también después de una decisión
aprobada de Veriff. No crea Wallet, cupos, vínculos laborales, movimientos ni
asientos contables. La incorporación al piloto financiero y la revisión de nómina
son etapas distintas, fuera de esta entrega. Las cuentas del piloto existente
mantienen sus controles actuales.

## Configuración

El nuevo paso está cerrado por defecto. Se abre solo con configuración exacta:

```text
XPAY_ALTA_PUBLICA_KYC_HABILITADA=true
XPAY_AUTORIZACION_KYC_VERSION=1.2
XPAY_AUTORIZACION_KYC_FECHA=2026-11-18
XPAY_AUTORIZACION_KYC_URL=https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf
XPAY_AUTORIZACION_KYC_SHA256=22a41de9c1c736925694c2ac64d92c591f935842e7193fbf9ce4b1ae01d20c6f
```

Las sesiones del autorregistro regresan a `/mi-wallet/perfil` en el origen HTTPS validado
de `Identidad:UrlFrontend`. Conservar el `VERIFF_CALLBACK_URL` global del piloto:
su recorrido existente no cambia. La URL de retorno del navegador es distinta
del webhook de decisiones de Veriff. Conservar el webhook existente
`/api/kyc/veriff/webhook` y sus verificaciones de autenticidad. No trasladar
credenciales entre ambientes.

Conservar `XPAY_REGISTRO_PUBLICO_HABILITADO=true`, modalidad `CUENTA_LIMITADA`,
las referencias de correo ACS, los documentos y el límite existente de 50 plazas
del piloto. El nuevo flag no depende de `XPAY_ALTA_PUBLICA_NOMINA_HABILITADA` y no
abre la aprobación de empleados. No se necesita una migración nueva: la evidencia
de autorización utiliza `auditoria` y el estado de identidad usa el esquema vigente.

## Comprobación de la entrega

Probar en un entorno aislado el bloqueo sin correo confirmado, sin consentimiento,
con documento/configuración inválidos y con el flag apagado; la continuidad de una
sesión pendiente; y que una respuesta aprobada no levante la limitación ni cree
Wallet o ledger. Comprobar que el piloto existente conserva su flujo.

Publicar API e interfaz con el nuevo paso cerrado, verificar los archivos,
documentos y salud, y activar únicamente la configuración de esta fase. La prueba
personal requiere que cada titular registre su cuenta, confirme su correo, otorgue
su autorización y complete Veriff por sí mismo. La entrega no acepta documentos ni
realiza biometría en nombre de clientes.
