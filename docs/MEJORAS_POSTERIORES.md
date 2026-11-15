# Mejoras posteriores

Mejoras de producto acordadas que no bloquean el piloto. Cada una se
implementa en su propio cambio, con su análisis de seguridad.

## M-001 — Alternar funciones de empresa y de persona en una misma cuenta

- **Registrada:** 2026-11-14.
- **Situación actual:** la vista se decide por la cuenta. Un usuario asociado
  a un convenio (`libranza_usuarios_empresa` ACTIVO) entra a la vista de
  empresa y las rutas de wallet le quedan bloqueadas. Para el piloto, la
  persona empleada y quien administra la empresa usan cuentas separadas.
- **Mejora:** permitir que una misma cuenta cambie entre «Mi empresa» y
  «Mi wallet» sin cerrar sesión.
- **A resolver antes de implementarla:**
  - Segregación de funciones: quien administra empleados no debe fijar el
    salario o los cortes que determinan su propio cupo de anticipo.
  - Autorización por vista en el backend (hoy `AsociarUsuarioEmpresaAsync`
    no revisa conflictos con la condición de empleado).
  - Auditoría del cambio de vista.
