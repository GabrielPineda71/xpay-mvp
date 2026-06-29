import { FormEvent, useCallback, useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { Html5Qrcode } from 'html5-qrcode';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';

// QA/Demo mapping — temporary rule: username → wallet/user IDs
// Documented in docs/QA_DEMO_TRANSACTIONAL_USERS.md
const DEMO_MAP: Record<string, { idWallet: number; idUsuario: number; defaultDestWallet: number }> = {
  'qa.usuario1': { idWallet: 2, idUsuario: 3, defaultDestWallet: 3 },
  'qa.usuario2': { idWallet: 3, idUsuario: 4, defaultDestWallet: 2 },
};

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
  movimientos:     Movimiento[];
}

type Msg = { ok: boolean; text: string };
type Tab = 'saldo' | 'recibir' | 'enviar' | 'pagar' | 'movimientos' | 'banco';

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

// PIN: format-only validation for QA/Demo phase
// Full cryptographic validation (backend hash, attempt limits, lockout) is pending for production
function validatePin(pin: string): string | null {
  if (!/^\d{7}$/.test(pin)) return 'La clave debe ser exactamente 7 dígitos numéricos.';
  return null;
}

function fmtTime(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
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
  const demoInfo = user ? DEMO_MAP[user.usuario] : undefined;
  const [tab, setTab] = useState<Tab>('saldo');

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
  const [recValor,   setRecValor]   = useState('');
  const [recQrSrc,   setRecQrSrc]   = useState<string | null>(null);
  const [recQrBusy,  setRecQrBusy]  = useState(false);

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
  const envScannerRef = useRef<Html5Qrcode | null>(null);

  // ── KYC state ─────────────────────────────────────────────────────────────
  const [kycEstado,     setKycEstado]     = useState<string>('NO_INICIADO');
  const [kycBusy,       setKycBusy]       = useState(false);
  const [kycMsg,        setKycMsg]        = useState<Msg | null>(null);
  const [kycRefreshing, setKycRefreshing] = useState(false);
  const [fromVeriff,    setFromVeriff]    = useState(false);
  const kycEstadoRef = useRef<string>('NO_INICIADO');

  // ── Bre-B / Retirar a mi banco ───────────────────────────────────────────
  const [brebLlave,      setBrebLlave]      = useState<BrebLlave | null>(null);
  const [brebLlaveLoad,  setBrebLlaveLoad]  = useState(false);
  const [brebKeyType,    setBrebKeyType]    = useState('ID');
  const [brebKeyValue,   setBrebKeyValue]   = useState('');
  const [brebRegBusy,    setBrebRegBusy]    = useState(false);
  const [brebRegMsg,     setBrebRegMsg]     = useState<Msg | null>(null);
  const [brebRetiros,    setBrebRetiros]    = useState<BrebRetiro[]>([]);
  const [brebRetValor,   setBrebRetValor]   = useState('');
  const [brebRetBusy,    setBrebRetBusy]    = useState(false);
  const [brebRetMsg,     setBrebRetMsg]     = useState<Msg | null>(null);

  // ── Pagar comercio QR ─────────────────────────────────────────────────────
  const [pagQrCode,    setPagQrCode]    = useState('');
  const [pagValor,     setPagValor]     = useState('');
  const [pagNeedValor, setPagNeedValor] = useState(false);
  const [pagPin,       setPagPin]       = useState('');
  const [pagBusy,      setPagBusy]      = useState(false);
  const [pagMsg,       setPagMsg]       = useState<Msg | null>(null);
  const [pagPasted,    setPagPasted]    = useState('');
  const [pagScanning,  setPagScanning]  = useState(false);
  const [pagScanErr,   setPagScanErr]   = useState<string | null>(null);
  const pagScannerRef = useRef<Html5Qrcode | null>(null);

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

  // ── Initial load ──────────────────────────────────────────────────────────
  const loadCuenta = useCallback(async () => {
    if (!demoInfo) return;
    setLoading(true); setDataErr(null);
    try {
      const r = await get<{ success: boolean; data: EstadoCuenta }>(
        `/api/reportes/wallet/${demoInfo.idWallet}/estado-cuenta`,
      );
      setCuenta(r.data);
      setLastUpdated(new Date());
      // Establish baseline for new-movement detection
      lastKnownMovIdRef.current = r.data.movimientos[0]?.idMovimiento ?? -1;
    } catch (e) { setDataErr((e as Error).message); }
    finally { setLoading(false); }
  }, [demoInfo]);

  // ── Silent background refresh (polling) ───────────────────────────────────
  const pollRefresh = useCallback(async () => {
    if (!demoInfo) return;
    if (opInProgressRef.current) return; // skip during financial transactions
    setRefreshing(true);
    try {
      const r = await get<{ success: boolean; data: EstadoCuenta }>(
        `/api/reportes/wallet/${demoInfo.idWallet}/estado-cuenta`,
      );
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
  }, [demoInfo]);

  useEffect(() => { void loadCuenta(); void loadKyc(); void loadBreb(); }, [loadCuenta, loadKyc, loadBreb]);

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
      if (p.receiverWalletId === demoInfo?.idWallet)
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
  useEffect(() => {
    if (!envScanning) return;
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
  }, [envScanning]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Pag scanner lifecycle (html5-qrcode) ──────────────────────────────────
  useEffect(() => {
    if (!pagScanning) return;
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
  }, [pagScanning]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── QR generation (Recibir) ───────────────────────────────────────────────
  async function handleGenerarQr() {
    if (!user || !demoInfo) return;
    setRecQrBusy(true);
    try {
      const payload: XpayTransferQR = {
        type:             'XPAY_TRANSFER',
        env:              'QA',
        version:          1,
        receiverUser:     user.usuario,
        receiverWalletId: demoInfo.idWallet,
        amount:           recValor ? Number(recValor) : null,
        currency:         'COP',
      };
      const dataUrl = await QRCode.toDataURL(JSON.stringify(payload), { width: 280, margin: 2, color: { dark: '#1a202c' } });
      setRecQrSrc(dataUrl);
    } finally { setRecQrBusy(false); }
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
    if (!demoInfo) return;
    const destId = envManual ? Number(envManualDest) : envDest;
    if (!destId || destId < 1) { setEnvMsg({ ok: false, text: 'Wallet destino inválido.' }); return; }
    if (destId === demoInfo.idWallet) { setEnvMsg({ ok: false, text: 'No puedes transferirte a tu propia wallet.' }); return; }
    const pinErr = validatePin(envPin);
    if (pinErr) { setEnvMsg({ ok: false, text: pinErr }); return; }
    setEnvBusy(true); setEnvMsg(null); opInProgressRef.current = true;
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/wallets/transferencia', {
        idWalletOrigen:  demoInfo.idWallet,
        idWalletDestino: destId,
        valor:           Number(envValor),
        descripcion:     envDestUser
          ? `Enviado a ${envDestUser} — Wallet #${destId}`
          : `Enviado a Wallet #${destId}`,
        creadoPor:       demoInfo.idUsuario,
      });
      setEnvMsg({ ok: r.success, text: r.message ?? (r.success ? 'Transferencia realizada.' : 'Error al transferir.') });
      if (r.success) { await loadCuenta(); }
    } catch (e) { setEnvMsg({ ok: false, text: (e as Error).message }); }
    finally { setEnvBusy(false); setEnvPin(''); opInProgressRef.current = false; }
  }

  // ── QR Payment handler ────────────────────────────────────────────────────
  async function handlePagarQr(e: FormEvent) {
    e.preventDefault();
    if (!demoInfo || !pagQrCode) return;
    const pinErr = validatePin(pagPin);
    if (pinErr) { setPagMsg({ ok: false, text: pinErr }); return; }
    setPagBusy(true); setPagMsg(null); opInProgressRef.current = true;
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/qr/pagar', {
        codigoQr:        pagQrCode,
        idWalletUsuario: demoInfo.idWallet,
        valor:           Number(pagValor),
        descripcion:     'Pago a Comercio Demo XPAY QA',
        creadoPor:       demoInfo.idUsuario,
      });
      setPagMsg({ ok: r.success, text: r.message ?? (r.success ? 'Pago QR realizado.' : 'Error al pagar QR.') });
      if (r.success) { await loadCuenta(); }
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
  async function handleRegistrarLlave(e: FormEvent) {
    e.preventDefault();
    if (!brebKeyValue.trim()) { setBrebRegMsg({ ok: false, text: 'Ingresa el valor de la llave.' }); return; }
    setBrebRegBusy(true); setBrebRegMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebLlave; message?: string }>(
        '/api/breb/mi-llave',
        { keyType: brebKeyType, keyValue: brebKeyValue.trim() },
      );
      if (r.success && r.data) {
        setBrebLlave(r.data);
        setBrebKeyValue('');
        setBrebRegMsg({ ok: true, text: `Llave registrada: ${r.data.keyValueMasked} — estado: ${r.data.estado}` });
      } else {
        setBrebRegMsg({ ok: false, text: r.message ?? 'Error registrando llave.' });
      }
    } catch (err) {
      setBrebRegMsg({ ok: false, text: (err as Error).message || 'Error registrando llave.' });
    } finally { setBrebRegBusy(false); }
  }

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

  // ── Helpers ───────────────────────────────────────────────────────────────
  function resetEnviar() {
    setEnvDest(null); setEnvDestUser(''); setEnvValor(''); setEnvNeedValor(false);
    setEnvMsg(null); setEnvScanErr(null); setEnvPasted(''); setEnvManual(false); setEnvManualDest('');
    setEnvScanning(false);
  }
  function resetPagar() {
    setPagQrCode(''); setPagValor(''); setPagNeedValor(false);
    setPagMsg(null); setPagScanErr(null); setPagPasted('');
    setPagScanning(false);
  }

  // ── Early return ──────────────────────────────────────────────────────────
  if (!user || !demoInfo) {
    return (
      <div className="page">
        <h2>Mi Wallet</h2>
        <div className="error-msg">Usuario no reconocido en el mapa demo QA. Contacta al administrador.</div>
      </div>
    );
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="page">
      <h2>Mi Wallet</h2>
      <p className="dashboard-subtitle">
        Usuario: <strong>{user.usuario}</strong>
        {' · '}Wallet #{demoInfo.idWallet}
        {' · '}<span className="badge badge-info">QA / Demo</span>
        {cuenta && !loading && (
          <span style={{ marginLeft: '1rem', color: '#276749', fontWeight: 700 }}>
            {fmtMoney(cuenta.saldoDisponible)}
          </span>
        )}
      </p>

      {/* ── Auto-refresh status bar ──────────────────────────────────────── */}
      <div className="wallet-refresh-bar">
        <span className="refresh-label">
          {refreshing ? '● Actualizando...' : '↻ Actualización automática activa'}
        </span>
        {lastUpdated && !refreshing && (
          <span className="refresh-time">Última actualización: {fmtTime(lastUpdated)}</span>
        )}
        <button
          className="btn-refresh-now"
          onClick={() => void loadCuenta()}
          disabled={loading || refreshing}
        >
          Actualizar ahora
        </button>
        {refreshErr && !loading && (
          <span className="refresh-err">{refreshErr}</span>
        )}
      </div>

      {/* ── KYC status section ───────────────────────────────────────────── */}
      {(() => {
        const canStart   = ['NO_INICIADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'].includes(kycEstado);
        const isPending  = kycEstado === 'PENDIENTE';
        const inReview   = kycEstado === 'EN_REVISION';
        const approved   = kycEstado === 'APROBADO';
        return (
          <div className="kyc-status-bar">
            <span className="kyc-label">Verificación de identidad:</span>
            <span className={KYC_BADGE_CLASS[kycEstado] ?? 'kyc-badge kyc-badge-no-iniciado'}>
              {kycLabel(kycEstado)}
            </span>
            {approved && <span className="kyc-nota kyc-nota-aprobado">Identidad verificada.</span>}
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

      {/* Tab navigation */}
      <div className="wallet-tabs">
        {(['saldo', 'recibir', 'enviar', 'pagar', 'movimientos', 'banco'] as Tab[]).map(t => {
          const labels: Record<Tab, string> = {
            saldo: 'Mi Saldo', recibir: 'Recibir', enviar: 'Enviar',
            pagar: 'Pagar QR', movimientos: 'Movimientos', banco: 'Retirar a mi banco',
          };
          return (
            <button
              key={t}
              className={`wallet-tab-btn${tab === t ? ' wallet-tab-btn--active' : ''}`}
              onClick={() => {
                setTab(t);
                if (t !== 'enviar') setEnvScanning(false);
                if (t !== 'pagar')  setPagScanning(false);
              }}
            >
              {labels[t]}
            </button>
          );
        })}
      </div>

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
            <div className="cards" style={{ marginTop: '1.25rem' }}>
              <div className="card">
                <div className="card-label">Saldo disponible</div>
                <div className="card-value" style={{ color: '#276749' }}>{fmtMoney(cuenta.saldoDisponible)}</div>
              </div>
              <div className="card" style={{ borderLeftColor: '#a0aec0' }}>
                <div className="card-label">Wallet</div>
                <div className="card-value" style={{ fontSize: '0.9rem', color: '#4a5568' }}>{cuenta.nombreWallet}</div>
              </div>
              <div className="card" style={{ borderLeftColor: cuenta.estado === 'ACTIVA' ? '#68d391' : '#fc8181' }}>
                <div className="card-label">Estado</div>
                <div className="card-value">
                  <span className={`badge ${cuenta.estado === 'ACTIVA' ? 'badge-ok' : 'badge-warn'}`}>{cuenta.estado}</span>
                </div>
              </div>
            </div>
            {cuenta.movimientos.length > 0 && (
              <div style={{ marginTop: '1rem', fontSize: '0.82rem', color: '#718096' }}>
                Último movimiento: {fmtDate(cuenta.movimientos[0].fecha)} — {cuenta.movimientos[0].tipoMovimiento}
              </div>
            )}
          </>
        ) : null
      )}

      {/* ── RECIBIR DINERO ────────────────────────────────────────────────── */}
      {tab === 'recibir' && (
        <div className="action-section" style={{ marginTop: '1.25rem', maxWidth: '420px' }}>
          <h3>Recibir dinero</h3>
          <p className="tab-hint">
            Genera un QR para que otra persona te transfiera. El receptor muestra este QR al emisor.
          </p>
          <label>
            Valor a recibir (opcional — COP ficticio)
            <input
              type="number"
              value={recValor}
              onChange={e => { setRecValor(e.target.value); setRecQrSrc(null); }}
              placeholder="Dejar vacío si el emisor elige el monto"
              min={0}
            />
          </label>
          <button
            className="btn-confirm"
            onClick={() => void handleGenerarQr()}
            disabled={recQrBusy}
            style={{ marginTop: '0.5rem' }}
          >
            {recQrBusy ? 'Generando...' : 'Generar QR'}
          </button>

          {recQrSrc && (
            <div className="qr-display">
              <img src={recQrSrc} alt="QR para recibir dinero" className="qr-image" />
              <p className="qr-caption">
                {recValor
                  ? `QR con valor ${fmtMoney(Number(recValor))} (COP ficticio)`
                  : 'QR sin valor fijo — el emisor ingresa el monto'}
              </p>
              <button className="btn-secondary" onClick={handleDescargarQr}>
                ↓ Descargar QR PNG
              </button>
            </div>
          )}

          <p className="tab-warn">
            QA/Demo · el QR contiene type=XPAY_TRANSFER, receiverWalletId={demoInfo.idWallet} ·
            sin dinero real · sin producción.
          </p>
        </div>
      )}

      {/* ── ENVIAR DINERO ─────────────────────────────────────────────────── */}
      {tab === 'enviar' && (
        <div className="action-section" style={{ marginTop: '1.25rem', maxWidth: '500px' }}>
          <h3>Enviar dinero</h3>
          <p className="tab-hint">Escanea el QR del receptor, pega su contenido, o ingresa el ID de wallet.</p>

          {!envDest && !envManual && (
            <div className="scan-section">
              {envScanning && <div id="env-qr-reader" className="qr-reader-container" />}

              {!envScanning && (
                <>
                  <button
                    className="btn-scan"
                    onClick={() => { setEnvScanErr(null); setEnvScanning(true); }}
                  >
                    📷 Escanear QR
                  </button>
                  <div style={{ marginTop: '0.75rem' }}>
                    <label>
                      Pegar contenido del QR
                      <textarea
                        className="qr-paste-area"
                        value={envPasted}
                        onChange={e => setEnvPasted(e.target.value)}
                        placeholder={'{"type":"XPAY_TRANSFER","env":"QA","receiverWalletId":3,...}'}
                        rows={3}
                      />
                    </label>
                    <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                      <button className="btn-secondary" disabled={!envPasted.trim()} onClick={() => parseTransferQr(envPasted.trim())}>
                        Usar QR pegado
                      </button>
                      <button className="link-btn" style={{ color: '#4a5568' }} onClick={() => setEnvManual(true)}>
                        Ingresar destino manualmente →
                      </button>
                    </div>
                  </div>
                </>
              )}
              {envScanning && (
                <button className="btn-secondary" style={{ marginTop: '0.5rem' }} onClick={() => setEnvScanning(false)}>
                  Cancelar escaneo
                </button>
              )}
              {envScanErr && <div className="error-msg" style={{ marginTop: '0.5rem' }}>{envScanErr}</div>}
            </div>
          )}

          {!envDest && envManual && (
            <div className="qr-manual-entry">
              <label>
                ID de wallet destino
                <input
                  type="number"
                  value={envManualDest}
                  onChange={e => setEnvManualDest(e.target.value)}
                  min={1}
                  placeholder="Ej. 3"
                />
              </label>
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <button
                  className="btn-secondary"
                  onClick={() => {
                    const id = Number(envManualDest);
                    if (!id || id < 1) { setEnvScanErr('ID de wallet inválido.'); return; }
                    if (id === demoInfo.idWallet) { setEnvScanErr('No puedes transferirte a tu propia wallet.'); return; }
                    setEnvDest(id); setEnvNeedValor(true); setEnvScanErr(null);
                  }}
                  disabled={!envManualDest}
                >
                  Confirmar destino
                </button>
                <button className="link-btn" style={{ color: '#4a5568' }} onClick={() => { setEnvManual(false); setEnvScanErr(null); }}>
                  ← Volver a QR
                </button>
              </div>
              {envScanErr && <div className="error-msg" style={{ marginTop: '0.5rem' }}>{envScanErr}</div>}
            </div>
          )}

          {envDest && (
            <>
              <div className="qr-parsed">
                <span className="badge badge-ok">Destino confirmado</span>
                {' → '}Wallet #{envDest}
                {envDestUser && <span style={{ marginLeft: '0.5rem', color: '#4a5568' }}>({envDestUser})</span>}
                {' '}
                <button className="link-btn" onClick={resetEnviar}>✕ Cambiar</button>
              </div>

              <form className="action-form" onSubmit={e => void handleEnviar(e)}>
                <label>
                  Valor a transferir (COP ficticio)
                  <input
                    type="number"
                    value={envValor}
                    onChange={e => setEnvValor(e.target.value)}
                    required
                    min={1}
                    placeholder={envNeedValor ? 'El QR no trae valor — ingresa el monto' : ''}
                  />
                </label>
                <label>
                  Clave de 7 dígitos
                  <span className="pin-hint"> — QA/Demo: solo se valida formato, no hay backend PIN en esta fase</span>
                  <input
                    type="password"
                    inputMode="numeric"
                    maxLength={7}
                    value={envPin}
                    onChange={e => setEnvPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                    required
                    placeholder="·······"
                    autoComplete="off"
                  />
                </label>
                <button
                  className="btn-confirm"
                  type="submit"
                  disabled={envBusy || !envValor || Number(envValor) < 1 || envPin.length !== 7}
                >
                  {envBusy ? 'Procesando...' : 'Enviar dinero'}
                </button>
              </form>
              {envMsg && (
                <div className={envMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.75rem' }}>
                  {envMsg.text}
                  {envMsg.ok && (
                    <button className="link-btn" style={{ display: 'block', marginTop: '0.5rem', color: '#276749' }} onClick={resetEnviar}>
                      Realizar otra transferencia
                    </button>
                  )}
                </div>
              )}
            </>
          )}

          <p className="tab-warn">
            QA/Demo · transferencia ficticia · sin dinero real ·
            Escanear QR solo rellena datos — la transferencia NO ocurre hasta confirmar.
          </p>
        </div>
      )}

      {/* ── PAGAR COMERCIO QR ─────────────────────────────────────────────── */}
      {tab === 'pagar' && (
        <div className="action-section" style={{ marginTop: '1.25rem', maxWidth: '500px' }}>
          <h3>Pagar comercio QR</h3>
          <p className="tab-hint">Escanea el QR del comercio o pega el código / contenido JSON.</p>

          {!pagQrCode && (
            <div className="scan-section">
              {pagScanning && <div id="pag-qr-reader" className="qr-reader-container" />}

              {!pagScanning && (
                <>
                  <button
                    className="btn-scan"
                    onClick={() => { setPagScanErr(null); setPagScanning(true); }}
                  >
                    📷 Escanear QR del comercio
                  </button>
                  <div style={{ marginTop: '0.75rem' }}>
                    <label>
                      Pegar código QR o contenido JSON
                      <textarea
                        className="qr-paste-area"
                        value={pagPasted}
                        onChange={e => setPagPasted(e.target.value)}
                        placeholder={`QR-DEMO-XPAY-QA-001\no\n{"type":"XPAY_MERCHANT_PAYMENT","env":"QA",...}`}
                        rows={3}
                      />
                    </label>
                    <button className="btn-secondary" disabled={!pagPasted.trim()} onClick={() => parseMerchantQr(pagPasted.trim())}>
                      Usar código pegado
                    </button>
                  </div>
                </>
              )}
              {pagScanning && (
                <button className="btn-secondary" style={{ marginTop: '0.5rem' }} onClick={() => setPagScanning(false)}>
                  Cancelar escaneo
                </button>
              )}
              {pagScanErr && <div className="error-msg" style={{ marginTop: '0.5rem' }}>{pagScanErr}</div>}
            </div>
          )}

          {pagQrCode && (
            <>
              <div className="qr-parsed">
                <span className="badge badge-ok">QR comercio leído</span>
                {' → '}<code style={{ fontSize: '0.85rem' }}>{pagQrCode}</code>
                {' '}
                <button className="link-btn" onClick={resetPagar}>✕ Cambiar</button>
              </div>

              <form className="action-form" onSubmit={e => void handlePagarQr(e)}>
                <label>
                  Valor a pagar (COP ficticio)
                  <input
                    type="number"
                    value={pagValor}
                    onChange={e => setPagValor(e.target.value)}
                    required
                    min={1}
                    placeholder={pagNeedValor ? 'El QR no trae valor — ingresa el monto' : ''}
                  />
                </label>
                <label>
                  Clave de 7 dígitos
                  <span className="pin-hint"> — QA/Demo: solo se valida formato, no hay backend PIN en esta fase</span>
                  <input
                    type="password"
                    inputMode="numeric"
                    maxLength={7}
                    value={pagPin}
                    onChange={e => setPagPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                    required
                    placeholder="·······"
                    autoComplete="off"
                  />
                </label>
                <button
                  className="btn-confirm"
                  type="submit"
                  disabled={pagBusy || !pagValor || Number(pagValor) < 1 || pagPin.length !== 7}
                >
                  {pagBusy ? 'Procesando...' : 'Pagar QR'}
                </button>
              </form>
              {pagMsg && (
                <div className={pagMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.75rem' }}>
                  {pagMsg.text}
                  {pagMsg.ok && (
                    <button className="link-btn" style={{ display: 'block', marginTop: '0.5rem', color: '#276749' }} onClick={resetPagar}>
                      Realizar otro pago
                    </button>
                  )}
                </div>
              )}
            </>
          )}

          <p className="tab-warn">
            QA/Demo · pago ficticio · sin dinero real ·
            Escanear QR solo rellena datos — el pago NO ocurre hasta confirmar.
          </p>
        </div>
      )}

      {/* ── MOVIMIENTOS ───────────────────────────────────────────────────── */}
      {tab === 'movimientos' && (
        <div className="table-wrapper" style={{ marginTop: '1.25rem' }}>
          {loading ? (
            <div className="loading">Cargando movimientos...</div>
          ) : dataErr ? (
            <div className="error-msg">
              {dataErr}{' '}
              <button className="retry-button" onClick={() => void loadCuenta()}>↺ Reintentar</button>
            </div>
          ) : cuenta && cuenta.movimientos.length > 0 ? (
            <>
              <div className="table-title">Movimientos ({cuenta.movimientos.length})</div>
              <table>
                <thead>
                  <tr>
                    <th>Tipo</th><th>Valor</th><th>Saldo después</th><th>Descripción</th><th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {cuenta.movimientos.map(m => (
                    <tr key={m.idMovimiento}>
                      <td>
                        <span className={`badge ${m.naturaleza === 'C' ? 'badge-ok' : 'badge-warn'}`}>
                          {m.tipoMovimiento}
                        </span>
                      </td>
                      <td className={m.naturaleza === 'C' ? 'credit' : 'debit'}>
                        {m.naturaleza === 'C' ? '+' : '−'}{fmtMoney(m.valor)}
                      </td>
                      <td>{fmtMoney(m.saldoDespues)}</td>
                      <td>{descripcionVisible(m)}</td>
                      <td className="mono">{fmtDate(m.fecha)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
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

              {/* Formulario registro de llave */}
              <h4 style={{ margin: '0 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>
                {brebLlave ? 'Actualizar llave Bre-B' : 'Registrar llave Bre-B'}
              </h4>
              <form className="breb-form" onSubmit={(e) => void handleRegistrarLlave(e)}>
                <label>
                  Tipo de llave
                  <select value={brebKeyType} onChange={e => setBrebKeyType(e.target.value)}>
                    <option value="ID">Cédula / ID</option>
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
                    placeholder={
                      brebKeyType === 'ID'    ? 'Ej: 1234567890' :
                      brebKeyType === 'PHONE' ? 'Ej: 3001234567' :
                      brebKeyType === 'EMAIL' ? 'Ej: correo@banco.com' :
                      brebKeyType === 'ALPHA' ? 'Ej: mi-alias-breb' : 'Ej: BREB-XXXXXX'
                    }
                  />
                </label>
                <p className="breb-confirm-text">
                  Al registrar confirmas que esta llave Bre-B te pertenece y corresponde a tu cuenta bancaria.
                  No se puede retirar a llaves de terceros.
                </p>
                <button type="submit" className="btn-breb" disabled={brebRegBusy || !brebKeyValue.trim()}>
                  {brebRegBusy ? 'Registrando...' : brebLlave ? 'Actualizar llave' : 'Registrar llave'}
                </button>
                {brebRegMsg && (
                  <span className={brebRegMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{brebRegMsg.text}</span>
                )}
              </form>

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
