import { FormEvent, useCallback, useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';
import { generarComprobantePdfCierre } from '../utils/comprobanteCierrePdf.ts';

// Fuente: GET /api/comercio/dashboard (ComercioViewController, rol COMERCIO).
// /api/reportes/comercios/{id}/resumen quedó restringido a ADMIN_XPAY/SUPERUSUARIO
// en la Fase 71.2-E-B y ya no es accesible desde esta pantalla (403 para
// usuarios COMERCIO). dashboard no incluye nombreComercial, idWalletComercio
// ni desglose de retiros — "retiros pendientes" se calcula localmente sobre
// el array ya cargado por /api/comercios/retiros (endpoint sin cambios).
interface ResumenComercio {
  saldoDisponible:    number;
  totalVentas:        number;
  valorTotalVentas:   number;
  ventasContingencia: number;
  ventasLiquidadas:   number;
}

interface VentaQr {
  idVentaQr:    number;
  valorBruto:   number;
  estado:       string;
  fechaVenta:   string;
}

interface RetiroComercio {
  idRetiro:     number;
  valor:        number;
  estado:       string;
  medioRetiro?: string;
  fechaCreacion:string;
}

type Msg = { ok: boolean; text: string };

interface BrebLlave {
  idBrebLlave:     number;
  tipoSujeto:      string;
  keyType:         string;
  keyValueMasked:  string;
  estado:          string;
  fechaRegistro?:  string;
  fechaValidacion?: string;
}

interface BrebRetiro {
  idBrebRetiro:      number;
  tipoSujeto:        string;
  valor:             number;
  moneda:            string;
  estado:            string;
  referenciaInterna: string;
  keyValueMasked:    string;
  fechaSolicitud:    string;
  motivoRechazo?:    string;
}

interface ResumenDisponibilidad {
  totalNoDisponibleBruto:          number;
  totalDescuentoConvenio:          number;
  totalIvaConvenio:                number;
  totalNoDisponibleNetoProgramado: number;
  totalDisponibleBruto:            number;
  totalLiquidado:                  number;
  cantidadNoDisponible:            number;
  proximaFechaDisponibilidad?:     string;
  valorEstimadoProximaLiberacion:  number;
}

interface VentaNoDisponible {
  idDisponibilidad:               number;
  idVentaQr:                      number;
  valorBruto:                     number;
  // Convenio
  porcentajeDescuentoConvenio:    number;
  valorDescuentoConvenio:         number;
  aplicaIvaConvenio:              boolean;
  porcentajeIvaConvenio:          number;
  valorIvaConvenio:               number;
  valorNetoProgramado:            number;
  // Metadata
  diasDisponibilidad:             number;
  fechaDisponibleProgramada:      string;
  diasFaltantes:                  number;
  // Anticipado
  porcentajeDescuentoAnticipado:  number;
  valorDescuentoAnticipado:       number;
  aplicaIvaAnticipado:            boolean;
  porcentajeIvaAnticipado:        number;
  valorIvaAnticipado:             number;
  valorNetoSiLiquidaAhora:        number;
  estado:                         string;
}

// celular/correo/saldoActual vienen null cuando quien busca es CAJERO —
// el backend los omite, no es un ocultamiento de UI (ver WalletRecargaComercioService).
interface BuscarUsuarioWalletResult {
  idUsuario:      number;
  nombreUsuario:  string;
  nombreCompleto: string;
  documento:      string;
  celular:        string | null;
  correo?:        string | null;
  idWallet:       number;
  saldoActual:    number | null;
  estadoWallet:   string;
}

interface RecargaWalletResult {
  idRecarga:            number;
  idTransaccionLedger?: number;
  idWallet:             number;
  idUsuarioWallet:      number;
  valor:                number;
  saldoWalletAntes:     number | null;
  saldoWalletDespues:   number | null;
  idComercio:           number;
  idTienda?:            number;
  idUsuarioCajero:      number;
  estado:               string;
  fechaRecarga:         string;
  comprobanteTexto:     string;
}

interface RecargaResumen {
  idRecarga:           number;
  idUsuarioWallet:     number;
  nombreUsuarioWallet: string;
  idWallet:            number;
  valor:               number;
  estado:              string;
  idTienda?:           number;
  idUsuarioCajero:     number;
  fechaRecarga:        string;
}

// PIN: format-only validation for QA/Demo phase — same convention as UserWalletPage/MiCarteraOrdinariaPage.
function validatePin(pin: string): string | null {
  if (!/^\d{7}$/.test(pin)) return 'La clave debe ser exactamente 7 dígitos numéricos.';
  return null;
}

// ── Cierre Diario de Comercio (Fase 70.3) ─────────────────────────────────
interface TotalesCierre {
  cantidadRecargas: number;
  valorTotal:       number;
  valorLiquidado:   number;
  valorPendiente:   number;
}

interface PreviewCierre {
  idComercio:            number;
  fecha:                 string;
  yaGenerado:            boolean;
  idCierreExistente:     number | null;
  estadoCierreExistente: string | null;
  cantidadRecargas:      number;
  valorTotalRecaudado:   number;
  valorLiquidado:        number;
  valorPendiente:        number;
  vistaEnVivo:           boolean;
  mensaje:               string;
}

interface GenerarCierreResult {
  idCierre:                number;
  idComercio:              number;
  fechaCierre:             string;
  fechaHoraCorteUtc:       string;
  codigoUnico:             string;
  cantidadRecargas:        number;
  valorTotalRecaudado:     number;
  valorLiquidadoAlGenerar: number;
  valorPendienteAlGenerar: number;
  estado:                  string;
  generadoPorUsuario:      number;
  fechaGeneracion:         string;
  notaCorte:               string;
}

interface CierreResumen {
  idCierre:              number;
  fechaCierre:           string;
  estado:                string;
  codigoUnico:           string;
  miParticipacion:       TotalesCierre;
  alcanceParticipacion:  string;
}

interface RecargaEnCierre {
  idRecarga:                number;
  idTienda:                 number | null;
  nombreTienda:             string | null;
  idUsuarioCajero:          number;
  nombreUsuarioCajero:      string | null;
  idUsuarioWallet:          number;
  nombreUsuarioWallet:      string | null;
  valor:                    number;
  estabaLiquidadaAlGenerar: boolean;
  fechaRecarga:             string;
}

interface CierreDetalle {
  idCierre:              number;
  idComercio:            number;
  nombreComercio:        string | null;
  fechaCierre:           string;
  fechaHoraCorteUtc:     string;
  codigoUnico:           string;
  estado:                string;
  fechaGeneracion:       string;
  fechaRevision:         string | null;
  fechaCerrado:          string | null;
  totalesComercio:       TotalesCierre | null;
  miParticipacion:       TotalesCierre;
  alcanceParticipacion:  string;
  recargas:              RecargaEnCierre[];
}


interface ComercioScope {
  idUsuario:               number;
  rolComercio:             string;
  idComercioAliado:        number;
  idComercioExistente?:    number;
  idEstablecimiento?:      number;
  puedeVerTodoComercio:    boolean;
  puedeDisponerRecursos:   boolean;
  puedeLiquidarAnticipado: boolean;
  puedeEnviarBreb:         boolean;
  puedeAnularVentasDiaActual: boolean;
  puedeGenerarQr:          boolean;
}

export function MiComercioPage() {
  const { user } = useAuth();

  // ── Scope operativo — resuelve el comercio dinámicamente vía /api/comercio/mi-scope,
  // nunca por username. Válido para cualquier rol COMERCIO (ADMIN_COMERCIO,
  // ADMIN_SEDE_COMERCIO, CAJERO), no solo para qa.comercio1.
  const [scope, setScope] = useState<ComercioScope | null>(null);
  const [scopeLoading, setScopeLoading] = useState(true);
  const idComercio = scope?.idComercioExistente;
  // CAJERO nunca ve saldos del cliente — el backend ya los omite (quedan null);
  // esta bandera solo evita columnas/filas vacías en la UI, no es la protección real.
  const esCajero = scope?.rolComercio === 'CAJERO';

  const [resumen,  setResumen]  = useState<ResumenComercio | null>(null);
  const [ventas,   setVentas]   = useState<VentaQr[]>([]);
  const [retiros,  setRetiros]  = useState<RetiroComercio[]>([]);
  const [loading,  setLoading]  = useState(true);
  const [dataErr,  setDataErr]  = useState<string | null>(null);

  // ── Filtros de fecha ─────────────────────────────────────────────────────
  const defaultDesde = (() => {
    const d = new Date(); d.setDate(d.getDate() - 30); return d.toISOString().slice(0, 10);
  })();
  const defaultHasta = new Date().toISOString().slice(0, 10);
  const [fechaDesde, setFechaDesde] = useState(defaultDesde);
  const [fechaHasta, setFechaHasta] = useState(defaultHasta);

  // ── Bre-B comercio ───────────────────────────────────────────────────────
  const [brebLlave,    setBrebLlave]    = useState<BrebLlave | null>(null);
  const [brebKeyType,  setBrebKeyType]  = useState('ID');
  const [brebKeyValue, setBrebKeyValue] = useState('');
  const [brebRegBusy,  setBrebRegBusy]  = useState(false);
  const [brebRegMsg,   setBrebRegMsg]   = useState<Msg | null>(null);
  const [brebRetiros,  setBrebRetiros]  = useState<BrebRetiro[]>([]);
  const [brebRetValor, setBrebRetValor] = useState('');
  const [brebRetBusy,  setBrebRetBusy]  = useState(false);
  const [brebRetMsg,   setBrebRetMsg]   = useState<Msg | null>(null);

  // ── Recargar Wallet (efectivo) ────────────────────────────────────────────
  const [rcQuery,        setRcQuery]        = useState('');
  const [rcResultados,   setRcResultados]   = useState<BuscarUsuarioWalletResult[]>([]);
  const [rcBuscando,     setRcBuscando]     = useState(false);
  const [rcSeleccionado, setRcSeleccionado] = useState<BuscarUsuarioWalletResult | null>(null);
  const [rcValor,        setRcValor]        = useState('');
  const [rcPin,          setRcPin]          = useState('');
  const [rcObservaciones, setRcObservaciones] = useState('');
  const [rcBusy,         setRcBusy]         = useState(false);
  const [rcMsg,          setRcMsg]          = useState<Msg | null>(null);
  const [rcResultado,    setRcResultado]    = useState<RecargaWalletResult | null>(null);
  const [rcRecargas,     setRcRecargas]     = useState<RecargaResumen[]>([]);

  // ── Cierre Diario de Comercio ───────────────────────────────────────────
  // Fecha operativa Colombia (America/Bogota) — NO usar new Date().toISOString()
  // aquí: eso da la fecha UTC cruda, que se adelanta un día respecto a Colombia
  // durante la ventana 19:00-00:00 hora Colombia (00:00-05:00 UTC). El backend
  // (HoyColombia()) ya aplica el mismo criterio — deben coincidir siempre.
  const hoyIso = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Bogota' }).format(new Date());
  const [cdFecha,        setCdFecha]        = useState(hoyIso);
  const [cdPreview,      setCdPreview]      = useState<PreviewCierre | null>(null);
  const [cdPreviewBusy,  setCdPreviewBusy]  = useState(false);
  const [cdConfirmHoy,   setCdConfirmHoy]   = useState(false);
  const [cdGenerando,    setCdGenerando]    = useState(false);
  const [cdMsg,          setCdMsg]          = useState<Msg | null>(null);
  const [cdResultado,    setCdResultado]    = useState<GenerarCierreResult | null>(null);
  const [cdCierres,      setCdCierres]      = useState<CierreResumen[]>([]);
  const [cdDetalle,      setCdDetalle]      = useState<CierreDetalle | null>(null);
  const [cdDetalleBusy,  setCdDetalleBusy]  = useState(false);

  // ── Disponibilidad ventas ─────────────────────────────────────────────────
  const [dispResumen,   setDispResumen]   = useState<ResumenDisponibilidad | null>(null);
  const [ventasNoDisp,  setVentasNoDisp]  = useState<VentaNoDisponible[]>([]);
  const [liquidando,    setLiquidando]    = useState<number | null>(null);
  const [dispMsg,       setDispMsg]       = useState<Msg | null>(null);

  // ── QR del comercio ───────────────────────────────────────────────────────
  const [qrComValor,   setQrComValor]   = useState('');
  const [qrComSrc,     setQrComSrc]     = useState<string | null>(null);
  const [qrComPayload, setQrComPayload] = useState<string>('');
  const [qrComBusy,    setQrComBusy]    = useState(false);
  const [qrComCopied,  setQrComCopied]  = useState(false);

  const loadData = useCallback(async () => {
    if (!idComercio) return;
    setLoading(true);
    setDataErr(null);
    try {
      const [dashboardResp, ventasResp, retirosResp] = await Promise.all([
        get<{ success: boolean; data: ResumenComercio }>('/api/comercio/dashboard'),
        get<{ success: boolean; data: { items?: VentaQr[] } | VentaQr[] }>(
          `/api/comercio/ventas?fechaDesde=${fechaDesde}&fechaHasta=${fechaHasta}`),
        get<{ success: boolean; data: { items?: RetiroComercio[] } | RetiroComercio[] }>(
          `/api/comercios/retiros?idComercio=${idComercio}&pageSize=50&desde=${fechaDesde}&hasta=${fechaHasta}`),
      ]);
      setResumen(dashboardResp.data);

      const ventasData = ventasResp.data;
      setVentas(Array.isArray(ventasData) ? ventasData : (ventasData.items ?? []));

      const retirosData = retirosResp.data;
      setRetiros(Array.isArray(retirosData) ? retirosData : (retirosData.items ?? []));
    } catch (e) {
      setDataErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [idComercio, fechaDesde, fechaHasta]);

  // Resuelve el scope operativo una sola vez al montar — de aquí sale idComercio,
  // nunca del username. Cualquier rol COMERCIO (ADMIN_COMERCIO, ADMIN_SEDE_COMERCIO,
  // CAJERO) queda soportado sin lógica adicional.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const r = await get<{ success: boolean; data: ComercioScope | null }>('/api/comercio/mi-scope');
        if (!cancelled) setScope(r.data ?? null);
      } catch {
        if (!cancelled) setScope(null);
      } finally {
        if (!cancelled) setScopeLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    void loadData();
    if (idComercio) {
      void (async () => {
        try {
          const [llaveR, retirosR] = await Promise.all([
            get<{ success: boolean; data: BrebLlave | null }>(
              `/api/breb/mi-llave/comercio?idComercio=${idComercio}`),
            get<{ success: boolean; data: BrebRetiro[] }>(
              `/api/breb/mis-retiros/comercio?idComercio=${idComercio}`),
          ]);
          setBrebLlave(llaveR.data);
          setBrebRetiros(retirosR.data ?? []);
        } catch { /* non-critical */ }
      })();
      void (async () => {
        try {
          const [resR, listR] = await Promise.all([
            get<{ success: boolean; data: ResumenDisponibilidad }>(
              `/api/comercio/ventas-disponibilidad/resumen?idComercio=${idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
            get<{ success: boolean; data: VentaNoDisponible[] }>(
              `/api/comercio/ventas-no-disponibles?idComercio=${idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
          ]);
          setDispResumen(resR.data);
          setVentasNoDisp(listR.data ?? []);
        } catch { /* non-critical — tablas pueden no existir aún */ }
      })();
      void (async () => {
        try {
          const r = await get<{ success: boolean; data: RecargaResumen[] }>('/api/comercio/wallet-recargas/mis-recargas');
          setRcRecargas(r.data ?? []);
        } catch { /* non-critical */ }
      })();
      void cargarMisCierres();
    }
  }, [loadData, idComercio]);

  async function cargarMisCierres() {
    try {
      const r = await get<{ success: boolean; data: CierreResumen[] }>('/api/comercio/wallet-cierres/mis-cierres');
      setCdCierres(r.data ?? []);
    } catch { /* non-critical */ }
  }

  async function cargarPreviewCierre() {
    setCdPreviewBusy(true); setCdMsg(null); setCdPreview(null); setCdResultado(null);
    try {
      const r = await get<{ success: boolean; data: PreviewCierre }>(`/api/comercio/wallet-cierres/preview?fecha=${cdFecha}`);
      setCdPreview(r.data);
    } catch (err) {
      setCdMsg({ ok: false, text: (err as Error).message || 'Error consultando el preview del cierre.' });
    } finally { setCdPreviewBusy(false); }
  }

  async function handleGenerarCierre() {
    if (cdFecha === hoyIso && !cdConfirmHoy) {
      setCdMsg({ ok: false, text: 'Debes confirmar explícitamente para generar el cierre del día actual.' });
      return;
    }
    if (!window.confirm(
      'Se generará el cierre definitivo para la fecha seleccionada. Los valores quedarán ' +
      'almacenados como snapshot histórico y no podrán modificarse desde el flujo normal.'
    )) return;
    setCdGenerando(true); setCdMsg(null); setCdResultado(null);
    try {
      const r = await post<{ success: boolean; data?: GenerarCierreResult; message?: string }>(
        '/api/comercio/wallet-cierres/generar',
        { fecha: cdFecha, confirmacionExplicita: cdFecha === hoyIso ? cdConfirmHoy : false },
      );
      if (r.success && r.data) {
        setCdResultado(r.data);
        setCdPreview(null);
        setCdConfirmHoy(false);
        void cargarMisCierres();
      } else {
        setCdMsg({ ok: false, text: r.message ?? 'Error generando el cierre.' });
      }
    } catch (err) {
      setCdMsg({ ok: false, text: (err as Error).message || 'Error generando el cierre.' });
    } finally { setCdGenerando(false); }
  }

  async function verDetalleCierre(idCierre: number) {
    setCdDetalleBusy(true); setCdMsg(null);
    try {
      const r = await get<{ success: boolean; data: CierreDetalle }>(`/api/comercio/wallet-cierres/${idCierre}`);
      setCdDetalle(r.data);
    } catch (err) {
      setCdMsg({ ok: false, text: (err as Error).message || 'Error cargando el detalle del cierre.' });
    } finally { setCdDetalleBusy(false); }
  }

  const cargarMisRecargas = async () => {
    try {
      const r = await get<{ success: boolean; data: RecargaResumen[] }>('/api/comercio/wallet-recargas/mis-recargas');
      setRcRecargas(r.data ?? []);
    } catch { /* non-critical */ }
  };

  async function handleBuscarUsuario(e: FormEvent) {
    e.preventDefault();
    if (!rcQuery.trim()) return;
    setRcBuscando(true); setRcMsg(null);
    try {
      const r = await get<{ success: boolean; data: BuscarUsuarioWalletResult[] }>(
        `/api/comercio/wallet-recargas/buscar-usuario?query=${encodeURIComponent(rcQuery.trim())}`);
      setRcResultados(r.data ?? []);
      if ((r.data ?? []).length === 0) setRcMsg({ ok: false, text: 'Sin resultados para esa búsqueda.' });
    } catch (err) {
      setRcMsg({ ok: false, text: (err as Error).message || 'Error buscando usuario.' });
    } finally { setRcBuscando(false); }
  }

  function seleccionarUsuarioRecarga(u: BuscarUsuarioWalletResult) {
    setRcSeleccionado(u);
    setRcResultados([]);
    setRcQuery('');
    setRcValor('');
    setRcPin('');
    setRcObservaciones('');
    setRcMsg(null);
    setRcResultado(null);
  }

  function cancelarRecarga() {
    setRcSeleccionado(null);
    setRcValor('');
    setRcPin('');
    setRcObservaciones('');
    setRcMsg(null);
    setRcResultado(null);
  }

  async function handleConfirmarRecarga() {
    if (!rcSeleccionado) return;
    const valorNum = Number(rcValor) || 0;
    if (valorNum < 1000) { setRcMsg({ ok: false, text: 'El valor mínimo de recarga es $1.000.' }); return; }
    if (valorNum > 2000000) { setRcMsg({ ok: false, text: 'El valor máximo por operación es $2.000.000.' }); return; }
    const pinErr = validatePin(rcPin);
    if (pinErr) { setRcMsg({ ok: false, text: pinErr }); return; }

    setRcBusy(true); setRcMsg(null);
    try {
      const r = await post<{ success: boolean; data?: RecargaWalletResult; message?: string }>(
        '/api/comercio/wallet-recargas',
        { idUsuarioWallet: rcSeleccionado.idUsuario, valor: valorNum, pin: rcPin, observaciones: rcObservaciones.trim() || null },
      );
      if (r.success && r.data) {
        setRcResultado(r.data);
        void cargarMisRecargas();
      } else {
        setRcMsg({ ok: false, text: r.message ?? 'Error procesando la recarga.' });
      }
    } catch (err) {
      setRcMsg({ ok: false, text: (err as Error).message || 'Error procesando la recarga.' });
    } finally { setRcBusy(false); setRcPin(''); }
  }

  if (!user) {
    return (
      <div className="page">
        <h2>Mi Comercio</h2>
        <div className="error-msg">Sesión no válida. Vuelve a iniciar sesión.</div>
      </div>
    );
  }

  if (scopeLoading) {
    return (
      <div className="page">
        <h2>Mi Comercio</h2>
        <div className="loading">Cargando tu comercio...</div>
      </div>
    );
  }

  if (!idComercio) {
    return (
      <div className="page">
        <h2>Mi Comercio</h2>
        <div className="error-msg">
          Tu usuario no tiene un comercio operativo asociado. Contacta al administrador de XPAY.
        </div>
      </div>
    );
  }

  async function handleGenerarQrComercio() {
    if (!resumen) return;
    setQrComBusy(true);
    try {
      const payload = JSON.stringify({
        type:     'XPAY_MERCHANT_PAYMENT',
        env:      'QA',
        version:  1,
        qrCode:   'QR-DEMO-XPAY-QA-001',
        amount:   qrComValor ? Number(qrComValor) : null,
        currency: 'COP',
      });
      const dataUrl = await QRCode.toDataURL(payload, { width: 280, margin: 2, color: { dark: '#1a202c' } });
      setQrComSrc(dataUrl);
      setQrComPayload(payload);
    } finally { setQrComBusy(false); }
  }

  function handleDescargarQrComercio() {
    if (!qrComSrc) return;
    const a = document.createElement('a');
    a.href = qrComSrc;
    a.download = 'xpay-comercio-QR-DEMO-XPAY-QA-001.png';
    a.click();
  }

  async function handleCopiarQrComercio() {
    if (!qrComPayload) return;
    try {
      await navigator.clipboard.writeText(qrComPayload);
      setQrComCopied(true);
      setTimeout(() => setQrComCopied(false), 2000);
    } catch { /* clipboard not available */ }
  }

  async function handleRegistrarLlaveComercio(e: FormEvent) {
    e.preventDefault();
    if (!brebKeyValue.trim() || !idComercio) return;
    setBrebRegBusy(true); setBrebRegMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebLlave; message?: string }>(
        '/api/breb/mi-llave/comercio',
        { keyType: brebKeyType, keyValue: brebKeyValue.trim(), idComercio },
      );
      if (r.success && r.data) {
        setBrebLlave(r.data);
        setBrebKeyValue('');
        setBrebRegMsg({ ok: true, text: `Llave registrada: ${r.data.keyValueMasked} — ${r.data.estado}` });
      } else {
        setBrebRegMsg({ ok: false, text: r.message ?? 'Error registrando llave.' });
      }
    } catch (err) {
      setBrebRegMsg({ ok: false, text: (err as Error).message || 'Error registrando llave.' });
    } finally { setBrebRegBusy(false); }
  }

  async function handleSolicitarRetiroComercio(e: FormEvent) {
    e.preventDefault();
    const val = Number(brebRetValor);
    if (!val || val <= 0 || !idComercio) { setBrebRetMsg({ ok: false, text: 'Ingresa un valor válido.' }); return; }
    setBrebRetBusy(true); setBrebRetMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebRetiro; message?: string }>(
        '/api/breb/retiros/simular/comercio',
        { valor: val, idComercio },
      );
      if (r.success && r.data) {
        setBrebRetiros(prev => [r.data!, ...prev]);
        setBrebRetValor('');
        setBrebRetMsg({ ok: true, text: `Retiro simulado. Ref: ${r.data.referenciaInterna} — ${r.data.estado}` });
      } else {
        setBrebRetMsg({ ok: false, text: r.message ?? 'Error creando retiro.' });
      }
    } catch (err) {
      setBrebRetMsg({ ok: false, text: (err as Error).message || 'Error creando retiro.' });
    } finally { setBrebRetBusy(false); }
  }

  return (
    <div className="page">
      <h2>Mi Comercio</h2>
      <p className="dashboard-subtitle">
        idComercio #{idComercio}
        {' · '}<span className="badge badge-info">QA / Demo</span>
        {scope && <>{' · '}<span className="badge badge-ok">{scope.rolComercio}</span></>}
      </p>

      {loading ? (
        <div className="loading">Cargando información del comercio...</div>
      ) : dataErr ? (
        <div className="error-msg">
          {dataErr}{' '}
          <button className="retry-button" onClick={() => void loadData()}>↺ Reintentar</button>
        </div>
      ) : resumen ? (
        <>
          {/* Saldo y resumen */}
          <div className="cards" style={{ marginBottom: '1.75rem' }}>
            <div className="card">
              <div className="card-label">Saldo disponible</div>
              <div className="card-value" style={{ color: '#276749' }}>{fmtMoney(resumen.saldoDisponible)}</div>
            </div>
            <div className="card">
              <div className="card-label">Ventas QR totales</div>
              <div className="card-value">{resumen.totalVentas}</div>
            </div>
            <div className="card">
              <div className="card-label">Valor ventas QR</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(resumen.valorTotalVentas)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">En contingencia</div>
              <div className="card-value">{resumen.ventasContingencia}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#68d391' }}>
              <div className="card-label">Liquidadas</div>
              <div className="card-value">{resumen.ventasLiquidadas}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#a0aec0' }}>
              <div className="card-label">Retiros pendientes</div>
              <div className="card-value">{retiros.filter(r => r.estado === 'PENDIENTE').length}</div>
            </div>
          </div>

          {/* Filtros de fecha */}
          <div style={{
            display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end',
            marginBottom: '1.25rem', padding: '0.75rem 1rem',
            background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
          }}>
            <span style={{ fontSize: '0.85rem', color: '#4a5568', fontWeight: 600, alignSelf: 'center' }}>
              Filtrar por fecha
            </span>
            <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
              Desde
              <input type="date" value={fechaDesde}
                onChange={e => setFechaDesde(e.target.value)}
                style={{ maxWidth: '160px' }} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
              Hasta
              <input type="date" value={fechaHasta}
                onChange={e => setFechaHasta(e.target.value)}
                style={{ maxWidth: '160px' }} />
            </label>
            <button className="btn-secondary" onClick={() => void loadData()}>
              Actualizar
            </button>
            <span style={{ fontSize: '0.78rem', color: '#a0aec0', alignSelf: 'center' }}>
              Aplica a ventas QR, retiros y disponibilidad
            </span>
          </div>

          {/* QR del comercio */}
          <div className="comercio-qr-section">
            <h3 style={{ marginBottom: '0.5rem' }}>QR del comercio</h3>
            <p className="tab-hint">
              Genera el QR de cobro de este comercio para mostrarlo a los usuarios o imprimirlo.
              Código: <code>QR-DEMO-XPAY-QA-001</code>
            </p>
            <label>
              Valor (opcional — COP ficticio)
              <input
                type="number"
                value={qrComValor}
                onChange={e => { setQrComValor(e.target.value); setQrComSrc(null); setQrComPayload(''); }}
                placeholder="Dejar vacío si el cliente elige el monto"
                min={0}
                style={{ maxWidth: '260px' }}
              />
            </label>
            <button
              className="btn-confirm"
              onClick={() => void handleGenerarQrComercio()}
              disabled={qrComBusy || !resumen}
              style={{ marginTop: '0.5rem' }}
            >
              {qrComBusy ? 'Generando...' : 'Generar QR comercio'}
            </button>

            {qrComSrc && (
              <div className="qr-display" style={{ marginTop: '1rem' }}>
                <img src={qrComSrc} alt="QR del comercio" className="qr-image" />
                <p className="qr-caption">
                  {qrComValor
                    ? `QR con valor ${fmtMoney(Number(qrComValor))} (COP ficticio)`
                    : 'QR sin valor fijo — el usuario ingresa el monto'}
                </p>
                <div className="qr-action-row">
                  <button className="btn-secondary" onClick={handleDescargarQrComercio}>
                    ↓ Descargar QR PNG
                  </button>
                  <button className="btn-secondary" onClick={() => void handleCopiarQrComercio()}>
                    {qrComCopied ? '✓ Copiado' : '⎘ Copiar JSON'}
                  </button>
                </div>
              </div>
            )}

            <p className="tab-warn">
              QA/Demo · el QR contiene type=XPAY_MERCHANT_PAYMENT, qrCode=QR-DEMO-XPAY-QA-001 ·
              datos ficticios · sin dinero real.
            </p>
          </div>

          {/* Ventas QR */}
          <div className="table-wrapper">
            <div className="table-title">Ventas QR del comercio ({ventas.length})</div>
            {ventas.length === 0 ? (
              <div className="empty">Sin ventas QR registradas.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Venta</th>
                    <th>Estado</th>
                    <th>Valor bruto</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {ventas.map(v => (
                    <tr key={v.idVentaQr}>
                      <td className="mono">{v.idVentaQr}</td>
                      <td>
                        <span className={`badge ${v.estado === 'LIQUIDADA' ? 'badge-ok' : v.estado === 'CONTINGENCIA' ? 'badge-warn' : 'badge-info'}`}>
                          {v.estado}
                        </span>
                      </td>
                      <td className="credit">+{fmtMoney(v.valorBruto)}</td>
                      <td className="mono">{fmtDate(v.fechaVenta)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {/* Retiros */}
          <div className="table-wrapper" style={{ marginTop: '1.25rem' }}>
            <div className="table-title">Retiros del comercio ({retiros.length})</div>
            {retiros.length === 0 ? (
              <div className="empty">Sin retiros registrados.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Retiro</th>
                    <th>Estado</th>
                    <th>Valor</th>
                    <th>Medio</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {retiros.map(r => (
                    <tr key={r.idRetiro}>
                      <td className="mono">{r.idRetiro}</td>
                      <td>
                        <span className={`badge ${r.estado === 'PAGADO' ? 'badge-ok' : r.estado === 'RECHAZADO' ? 'badge-warn' : 'badge-info'}`}>
                          {r.estado}
                        </span>
                      </td>
                      <td>{fmtMoney(r.valor)}</td>
                      <td>{r.medioRetiro ?? '—'}</td>
                      <td className="mono">{fmtDate(r.fechaCreacion)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      ) : null}

      {/* ── DISPONIBILIDAD VENTAS (liquidación anticipada) ──────────────── */}
      {(scope == null || scope.puedeLiquidarAnticipado) && dispResumen && (
        <>
          <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />
          <h3 style={{ margin: '0 0 0.75rem', fontSize: '1rem', color: '#2d3748' }}>Disponibilidad de ventas</h3>
          <div className="cards" style={{ marginBottom: '1rem' }}>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">No disponibles</div>
              <div className="card-value">{dispResumen.cantidadNoDisponible}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">Bruto retenido</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(dispResumen.totalNoDisponibleBruto)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#e53e3e' }}>
              <div className="card-label">Desc. convenio</div>
              <div className="card-value" style={{ fontSize: '1rem', color: '#c53030' }}>−{fmtMoney(dispResumen.totalDescuentoConvenio ?? 0)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#fc8181' }}>
              <div className="card-label">IVA convenio</div>
              <div className="card-value" style={{ fontSize: '1rem', color: '#c53030' }}>−{fmtMoney(dispResumen.totalIvaConvenio ?? 0)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#4299e1' }}>
              <div className="card-label">Neto programado</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(dispResumen.totalNoDisponibleNetoProgramado)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#68d391' }}>
              <div className="card-label">Ya liquidado</div>
              <div className="card-value" style={{ fontSize: '1.1rem', color:'#276749' }}>{fmtMoney(dispResumen.totalLiquidado)}</div>
            </div>
          </div>
          {dispResumen.proximaFechaDisponibilidad && (
            <p style={{ fontSize: '0.84rem', color: '#4a5568', marginBottom: '1rem' }}>
              Próxima liberación automática: <strong>{dispResumen.proximaFechaDisponibilidad}</strong> · valor estimado: <strong>{fmtMoney(dispResumen.valorEstimadoProximaLiberacion)}</strong>
            </p>
          )}

          {dispMsg && (
            <div className={dispMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginBottom: '0.75rem' }}>{dispMsg.text}</div>
          )}

          {ventasNoDisp.length === 0 ? (
            <p style={{ color: '#718096', fontSize: '0.87rem' }}>Sin ventas en periodo de indisponibilidad.</p>
          ) : (
            <div className="table-wrapper">
              <div className="table-title">Ventas no disponibles — Liquidar anticipadamente</div>
              <p style={{ fontSize: '0.8rem', color: '#718096', margin: '0.25rem 0 0.5rem' }}>
                Desc. convenio y desc. anticipado se calculan sobre el valor bruto. El IVA se aplica sobre cada descuento según la parametrización del comercio aliado.
              </p>
              <div style={{ overflowX: 'auto' }}>
                <table>
                  <thead>
                    <tr>
                      <th>#Venta</th>
                      <th>Bruto</th>
                      <th style={{ whiteSpace: 'nowrap' }}>% conv.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Desc. conv.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>IVA conv.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Neto prog.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>% ant.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Desc. ant.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>IVA ant.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Neto si liquida ahora</th>
                      <th>Días falt.</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Disponible el</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {ventasNoDisp.map(v => (
                      <tr key={v.idDisponibilidad}>
                        <td className="mono">{v.idVentaQr}</td>
                        <td className="mono">{fmtMoney(v.valorBruto)}</td>
                        <td className="mono">{v.porcentajeDescuentoConvenio}%</td>
                        <td className="mono" style={{ color: '#c53030' }}>−{fmtMoney(v.valorDescuentoConvenio)}</td>
                        <td className="mono" style={{ color: '#c53030' }}>
                          {v.aplicaIvaConvenio ? `−${fmtMoney(v.valorIvaConvenio)}` : '—'}
                        </td>
                        <td className="mono">{fmtMoney(v.valorNetoProgramado)}</td>
                        <td className="mono">{v.porcentajeDescuentoAnticipado}%</td>
                        <td className="mono" style={{ color: '#c53030' }}>−{fmtMoney(v.valorDescuentoAnticipado)}</td>
                        <td className="mono" style={{ color: '#c53030' }}>
                          {v.aplicaIvaAnticipado ? `−${fmtMoney(v.valorIvaAnticipado)}` : '—'}
                        </td>
                        <td className="credit" style={{ fontWeight: 700 }}>{fmtMoney(v.valorNetoSiLiquidaAhora)}</td>
                        <td className="mono">{v.diasFaltantes}</td>
                        <td className="mono" style={{ fontSize: '0.8rem' }}>{v.fechaDisponibleProgramada.replace('T', ' ').slice(0, 16)}</td>
                        <td>
                          <button
                            className="btn-confirm"
                            style={{ fontSize: '0.78rem', padding: '0.25rem 0.7rem' }}
                            disabled={liquidando === v.idDisponibilidad}
                            onClick={async () => {
                              if (!confirm(
                                `¿Liquidar anticipadamente venta #${v.idVentaQr}?\n` +
                                `Bruto: ${fmtMoney(v.valorBruto)}\n` +
                                `− Desc. convenio ${v.porcentajeDescuentoConvenio}%: ${fmtMoney(v.valorDescuentoConvenio)}\n` +
                                (v.aplicaIvaConvenio ? `− IVA convenio ${v.porcentajeIvaConvenio}%: ${fmtMoney(v.valorIvaConvenio)}\n` : '') +
                                `− Desc. anticipado ${v.porcentajeDescuentoAnticipado}%: ${fmtMoney(v.valorDescuentoAnticipado)}\n` +
                                (v.aplicaIvaAnticipado ? `− IVA anticipado ${v.porcentajeIvaAnticipado}%: ${fmtMoney(v.valorIvaAnticipado)}\n` : '') +
                                `Recibirás: ${fmtMoney(v.valorNetoSiLiquidaAhora)}`
                              )) return;
                              setLiquidando(v.idDisponibilidad);
                              setDispMsg(null);
                              try {
                                const r = await post<{ success: boolean; data: any; message?: string }>(
                                  `/api/comercio/ventas-no-disponibles/${v.idDisponibilidad}/liquidar-ahora?idComercio=${idComercio}`, {}
                                );
                                if (r.success) {
                                  setDispMsg({ ok: true, text: `Venta #${v.idVentaQr} liquidada. Neto recibido: ${fmtMoney(r.data.valorNetoLiberado)}` });
                                  // Refresh
                                  const [rR, lR] = await Promise.all([
                                    get<{ success: boolean; data: ResumenDisponibilidad }>(`/api/comercio/ventas-disponibilidad/resumen?idComercio=${idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
                                    get<{ success: boolean; data: VentaNoDisponible[] }>(`/api/comercio/ventas-no-disponibles?idComercio=${idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
                                  ]);
                                  setDispResumen(rR.data);
                                  setVentasNoDisp(lR.data ?? []);
                                  await loadData(); // refresh wallet balance
                                } else {
                                  setDispMsg({ ok: false, text: r.message ?? 'Error liquidando.' });
                                }
                              } catch(e) {
                                setDispMsg({ ok: false, text: (e as Error).message });
                              } finally {
                                setLiquidando(null);
                              }
                            }}
                          >
                            {liquidando === v.idDisponibilidad ? '...' : 'Liquidar ahora'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}

      {/* ── RETIRAR SALDO DEL COMERCIO (Bre-B) ──────────────────────────── */}
      {/* Visible only for ADMIN_COMERCIO (puedeEnviarBreb) or when scope not loaded (legacy) */}
      {(scope == null || scope.puedeEnviarBreb) && <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />}
      {(scope == null || scope.puedeEnviarBreb) && <>
      <h3 style={{ margin: '0 0 0.5rem', fontSize: '1rem', color: '#2d3748' }}>Retirar saldo del comercio</h3>
      <div className="breb-section">
        <span className="breb-sandbox-badge">Sandbox Passport — retiro simulado, sin dinero real</span>

        <div className="breb-status-card">
          <div className="breb-status-row">
            <span className="breb-status-label">Llave Bre-B del comercio aliado:</span>
            {brebLlave ? (
              <>
                <span className={`breb-badge breb-badge-${brebLlave.estado.toLowerCase().replace(/_/g, '-')}`}>
                  {brebLlave.estado.replace(/_/g, ' ')}
                </span>
                <span className="breb-key-masked">{brebLlave.keyType} · {brebLlave.keyValueMasked}</span>
              </>
            ) : (
              <span className="breb-badge breb-badge-no-registrada">NO REGISTRADA</span>
            )}
          </div>
        </div>

        <h4 style={{ margin: '0 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>
          {brebLlave ? 'Actualizar llave' : 'Registrar llave Bre-B'}
        </h4>
        <form className="breb-form" onSubmit={(e) => void handleRegistrarLlaveComercio(e)}>
          <label>
            Tipo de llave
            <select value={brebKeyType} onChange={e => setBrebKeyType(e.target.value)}>
              <option value="ID">NIT / ID</option>
              <option value="PHONE">Número de celular</option>
              <option value="EMAIL">Correo electrónico</option>
              <option value="ALPHA">Alias alfanumérico</option>
              <option value="BCODE">Código Bre-B</option>
            </select>
          </label>
          <label>
            Valor de la llave
            <input
              type="text"
              value={brebKeyValue}
              onChange={e => setBrebKeyValue(e.target.value)}
              placeholder="Llave Bre-B del comercio"
            />
          </label>
          <p className="breb-confirm-text">
            Esta es la llave Bre-B del comercio aliado como destinatario. XPAY realizará el pago desde su cuenta bancaria operativa en Coopcentral.
          </p>
          <button type="submit" className="btn-breb" disabled={brebRegBusy || !brebKeyValue.trim()}>
            {brebRegBusy ? 'Registrando...' : brebLlave ? 'Actualizar llave' : 'Registrar llave'}
          </button>
          {brebRegMsg && (
            <span className={brebRegMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{brebRegMsg.text}</span>
          )}
        </form>

        {brebLlave?.estado === 'VALIDADA' && (
          <form className="breb-retiro-form" onSubmit={(e) => void handleSolicitarRetiroComercio(e)}>
            <h4 style={{ margin: '0', fontSize: '0.88rem', color: '#2d3748' }}>Solicitar retiro de saldo</h4>
            <p className="breb-retiro-note">
              Destino: <strong>{brebLlave.keyType} · {brebLlave.keyValueMasked}</strong>
            </p>
            <label>
              Valor a retirar (COP ficticio)
              <input
                type="number"
                min="1"
                step="1"
                value={brebRetValor}
                onChange={e => setBrebRetValor(e.target.value)}
                placeholder="Ej: 100000"
              />
            </label>
            <button type="submit" className="btn-breb" disabled={brebRetBusy || !brebRetValor}>
              {brebRetBusy ? 'Procesando...' : 'Solicitar retiro simulado'}
            </button>
            {brebRetMsg && (
              <span className={brebRetMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{brebRetMsg.text}</span>
            )}
          </form>
        )}

        {brebRetiros.length > 0 && (
          <>
            <h4 style={{ margin: '1rem 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>Historial retiros Bre-B</h4>
            <table className="breb-retiros-table">
              <thead>
                <tr><th>Ref</th><th>Valor</th><th>Estado</th><th>Llave</th><th>Fecha</th></tr>
              </thead>
              <tbody>
                {brebRetiros.map(r => (
                  <tr key={r.idBrebRetiro}>
                    <td className="mono">{r.referenciaInterna}</td>
                    <td>{fmtMoney(r.valor)}</td>
                    <td><span className={`breb-badge breb-badge-${r.estado.toLowerCase().replace(/_/g, '-')}`}>{r.estado}</span></td>
                    <td>{r.keyValueMasked}</td>
                    <td>{fmtDate(r.fechaSolicitud)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>
      </>}

      {scope && !scope.puedeEnviarBreb && (
        <p style={{ fontSize:'0.82rem', color:'#a0aec0', margin:'1.5rem 0 0' }}>
          Tu rol ({scope.rolComercio}) no tiene acceso a la sección de retiros Bre-B.
        </p>
      )}

      {/* ── RECARGAR WALLET (efectivo, cajero de comercio) ──────────────── */}
      {(scope == null || ['ADMIN_COMERCIO', 'ADMIN_SEDE_COMERCIO', 'CAJERO'].includes(scope.rolComercio)) && (
        <>
          <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />
          <h3 style={{ margin: '0 0 0.5rem', fontSize: '1rem', color: '#2d3748' }}>Recargar Wallet</h3>
          <p className="tab-hint">
            Recibe efectivo del usuario y recarga su Wallet XPAY. El efectivo queda en poder del
            comercio como recaudo pendiente — no se registra como dinero recibido por XPAY todavía.
          </p>

          {!rcSeleccionado ? (
            <>
              <form
                onSubmit={e => void handleBuscarUsuario(e)}
                style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', alignItems: 'flex-end', marginBottom: '0.75rem' }}
              >
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.82rem', flex: '1 1 260px' }}>
                  Buscar usuario por documento, celular o usuario
                  <input
                    type="text"
                    value={rcQuery}
                    onChange={e => setRcQuery(e.target.value)}
                    placeholder="Ej: qa.usuario1"
                  />
                </label>
                <button className="btn-secondary" type="submit" disabled={rcBuscando || !rcQuery.trim()}>
                  {rcBuscando ? 'Buscando...' : 'Buscar'}
                </button>
              </form>

              {rcMsg && !rcSeleccionado && (
                <div className={rcMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginBottom: '0.75rem' }}>{rcMsg.text}</div>
              )}

              {rcResultados.length > 0 && (
                <div className="table-wrapper" style={{ marginBottom: '1rem' }}>
                  <table>
                    <thead>
                      <tr>
                        <th>Usuario</th>
                        <th>Documento</th>
                        {!esCajero && <th>Celular</th>}
                        {!esCajero && <th>Saldo Wallet</th>}
                        <th></th>
                      </tr>
                    </thead>
                    <tbody>
                      {rcResultados.map(u => (
                        <tr key={u.idUsuario}>
                          <td>{u.nombreUsuario} — {u.nombreCompleto}</td>
                          <td className="mono">{u.documento}</td>
                          {!esCajero && <td className="mono">{u.celular}</td>}
                          {!esCajero && <td>{fmtMoney(u.saldoActual)}</td>}
                          <td>
                            <button
                              className="btn-confirm"
                              style={{ fontSize: '0.78rem', padding: '0.25rem 0.7rem' }}
                              onClick={() => seleccionarUsuarioRecarga(u)}
                            >
                              Seleccionar
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          ) : rcResultado ? (
            <div className="breb-status-card" style={{ maxWidth: 480 }}>
              <h4 style={{ margin: '0 0 0.5rem', fontSize: '0.9rem' }}>Recarga confirmada #{rcResultado.idRecarga}</h4>
              <table style={{ width: '100%', fontSize: '0.85rem' }}>
                <tbody>
                  <tr><td>Cliente</td><td style={{ textAlign: 'right' }}>{rcSeleccionado.nombreUsuario} ({rcSeleccionado.documento})</td></tr>
                  <tr><td>Valor recargado</td><td style={{ textAlign: 'right', fontWeight: 700 }}>{fmtMoney(rcResultado.valor)}</td></tr>
                  {rcResultado.saldoWalletAntes != null && (
                    <tr><td>Saldo antes</td><td style={{ textAlign: 'right' }}>{fmtMoney(rcResultado.saldoWalletAntes)}</td></tr>
                  )}
                  {rcResultado.saldoWalletDespues != null && (
                    <tr><td>Saldo después</td><td style={{ textAlign: 'right', color: '#276749', fontWeight: 700 }}>{fmtMoney(rcResultado.saldoWalletDespues)}</td></tr>
                  )}
                  <tr><td>Referencia (recarga / ledger)</td><td style={{ textAlign: 'right' }}>#{rcResultado.idRecarga} / #{rcResultado.idTransaccionLedger ?? '—'}</td></tr>
                  <tr><td>Fecha</td><td style={{ textAlign: 'right' }}>{fmtDate(rcResultado.fechaRecarga)}</td></tr>
                  <tr><td>Comercio / sede / cajero</td><td style={{ textAlign: 'right' }}>#{rcResultado.idComercio} / {rcResultado.idTienda ?? '—'} / #{rcResultado.idUsuarioCajero}</td></tr>
                </tbody>
              </table>
              <p style={{ fontSize: '0.78rem', color: '#4a5568', marginTop: '0.5rem' }}>{rcResultado.comprobanteTexto}</p>
              <button className="btn-secondary" style={{ marginTop: '0.5rem' }} onClick={cancelarRecarga}>
                Nueva recarga
              </button>
            </div>
          ) : (
            <div className="breb-status-card" style={{ maxWidth: 420 }}>
              <p style={{ fontSize: '0.85rem', margin: '0 0 0.5rem' }}>
                <strong>{rcSeleccionado.nombreUsuario}</strong> — {rcSeleccionado.nombreCompleto}
                {rcSeleccionado.saldoActual != null && <><br />Saldo actual: {fmtMoney(rcSeleccionado.saldoActual)}</>}
              </p>
              <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.82rem', marginBottom: '0.5rem' }}>
                Valor recibido en efectivo (COP)
                <input type="number" min={1000} max={2000000} value={rcValor} onChange={e => setRcValor(e.target.value)} placeholder="Ej: 100000" />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.82rem', marginBottom: '0.5rem', maxWidth: 200 }}>
                Clave de 7 dígitos
                <span style={{ fontSize: 11, color: '#888', fontStyle: 'italic' }}> — QA/Demo: solo se valida formato</span>
                <input
                  type="password"
                  inputMode="numeric"
                  maxLength={7}
                  value={rcPin}
                  onChange={e => setRcPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                  placeholder="·······"
                  autoComplete="off"
                />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.82rem', marginBottom: '0.75rem' }}>
                Observaciones (opcional)
                <input type="text" value={rcObservaciones} onChange={e => setRcObservaciones(e.target.value)} placeholder="Ej: Recarga efectivo caja principal" />
              </label>
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <button
                  className="btn-confirm"
                  disabled={rcBusy || !rcValor || Number(rcValor) < 1000 || Number(rcValor) > 2000000 || rcPin.length !== 7}
                  onClick={() => void handleConfirmarRecarga()}
                >
                  {rcBusy ? 'Procesando...' : 'Confirmar recarga'}
                </button>
                <button className="btn-secondary" onClick={cancelarRecarga}>Cancelar</button>
              </div>
              {rcMsg && (
                <div className={rcMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.5rem' }}>{rcMsg.text}</div>
              )}
            </div>
          )}

          <div className="table-wrapper" style={{ marginTop: '1.25rem' }}>
            <div className="table-title">Mis recargas recientes ({rcRecargas.length})</div>
            {rcRecargas.length === 0 ? (
              <div className="empty">Sin recargas registradas todavía.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Recarga</th>
                    <th>Usuario</th>
                    <th>Valor</th>
                    <th>Estado</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {rcRecargas.map(r => (
                    <tr key={r.idRecarga}>
                      <td className="mono">{r.idRecarga}</td>
                      <td>{r.nombreUsuarioWallet}</td>
                      <td className="credit">+{fmtMoney(r.valor)}</td>
                      <td><span className="badge badge-ok">{r.estado}</span></td>
                      <td className="mono">{fmtDate(r.fechaRecarga)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}

      {/* ── CIERRE DIARIO DE COMERCIO ────────────────────────────────────────
          Capacidad administrativa (ADMIN_COMERCIO/ADMIN_SEDE_COMERCIO) — no es
          un cierre individual de cajero. CAJERO no ve esta sección: mostrar su
          MiParticipacion aquí simularía un "cierre propio" que no existe en
          esta fase — esa capacidad es la Fase 70.4 (caja/turno individual). */}
      {(scope == null || ['ADMIN_COMERCIO', 'ADMIN_SEDE_COMERCIO'].includes(scope.rolComercio)) && (
        <>
          <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />
          <h3 style={{ margin: '0 0 0.5rem', fontSize: '1rem', color: '#2d3748' }}>Cierre Diario de Comercio</h3>
          <p className="tab-hint">
            Consolida las recargas en efectivo de una jornada del comercio completo. Una vez
            generado, el cierre queda congelado — no se edita, no se elimina y no se puede
            regenerar para la misma fecha.
          </p>

          {(scope == null || scope.rolComercio === 'ADMIN_COMERCIO') ? (
            <div className="breb-status-card" style={{ maxWidth: 480, marginBottom: '1rem' }}>
              <label style={{ display: 'flex', flexDirection: 'column', fontSize: '0.82rem', marginBottom: '0.5rem', maxWidth: 200 }}>
                Fecha a consolidar
                <input
                  type="date"
                  value={cdFecha}
                  max={hoyIso}
                  onChange={e => { setCdFecha(e.target.value); setCdPreview(null); setCdConfirmHoy(false); setCdMsg(null); }}
                />
              </label>

              <button className="btn-secondary" onClick={() => void cargarPreviewCierre()} disabled={cdPreviewBusy}>
                {cdPreviewBusy ? 'Consultando...' : 'Ver preview'}
              </button>

              {cdPreview && (
                <div style={{ marginTop: '0.75rem', fontSize: '0.85rem' }}>
                  <p style={{ color: '#718096', fontSize: '0.78rem' }}>{cdPreview.mensaje}</p>
                  <table style={{ width: '100%' }}>
                    <tbody>
                      <tr><td>Recargas</td><td style={{ textAlign: 'right' }}>{cdPreview.cantidadRecargas}</td></tr>
                      <tr><td>Valor total</td><td style={{ textAlign: 'right', fontWeight: 700 }}>{fmtMoney(cdPreview.valorTotalRecaudado)}</td></tr>
                      <tr><td>Valor liquidado</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdPreview.valorLiquidado)}</td></tr>
                      <tr><td>Valor pendiente</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdPreview.valorPendiente)}</td></tr>
                    </tbody>
                  </table>

                  {!cdPreview.yaGenerado && cdPreview.cantidadRecargas > 0 && (
                    <>
                      {cdFecha === hoyIso && (
                        <label style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', fontSize: '0.8rem', margin: '0.75rem 0' }}>
                          <input type="checkbox" checked={cdConfirmHoy} onChange={e => setCdConfirmHoy(e.target.checked)} />
                          Confirmo generar el cierre de HOY — las recargas posteriores a este momento no quedarán incluidas.
                        </label>
                      )}
                      <button
                        className="btn-confirm"
                        disabled={cdGenerando || (cdFecha === hoyIso && !cdConfirmHoy)}
                        onClick={() => void handleGenerarCierre()}
                      >
                        {cdGenerando ? 'Generando...' : 'Generar y cerrar'}
                      </button>
                    </>
                  )}
                </div>
              )}

              {cdMsg && <div className={cdMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.5rem' }}>{cdMsg.text}</div>}

              {cdResultado && (
                <div style={{ marginTop: '0.75rem', padding: '0.75rem', background: '#f0fff4', borderRadius: 6, borderLeft: '3px solid #48bb78' }}>
                  <strong>Cierre #{cdResultado.idCierre} generado y cerrado.</strong>
                  <p style={{ fontSize: '0.8rem', margin: '0.4rem 0' }}>{cdResultado.notaCorte}</p>
                  <button
                    className="btn-secondary"
                    style={{ fontSize: '0.78rem' }}
                    onClick={() => generarComprobantePdfCierre({
                      idCierre: cdResultado.idCierre,
                      idComercio: cdResultado.idComercio,
                      fechaCierre: cdResultado.fechaCierre,
                      fechaHoraCorteUtc: cdResultado.fechaHoraCorteUtc,
                      codigoUnico: cdResultado.codigoUnico,
                      estado: cdResultado.estado,
                      cantidadRecargas: cdResultado.cantidadRecargas,
                      valorTotalRecaudado: cdResultado.valorTotalRecaudado,
                      valorLiquidadoAlGenerar: cdResultado.valorLiquidadoAlGenerar,
                      valorPendienteAlGenerar: cdResultado.valorPendienteAlGenerar,
                    })}
                  >
                    Descargar comprobante PDF
                  </button>
                </div>
              )}
            </div>
          ) : (
            cdMsg && <div className={cdMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginBottom: '0.75rem' }}>{cdMsg.text}</div>
          )}

          <div className="table-wrapper" style={{ marginTop: '1rem' }}>
            <div className="table-title">Cierres diarios ({cdCierres.length})</div>
            {cdCierres.length === 0 ? (
              <div className="empty">Sin cierres generados todavía.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Cierre</th><th>Fecha</th><th>Estado</th><th>Alcance</th>
                    <th>Recargas</th><th>Valor</th><th>Liquidado</th><th>Pendiente</th><th></th>
                  </tr>
                </thead>
                <tbody>
                  {cdCierres.map(c => (
                    <tr key={c.idCierre}>
                      <td className="mono">{c.idCierre}</td>
                      <td className="mono">{c.fechaCierre}</td>
                      <td><span className="badge badge-ok">{c.estado}</span></td>
                      <td>{c.alcanceParticipacion}</td>
                      <td className="mono">{c.miParticipacion.cantidadRecargas}</td>
                      <td>{fmtMoney(c.miParticipacion.valorTotal)}</td>
                      <td>{fmtMoney(c.miParticipacion.valorLiquidado)}</td>
                      <td>{fmtMoney(c.miParticipacion.valorPendiente)}</td>
                      <td>
                        <button
                          className="btn-secondary"
                          style={{ fontSize: '0.78rem', padding: '0.25rem 0.7rem' }}
                          onClick={() => void verDetalleCierre(c.idCierre)}
                        >
                          Ver detalle
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {cdDetalleBusy && <div className="loading">Cargando detalle...</div>}
          {cdDetalle && (
            <div className="breb-status-card" style={{ marginTop: '1rem' }}>
              <h4 style={{ margin: '0 0 0.5rem', fontSize: '0.9rem' }}>
                Cierre #{cdDetalle.idCierre} — {cdDetalle.fechaCierre} — {cdDetalle.estado}
              </h4>
              <p style={{ fontSize: '0.78rem', color: '#718096' }}>Código único: {cdDetalle.codigoUnico}</p>

              {cdDetalle.totalesComercio && (
                <>
                  <p style={{ fontSize: '0.82rem', fontWeight: 600, margin: '0.5rem 0 0.25rem' }}>Total comercio (todas las sedes)</p>
                  <table style={{ width: '100%', fontSize: '0.85rem', marginBottom: '0.5rem' }}>
                    <tbody>
                      <tr><td>Recargas</td><td style={{ textAlign: 'right' }}>{cdDetalle.totalesComercio.cantidadRecargas}</td></tr>
                      <tr><td>Valor total</td><td style={{ textAlign: 'right', fontWeight: 700 }}>{fmtMoney(cdDetalle.totalesComercio.valorTotal)}</td></tr>
                      <tr><td>Liquidado</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdDetalle.totalesComercio.valorLiquidado)}</td></tr>
                      <tr><td>Pendiente</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdDetalle.totalesComercio.valorPendiente)}</td></tr>
                    </tbody>
                  </table>
                </>
              )}

              <p style={{ fontSize: '0.82rem', fontWeight: 600, margin: '0.5rem 0 0.25rem' }}>
                {cdDetalle.alcanceParticipacion === 'COMERCIO_COMPLETO' ? 'Tu participación (todo el comercio)'
                  : cdDetalle.alcanceParticipacion === 'SEDE' ? 'Participación de tu sede en el cierre'
                  : 'Tus recargas incluidas en el cierre'}
              </p>
              <table style={{ width: '100%', fontSize: '0.85rem', marginBottom: '0.75rem' }}>
                <tbody>
                  <tr><td>Recargas</td><td style={{ textAlign: 'right' }}>{cdDetalle.miParticipacion.cantidadRecargas}</td></tr>
                  <tr><td>Valor total</td><td style={{ textAlign: 'right', fontWeight: 700 }}>{fmtMoney(cdDetalle.miParticipacion.valorTotal)}</td></tr>
                  <tr><td>Liquidado</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdDetalle.miParticipacion.valorLiquidado)}</td></tr>
                  <tr><td>Pendiente</td><td style={{ textAlign: 'right' }}>{fmtMoney(cdDetalle.miParticipacion.valorPendiente)}</td></tr>
                </tbody>
              </table>

              <div className="table-wrapper">
                <table>
                  <thead>
                    <tr><th>#Recarga</th><th>Sede</th><th>Cajero</th><th>Usuario wallet</th><th>Valor</th><th>Liquidada</th><th>Fecha</th></tr>
                  </thead>
                  <tbody>
                    {cdDetalle.recargas.map(r => (
                      <tr key={r.idRecarga}>
                        <td className="mono">{r.idRecarga}</td>
                        <td>{r.nombreTienda ?? (r.idTienda ? `#${r.idTienda}` : '—')}</td>
                        <td>{r.nombreUsuarioCajero ?? `#${r.idUsuarioCajero}`}</td>
                        <td>{r.nombreUsuarioWallet ?? `#${r.idUsuarioWallet}`}</td>
                        <td>{fmtMoney(r.valor)}</td>
                        <td>{r.estabaLiquidadaAlGenerar ? 'Sí' : 'No'}</td>
                        <td className="mono">{fmtDate(r.fechaRecarga)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <button
                className="btn-secondary"
                style={{ marginTop: '0.75rem', fontSize: '0.78rem' }}
                onClick={() => generarComprobantePdfCierre({
                  idCierre: cdDetalle.idCierre,
                  idComercio: cdDetalle.idComercio,
                  nombreComercio: cdDetalle.nombreComercio,
                  fechaCierre: cdDetalle.fechaCierre,
                  fechaHoraCorteUtc: cdDetalle.fechaHoraCorteUtc,
                  codigoUnico: cdDetalle.codigoUnico,
                  estado: cdDetalle.estado,
                  cantidadRecargas: cdDetalle.totalesComercio?.cantidadRecargas ?? cdDetalle.miParticipacion.cantidadRecargas,
                  valorTotalRecaudado: cdDetalle.totalesComercio?.valorTotal ?? cdDetalle.miParticipacion.valorTotal,
                  valorLiquidadoAlGenerar: cdDetalle.totalesComercio?.valorLiquidado ?? cdDetalle.miParticipacion.valorLiquidado,
                  valorPendienteAlGenerar: cdDetalle.totalesComercio?.valorPendiente ?? cdDetalle.miParticipacion.valorPendiente,
                })}
              >
                Descargar comprobante PDF
              </button>
            </div>
          )}
        </>
      )}

      <p className="user-wallet-footer">
        Ambiente QA/Demo · datos ficticios · sin dinero real · sin producción
      </p>
    </div>
  );
}
