# Alta pública y aprobación manual de nómina

El registro confirma el correo y activa el acceso a Perfil. La incorporación
al piloto financiero ocurre después, sobre la misma Persona y el mismo Usuario,
cuando un administrador de XPAY revisa y aprueba la pertenencia laboral.

## Recorrido del cliente y de la empresa

1. El cliente se registra y confirma el enlace recibido por correo.
2. En Perfil autoriza por separado la verificación biométrica, si desea continuar,
   y completa Veriff. La casilla C del documento aprobado v1.2 empieza desmarcada.
   Puede conservar su cuenta limitada sin autorizarla.
3. Confirma su celular con el código real de WhatsApp. No se simula una
   verificación cuando el proveedor falta o está temporalmente caído.
4. La empresa carga sus empleados mediante el CSV existente. La detección también
   funciona si primero se cargó el archivo y después se registró/verificó el cliente.
5. En **Coincidencias de nómina**, ADMIN_XPAY o SUPERUSUARIO ve las coincidencias
   por tipo y número de documento de identidad verificado. Revisa empresa,
   persona, verificaciones, cupo preliminar y cupos efectivos por corte.
6. Puede revisar una persona o seleccionar hasta 50 coincidencias de forma
   explícita. Confirma que comprobó la pertenencia laboral y registra una
   referencia interna al soporte revisado, sin datos personales en ese texto.
7. Cada elemento se revalida y confirma en una transacción independiente. La
   respuesta distingue aprobaciones y rechazos. Las aprobaciones anteriores
   siguen siendo idempotentes si se vuelve a consultar o confirmar el mismo caso.
8. La cuenta pública aprobada recibe una Wallet propia con saldo cero y consume
   una plaza del piloto. El cliente vuelve a iniciar sesión, configura su PIN de
   seis dígitos y conserva los controles de cada producto. La llave Bre-B propia
   verificada es obligatoria para retirar al banco.

**Una coincidencia documental acredita identidad, no prueba una relación laboral
vigente.** Inicialmente esa comprobación corresponde al administrador con la base
y el soporte de la empresa. Cargar empleados, autorizar biometría o aprobar KYC
no crea un vínculo ni habilita un cupo utilizable automáticamente.

## Controles del servidor

- Actor administrativo activo y con rol vigente, comprobado en la base de datos.
- Usuario final activo, Persona activa y contraseña vigente.
- Correo y WhatsApp verificados; para esta vía son obligatorios aunque el
  interruptor histórico de contactos esté apagado o no haya un canal disponible.
- Una única sesión actual VERIFF/APROBADA del mismo titular, decisión `approved`,
  identidad consolidada y documento legal coincidente. Casilla C vigente para
  las cuentas públicas.
- Rechazo de identidades o empleados ambiguos, incluso por separadores o por
  presencia en varios convenios; no se vincula por nombre, correo o teléfono.
- Convenio y empleado activos, condiciones vigentes y parámetros/cortes válidos.
  Pagos por corte coherentes con el salario; no se acepta un pago inflado que
  genere más cupo que la base salarial revisada.
- Una asociación activa por usuario y por empleado. Huella de las condiciones
  revisadas; si cambian datos relevantes, la aprobación anterior no autoriza
  nuevos anticipos hasta revisar nuevamente.
- Las altas administrativas históricas y las transiciones públicas comparten el
  límite del piloto y el mismo bloqueo de capacidad. Confirmar de nuevo no
  consume otra plaza. No se borran las aceptaciones originales del registro.
- Antes del desembolso se relee la asociación y se conservan los bloqueos de
  identidad, contactos y condiciones hasta confirmar la operación. Revocación,
  Wallet diferente o cambios desde la revisión bloquean el uso de datos antiguos.
- La aprobación no crea préstamos, movimientos monetarios ni ledger. El
  interruptor financiero, KYC, contactos, PIN, límites y saldo se comprueban en
  las operaciones posteriores. Los vínculos históricos sin metadata conservan
  su comportamiento; este cambio no los aprueba ni los modifica en lote.

## Configuración y publicación

La implementación está cerrada por defecto. Aplicar primero
`database/054_libranza_vinculacion_revision.sql`; agrega cinco columnas nullable
de revisión a `libranza_empleado_usuario`, sin backfill ni cambios de dinero.
Validar estructura, CHECK y repetición antes de publicar el backend que las lee.

Después de publicar el código revisado y comprobar la configuración del ambiente:

```text
XPAY_ALTA_PUBLICA_NOMINA_HABILITADA=true
XPAY_PILOTO_ALTA_HABILITADA=true
XPAY_PILOTO_MAX_USUARIOS=50
XPAY_AUTORIZACION_KYC_VERSION=1.2
XPAY_AUTORIZACION_KYC_FECHA=2026-11-18
XPAY_AUTORIZACION_KYC_SHA256=22a41de9c1c736925694c2ac64d92c591f935842e7193fbf9ce4b1ae01d20c6f
```

`XPAY_AUTORIZACION_KYC_URL` debe apuntar al PDF aprobado
`/legal/v1.2/autorizaciones.pdf` en el mismo origen HTTPS que
`Identidad:UrlFrontend`. En producción se exige `https://app.xpaycol.com/`.
Conservar el destino QA al probar en QA; no copiar claves entre ambientes.
`VERIFF_CALLBACK_URL` debe volver a Perfil del frontend correspondiente.

El proveedor de WhatsApp debe estar conectado para completar una prueba real.
Configurar un canal o cambiar un flag no verifica teléfonos ni acepta
consentimientos por los clientes. En QA, el correo mantiene su lista de
destinatarios autorizados y los proveedores deben ser los de ese ambiente.

Apagar el flag del nuevo flujo cierra nuevas aprobaciones e inicios públicos de
KYC. No revoca las habilitaciones ya confirmadas ni sustituye al interruptor
global de operaciones financieras. Mantener los controles de cuenta limitada
durante cualquier recuperación; no borrar auditoría para revertir una cuenta.

Esta fase no habilita MiDecisor en producción ni resuelve su autorización externa.

## Prueba funcional antes de incorporar usuarios reales

Usar cuentas y empleados ficticios exclusivamente en el entorno aislado para
probar: orden de carga/registro invertido, consentimiento separado, verificación
pendiente, aprobación individual, selección masiva parcial, documento duplicado,
cambio de salario/cortes, revocación y aprobaciones simultáneas. Verificar que se
conserve la misma cuenta, saldo cero, una plaza y ningún asiento al aprobar.

La prueba real requiere que cada titular registre y confirme personalmente sus
contactos y complete Veriff; el administrador realiza sus decisiones laborales.
No ejecutar aprobaciones reales desde una simulación o reutilizar usuarios de
producción en las pruebas automatizadas.

## Automatización posterior

Esta versión solo registra el método **MANUAL**. No hay un flag que convierta
la coincidencia en aprobación automática. Para esa fase se requiere una fuente
laboral confiable y vigente, gestión de bajas y cambios, ausencia de ambigüedades,
criterios aprobados por convenio y una auditoría equivalente. La futura política
debe reutilizar las mismas verificaciones; no basta con que coincida una cédula.
