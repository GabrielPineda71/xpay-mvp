# Registro guiado y Wallet del piloto

Esta entrega permite que el titular de una cuenta pública solicite habilitar
su Wallet después de verificar correo e identidad, completar domicilio DANE,
elegir registrar u omitir su llave propia y configurar un PIN de seis dígitos.
La acción final exige aceptar un anexo independiente y pulsar Activar mi Wallet.
No se habilita nada al consultar, regresar de Veriff ni guardar el PIN.

## Controles

- Actor exclusivamente del JWT; cuenta personal activa, rol único USUARIO_FINAL,
  contraseña vigente, misma Persona y una única sesión actual VERIFF/approved.
- Autorización biométrica C1.2 vigente; no se amplía ni sustituye A/B/C existentes.
- Documento legal consolidado y único. Se conserva el número raw de Veriff.
  Para CC/CO se reconocen puntos de miles ASCII con grupos exactos; nunca se
  eliminan separadores arbitrarios ni se infiere el país por la residencia.
- Domicilio elegido del catálogo activo de Colombia; la ciudad determina el
  departamento. El PATCH libre histórico invalida el ID si cambia la localidad.
- PIN BCrypt activo, no bloqueado y de longitud6; contraseña de ingreso para
  crearlo y el mismo control de intentos para cambiarlo/verificarlo.
- Transacción Serializable, locks compartidos con KYC y alta administrativa,
  capacidad conjunta de50 y comprobaciones mantenidas hasta confirmar.
- Misma Persona/Usuario; una Wallet PERSONA activa, saldo0 y evidencia auditada.
  No crea movimientos, ledger, cupos, préstamos ni vínculos laborales.
- Revoca las sesiones anteriores; la respuesta envía una sesión completa para
  el mismo titular, con requierePerfil=false explícito. Si se pierde, reingresar.
- WhatsApp aplazado: nunca se marca verificado. Llave propia opcional para
  activar/enviar/pagar; pendiente de validación bancaria hasta el retiro.
- Los controladores de crédito/nómina rechazan las cuentas de autorregistro
  aunque tengan Wallet. La habilitación separada de esos productos se entrega
  por otro flujo; el piloto administrativo previo mantiene sus controles.

## Identificación existente

POST `/api/usuarios/preparacion-wallet/identidad/actualizar` consulta Veriff solo
por clic explícito y fuera de SQL. Revalida sesión, intento, decisión, titular,
campos raw y país documental; solo completa los campos legales vacíos, con
auditoría y comprobación global de unicidad. No inicia una nueva captura, no
modifica la decisión KYC ni activa Wallet. Una discrepancia requiere revisión.
GET de preparación no hace esa consulta ni escribe datos.

## Instalación y cierre

1. Aprobar el anexo concreto incluido en la rama. Sus bytes y huella están
   preparados para revisión; no publicarlos ni habilitarlos antes de la
   aprobación de XPAY. La configuración debe usar esa misma huella y ruta.
   Mantener A/B/C1.2 intactos.
2. Ejecutar y verificar055 y056 antes del backend, incluso con el flujo cerrado:
   el mapeo Persona y el filtro de cuentas limitadas necesitan las columnas/tablas.
   Son migraciones aditivas sin backfill.054 pertenece a otra entrega separada.
3. Publicar solo artefactos del commit revisado y validado en CI; la compilación
   local ficticia sirve para inspección, nunca para desplegar.
4. Configurar `XPAY_CONDICIONES_WALLET_VERSION/FECHA/URL/SHA256` y habilitar
   `XPAY_ALTA_WALLET_GUIADA_HABILITADA=true` únicamente con el documento aprobado.
   Conservar piloto habilitado y máximo50, y las configuraciones KYC/correo.
5. Cada titular completa sus pasos y toma la acción final. No activar usuarios,
   aceptar consentimientos ni crear PIN reales mediante herramientas.

El interruptor está cerrado por defecto: faltantes/incoherencias de documento
devuelven404 y conservan el Perfil anterior. Cerrarlo detiene nuevas solicitudes;
no borra Wallets ni libera plazas. Para pausar operaciones se conserva el control
financiero existente. No retirar tablas/columnas para deshacer una publicación.
Las huellas legales y la evidencia de activación forman parte de la prueba
durable: una futura versión debe preservar explícitamente las aceptaciones
anteriores válidas; no cambiar la constante sin planificar esa compatibilidad.

## Verificación

Pruebas SQL con cuentas sintéticas: progresión y requisitos, formatos y unicidad,
rollbacks, idempotencia, carreras por última plaza manual/pública, revocación
administrativa y separación del crédito. UI con datos ficticios: una tarea por
pantalla, DANE, llave omitida, PIN6 y final sin aceptación preseleccionada.
No se hicieron operaciones financieras ni aceptaciones reales en esas pruebas.
