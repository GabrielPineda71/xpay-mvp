# XPAY — Passport/Bre-B Certification — M3 Submission Summary

XPAY ejecutó las pruebas del módulo M3 (gestión de llaves Bre-B) desde su
backend productivo contra Passport Sandbox. Las evidencias adjuntas están
sanitizadas: no contienen identificadores crudos, credenciales, ni datos
personales — cada evidencia conserva únicamente huellas (fingerprints) no
reversibles y campos no sensibles suficientes para demostrar el contrato
ejercitado. El detalle técnico completo está en
[`M3-MANIFEST.md`](M3-MANIFEST.md).

## Casos ejecutados/disponibles

- **M3-T1 — Create Key**: ejecutado, evidencia disponible.
- **M3-T2 — Resolve Key**: ejecutado, evidencia disponible.
- **M3-T3 — Suspend Key**: ejecutado, evidencia disponible.
- **M3-T4 — Activate Key**: ejecutado, evidencia disponible.
- **M3-T5 — Delete Key**: ejecutado, evidencia disponible.
- **M3-T6 — Missing / Invalid / Duplicate** (pruebas de error de Create Key):
  - **MISSING**: XPAY validó localmente, antes de cualquier comunicación con
    Passport, que una solicitud de Create Key sin `key_value` es rechazada
    por el propio backend de XPAY.
  - **INVALID**: XPAY envió a Passport una solicitud de Create Key con un
    `key_value` de formato deliberadamente inválido y obtuvo **HTTP 400**,
    el resultado esperado según la documentación pública de Passport.
  - **DUPLICATE**: no fue ejecutado. La documentación disponible no
    especifica inequívocamente el comportamiento/status/error esperado al
    intentar registrar una llave ya registrada, y XPAY prefiere solicitar
    confirmación de Passport antes de provocar deliberadamente esa
    condición sobre un recurso de Sandbox.
- **M3-T7 — Delete Already-Deleted Key**: ejecutado. XPAY intentó eliminar
  nuevamente una llave que ya había sido eliminada exitosamente en M3-T5 y
  obtuvo **HTTP 404**; se entrega para interpretación de Passport.

## Solicitud a Passport

1. Confirmar el contrato esperado (status HTTP y forma del error) para un
   intento de Create Key con un `key_value` ya registrado (M3-T6-DUPLICATE),
   para que XPAY pueda ejecutar ese caso de forma segura y capturar la
   evidencia correspondiente.
2. Revisar e interpretar el HTTP 404 observado en M3-T7 (intento de
   eliminar una llave ya eliminada).

---

Este resumen no constituye una afirmación de que Passport ya aprobó algún
caso, ni que la certificación del módulo M3 esté completa, ni que XPAY
cuenta con autorización para producción. Todas las evidencias permanecen
en estado `PENDING_PASSPORT_REVIEW` hasta que Passport complete su propia
revisión.
