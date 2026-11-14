# Verificación de contactos — configuración en Azure y Meta

Lista única de lo que se configura fuera del código. Nada de esto está creado
ni aprobado todavía. Ningún secreto se escribe en este repositorio ni se pasa
por chat: los secretos van solo a Key Vault (`kv-xpay-prod`).

Mientras un canal no esté completo, la API lo reporta como no disponible y el
requisito de contactos verificados **no se aplica** (aunque se active su
interruptor).

## Costos (confirmar la tarifa vigente el día de crear cada recurso)

| Concepto | Costo | Fuente a confirmar |
|---|---|---|
| Recurso Communication Services y Email Communication Service | Sin cargo fijo publicado | Calculadora de precios de Azure |
| Correo enviado (ACS Email) | Por correo enviado + por MB transferido (referencia histórica: USD 0,00025 por correo y USD 0,00012 por MB) | Calculadora de Azure; la página de precios no muestra cifras |
| Dominio personalizado en ACS | Sin cargo publicado; los registros DNS se crean donde se administra `xpaycol.com` | Proveedor DNS de `xpaycol.com` |
| Cuenta de WhatsApp Business, verificación del negocio, número en Cloud API | Sin cargo publicado por Meta | Meta |
| Línea telefónica para el número remitente | La cobra el operador de la línea | Operador |
| Mensaje de plantilla de autenticación a Colombia | Por mensaje entregado; tarifa del mercado Colombia, categoría Authentication | Tarjeta de tarifas de Meta (USD) |

Volumen del piloto (≈10 usuarios, unos pocos códigos cada uno): costo
marginal, pero cada recurso facturable se crea solo con tu aprobación.

## A. Correo — Azure Communication Services Email

1. **Proveedor de recursos:** registrar `Microsoft.Communication` en la
   suscripción (si no está registrado).
2. **Email Communication Service:** crear en `rg-xpay-prod`. Elegir la
   ubicación de datos.
3. **Dominio:** en ese recurso, *Provision domains → Custom domain* →
   `xpaycol.com`. Azure entrega los registros DNS:
   - TXT de verificación del dominio.
   - SPF: si `xpaycol.com` ya tiene un registro SPF (por ejemplo para el
     buzón `xpay@xpaycol.com`), **se agrega** el `include` de Azure al registro
     existente; nunca dos registros SPF.
   - DKIM: dos CNAME (`selector1-…` y `selector2-…`).
   - Recomendado: DMARC.
   Esperar a que los cinco estados queden «Verified».
4. **Remitente:** en el dominio, *MailFrom addresses* → `no-reply@xpaycol.com`
   con nombre visible «XPAY».
5. **Communication Services:** crear el recurso en `rg-xpay-prod` y en
   *Email → Domains* conectar el dominio `xpaycol.com`.
6. **Permiso para la API (sin secretos):** en el recurso Communication
   Services → *Access control (IAM)* → asignar el rol
   **Communication and Email Service Owner** a la identidad administrada del
   App Service de la API. El permiso puede tardar en propagarse.
7. **App Settings de la API** (no son secretos):
   - `XPAY_CONTACTOS_EMAIL_ACS_ENDPOINT` = `https://<recurso>.<región>.communication.azure.com`
   - `XPAY_CONTACTOS_EMAIL_REMITENTE` = `no-reply@xpaycol.com`

La API valida que el endpoint sea `https://…communication.azure.com` y que el
remitente termine en `@xpaycol.com`; si no, el canal queda no disponible.

## B. Celular — WhatsApp Business Platform (Cloud API)

1. **Meta Business Portfolio** de XPAY con la **verificación del negocio**
   completada (documentos de la empresa).
2. **App de Meta** (tipo Business) con el producto **WhatsApp** agregado.
3. **WhatsApp Business Account (WABA)** dentro del portfolio.
4. **Número remitente:** una línea propia que reciba SMS o llamada para el
   código de registro y que **no** esté activa en la app de WhatsApp.
   Registrar el **nombre visible** (p. ej. «XPAY») y esperar su aprobación.
   Anotar el **Phone number ID** (no es secreto).
5. **Plantilla de autenticación** en WhatsApp Manager:
   - Categoría **Authentication**, botón **Copiar código**.
   - Nombre: `xpay_codigo_verificacion` (minúsculas, dígitos y `_`).
   - Idioma: Español; anotar el código exacto que muestra Meta (`es` u otro).
   - Activar la recomendación de seguridad y la caducidad: **10 minutos**
     (igual que la API).
   - Esperar el estado **Aprobada**. Meta fija el texto del cuerpo.
6. **Usuario del sistema** en el portfolio con acceso a la WABA y a la app,
   y un **token permanente** con los permisos `whatsapp_business_messaging`
   (y `whatsapp_business_management` si administrará plantillas).
7. **Secreto en Key Vault:** guardar el token como secreto en `kv-xpay-prod`
   (p. ej. `xpay-whatsapp-token`). Solo tú lo copias; no se muestra ni se
   descarga desde la API.
8. **App Settings de la API:**
   - `XPAY_CONTACTOS_WHATSAPP_PHONE_NUMBER_ID` = Phone number ID
   - `XPAY_CONTACTOS_WHATSAPP_PLANTILLA` = `xpay_codigo_verificacion`
   - `XPAY_CONTACTOS_WHATSAPP_IDIOMA` = código de idioma de la plantilla aprobada
   - `XPAY_CONTACTOS_WHATSAPP_GRAPH_VERSION` = versión vigente de Graph API (p. ej. `v23.0`)
   - `XPAY_CONTACTOS_WHATSAPP_TOKEN` = `@Microsoft.KeyVault(SecretUri=…/secrets/xpay-whatsapp-token/)`
9. **Método de pago** en el portfolio de Meta para los mensajes de
   autenticación (sin él, Meta no entrega plantillas pagadas).

No se usa el webhook de estado de entrega de Meta: el celular se verifica
cuando el usuario escribe el código, no cuando WhatsApp confirma la entrega.

## C. Base de datos

- Aplicar `database/050_verificacion_contactos.sql` en PROD con el mismo
  procedimiento de las migraciones anteriores (idempotente; aborta ante
  cualquier incompatibilidad).

## D. Activación del requisito (último paso)

1. Con A, B y C listos, desplegar y comprobar que `GET api/contactos/estado`
   muestra `canalEmailDisponible` y `canalWhatsAppDisponible` en `true`.
2. Prueba tuya: verificar tu correo y tu celular desde Perfil.
3. Solo entonces: `XPAY_CONTACTOS_VERIFICADOS_REQUERIDOS=true`.
4. Para desactivarlo: borrar la clave o poner cualquier otro valor. Las
   operaciones existentes, los webhooks y la conciliación nunca dependen de él.
