import { FormEvent, useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import QRCode from 'qrcode';
import { Html5Qrcode } from 'html5-qrcode';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post, HttpUncertainError } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';
import { HeroBalanceCard } from '../components/wallet/HeroBalanceCard.tsx';

// Fase 71.2-E-C: DEMO_MAP eliminado — la wallet propia se resuelve vía
// GET /api/wallets/mi-wallet (claim idPersona del JWT), no por username.

// QA wallet-to-username reverse map — used to show counterpart in movement descriptions
const WALLET_USER_MAP: Record<number, string> = {
  2: 'qa.usuario1',
  3: 'qa.usuario2',
};

// Polling interval for automatic wallet refresh (QA/Demo phase)
// Production: replace with SignalR/WebSocket push notifications
const POLL_INTERVAL_MS = 7000;

// XPAY QR payload types — QA/Demo phase (no cryptographic signing in this phase)
// See docs/QA_QR_MONEY_FLOW.md for full specification
interface XpayTransferQR {
  type: 'XPAY_TRANSFER';
  env:  'QA';
  version: number;
  receiverUser:     string;
  receiverWalletId: number;
  amount:   number | null;
  currency: string;
}

interface XpayMerchantQR {
  type: 'XPAY_MERCHANT_PAYMENT';
  env:  'QA';
  version: number;
  merchantName: string;
  qrCode:  string;
  amount:  number | null;
  currency: string;
}

interface Movimiento {
  idMovimiento:   number;
  fecha:          string;
  tipoMovimiento: string;
  naturaleza:     string;
  valor:          number;
  saldoDespues:   number;
  descripcion:    string | null;
  referenciaTipo: string | null;
  referenciaId:   number | null;
}

interface EstadoCuenta {
  idWallet:        number;
  nombreWallet:    string;
  estado:          string;
  saldoDisponible: number;
  // XPAY-375 — expuesto por primera vez por el backend (ReportesService).
  // Fuente de verdad es SIEMPRE el backend — nunca se calcula localmente.
  saldoRetenido:   number;
  movimientos:     Movimiento[];
}

interface MiWallet {
  idWallet:     number;
  idPersona:    number;
  nombreWallet: string;
  estado:       string;
}

type Msg = { ok: boolean; text: string };
type Tab = 'saldo' | 'recibir' | 'enviar' | 'pagar' | 'movimientos' | 'banco' | 'retirar-breb';
const VALID_TABS: Tab[] = ['saldo', 'recibir', 'enviar', 'pagar', 'movimientos', 'banco', 'retirar-breb'];

// XPAY-392 — exportado para que useMyBrebKey.ts (Perfil) reutilice
// exactamente el mismo tipo, sin duplicarlo. No cambia forma ni semántica.
export interface BrebLlave {
  idBrebLlave:     number;
  tipoSujeto:      string;
  keyType:         string;
  keyValueMasked:  string;
  estado:          string;
  fechaRegistro?:  string;
  fechaValidacion?: string;
  // XPAY-375 — distingue una llave validada por Resolve real de Passport
  // (XPAY-371) de una validada sólo por el botón admin QA de simulación.
  // Ausente en respuestas de backends anteriores a XPAY-371 — por eso
  // opcional, tratado como false si falta.
  resolucionVerificadaPassport?: boolean;
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
  // XPAY-377 — presentes sólo para retiros del flujo real (XPAY-373);
  // ausentes/"ABSENT" (Fingerprint.AbsentMarker) para retiros simulados.
  paymentIdFingerprint?:    string;
  resolutionIdFingerprint?: string;
}

// XPAY-375 — flujo REAL (dinero real, cuenta operativa XPAY en Passport
// Sandbox), distinto y separado del flujo simulado (BrebLlave/BrebRetiro
// arriba). Formas exactas de MiLlaveResolveResponse/RetiroRealResponse
// (backend, XPAY-371/373) — camelCase por serialización JSON default de
// ASP.NET Core.
interface LlaveResolveResult {
  idBrebLlave:                  number;
  keyType:                      string;
  keyValueMasked:                string;
  estado:                       string;
  resolucionVerificadaPassport: boolean;
  titularNombreMasked:          string;
  titularIdentificacionTipo?:   string | null;
  titularIdentificacionMasked?: string | null;
  entidadFinanciera?:           string | null;
  tipoCuenta?:                  string | null;
  cuentaMasked?:                string | null;
  vigenteHasta?:                string | null;
}

interface RetiroReal {
  idBrebRetiro:            number;
  valor:                   number;
  moneda:                  string;
  estado:                  string;
  paymentIdFingerprint?:   string | null;
  resolutionIdFingerprint?: string | null;
  fechaSolicitud:          string;
  fechaEnvioPassport?:     string | null;
  motivoRechazo?:          string | null;
}

// XPAY-375 FASE 6 — clasificación de estados LOCALES del retiro real
// (los mismos que persiste BrebPaymentService/BrebPaymentStateMachine,
// XPAY-373) para decidir el mensaje mostrado — nunca se asume éxito sólo
// porque el POST respondió 2xx.
type RetiroRealEstadoClase = 'transitorio' | 'exitoso' | 'fallido' | 'desconocido';

function clasificarRetiroRealEstado(estado: string): RetiroRealEstadoClase {
  if (estado === 'LIQUIDADO') return 'exitoso';
  if (estado === 'RECHAZADO' || estado === 'ERROR') return 'fallido';
  if (estado === 'PENDIENTE_ENVIO_PASSPORT' || estado === 'ENVIADO_PASSPORT' || estado === 'CREADO') return 'transitorio';
  return 'desconocido';
}

function retiroRealMensaje(estado: string, motivoRechazo?: string | null): string {
  const clase = clasificarRetiroRealEstado(estado);
  if (clase === 'exitoso') return 'Retiro completado.';
  if (clase === 'fallido') {
    return motivoRechazo
      ? `El retiro fue rechazado y el dinero reservado volvió a estar disponible. Motivo: ${motivoRechazo}`
      : 'El retiro fue rechazado y el dinero reservado volvió a estar disponible.';
  }
  // transitorio/desconocido: nunca afirmar éxito ni fallo sin confirmación.
  return 'Tu retiro está siendo procesado.';
}

// PIN: format-only validation for QA/Demo phase
// Full cryptographic validation (backend hash, attempt limits, lockout) is pending for production
function validatePin(pin: string): string | null {
  if (!/^\d{7}$/.test(pin)) return 'La clave debe ser exactamente 7 dígitos numéricos.';
  return null;
}

// XPAY-390 — valor opcional COP del QR de Recibir. Vacío → QR base sin
// amount (válido, no es un error). Si hay texto, debe ser un entero
// positivo (sin decimales, sin negativos, sin cero explícito, sin texto/
// NaN) — nunca se deja pasar un valor "raro" al payload del QR.
function parseRecValorAmount(raw: string): { amount: number | null; error: string | null } {
  const trimmed = raw.trim();
  if (trimmed === '') return { amount: null, error: null };
  const n = Number(trimmed);
  if (!Number.isFinite(n)) return { amount: null, error: 'Ingresa un valor numérico válido.' };
  if (n <= 0) return { amount: null, error: 'El valor debe ser mayor a cero.' };
  if (!Number.isInteger(n)) return { amount: null, error: 'Ingresa un valor en pesos, sin decimales.' };
  return { amount: n, error: null };
}

function kycLabel(estado: string): string {
  const labels: Record<string, string> = {
    NO_INICIADO: 'No iniciado',
    PENDIENTE:   'Pendiente',
    EN_REVISION: 'En revisión',
    APROBADO:    'Aprobado',
    RECHAZADO:   'Rechazado',
    EXPIRADO:    'Expirado',
    ERROR:       'Error',
  };
  return labels[estado] ?? estado;
}

const KYC_BADGE_CLASS: Record<string, string> = {
  NO_INICIADO: 'kyc-badge kyc-badge-no-iniciado',
  PENDIENTE:   'kyc-badge kyc-badge-pendiente',
  EN_REVISION: 'kyc-badge kyc-badge-en-revision',
  APROBADO:    'kyc-badge kyc-badge-aprobado',
  RECHAZADO:   'kyc-badge kyc-badge-rechazado',
  EXPIRADO:    'kyc-badge kyc-badge-expirado',
  ERROR:       'kyc-badge kyc-badge-error',
};

// Computes a human-readable description for a wallet movement.
// Uses tipoMovimiento + referenciaId (already in the API response) to identify the counterpart.
// Falls back to the stored descripcion for unknown types.
function descripcionVisible(m: Movimiento): string {
  if (m.tipoMovimiento === 'TRANSFERENCIA_SALIDA' && m.referenciaTipo === 'wallets' && m.referenciaId) {
    const destUser = WALLET_USER_MAP[m.referenciaId];
    return destUser
      ? `Enviado a ${destUser} — Wallet #${m.referenciaId}`
      : `Enviado a Wallet #${m.referenciaId}`;
  }
  if (m.tipoMovimiento === 'TRANSFERENCIA_ENTRADA' && m.referenciaTipo === 'wallets' && m.referenciaId) {
    const srcUser = WALLET_USER_MAP[m.referenciaId];
    return srcUser
      ? `Recibido de ${srcUser} — Wallet #${m.referenciaId}`
      : `Recibido de Wallet #${m.referenciaId}`;
  }
  if (m.tipoMovimiento === 'PAGO_QR') {
    return 'Pago a Comercio Demo XPAY QA';
  }
  return m.descripcion ?? '—';
}

