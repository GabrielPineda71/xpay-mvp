# Registro guiado y activación de Wallet

Recorrido del titular: correo → identidad con Veriff → domicilio DANE → PIN6 →
Activar mi Wallet. No hay aceptación de piloto, anexo adicional, registro de
llave durante el alta ni avisos de una etapa futura de WhatsApp.

## Aceptaciones y evidencia

- A: Términos y Condiciones1.3. B: Política de Tratamiento de Datos1.3. Se
  solicitan en el registro con casillas separadas vacías y versiones del API.
- C: autorización biométrica opcional y expresa, antes de iniciar Veriff. El
  texto C y su finalidad no cambian entre los documentos1.2 y1.3. Se conserva la
  evidencia histórica1.2 válida sin reetiquetarla, crear una aceptación1.3 ni
  pedir otra captura. Una última evidencia inválida/revocada no rescata otra.
- Quien ya aceptó A/B1.3 no vuelve a marcar casillas al activar la Wallet.
  Las cuentas anteriores renuevan únicamente las categorías A/B pendientes,
  porque1.2 limitaba el acceso y no autorizaba la nueva operativa de Wallet.
- Se revalida la última fila del mismo usuario/Persona/acción/entidad con tipo,
  versión, huella y canal reconocidos. Faltantes o incoherencias cierran el paso.
  No se sustituyen aceptaciones por el clic de guardar PIN ni un retorno de KYC.

## Habilitación y controles

Cuenta personal activa, rol único USUARIO_FINAL y contraseña vigente; correo
confirmado; una única sesión actual VERIFF/approved con identidad real, no QA;
documento legal consolidado y único; dirección y ciudad/departamento DANE activos;
PIN BCrypt6 activo y sin bloqueo; A/B1.3 vigentes; solicitud expresa de activación.

La transacción Serializable y los bloqueos compartidos con KYC/alta administrativa
mantienen esos controles frente a cambios concurrentes. La misma Persona/Usuario
recibe una Wallet PERSONA con saldo0 y una prueba durable idempotente. No genera
movimientos, ledger, créditos ni vinculaciones laborales. La capacidad interna
conjunta se conserva en50, sin liberar plazas automáticamente al bloquear cuentas.

La sesión nueva revoca los JWT anteriores y debe corresponder al mismo titular.
Si la respuesta se pierde, reingresar; nunca repetir automáticamente una activación.
Cambiar correo a uno sin verificar vuelve a limitar el acceso. Los productos de
crédito/nómina requieren habilitación separada; activar Wallet no los habilita.

La llave Bre-B propia se registra en Perfil o desde el aviso del retiro. Solo
retirar al banco exige que esté activa, validada y con una resolución Passport
real/vigente a nombre del titular. Enviar entre Wallets y pagar QR no la requieren.
El proveedor no se consulta automáticamente al montar formularios.

## Corrección de identificación

El POST manual identidad/actualizar confirma la decisión actual mediante GET
fuera de SQL; después revalida titular, sesión/attempt/time y raw idénticos. Solo
completa el documento legal vacío, con auditoría y unicidad global. Para CC/CO se
reconocen puntos de miles en grupos ASCII exactos; no se limpian separadores
arbitrarios ni se infiere país por residencia. Raw y decisión KYC se conservan.

## Instalación y reversión

Aplicar055 y056 antes del backend incluso con el flujo cerrado. Son aditivas,
sin backfill;056 contiene11columnas de prueba de activación/A/B, ninguna llave.
054 pertenece a otra entrega. Conservar los PDF1.2 como historia inalterada.

Publicar únicamente artefactos CI del commit revisado, después del respaldo y
preflight. Activar en un único cambio los metadatos A/B/C1.3 (versión, fecha, URL y
SHA) y XPAY_ALTA_WALLET_GUIADA_HABILITADA=true. Son13claves; no tocar configuración
QA, Passport, ACS, callback global, roles ni los demás controles financieros.

El flag está cerrado por defecto y los metadatos1.3 deben coincidir con los bytes
versionados. Cerrarlo impide nuevas preparaciones, sin borrar Wallets ni plazas.
No retirar columnas/tablas para deshacer una aplicación; para pausar operaciones
se conserva el interruptor financiero existente. Los documentos futuros deben
planificar expresamente la compatibilidad de evidencia anterior válida.

Cada titular confirma sus datos, consentimientos y PIN reales y toma el clic final;
las herramientas de desarrollo prueban cuentas sintéticas y no operan por él.