export function UserWalletPage() {
  const { user } = useAuth();
  const navigate  = useNavigate();
  // Tab en la URL (?tab=...) en vez de estado local puro — permite que
  // WalletHero/BottomNav (Layout.tsx) y la recarga de página abran un tab
  // específico. Sin valor o valor inválido → 'saldo' (mismo comportamiento
  // que el default anterior).
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get('tab');
  const tab: Tab = VALID_TABS.includes(rawTab as Tab) ? (rawTab as Tab) : 'saldo';
  function setTab(t: Tab) {
    if (t === 'saldo') setSearchParams({});
    else setSearchParams({ tab: t });
  }

  // ── Mi wallet (Fase 71.2-E-C: resuelta vía GET /api/wallets/mi-wallet) ────
  const [miWallet,        setMiWallet]        = useState<MiWallet | null>(null);
  const [miWalletLoading, setMiWalletLoading]  = useState(true);

  // ── Account data ──────────────────────────────────────────────────────────
  const [cuenta,     setCuenta]     = useState<EstadoCuenta | null>(null);
  const [loading,    setLoading]    = useState(true);
  const [dataErr,    setDataErr]    = useState<string | null>(null);

  // ── Auto-refresh state ────────────────────────────────────────────────────
  const [refreshing,  setRefreshing]  = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [refreshErr,  setRefreshErr]  = useState<string | null>(null);
  const [newMovMsg,   setNewMovMsg]   = useState<string | null>(null);

  // Refs for polling: baseline movement ID, toast timer, in-progress guard
  const lastKnownMovIdRef   = useRef<number>(-1);
  const newMovToastTimerRef = useRef<number | null>(null);
  const opInProgressRef     = useRef(false); // true during financial transactions

  // ── Recibir dinero ────────────────────────────────────────────────────────
  const [recValor,    setRecValor]    = useState('');
  const [recValorErr, setRecValorErr] = useState<string | null>(null);
  const [recQrSrc,    setRecQrSrc]    = useState<string | null>(null);
  const [recQrAmount, setRecQrAmount] = useState<number | null>(null);
  const [recQrBusy,   setRecQrBusy]   = useState(false);
  // XPAY-390 — recuerda el idWallet para el que se generó el último QR, de
  // modo que si cambia la wallet activa (usuario distinto en la misma
  // instancia montada) el QR obsoleto se invalide y se regenere para la
  // wallet correcta — nunca mostrar un QR de otra wallet.
  const recQrWalletIdRef = useRef<number | null>(null);

  // ── Enviar dinero ─────────────────────────────────────────────────────────
  const [envDest,       setEnvDest]       = useState<number | null>(null);
  const [envDestUser,   setEnvDestUser]   = useState('');
  const [envValor,      setEnvValor]      = useState('');
  const [envNeedValor,  setEnvNeedValor]  = useState(false);
  const [envPin,        setEnvPin]        = useState('');
  const [envBusy,       setEnvBusy]       = useState(false);
  const [envMsg,        setEnvMsg]        = useState<Msg | null>(null);
  const [envPasted,     setEnvPasted]     = useState('');
  const [envScanning,   setEnvScanning]   = useState(false);
  const [envScanErr,    setEnvScanErr]    = useState<string | null>(null);
  const [envManual,     setEnvManual]     = useState(false);
  const [envManualDest, setEnvManualDest] = useState('');
  // QA-WALLET-7A: mensaje de confirmación mostrado tras éxito, independiente
  // de envMsg (que sigue reservado para errores del formulario reutilizable).
  const [envSuccessMsg, setEnvSuccessMsg] = useState<string | null>(null);
  const envSuccessTimerRef = useRef<number | null>(null);
  const envScannerRef = useRef<Html5Qrcode | null>(null);
  // Fase 71.2-E-G: una Idempotency-Key por intento lógico de transferencia —
  // se reutiliza mientras destino/valor/descripción no cambien (reintento del
  // mismo intento); se descarta al tener éxito o al cambiar cualquiera de
  // esos campos (ver getEnvIdempotencyKey).
  const envIdemRef = useRef<{ key: string; destId: number; valor: string; descripcion: string } | null>(null);

  // ── KYC state ─────────────────────────────────────────────────────────────
  const [kycEstado,     setKycEstado]     = useState<string>('NO_INICIADO');
  const [kycBusy,       setKycBusy]       = useState(false);
  const [kycMsg,        setKycMsg]        = useState<Msg | null>(null);
  const [kycRefreshing, setKycRefreshing] = useState(false);
  const [fromVeriff,    setFromVeriff]    = useState(false);
  const kycEstadoRef = useRef<string>('NO_INICIADO');

  // ── Bre-B / Retirar a mi banco ───────────────────────────────────────────
  // XPAY-392A — brebKeyType/brebKeyValue/brebRegBusy/brebRegMsg y
  // handleRegistrarLlave (POST /api/breb/mi-llave) fueron eliminados de
  // aquí: el registro/actualización de la llave vive ahora exclusivamente
  // en Perfil → Mi llave Bre-B (useMyBrebKey.ts/BrebMyKeySection.tsx,
  // mismo endpoint). Ya no tenían ningún formulario que los invocara desde
  // XPAY-392. brebLlave/brebLlaveLoad se conservan — siguen siendo la
  // fuente de estado de la llave para este tab y para "retirar-breb".
  const [brebLlave,      setBrebLlave]      = useState<BrebLlave | null>(null);
  const [brebLlaveLoad,  setBrebLlaveLoad]  = useState(false);
  const [brebRetiros,    setBrebRetiros]    = useState<BrebRetiro[]>([]);
  const [brebRetValor,   setBrebRetValor]   = useState('');
  const [brebRetBusy,    setBrebRetBusy]    = useState(false);
  const [brebRetMsg,     setBrebRetMsg]     = useState<Msg | null>(null);

  // ── XPAY-375 — Retirar a mi llave Bre-B (REAL, dinero real) ────────────
  // Separado deliberadamente del estado del flujo simulado de arriba — un
  // usuario nunca debe poder confundir ambos flujos.
  const [realKeyValueInput, setRealKeyValueInput] = useState('');
  const [realResolveBusy,   setRealResolveBusy]   = useState(false);
  const [realResolveMsg,    setRealResolveMsg]    = useState<Msg | null>(null);
  const [realResolveResult, setRealResolveResult] = useState<LlaveResolveResult | null>(null);
  const [realMonto,         setRealMonto]         = useState('');
  const [realMontoErr,      setRealMontoErr]      = useState<string | null>(null);
  const [realStep,          setRealStep]          = useState<'monto' | 'confirmar'>('monto');
  const [realConfirmBusy,   setRealConfirmBusy]   = useState(false);
  const [realConfirmMsg,    setRealConfirmMsg]    = useState<Msg | null>(null);
  const [realRetiroResult,  setRealRetiroResult]  = useState<RetiroReal | null>(null);
  // XPAY-377 FASE 4 — true SÓLO tras un timeout del cliente (HttpUncertainError):
  // el servidor pudo haber recibido/procesado la operación aunque el
  // navegador no haya recibido respuesta. Nunca se presenta como éxito ni
  // como fallo — es su propio estado, con su propia UI (FASE 4/5).
  const [realUncertain,     setRealUncertain]     = useState(false);
  const [realCheckBusy,     setRealCheckBusy]     = useState(false);
  const [realCheckMsg,      setRealCheckMsg]      = useState<Msg | null>(null);

  // ── Pagar comercio QR ─────────────────────────────────────────────────────
  const [pagQrCode,       setPagQrCode]       = useState('');
  const [pagValor,        setPagValor]        = useState('');
  const [pagNeedValor,    setPagNeedValor]    = useState(false);
  const [pagPin,          setPagPin]          = useState('');
  const [pagBusy,         setPagBusy]         = useState(false);
  const [pagMsg,          setPagMsg]          = useState<Msg | null>(null);
  const [pagPasted,       setPagPasted]       = useState('');
  const [pagScanning,     setPagScanning]     = useState(false);
  const [pagScanErr,      setPagScanErr]      = useState<string | null>(null);
  const [pagMetodoPago,   setPagMetodoPago]   = useState<'wallet' | 'cupo' | null>(null);
  const pagScannerRef = useRef<Html5Qrcode | null>(null);
  // Fase 71.2-E-G: misma idea que envIdemRef, para el pago QR.
  const pagIdemRef = useRef<{ key: string; qrCode: string; valor: string } | null>(null);

  // ── KYC load / manual refresh ─────────────────────────────────────────────
  const loadKyc = useCallback(async (silent = false) => {
    if (!silent) setKycRefreshing(true);
    try {
      const r = await get<{ success: boolean; data: { estadoKyc: string; sessionUrl?: string } }>(
        '/api/kyc/mi-estado',
      );
      setKycEstado(r.data.estadoKyc);
      kycEstadoRef.current = r.data.estadoKyc;
    } catch {
      /* non-critical: keep last known state */
    } finally {
      if (!silent) setKycRefreshing(false);
    }
  }, []);

  // ── Bre-B load ────────────────────────────────────────────────────────────
  const loadBreb = useCallback(async () => {
    setBrebLlaveLoad(true);
    try {
      const [llaveResp, retirosResp] = await Promise.all([
        get<{ success: boolean; data: BrebLlave | null }>('/api/breb/mi-llave'),
        get<{ success: boolean; data: BrebRetiro[] }>('/api/breb/mis-retiros'),
      ]);
      setBrebLlave(llaveResp.data);
      setBrebRetiros(retirosResp.data ?? []);
    } catch {
      /* non-critical */
    } finally {
      setBrebLlaveLoad(false);
    }
  }, []);

  // ── Mi wallet load (Fase 71.2-E-C) ─────────────────────────────────────────
  const loadMiWallet = useCallback(async () => {
    if (!user) return;
    setMiWalletLoading(true);
    try {
      const r = await get<{ success: boolean; data: MiWallet }>('/api/wallets/mi-wallet');
      setMiWallet(r.data);
    } catch {
      setMiWallet(null);
    } finally {
      setMiWalletLoading(false);
    }
  }, [user]);

  // ── Initial load ──────────────────────────────────────────────────────────
  const loadCuenta = useCallback(async () => {
    if (!user) return;
    setLoading(true); setDataErr(null);
    try {
      const r = await get<{ success: boolean; data: EstadoCuenta }>('/api/reportes/mi-estado-cuenta');
      setCuenta(r.data);
      setLastUpdated(new Date());
      // Establish baseline for new-movement detection
      lastKnownMovIdRef.current = r.data.movimientos[0]?.idMovimiento ?? -1;
    } catch (e) { setDataErr((e as Error).message); }
    finally { setLoading(false); }
  }, [user]);

  // ── Silent background refresh (polling) ───────────────────────────────────
  const pollRefresh = useCallback(async () => {
    if (!user) return;
    if (opInProgressRef.current) return; // skip during financial transactions
    setRefreshing(true);
    try {
      const r = await get<{ success: boolean; data: EstadoCuenta }>('/api/reportes/mi-estado-cuenta');
      const fresh = r.data;
      const latestId = fresh.movimientos[0]?.idMovimiento ?? -1;

      // Detect new movement since last known baseline
      if (lastKnownMovIdRef.current !== -1 && latestId > lastKnownMovIdRef.current) {
        const newest = fresh.movimientos[0];
        let msg = 'Movimiento realizado. Saldo actualizado.';
        if (newest?.naturaleza === 'C') {
          if (newest.tipoMovimiento === 'TRANSFERENCIA_ENTRADA'
              && newest.referenciaTipo === 'wallets'
              && newest.referenciaId) {
            const from = WALLET_USER_MAP[newest.referenciaId] ?? `Wallet #${newest.referenciaId}`;
            msg = `Recibiste dinero de ${from}. Saldo actualizado.`;
          } else {
            msg = 'Recibiste dinero. Saldo actualizado.';
          }
        }
        setNewMovMsg(msg);
        if (newMovToastTimerRef.current) clearTimeout(newMovToastTimerRef.current);
        newMovToastTimerRef.current = window.setTimeout(() => setNewMovMsg(null), 6000);
      }

      lastKnownMovIdRef.current = latestId;
      setCuenta(fresh);
      setLastUpdated(new Date());
      setRefreshErr(null);
    } catch {
      setRefreshErr('No se pudo actualizar automáticamente. Usa "Actualizar ahora".');
    } finally {
      setRefreshing(false);
    }
  }, [user]);

  useEffect(() => {
    void loadMiWallet(); void loadCuenta(); void loadKyc(); void loadBreb();
  }, [loadMiWallet, loadCuenta, loadKyc, loadBreb]);

  // Part G: detect ?kyc=return — user returned from Veriff, refresh KYC state immediately
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.get('kyc') === 'return') {
      setFromVeriff(true);
      setKycMsg({ ok: true, text: 'Regresaste de Veriff. Estamos actualizando tu estado...' });
      window.history.replaceState({}, '', window.location.pathname);
      void loadKyc();
    }
  }, [loadKyc]);

  // ── Wallet polling every 7 seconds ────────────────────────────────────────
  useEffect(() => {
    const id = window.setInterval(() => { void pollRefresh(); }, POLL_INTERVAL_MS);
    return () => clearInterval(id);
  }, [pollRefresh]);

  // ── KYC polling while PENDIENTE — stops when a final state is reached ─────
  // Polls every 12 seconds. Stops automatically for APROBADO/RECHAZADO/EXPIRADO/ERROR.
  // Production: replace with webhook push or server-sent events.
  const KYC_FINAL_STATES = new Set(['APROBADO', 'RECHAZADO', 'EXPIRADO', 'ERROR']);
  useEffect(() => {
    if (kycEstado !== 'PENDIENTE') return;
    const id = window.setInterval(() => {
      if (KYC_FINAL_STATES.has(kycEstadoRef.current)) {
        clearInterval(id);
        return;
      }
      void loadKyc(true);
    }, 12000);
    return () => clearInterval(id);
  }, [kycEstado, loadKyc]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Cleanup on unmount ────────────────────────────────────────────────────
  useEffect(() => {
    return () => {
      if (newMovToastTimerRef.current) clearTimeout(newMovToastTimerRef.current);
      if (envSuccessTimerRef.current) clearTimeout(envSuccessTimerRef.current);
      const stopScanner = async (s: Html5Qrcode | null) => {
        if (!s) return;
        try { await s.stop(); } catch { /* ignore */ }
        try { s.clear(); } catch { /* ignore */ }
      };
      void stopScanner(envScannerRef.current);
      void stopScanner(pagScannerRef.current);
    };
  }, []);

  // ── QR parse helpers ──────────────────────────────────────────────────────
  function parseTransferQr(raw: string): void {
    try {
      const p = JSON.parse(raw) as XpayTransferQR;
      if (p.type !== 'XPAY_TRANSFER')       { setEnvScanErr('El QR no es de tipo XPAY_TRANSFER.'); return; }
      if (p.env  !== 'QA')                  { setEnvScanErr('El QR no corresponde al ambiente QA.'); return; }
      if (!p.receiverWalletId)              { setEnvScanErr('QR sin wallet destino.'); return; }
      if (p.receiverWalletId === miWallet?.idWallet)
                                             { setEnvScanErr('No puedes transferirte a tu propia wallet.'); return; }
      setEnvDest(p.receiverWalletId);
      setEnvDestUser(p.receiverUser ?? '');
      if (p.amount && p.amount > 0) { setEnvValor(String(p.amount)); setEnvNeedValor(false); }
      else                          { setEnvValor('');                setEnvNeedValor(true); }
      setEnvScanErr(null); setEnvMsg(null); setEnvManual(false);
    } catch { setEnvScanErr('El contenido del QR no es JSON válido XPAY_TRANSFER.'); }
  }

  function parseMerchantQr(raw: string): void {
    try {
      const p = JSON.parse(raw) as XpayMerchantQR;
      if (p.type === 'XPAY_MERCHANT_PAYMENT') {
        setPagQrCode(p.qrCode);
        if (p.amount && p.amount > 0) { setPagValor(String(p.amount)); setPagNeedValor(false); }
        else                          { setPagValor('');                setPagNeedValor(true); }
        setPagScanErr(null); setPagMsg(null); return;
      }
    } catch { /* not JSON — try plain text */ }
    const code = raw.trim();
    if (code) { setPagQrCode(code); setPagValor(''); setPagNeedValor(true); setPagScanErr(null); setPagMsg(null); }
    else       { setPagScanErr('No se pudo leer el contenido del QR.'); }
  }

  // Stable refs so scanner effects always call the latest parse functions
  const parseTransferQrRef = useRef(parseTransferQr);
  parseTransferQrRef.current = parseTransferQr;
  const parseMerchantQrRef = useRef(parseMerchantQr);
  parseMerchantQrRef.current = parseMerchantQr;

  // ── Env scanner lifecycle (html5-qrcode) ──────────────────────────────────
  // XPAY-390 — se agrega el guard `tab !== 'enviar'` (y `tab` a las deps):
  // si el usuario cambia de tab sin que este componente se desmonte (el
  // sistema de tabs es puramente condicional dentro del mismo render), el
  // efecto se reevalúa, la condición falla, y React ejecuta el cleanup de
  // la ejecución anterior (teardown → scanner.stop()/clear()) ANTES de
  // hacer nada más — libera la cámara aunque `envScanning` siga en `true`
  // en el estado. Sin este guard, cambiar de tab solo removía el <div>
  // contenedor del DOM (por el renderizado condicional de la pestaña) pero
  // NO detenía el MediaStream de la cámara.
  useEffect(() => {
    if (!envScanning || tab !== 'enviar') return;
    let done = false;
    const scanner = new Html5Qrcode('env-qr-reader');
    envScannerRef.current = scanner;

    const teardown = async () => {
      try { await scanner.stop(); } catch { /* ignore */ }
      try { scanner.clear(); } catch { /* ignore */ }
      envScannerRef.current = null;
    };

    void scanner.start(
      { facingMode: 'environment' },
      { fps: 10, qrbox: { width: 250, height: 250 } },
      (text) => {
        if (done) return;
        done = true;
        void teardown().then(() => { setEnvScanning(false); parseTransferQrRef.current(text); });
      },
      () => { /* per-frame decode miss — normal, ignored */ },
    ).catch(() => {
      if (done) return;
      done = true;
      void teardown().then(() => {
        setEnvScanning(false);
        setEnvScanErr('No se pudo abrir la cámara. Puedes pegar el código QR manualmente.');
      });
    });

    return () => { done = true; void teardown(); };
  }, [envScanning, tab]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Pag scanner lifecycle (html5-qrcode) ──────────────────────────────────
  // XPAY-390 — mismo guard/razón que el scanner de Enviar (ver comentario
  // arriba): libera la cámara al cambiar de tab, no solo al desmontar.
  useEffect(() => {
    if (!pagScanning || tab !== 'pagar') return;
    let done = false;
    const scanner = new Html5Qrcode('pag-qr-reader');
    pagScannerRef.current = scanner;

    const teardown = async () => {
      try { await scanner.stop(); } catch { /* ignore */ }
      try { scanner.clear(); } catch { /* ignore */ }
      pagScannerRef.current = null;
    };

    void scanner.start(
      { facingMode: 'environment' },
      { fps: 10, qrbox: { width: 250, height: 250 } },
      (text) => {
        if (done) return;
        done = true;
        void teardown().then(() => { setPagScanning(false); parseMerchantQrRef.current(text); });
      },
      () => { /* per-frame decode miss — normal, ignored */ },
    ).catch(() => {
      if (done) return;
      done = true;
      void teardown().then(() => {
        setPagScanning(false);
        setPagScanErr('No se pudo abrir la cámara. Puedes pegar el código QR manualmente.');
      });
    });

    return () => { done = true; void teardown(); };
  }, [pagScanning, tab]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Auto-inicio de escaneo al entrar a 'enviar'/'pagar', y liberación al
  //    salir (XPAY-390 R4/R5) ─────────────────────────────────────────────
  // Auto-inicio: sólo si aún no hay destino/QR resuelto y no se está
  // escaneando ya (guard evita reintentos en loop). Reutiliza exactamente
  // el mismo estado/efecto de escaneo que el botón manual usaba antes —
  // ningún handler financiero se modifica.
  useEffect(() => {
    if (tab === 'enviar' && !envDest && !envManual && !envScanning) {
      setEnvScanning(true);
    }
  }, [tab, envDest, envManual, envScanning]);

  useEffect(() => {
    if (tab === 'pagar' && !pagQrCode && !pagScanning) {
      setPagScanning(true);
    }
  }, [tab, pagQrCode, pagScanning]);

  // Liberación explícita del flag al abandonar el tab — el guard `tab !==`
  // dentro de cada efecto de scanner ya detiene la cámara real; esto sólo
  // mantiene el estado de React consistente con la realidad (no queda
  // "envScanning=true" fantasma mientras no hay cámara activa).
  useEffect(() => {
    if (tab !== 'enviar' && envScanning) setEnvScanning(false);
  }, [tab, envScanning]);

  useEffect(() => {
    if (tab !== 'pagar' && pagScanning) setPagScanning(false);
  }, [tab, pagScanning]);

  // ── Auto-generación del QR base de Recibir al entrar al tab (XPAY-390 R2/
  //    R3) — SIN backend, SIN Passport: mismo `generarQr(null)` ya usado por
  //    el botón manual. Guard `!recQrSrc` evita loops/regeneraciones
  //    innecesarias — una vez generado, no se vuelve a generar solo por
  //    reentrar al tab (se conserva el QR ya mostrado).
  useEffect(() => {
    if (tab === 'recibir' && user && miWallet && !recQrSrc && !recQrBusy) {
      void generarQr(null);
    }
  }, [tab, user, miWallet, recQrSrc, recQrBusy]); // eslint-disable-line react-hooks/exhaustive-deps

  // Si la wallet activa cambia (usuario distinto en la misma instancia
  // montada), el QR ya mostrado queda obsoleto — se invalida para que el
  // efecto de arriba lo regenere para la wallet correcta.
  useEffect(() => {
    if (!miWallet) return;
    if (recQrWalletIdRef.current !== null && recQrWalletIdRef.current !== miWallet.idWallet) {
      setRecQrSrc(null);
      setRecQrAmount(null);
    }
    recQrWalletIdRef.current = miWallet.idWallet;
  }, [miWallet]);

  // ── QR generation (Recibir) ───────────────────────────────────────────────
  // XPAY-390 — separado en dos funciones: `generarQr` es la operación pura
  // (mismo payload XPAY_TRANSFER, mismos campos type/env/version/
  // receiverUser/receiverWalletId/currency que ya existían — NUNCA se
  // cambia su forma, para mantener compatibilidad total con
  // parseTransferQr() de Enviar) y se usa tanto por el auto-generado al
  // entrar al tab (amount=null, sin backend/Passport) como por el botón
  // manual "Generar QR" (amount validado desde recValor).
  async function generarQr(amount: number | null) {
    if (!user || !miWallet) return;
    setRecQrBusy(true);
    try {
      const payload: XpayTransferQR = {
        type:             'XPAY_TRANSFER',
        env:              'QA',
        version:          1,
        receiverUser:     user.usuario,
        receiverWalletId: miWallet.idWallet,
        amount,
        currency:         'COP',
      };
      const dataUrl = await QRCode.toDataURL(JSON.stringify(payload), { width: 280, margin: 2, color: { dark: '#1a202c' } });
      setRecQrSrc(dataUrl);
      setRecQrAmount(amount);
    } finally { setRecQrBusy(false); }
  }

  async function handleGenerarQr() {
    const { amount, error } = parseRecValorAmount(recValor);
    if (error) { setRecValorErr(error); return; }
    setRecValorErr(null);
    await generarQr(amount);
  }

  function handleDescargarQr() {
    if (!recQrSrc || !user) return;
    const a = document.createElement('a');
    a.href = recQrSrc;
    a.download = `xpay-recibir-${user.usuario}.png`;
    a.click();
  }

  // ── Transfer handler ──────────────────────────────────────────────────────
  async function handleEnviar(e: FormEvent) {
    e.preventDefault();
    if (!miWallet) return;
    const destId = envManual ? Number(envManualDest) : envDest;
    if (!destId || destId < 1) { setEnvMsg({ ok: false, text: 'Wallet destino inválido.' }); return; }
    if (destId === miWallet.idWallet) { setEnvMsg({ ok: false, text: 'No puedes transferirte a tu propia wallet.' }); return; }
    const pinErr = validatePin(envPin);
    if (pinErr) { setEnvMsg({ ok: false, text: pinErr }); return; }
    const descripcion = envDestUser
      ? `Enviado a ${envDestUser} — Wallet #${destId}`
      : `Enviado a Wallet #${destId}`;
    // Fase 71.2-E-G: misma clave si es un reintento del mismo intento
    // (destino/valor/descripción sin cambios); clave nueva si algo cambió.
    if (!envIdemRef.current
      || envIdemRef.current.destId !== destId
      || envIdemRef.current.valor !== envValor
      || envIdemRef.current.descripcion !== descripcion) {
      envIdemRef.current = { key: crypto.randomUUID(), destId, valor: envValor, descripcion };
    }
    setEnvBusy(true); setEnvMsg(null); opInProgressRef.current = true;
    try {
      // Fase 71.2-E-C: idWalletOrigen/creadoPor ya no se envían — el backend
      // los resuelve desde el JWT (ver TransferenciaWalletRequest).
      const r = await post<{ success: boolean; message?: string }>('/api/wallets/transferencia', {
        idWalletDestino: destId,
        valor:           Number(envValor),
        descripcion,
      }, { 'Idempotency-Key': envIdemRef.current.key });
      if (r.success) {
        envIdemRef.current = null;
        await loadCuenta();
        // QA-WALLET-7A: limpiar el formulario para que no quede listo para
        // repetir el envío, mostrar confirmación aparte y navegar a
        // Movimientos tras una breve pausa.
        setEnvDest(null); setEnvDestUser(''); setEnvValor(''); setEnvNeedValor(false);
        setEnvPasted(''); setEnvManual(false); setEnvManualDest(''); setEnvScanErr(null);
        setEnvMsg(null);
        setEnvSuccessMsg(r.message ?? 'Transferencia realizada exitosamente.');
        if (envSuccessTimerRef.current) clearTimeout(envSuccessTimerRef.current);
        envSuccessTimerRef.current = window.setTimeout(() => {
          setEnvSuccessMsg(null);
          setTab('movimientos');
        }, 1800);
      } else {
        setEnvMsg({ ok: false, text: r.message ?? 'Error al transferir.' });
      }
    } catch (e) { setEnvMsg({ ok: false, text: (e as Error).message }); }
    finally { setEnvBusy(false); setEnvPin(''); opInProgressRef.current = false; }
  }

  // ── QR Payment handler ────────────────────────────────────────────────────
  async function handlePagarQr(e: FormEvent) {
    e.preventDefault();
    if (!miWallet || !pagQrCode) return;
    const pinErr = validatePin(pagPin);
    if (pinErr) { setPagMsg({ ok: false, text: pinErr }); return; }
    // Fase 71.2-E-G: misma clave si es un reintento del mismo intento
    // (código QR/valor sin cambios); clave nueva si algo cambió.
    if (!pagIdemRef.current || pagIdemRef.current.qrCode !== pagQrCode || pagIdemRef.current.valor !== pagValor) {
      pagIdemRef.current = { key: crypto.randomUUID(), qrCode: pagQrCode, valor: pagValor };
    }
    setPagBusy(true); setPagMsg(null); opInProgressRef.current = true;
    try {
      // Fase 71.2-E-D: idWalletUsuario/creadoPor ya no se envían — el backend
      // los resuelve desde el JWT (ver PagoQrRequest).
      const r = await post<{ success: boolean; message?: string }>('/api/qr/pagar', {
        codigoQr:    pagQrCode,
        valor:       Number(pagValor),
        descripcion: 'Pago a Comercio Demo XPAY QA',
      }, { 'Idempotency-Key': pagIdemRef.current.key });
      setPagMsg({ ok: r.success, text: r.message ?? (r.success ? 'Pago QR realizado.' : 'Error al pagar QR.') });
      if (r.success) { pagIdemRef.current = null; await loadCuenta(); }
    } catch (e) { setPagMsg({ ok: false, text: (e as Error).message }); }
    finally { setPagBusy(false); setPagPin(''); opInProgressRef.current = false; }
  }

  // ── KYC: iniciar verificación Veriff ─────────────────────────────────────
  async function handleIniciarVerificacion() {
    setKycBusy(true);
    setKycMsg(null);
    try {
      const r = await post<{
        success: boolean;
        data: { estadoKyc: string; sessionId: string; sessionUrl: string };
      }>('/api/kyc/veriff/session', {});
      if (r.success && r.data.sessionUrl) {
        setKycEstado('PENDIENTE');
        kycEstadoRef.current = 'PENDIENTE';
        setKycMsg({ ok: true, text: 'Verificación iniciada. Abriendo Veriff...' });
        const url = r.data.sessionUrl;
        window.setTimeout(() => {
          // Open Veriff in new tab so KYC polling continues on this page.
          // If browser blocks the popup, fall back to same-tab navigation.
          const tab = window.open(url, '_blank');
          if (!tab) window.location.href = url;
          else setKycBusy(false);
        }, 800);
      } else {
        setKycMsg({ ok: false, text: 'Error iniciando verificación. Intenta de nuevo.' });
        setKycBusy(false);
      }
    } catch (err) {
      setKycMsg({ ok: false, text: (err as Error).message || 'Error iniciando verificación.' });
      setKycBusy(false);
    }
  }

  // ── Bre-B handlers ────────────────────────────────────────────────────────
  async function handleSolicitarRetiro(e: FormEvent) {
    e.preventDefault();
    const val = Number(brebRetValor);
    if (!val || val <= 0) { setBrebRetMsg({ ok: false, text: 'Ingresa un valor válido.' }); return; }
    setBrebRetBusy(true); setBrebRetMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebRetiro; message?: string }>(
        '/api/breb/retiros/simular',
        { valor: val },
      );
      if (r.success && r.data) {
        setBrebRetiros(prev => [r.data!, ...prev]);
        setBrebRetValor('');
        setBrebRetMsg({ ok: true, text: `Retiro simulado creado. Ref: ${r.data.referenciaInterna} — ${r.data.estado}` });
      } else {
        setBrebRetMsg({ ok: false, text: r.message ?? 'Error creando retiro.' });
      }
    } catch (err) {
      setBrebRetMsg({ ok: false, text: (err as Error).message || 'Error creando retiro.' });
    } finally { setBrebRetBusy(false); }
  }

  // ── XPAY-375 — Retirar a mi llave Bre-B (REAL) ─────────────────────────
  //
  // "Verificar mi llave" — llama POST /api/breb/mi-llave/resolver. El
  // backend NUNCA persiste el valor en claro de la llave (XPAY-371) — por
  // eso este formulario pide reconfirmar el mismo valor ya registrado; el
  // backend lo valida por hash contra la llave activa de la wallet del
  // usuario ANTES de llamar a Passport, y rechaza cualquier valor que no
  // coincida — esto NO es "una llave arbitraria", es la reconfirmación de
  // la llave que el propio usuario ya registró.
  async function handleVerificarLlaveReal(e: FormEvent) {
    e.preventDefault();
    if (!realKeyValueInput.trim()) {
      setRealResolveMsg({ ok: false, text: 'Ingresa el valor de tu llave para confirmarla.' });
      return;
    }
    setRealResolveBusy(true);
    setRealResolveMsg(null);
    try {
      const r = await post<{ success: boolean; data?: LlaveResolveResult; message?: string }>(
        '/api/breb/mi-llave/resolver',
        { KeyValue: realKeyValueInput.trim() },
      );
      if (r.success && r.data) {
        setRealResolveResult(r.data);
        setRealKeyValueInput('');
        setRealResolveMsg({ ok: true, text: 'Llave verificada con Passport.' });
        await loadBreb();
      } else {
        setRealResolveMsg({ ok: false, text: r.message ?? 'No se pudo verificar la llave.' });
      }
    } catch (err) {
      // XPAY-375 FASE 5 — un error aquí (resolución vencida, error de
      // Passport, etc.) nunca debe intentar reintentar automáticamente ni
      // avanzar al Payment. El usuario decide si vuelve a intentar.
      setRealResolveMsg({ ok: false, text: (err as Error).message || 'No se pudo verificar la llave.' });
    } finally {
      setRealResolveBusy(false);
    }
  }

  // "Continuar" — sólo valida el monto localmente y avanza a la pantalla
  // de confirmación. NUNCA llama al backend — el Payment sólo puede
  // dispararse desde "Confirmar retiro" (FASE 4: el clic financiero debe
  // ser explícito y separado).
  function handleContinuarRetiroReal(e: FormEvent) {
    e.preventDefault();
    const val = Number(realMonto);
    if (!val || val <= 0) { setRealMontoErr('Ingresa un monto válido.'); return; }
    if (cuenta && val > cuenta.saldoDisponible) {
      setRealMontoErr(`El monto no puede superar tu saldo disponible (${fmtMoney(cuenta.saldoDisponible)}).`);
      return;
    }
    setRealMontoErr(null);
    setRealStep('confirmar');
  }

  // "Confirmar retiro" — ÚNICO punto del frontend que llama
  // POST /api/breb/retiros/real. El body sólo lleva Monto — account_id,
  // resolution_id, payment_id y la llave destino se resuelven 100%
  // server-side (BrebPaymentService, XPAY-373); este formulario no tiene
  // ningún campo para ninguno de ellos.
  // XPAY-377 FASE 4 — timeout propio, mayor al default (20s): esta llamada
  // puede incluir un viaje real a Passport del lado del servidor. 45s es
  // generoso mientras no exista un valor medido en producción — preferible
  // a dejarlo sin límite (el bug original).
  const CONFIRMAR_RETIRO_TIMEOUT_MS = 45_000;

  async function handleConfirmarRetiroReal() {
    if (realConfirmBusy) return; // protección contra doble clic — se mantiene intacta
    const val = Number(realMonto);
    if (!val || val <= 0) { setRealConfirmMsg({ ok: false, text: 'Monto inválido.' }); return; }
    setRealConfirmBusy(true);
    setRealConfirmMsg(null);
    setRealUncertain(false);
    try {
      const r = await post<{ success: boolean; data?: RetiroReal; message?: string; warning?: string }>(
        '/api/breb/retiros/real',
        { Monto: val },
        undefined,
        CONFIRMAR_RETIRO_TIMEOUT_MS,
      );
      if (r.success && r.data) {
        setRealRetiroResult(r.data);
        setRealConfirmMsg({ ok: true, text: r.warning ?? retiroRealMensaje(r.data.estado, r.data.motivoRechazo) });
        await loadCuenta();
      } else {
        setRealConfirmMsg({ ok: false, text: r.message ?? 'No se pudo procesar el retiro.' });
      }
    } catch (err) {
      if (err instanceof HttpUncertainError) {
        // FASE 4 — REQUISITO CRÍTICO: nunca decir "el retiro falló". El
        // servidor pudo haber recibido/creado la operación. Se sale de
        // "Procesando..." pero se entra a un estado propio de
        // incertidumbre, nunca a un mensaje de error normal.
        setRealUncertain(true);
      } else {
        setRealConfirmMsg({ ok: false, text: (err as Error).message || 'No se pudo procesar el retiro.' });
      }
    } finally {
      setRealConfirmBusy(false);
    }
  }

  // XPAY-377 FASE 5 — reconciliación segura tras un timeout: el cliente
  // NUNCA tuvo un id_breb_retiro (la respuesta que lo traía es
  // exactamente la que no llegó) — por eso se recarga la lista completa
  // de "mis retiros" (ya existente, GET /api/breb/mis-retiros) en vez de
  // pedir un id. El usuario ve si algo se creó, sin adivinar ningún
  // identificador.
  async function handleVerMisRetirosTrasIncertidumbre() {
    setRealCheckBusy(true);
    setRealCheckMsg(null);
    try {
      await loadBreb();
      setRealCheckMsg({ ok: true, text: 'Lista de retiros actualizada — revisa el más reciente abajo.' });
    } catch (err) {
      setRealCheckMsg({ ok: false, text: (err as Error).message || 'No se pudo consultar tus retiros.' });
    } finally {
      setRealCheckBusy(false);
    }
  }

  // XPAY-377 FASE 5 — consulta el estado de UN retiro propio ya conocido
  // (por su id local, nunca por payment_id) vía el nuevo endpoint
  // user-side. Idempotente: puede llamarse tantas veces como haga falta
  // sin riesgo de doble efecto financiero (BrebPaymentStateMachine ya lo
  // garantiza del lado del backend).
  async function handleConsultarEstadoRetiro(idBrebRetiro: number) {
    setRealCheckBusy(true);
    setRealCheckMsg(null);
    try {
      const r = await post<{ success: boolean; data?: RetiroReal; message?: string }>(
        `/api/breb/mis-retiros/${idBrebRetiro}/actualizar-estado`,
        {},
      );
      if (r.success && r.data) {
        setRealRetiroResult(r.data);
        setRealCheckMsg({ ok: true, text: retiroRealMensaje(r.data.estado, r.data.motivoRechazo) });
        await loadCuenta();
        await loadBreb();
      } else {
        setRealCheckMsg({ ok: false, text: r.message ?? 'No se pudo consultar el estado.' });
      }
    } catch (err) {
      setRealCheckMsg({ ok: false, text: (err as Error).message || 'No se pudo consultar el estado.' });
    } finally {
      setRealCheckBusy(false);
    }
  }

  function resetRetiroRealFlow() {
    setRealStep('monto');
    setRealMonto('');
    setRealMontoErr(null);
    setRealConfirmMsg(null);
    setRealRetiroResult(null);
    setRealUncertain(false);
    setRealCheckMsg(null);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  function resetEnviar() {
    setEnvDest(null); setEnvDestUser(''); setEnvValor(''); setEnvNeedValor(false);
    setEnvMsg(null); setEnvScanErr(null); setEnvPasted(''); setEnvManual(false); setEnvManualDest('');
    setEnvScanning(false);
    envIdemRef.current = null; // Fase 71.2-E-G: cambio material del formulario → clave nueva en el próximo intento
  }
  function resetPagar() {
    setPagQrCode(''); setPagValor(''); setPagNeedValor(false);
    setPagMsg(null); setPagScanErr(null); setPagPasted('');
    setPagScanning(false); setPagMetodoPago(null);
    pagIdemRef.current = null; // Fase 71.2-E-G: cambio material del formulario → clave nueva en el próximo intento
  }

  // ── Early return ──────────────────────────────────────────────────────────
  if (!user || miWalletLoading) {
    return (
      <div className="page">
        <h2>Mi Wallet</h2>
        <div className="loading">Cargando wallet...</div>
      </div>
    );
  }
  if (!miWallet) {
    return (
      <div className="page">
        <h2>Mi Wallet</h2>
        <div className="error-msg">No se encontró una wallet activa para tu usuario. Contacta al administrador.</div>
      </div>
    );
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="page">
      <h2>Mi Wallet</h2>

      {/* XPAY-415 — el encabezado técnico (Usuario/Wallet #/QA-Demo, la barra
          de auto-refresh visible y el badge "Verificación de identidad"
          repetido) se retira de esta vista: no aporta a la operación normal
          y esa misma información (identidad, actualización) ya vive en
          Perfil. La lógica de actualización automática (polling, loadCuenta,
          useEffect) sigue intacta y corriendo en segundo plano — solo se
          quita su indicador visual. */}

      {/* ── KYC status section ───────────────────────────────────────────── */}
      {/* XPAY-415A — corrección de alcance: XPAY-415 pedía únicamente retirar
          el texto técnico repetitivo ("Verificación de identidad:" y
          "Identidad verificada.") — NO crear una política nueva de
          visibilidad por estado KYC. Este bloque vuelve a renderizarse
          exactamente en las mismas condiciones que antes de XPAY-415 (mismo
          contenedor siempre presente, mismo badge siempre visible, mismas
          ramas pendiente/en revisión/canStart/kycMsg, mismos botones y
          handlers) — el único cambio real es la eliminación de esas dos
          líneas de texto, sin tocar cuándo se puede iniciar/reintentar
          Veriff ni ninguna acción necesaria para KYC no aprobado. */}
      {(() => {
        const canStart   = ['NO_INICIADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'].includes(kycEstado);
        const isPending  = kycEstado === 'PENDIENTE';
        const inReview   = kycEstado === 'EN_REVISION';
        return (
          <div className="kyc-status-bar">
            <span className={KYC_BADGE_CLASS[kycEstado] ?? 'kyc-badge kyc-badge-no-iniciado'}>
              {kycLabel(kycEstado)}
            </span>
            {inReview && <span className="kyc-nota">Tu verificación está en revisión.</span>}
            {isPending && (
              <>
                <span className="kyc-nota">
                  {fromVeriff
                    ? 'La verificación fue enviada. Veriff puede tardar unos minutos en confirmar el resultado.'
                    : <>Tu verificación está pendiente. Si ya terminaste en Veriff, toca <strong>Actualizar estado</strong>.</>}
                </span>
                <button
                  className="btn-kyc-start"
                  disabled={kycRefreshing}
                  onClick={() => void loadKyc()}
                >
                  {kycRefreshing ? 'Actualizando...' : 'Actualizar estado'}
                </button>
              </>
            )}
            {canStart && (
              <>
                <span className="kyc-nota">
                  En producción, esta wallet requerirá verificación de identidad aprobada.
                </span>
                <button
                  className="btn-kyc-start"
                  disabled={kycBusy}
                  onClick={() => void handleIniciarVerificacion()}
                >
                  {kycBusy ? 'Iniciando...' : 'Iniciar verificación'}
                </button>
              </>
            )}
            {kycMsg && (
              <span className={kycMsg.ok ? 'kyc-nota kyc-nota-aprobado' : 'kyc-nota kyc-nota-error'}>
                {kycMsg.text}
              </span>
            )}
          </div>
        );
      })()}

      {/* ── SALDO ─────────────────────────────────────────────────────────── */}
      {tab === 'saldo' && (
        loading ? (
          <div className="loading">Cargando saldo...</div>
        ) : dataErr ? (
          <div className="error-msg">
            {dataErr}{' '}
            <button className="retry-button" onClick={() => void loadCuenta()}>↺ Reintentar</button>
          </div>
        ) : cuenta ? (
          <>
            <HeroBalanceCard
              titulo={user.usuario}
              saldoFormateado={fmtMoney(cuenta.saldoDisponible)}
              estado={cuenta.estado}
            />
            {/* XPAY-375 FASE 7 — saldo retenido, visible siempre que exista
                cualquier reserva en curso (retiro Bre-B real en proceso).
                Valor siempre del backend, nunca calculado localmente. */}
            {cuenta.saldoRetenido > 0 && (
              <div className="wallet-held-balance-row" data-testid="saldo-retenido">
                <span>Saldo retenido (retiro en proceso)</span>
                <strong>{fmtMoney(cuenta.saldoRetenido)}</strong>
              </div>
            )}

            {/* XPAY-415 — "Últimos movimientos": mismos datos ya cargados en
                `cuenta.movimientos` (GET /api/reportes/mi-estado-cuenta, sin
                endpoint nuevo), mismo orden que entrega el backend (más
                reciente primero) y misma función descripcionVisible()/estilo
                de fila que el tab "Movimientos" — solo recortado a 10 y con
                jerarquía visual propia, separado de la tarjeta de saldo. */}
            <div className="wallet-recent-movements">
              <div className="wallet-recent-movements-header">
                <span className="wallet-movements-title">Últimos movimientos</span>
                {cuenta.movimientos.length > 0 && (
                  <button
                    type="button"
                    className="wallet-send-link-btn"
                    onClick={() => setTab('movimientos')}
                  >
                    Ver todos los movimientos
                  </button>
                )}
              </div>
              {cuenta.movimientos.length > 0 ? (
                <ul className="wallet-movements-list">
                  {cuenta.movimientos.slice(0, 10).map(m => (
                    <li key={m.idMovimiento} className="wallet-movement-item">
                      <div className="wallet-movement-main">
                        <span className="wallet-movement-desc">{descripcionVisible(m)}</span>
                        <span className={`wallet-movement-value${m.naturaleza === 'C' ? ' wallet-movement-value--credit' : ' wallet-movement-value--debit'}`}>
                          {m.naturaleza === 'C' ? '+' : '−'}{fmtMoney(m.valor)}
                        </span>
                      </div>
                      <div className="wallet-movement-meta">
                        <span className={`wallet-movement-badge${m.naturaleza === 'C' ? ' wallet-movement-badge--credit' : ' wallet-movement-badge--debit'}`}>
                          {m.tipoMovimiento}
                        </span>
                        <span className="wallet-movement-date">{fmtDate(m.fecha)}</span>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <div className="empty">Sin movimientos registrados.</div>
              )}
            </div>
          </>
        ) : null
      )}

      {/* ── RECIBIR ───────────────────────────────────────────────────────── */}
      {/* XPAY-390 R2/R3 — el QR base se genera automáticamente al entrar
          (ver efecto "Auto-generación del QR base de Recibir" más arriba);
          esta vista ya no requiere pulsar "Generar QR" para el caso sin
          monto. Se retira el párrafo introductorio — la jerarquía visual es
          ahora: título → QR (o estado de carga) → campo de valor opcional →
          botón. */}
      {tab === 'recibir' && (
        <div className="wallet-receive">
          <h3 className="wallet-receive-title">Recibir</h3>

          {recQrSrc ? (
            <div className="wallet-receive-qr-display">
              <img src={recQrSrc} alt="QR para recibir dinero" className="wallet-receive-qr-image" />
              <p className="wallet-receive-qr-caption">Este es tu QR para recibir dinero en XPAY</p>
              {recQrAmount != null && (
                <p className="wallet-receive-qr-subcaption">Con valor {fmtMoney(recQrAmount)}</p>
              )}
              <button className="wallet-receive-download-btn" onClick={handleDescargarQr}>
                ↓ Descargar QR PNG
              </button>
            </div>
          ) : (
            <div className="wallet-receive-qr-loading">Generando tu QR...</div>
          )}

          <label className="wallet-receive-field">
            <span className="wallet-receive-field-label">Valor opcional (COP)</span>
            <input
              type="number"
              className="wallet-receive-input"
              value={recValor}
              onChange={e => { setRecValor(e.target.value); setRecValorErr(null); }}
              placeholder="Ej. 10000 — vacío para QR sin valor fijo"
              min={0}
            />
          </label>
          {recValor && !recValorErr && Number.isFinite(Number(recValor)) && Number(recValor) > 0 && (
            <p className="wallet-receive-cop-preview">{fmtMoney(Number(recValor))}</p>
          )}
          {recValorErr && <p className="wallet-receive-error">{recValorErr}</p>}

          <button
            className="wallet-receive-generate-btn"
            onClick={() => void handleGenerarQr()}
            disabled={recQrBusy}
          >
            {recQrBusy ? 'Generando...' : 'Generar QR'}
          </button>
        </div>
      )}

      {/* ── ENVIAR DINERO ─────────────────────────────────────────────────── */}
      {tab === 'enviar' && (
        <div className="wallet-send">
          <h3 className="wallet-send-title">Enviar dinero</h3>

          {envSuccessMsg ? (
            <div className="wallet-send-msg wallet-send-msg--ok">{envSuccessMsg}</div>
          ) : (
          <>
          {/* XPAY-390 R4 — el escaneo se inicia automáticamente al entrar
              (ver efecto "Auto-inicio de escaneo" más arriba); ya no exige
              pulsar "Escanear QR" primero. Pegar contenido / ingresar
              destino manualmente se conservan íntegros (mismos handlers)
              como fallback secundario, colapsado dentro de "Otras
              opciones". */}
          {!envDest && !envManual && (
            <div className="wallet-send-scan">
              <p className="wallet-send-scan-title">Escanea el QR del receptor</p>

              {envScanning && <div id="env-qr-reader" className="qr-reader-container" />}

              {envScanning && (
                <button className="wallet-send-secondary-btn wallet-send-cancel-btn" onClick={() => setEnvScanning(false)}>
                  Cancelar escaneo
                </button>
              )}
              {!envScanning && (
                <button
                  className="wallet-send-scan-btn"
                  onClick={() => { setEnvScanErr(null); setEnvScanning(true); }}
                >
                  📷 {envScanErr ? 'Reintentar escaneo' : 'Activar cámara'}
                </button>
              )}
              {envScanErr && <div className="wallet-send-error">{envScanErr}</div>}

              <details className="wallet-send-other-options">
                <summary className="wallet-send-other-options-summary">Otras opciones</summary>
                <div className="wallet-send-paste-block">
                  <label className="wallet-send-field">
                    <span className="wallet-send-field-label">Pegar contenido del QR</span>
                    <textarea
                      className="wallet-send-textarea"
                      value={envPasted}
                      onChange={e => setEnvPasted(e.target.value)}
                      placeholder={'{"type":"XPAY_TRANSFER","env":"QA","receiverWalletId":3,...}'}
                      rows={3}
                    />
                  </label>
                  <div className="wallet-send-actions-row">
                    <button className="wallet-send-secondary-btn" disabled={!envPasted.trim()} onClick={() => parseTransferQr(envPasted.trim())}>
                      Usar QR pegado
                    </button>
                    <button className="wallet-send-link-btn" onClick={() => { setEnvScanning(false); setEnvManual(true); }}>
                      Ingresar destino manualmente →
                    </button>
                  </div>
                </div>
              </details>
            </div>
          )}

          {!envDest && envManual && (
            <div className="wallet-send-manual">
              <label className="wallet-send-field">
                <span className="wallet-send-field-label">ID de wallet destino</span>
                <input
                  type="number"
                  className="wallet-send-input"
                  value={envManualDest}
                  onChange={e => setEnvManualDest(e.target.value)}
                  min={1}
                  placeholder="Ej. 3"
                />
              </label>
              <div className="wallet-send-actions-row">
                <button
                  className="wallet-send-secondary-btn"
                  onClick={() => {
                    const id = Number(envManualDest);
                    if (!id || id < 1) { setEnvScanErr('ID de wallet inválido.'); return; }
                    if (miWallet && id === miWallet.idWallet) { setEnvScanErr('No puedes transferirte a tu propia wallet.'); return; }
                    setEnvDest(id); setEnvNeedValor(true); setEnvScanErr(null);
                  }}
                  disabled={!envManualDest}
                >
                  Confirmar destino
                </button>
                <button className="wallet-send-link-btn" onClick={() => { setEnvManual(false); setEnvScanErr(null); }}>
                  ← Volver a QR
                </button>
              </div>
              {envScanErr && <div className="wallet-send-error">{envScanErr}</div>}
            </div>
          )}

          {envDest && (
            <>
              <div className="wallet-send-confirmed">
                <span className="wallet-send-confirmed-badge">Destino confirmado</span>
                {' → '}Wallet #{envDest}
                {envDestUser && <span className="wallet-send-confirmed-user">({envDestUser})</span>}
                {' '}
                <button className="wallet-send-link-btn" onClick={resetEnviar}>✕ Cambiar</button>
              </div>

              <form className="wallet-send-form" onSubmit={e => void handleEnviar(e)}>
                <label className="wallet-send-field">
                  <span className="wallet-send-field-label">Valor a transferir (COP ficticio)</span>
                  <input
                    type="number"
                    className="wallet-send-input"
                    value={envValor}
                    onChange={e => setEnvValor(e.target.value)}
                    required
                    min={1}
                    placeholder={envNeedValor ? 'El QR no trae valor — ingresa el monto' : ''}
                  />
                </label>
                <label className="wallet-send-field">
                  <span className="wallet-send-field-label">
                    Clave de 7 dígitos
                    <span className="wallet-send-pin-hint"> — QA/Demo: solo se valida formato, no hay backend PIN en esta fase</span>
                  </span>
                  <input
                    type="password"
                    inputMode="numeric"
                    maxLength={7}
                    className="wallet-send-input"
                    value={envPin}
                    onChange={e => setEnvPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                    required
                    placeholder="·······"
                    autoComplete="off"
                  />
                </label>
                <button
                  className="wallet-send-submit-btn"
                  type="submit"
                  disabled={envBusy || !envValor || Number(envValor) < 1 || envPin.length !== 7}
                >
                  {envBusy ? 'Procesando...' : 'Enviar dinero'}
                </button>
              </form>
              {envMsg && (
                <div className={`wallet-send-msg${envMsg.ok ? ' wallet-send-msg--ok' : ' wallet-send-msg--err'}`}>
                  {envMsg.text}
                  {envMsg.ok && (
                    <button className="wallet-send-link-btn wallet-send-retry-link" onClick={resetEnviar}>
                      Realizar otra transferencia
                    </button>
                  )}
                </div>
              )}
            </>
          )}
          </>
          )}
        </div>
      )}

      {/* ── COMPRAR CON QR ───────────────────────────────────────────────── */}
      {/* XPAY-390 R5 — el escaneo se inicia automáticamente al entrar (ver
          efecto "Auto-inicio de escaneo" más arriba). Handlers/endpoint
          (POST /api/qr/pagar, parseMerchantQr) sin cambios — separado por
          diseño de Enviar (ver comentario en R4/handleEnviar). */}
      {tab === 'pagar' && (
        <div className="wallet-pay">
          <h3 className="wallet-pay-title">Comprar con QR</h3>

          {!pagQrCode && (
            <div className="wallet-pay-scan">
              <p className="wallet-pay-scan-title">Escanea el QR del comercio</p>

              {pagScanning && <div id="pag-qr-reader" className="qr-reader-container" />}

              {pagScanning && (
                <button className="wallet-pay-secondary-btn wallet-pay-cancel-btn" onClick={() => setPagScanning(false)}>
                  Cancelar escaneo
                </button>
              )}
              {!pagScanning && (
                <button
                  className="wallet-pay-scan-btn"
                  onClick={() => { setPagScanErr(null); setPagScanning(true); }}
                >
                  📷 {pagScanErr ? 'Reintentar escaneo' : 'Activar cámara'}
                </button>
              )}
              {pagScanErr && <div className="wallet-pay-error">{pagScanErr}</div>}

              <details className="wallet-pay-other-options">
                <summary className="wallet-pay-other-options-summary">Otras opciones</summary>
                <div className="wallet-pay-paste-block">
                  <label className="wallet-pay-field">
                    <span className="wallet-pay-field-label">Pegar código QR o contenido JSON</span>
                    <textarea
                      className="wallet-pay-textarea"
                      value={pagPasted}
                      onChange={e => setPagPasted(e.target.value)}
                      placeholder={`QR-DEMO-XPAY-QA-001\no\n{"type":"XPAY_MERCHANT_PAYMENT","env":"QA",...}`}
                      rows={3}
                    />
                  </label>
                  <button className="wallet-pay-secondary-btn" disabled={!pagPasted.trim()} onClick={() => parseMerchantQr(pagPasted.trim())}>
                    Usar código pegado
                  </button>
                </div>
              </details>
            </div>
          )}

          {pagQrCode && (
            <>
              <div className="wallet-pay-confirmed">
                <span className="wallet-pay-confirmed-badge">QR comercio leído</span>
                {' → '}<code className="wallet-pay-confirmed-code">{pagQrCode}</code>
                {' '}
                <button className="wallet-pay-link-btn" onClick={resetPagar}>✕ Cambiar</button>
              </div>

              {/* ── Selector de método de pago ─────────────────────── */}
              {!pagMetodoPago && (
                <div className="wallet-pay-method">
                  <p className="wallet-pay-method-title">¿Cómo quieres pagar?</p>
                  <div className="wallet-pay-method-options">
                    <button className="wallet-pay-method-btn wallet-pay-method-btn--wallet" onClick={() => setPagMetodoPago('wallet')}>
                      💳 Pagar con Wallet
                    </button>
                    <button
                      className="wallet-pay-method-btn wallet-pay-method-btn--cupo"
                      onClick={() => {
                        const params = new URLSearchParams({
                          tipo:    'COMPRA_COMERCIO',
                          valor:   pagValor || '',
                          qrCode:  pagQrCode,
                          origen:  'QR',
                        });
                        navigate(`/mi-wallet/cartera?${params.toString()}`);
                      }}
                    >
                      📊 Pagar con Cupo Ordinario
                    </button>
                  </div>
                  <p className="wallet-pay-method-note">
                    Cupo Ordinario: financiación en cuotas · sin débito inmediato a Wallet
                  </p>
                </div>
              )}

              {/* ── Flujo pago con Wallet ──────────────────────────── */}
              {pagMetodoPago === 'wallet' && (
                <>
                  <form className="wallet-pay-form" onSubmit={e => void handlePagarQr(e)}>
                    <label className="wallet-pay-field">
                      <span className="wallet-pay-field-label">Valor a pagar (COP ficticio)</span>
                      <input
                        type="number"
                        className="wallet-pay-input"
                        value={pagValor}
                        onChange={e => setPagValor(e.target.value)}
                        required
                        min={1}
                        placeholder={pagNeedValor ? 'El QR no trae valor — ingresa el monto' : ''}
                      />
                    </label>
                    <label className="wallet-pay-field">
                      <span className="wallet-pay-field-label">
                        Clave de 7 dígitos
                        <span className="wallet-pay-pin-hint"> — QA/Demo: solo se valida formato, no hay backend PIN en esta fase</span>
                      </span>
                      <input
                        type="password"
                        inputMode="numeric"
                        maxLength={7}
                        className="wallet-pay-input"
                        value={pagPin}
                        onChange={e => setPagPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                        required
                        placeholder="·······"
                        autoComplete="off"
                      />
                    </label>
                    <button
                      className="wallet-pay-submit-btn"
                      type="submit"
                      disabled={pagBusy || !pagValor || Number(pagValor) < 1 || pagPin.length !== 7}
                    >
                      {pagBusy ? 'Procesando...' : 'Pagar QR con Wallet'}
                    </button>
                    <button type="button" className="wallet-pay-secondary-btn wallet-pay-change-method-btn"
                      onClick={() => setPagMetodoPago(null)}>
                      ← Cambiar método
                    </button>
                  </form>
                  {pagMsg && (
                    <div className={`wallet-pay-msg${pagMsg.ok ? ' wallet-pay-msg--ok' : ' wallet-pay-msg--err'}`}>
                      {pagMsg.text}
                      {pagMsg.ok && (
                        <button className="wallet-pay-link-btn wallet-pay-retry-link" onClick={resetPagar}>
                          Realizar otro pago
                        </button>
                      )}
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </div>
      )}

      {/* ── MOVIMIENTOS ───────────────────────────────────────────────────── */}
      {tab === 'movimientos' && (
        <div className="wallet-movements" style={{ marginTop: '1.25rem' }}>
          {loading ? (
            <div className="loading">Cargando movimientos...</div>
          ) : dataErr ? (
            <div className="error-msg">
              {dataErr}{' '}
              <button className="retry-button" onClick={() => void loadCuenta()}>↺ Reintentar</button>
            </div>
          ) : cuenta && cuenta.movimientos.length > 0 ? (
            <>
              <div className="wallet-movements-title">Movimientos ({cuenta.movimientos.length})</div>
              <ul className="wallet-movements-list">
                {cuenta.movimientos.map(m => (
                  <li key={m.idMovimiento} className="wallet-movement-item">
                    <div className="wallet-movement-main">
                      <span className="wallet-movement-desc">{descripcionVisible(m)}</span>
                      <span className={`wallet-movement-value${m.naturaleza === 'C' ? ' wallet-movement-value--credit' : ' wallet-movement-value--debit'}`}>
                        {m.naturaleza === 'C' ? '+' : '−'}{fmtMoney(m.valor)}
                      </span>
                    </div>
                    <div className="wallet-movement-meta">
                      <span className={`wallet-movement-badge${m.naturaleza === 'C' ? ' wallet-movement-badge--credit' : ' wallet-movement-badge--debit'}`}>
                        {m.tipoMovimiento}
                      </span>
                      <span className="wallet-movement-date">{fmtDate(m.fecha)}</span>
                    </div>
                    <div className="wallet-movement-balance">Saldo después: {fmtMoney(m.saldoDespues)}</div>
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <div className="empty">Sin movimientos registrados.</div>
          )}
        </div>
      )}

      {/* ── RETIRAR A MI BANCO (Bre-B) ───────────────────────────────────── */}
      {tab === 'banco' && (
        <div className="breb-section">
          <span className="breb-sandbox-badge">Sandbox Passport — retiro simulado, sin dinero real</span>

          {brebLlaveLoad ? (
            <div className="loading">Cargando llave Bre-B...</div>
          ) : (
            <>
              {/* Estado de la llave */}
              <div className="breb-status-card">
                <div className="breb-status-row">
                  <span className="breb-status-label">Llave Bre-B:</span>
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
                {brebLlave?.fechaValidacion && (
                  <div style={{ fontSize: '0.75rem', color: '#718096' }}>
                    Validada: {fmtDate(brebLlave.fechaValidacion)}
                  </div>
                )}
              </div>

              {/* XPAY-392/392A — el formulario de registro/actualización de
                  la llave se movió a Perfil → Mi llave Bre-B
                  (BrebMyKeySection, mismo endpoint POST /api/breb/mi-llave
                  vía useMyBrebKey) — evita mantener dos formularios activos
                  para la misma asociación. Este tab conserva sin cambios el
                  estado (arriba) y el flujo simulado de retiro (abajo,
                  handleSolicitarRetiro). handleRegistrarLlave/brebKeyType/
                  brebKeyValue/brebRegBusy/brebRegMsg (código muerto tras la
                  extracción, sin ningún formulario que los invocara) fueron
                  eliminados de este archivo en XPAY-392A. */}
              <p className="breb-retiro-note">
                Gestiona el registro o la actualización de tu llave Bre-B desde{' '}
                <button
                  type="button"
                  className="wallet-send-link-btn"
                  onClick={() => navigate('/mi-wallet/perfil')}
                >
                  Perfil → Mi llave Bre-B
                </button>.
              </p>

              {/* Formulario retiro — solo si llave VALIDADA */}
              {brebLlave?.estado === 'VALIDADA' && (
                <form className="breb-retiro-form" onSubmit={(e) => void handleSolicitarRetiro(e)}>
                  <h4 style={{ margin: '0', fontSize: '0.88rem', color: '#2d3748' }}>Solicitar retiro</h4>
                  <p className="breb-retiro-note">
                    El retiro se enviará a: <strong>{brebLlave.keyType} · {brebLlave.keyValueMasked}</strong>
                  </p>
                  <label>
                    Valor a retirar (COP ficticio)
                    <input
                      type="number"
                      min="1"
                      step="1"
                      value={brebRetValor}
                      onChange={e => setBrebRetValor(e.target.value)}
                      placeholder="Ej: 50000"
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

              {brebLlave && brebLlave.estado !== 'VALIDADA' && (
                <p className="breb-retiro-note" style={{ marginTop: '0.75rem' }}>
                  Solo puedes solicitar retiros una vez que tu llave esté <strong>VALIDADA</strong>.
                  En QA: usa el endpoint admin <code>POST /api/breb/admin/simular-validacion-llave</code>.
                </p>
              )}

              {/* Historial de retiros */}
              {brebRetiros.length > 0 && (
                <>
                  <h4 style={{ margin: '1rem 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>Historial de retiros</h4>
                  <table className="breb-retiros-table">
                    <thead>
                      <tr>
                        <th>Ref</th>
                        <th>Valor</th>
                        <th>Estado</th>
                        <th>Llave</th>
                        <th>Fecha</th>
                      </tr>
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
            </>
          )}
        </div>
      )}

      {/* ── RETIRAR A MI LLAVE BRE-B (REAL — dinero real) ─────────────────── */}
      {/* XPAY-375 — deliberadamente separado del tab 'banco' (simulado) de
          arriba: el usuario nunca debe confundir ambos flujos. Nada aquí se
          ejecuta salvo que el propio usuario dispare cada acción. */}
      {tab === 'retirar-breb' && (
        <div className="breb-real-section">
          {brebLlaveLoad ? (
            <div className="loading">Cargando llave Bre-B...</div>
          ) : !brebLlave || brebLlave.keyType !== 'BCODE' ? (
            // XPAY-392 — CTA hacia Perfil (antes dirigía al tab 'banco', que
            // ya no tiene entrada en la franja verde desde XPAY-390). No
            // cambia la condición (misma regla: requiere una llave BCODE
            // apta), solo el mensaje/destino.
            <div className="breb-no-key-card">
              <p className="breb-retiro-note">Primero configura tu llave Bre-B.</p>
              <button
                type="button"
                className="btn-breb"
                onClick={() => navigate('/mi-wallet/perfil')}
              >
                Configurar mi llave Bre-B
              </button>
            </div>
          ) : (
            <>
              {/* Saldo de referencia — siempre desde `cuenta` (backend), nunca calculado aquí */}
              <div className="breb-real-saldo-row">
                <span>Saldo disponible</span>
                <strong>{cuenta ? fmtMoney(cuenta.saldoDisponible) : '—'}</strong>
              </div>
              {cuenta && cuenta.saldoRetenido > 0 && (
                <div className="breb-real-saldo-row">
                  <span>Saldo retenido</span>
                  <strong>{fmtMoney(cuenta.saldoRetenido)}</strong>
                </div>
              )}

              {/* Estado de la llave propia */}
              <div className="breb-status-card">
                <div className="breb-status-row">
                  <span className="breb-status-label">Mi llave:</span>
                  <span className="breb-key-masked">{brebLlave.keyType} · {brebLlave.keyValueMasked}</span>
                  <span className={`breb-badge breb-badge-${brebLlave.estado.toLowerCase().replace(/_/g, '-')}`}>
                    {brebLlave.estado.replace(/_/g, ' ')}
                  </span>
                </div>
                <div className="breb-real-verif-row">
                  {brebLlave.resolucionVerificadaPassport ? (
                    <span className="breb-msg-ok">Verificada realmente con Passport.</span>
                  ) : (
                    <span className="breb-real-verif-pendiente">Aún no verificada con Passport (esta sesión).</span>
                  )}
                </div>
              </div>

              {/* Verificar mi llave — única acción que llama a Passport real */}
              <form className="breb-form" onSubmit={(e) => void handleVerificarLlaveReal(e)}>
                <label>
                  Ingresa tu llave Bre-B para verificarla
                  <input
                    type="text"
                    value={realKeyValueInput}
                    onChange={e => setRealKeyValueInput(e.target.value)}
                    placeholder="Valor de tu llave BCODE"
                    autoComplete="off"
                  />
                </label>
                <button type="submit" className="btn-breb" disabled={realResolveBusy || !realKeyValueInput.trim()}>
                  {realResolveBusy ? 'Verificando con Passport...' : 'Verificar mi llave'}
                </button>
                {realResolveMsg && (
                  <span className={realResolveMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{realResolveMsg.text}</span>
                )}
              </form>

              {/* Resultado sanitizado de la verificación — SOLO datos reales del backend */}
              {realResolveResult && (
                <div className="breb-real-destino-card" data-testid="breb-real-destino">
                  <h4>Cuenta asociada a tu llave Bre-B</h4>
                  <dl className="breb-real-destino-list">
                    <dt>Titular</dt><dd>{realResolveResult.titularNombreMasked}</dd>
                    {realResolveResult.entidadFinanciera && (
                      <><dt>Institución</dt><dd>{realResolveResult.entidadFinanciera}</dd></>
                    )}
                    {realResolveResult.tipoCuenta && (
                      <><dt>Tipo de cuenta</dt><dd>{realResolveResult.tipoCuenta}</dd></>
                    )}
                    {realResolveResult.cuentaMasked && (
                      <><dt>Cuenta</dt><dd>{realResolveResult.cuentaMasked}</dd></>
                    )}
                    {realResolveResult.vigenteHasta && (
                      <><dt>Verificación vigente hasta</dt><dd>{fmtDate(realResolveResult.vigenteHasta)}</dd></>
                    )}
                  </dl>
                </div>
              )}

              {/* Monto — sólo visible una vez que hay una verificación de esta sesión */}
              {realResolveResult && realStep === 'monto' && !realRetiroResult && (
                <form className="breb-form" onSubmit={handleContinuarRetiroReal}>
                  <label>
                    Monto a retirar (COP)
                    <input
                      type="number"
                      min="1"
                      step="1"
                      value={realMonto}
                      onChange={e => { setRealMonto(e.target.value); setRealMontoErr(null); }}
                      placeholder="Ej: 5000"
                    />
                  </label>
                  {realMontoErr && <span className="breb-msg-err">{realMontoErr}</span>}
                  <button type="submit" className="btn-breb" disabled={!realMonto}>
                    Continuar
                  </button>
                </form>
              )}

              {/* Confirmación explícita — separada del ingreso de monto (FASE 4) */}
              {realResolveResult && realStep === 'confirmar' && !realRetiroResult && !realUncertain && (
                <div className="breb-real-confirm-card" data-testid="breb-real-confirm">
                  <h4>Confirma tu retiro</h4>
                  <dl className="breb-real-destino-list">
                    <dt>Retiras</dt><dd>{fmtMoney(Number(realMonto))}</dd>
                    <dt>Desde</dt><dd>Wallet XPAY</dd>
                    <dt>Hacia</dt><dd>Mi llave Bre-B {brebLlave.keyValueMasked}</dd>
                    {realResolveResult.cuentaMasked && (
                      <><dt>Cuenta destino</dt><dd>{realResolveResult.cuentaMasked}</dd></>
                    )}
                    {realResolveResult.entidadFinanciera && (
                      <><dt>Institución</dt><dd>{realResolveResult.entidadFinanciera}</dd></>
                    )}
                  </dl>
                  <p className="breb-confirm-text">
                    Esta acción moverá dinero real desde la cuenta operativa de XPAY hacia tu llave Bre-B.
                  </p>
                  <div className="breb-real-confirm-actions">
                    <button
                      type="button"
                      className="btn-breb btn-breb--financial"
                      disabled={realConfirmBusy}
                      onClick={() => void handleConfirmarRetiroReal()}
                    >
                      {realConfirmBusy ? 'Procesando...' : 'Confirmar retiro'}
                    </button>
                    <button
                      type="button"
                      className="wallet-send-link-btn"
                      disabled={realConfirmBusy}
                      onClick={() => setRealStep('monto')}
                    >
                      ← Cambiar monto
                    </button>
                  </div>
                  {realConfirmMsg && !realConfirmMsg.ok && (
                    <span className="breb-msg-err">
                      {realConfirmMsg.text}
                      {realConfirmMsg.text.includes('vencida') || realConfirmMsg.text.includes('resolver la llave nuevamente') ? (
                        <>
                          {' '}
                          <br />
                          Necesitamos verificar nuevamente tu llave antes de continuar.
                        </>
                      ) : null}
                    </span>
                  )}
                </div>
              )}

              {/* XPAY-377 FASE 4/5 — estado INCIERTO tras timeout del cliente.
                  Nunca "falló", nunca "tuvo éxito" — mensaje textual exigido
                  por el ticket, más la vía de reconciliación segura. */}
              {realUncertain && (
                <div className="breb-real-result-card breb-real-result--desconocido" data-testid="breb-real-uncertain">
                  <h4>No pudimos confirmar todavía el resultado de tu retiro</h4>
                  <p className="breb-confirm-text">
                    No vuelvas a intentarlo. Es posible que el servidor ya haya recibido la operación aunque tu
                    celular no recibió la respuesta a tiempo. Consulta el estado antes de realizar otra operación.
                  </p>
                  <button
                    type="button"
                    className="btn-breb"
                    disabled={realCheckBusy}
                    onClick={() => void handleVerMisRetirosTrasIncertidumbre()}
                  >
                    {realCheckBusy ? 'Consultando...' : 'Ver mis retiros'}
                  </button>
                  {realCheckMsg && (
                    <span className={realCheckMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{realCheckMsg.text}</span>
                  )}
                  {brebRetiros.length > 0 && (
                    <ul className="breb-real-uncertain-list">
                      {brebRetiros.slice(0, 3).map(r => (
                        <li key={r.idBrebRetiro}>
                          {fmtMoney(r.valor)} · {r.estado.replace(/_/g, ' ')} · {fmtDate(r.fechaSolicitud)}
                          {r.paymentIdFingerprint && r.paymentIdFingerprint !== 'ABSENT' &&
                            clasificarRetiroRealEstado(r.estado) === 'transitorio' && (
                            <button
                              type="button"
                              className="wallet-send-link-btn"
                              disabled={realCheckBusy}
                              onClick={() => void handleConsultarEstadoRetiro(r.idBrebRetiro)}
                            >
                              Consultar estado
                            </button>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}

              {/* Resultado del retiro — SIEMPRE según el estado real devuelto por backend */}
              {realRetiroResult && (
                <div
                  className={`breb-real-result-card breb-real-result--${clasificarRetiroRealEstado(realRetiroResult.estado)}`}
                  data-testid="breb-real-result"
                >
                  <h4>{retiroRealMensaje(realRetiroResult.estado, realRetiroResult.motivoRechazo)}</h4>
                  <dl className="breb-real-destino-list">
                    <dt>Monto</dt><dd>{fmtMoney(realRetiroResult.valor)}</dd>
                    <dt>Estado</dt><dd>{realRetiroResult.estado.replace(/_/g, ' ')}</dd>
                    <dt>Fecha de solicitud</dt><dd>{fmtDate(realRetiroResult.fechaSolicitud)}</dd>
                  </dl>
                  {clasificarRetiroRealEstado(realRetiroResult.estado) === 'transitorio' && (
                    <>
                      <p className="breb-retiro-note">
                        Este retiro sigue en proceso.
                      </p>
                      <button
                        type="button"
                        className="btn-breb"
                        disabled={realCheckBusy}
                        onClick={() => void handleConsultarEstadoRetiro(realRetiroResult.idBrebRetiro)}
                      >
                        {realCheckBusy ? 'Consultando...' : 'Consultar estado'}
                      </button>
                      {realCheckMsg && (
                        <span className={realCheckMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{realCheckMsg.text}</span>
                      )}
                    </>
                  )}
                  <button type="button" className="wallet-send-link-btn" onClick={resetRetiroRealFlow}>
                    Hacer otro retiro
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      )}

      <p className="user-wallet-footer">
        Ambiente QA/Demo · saldos y transacciones ficticios · sin dinero real · sin producción
      </p>

      {/* ── New movement toast ─────────────────────────────────────────────── */}
      {newMovMsg && (
        <div className="wallet-toast" role="alert">
          <span>{newMovMsg}</span>
          <button
            className="wallet-toast-close"
            onClick={() => setNewMovMsg(null)}
            aria-label="Cerrar"
          >
            ✕
          </button>
        </div>
      )}
    </div>
  );
}
