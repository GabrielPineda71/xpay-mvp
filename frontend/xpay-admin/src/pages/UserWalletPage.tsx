import { FormEvent, useCallback, useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';

// BarcodeDetector — Web API, not yet in TS stdlib (Chrome/Edge/Android/Safari 17+)
interface BarcodeResult { rawValue: string }
declare class BarcodeDetector {
  constructor(opts: { formats: string[] });
  detect(src: HTMLVideoElement): Promise<BarcodeResult[]>;
}

// QA/Demo mapping — temporary rule: username → wallet/user IDs
// Documented in docs/QA_DEMO_TRANSACTIONAL_USERS.md
const DEMO_MAP: Record<string, { idWallet: number; idUsuario: number; defaultDestWallet: number }> = {
  'qa.usuario1': { idWallet: 2, idUsuario: 3, defaultDestWallet: 3 },
  'qa.usuario2': { idWallet: 3, idUsuario: 4, defaultDestWallet: 2 },
};

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
}

interface EstadoCuenta {
  idWallet:        number;
  nombreWallet:    string;
  estado:          string;
  saldoDisponible: number;
  movimientos:     Movimiento[];
}

type Msg = { ok: boolean; text: string };
type Tab = 'saldo' | 'recibir' | 'enviar' | 'pagar' | 'movimientos';

const HAS_BARCODE_DETECTOR = typeof window !== 'undefined' && 'BarcodeDetector' in window;

// PIN: format-only validation for QA/Demo phase
// Full cryptographic validation (backend hash, attempt limits, lockout) is pending for production
function validatePin(pin: string): string | null {
  if (!/^\d{7}$/.test(pin)) return 'La clave debe ser exactamente 7 dígitos numéricos.';
  return null;
}

function stopScan(
  streamRef:  { current: MediaStream | null },
  intervalRef: { current: number | null },
  setScanningFn: (v: boolean) => void,
): void {
  if (intervalRef.current !== null) { clearInterval(intervalRef.current); intervalRef.current = null; }
  if (streamRef.current) { streamRef.current.getTracks().forEach(t => t.stop()); streamRef.current = null; }
  setScanningFn(false);
}

export function UserWalletPage() {
  const { user } = useAuth();
  const demoInfo = user ? DEMO_MAP[user.usuario] : undefined;
  const [tab, setTab] = useState<Tab>('saldo');

  // ── Account data ──────────────────────────────────────────────────────────
  const [cuenta,  setCuenta]  = useState<EstadoCuenta | null>(null);
  const [loading, setLoading] = useState(true);
  const [dataErr, setDataErr] = useState<string | null>(null);

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
  const envVideoRef   = useRef<HTMLVideoElement>(null);
  const envStreamRef  = useRef<MediaStream | null>(null);
  const envIntervalRef = useRef<number | null>(null);

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
  const pagVideoRef   = useRef<HTMLVideoElement>(null);
  const pagStreamRef  = useRef<MediaStream | null>(null);
  const pagIntervalRef = useRef<number | null>(null);

  // ── Load account ──────────────────────────────────────────────────────────
  const loadCuenta = useCallback(async () => {
    if (!demoInfo) return;
    setLoading(true); setDataErr(null);
    try {
      const r = await get<{ success: boolean; data: EstadoCuenta }>(
        `/api/reportes/wallet/${demoInfo.idWallet}/estado-cuenta`,
      );
      setCuenta(r.data);
    } catch (e) { setDataErr((e as Error).message); }
    finally { setLoading(false); }
  }, [demoInfo]);

  useEffect(() => { void loadCuenta(); }, [loadCuenta]);

  // Cleanup camera on unmount
  useEffect(() => {
    return () => {
      stopScan(envStreamRef, envIntervalRef, () => {});
      stopScan(pagStreamRef, pagIntervalRef, () => {});
    };
  }, []);

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

  // ── Camera scanner ────────────────────────────────────────────────────────
  async function startScan(
    videoRef:     React.RefObject<HTMLVideoElement>,
    streamRef:    { current: MediaStream | null },
    intervalRef:  { current: number | null },
    setScanFn:    (v: boolean) => void,
    setScanErrFn: (e: string | null) => void,
    onDetect:     (raw: string) => void,
  ) {
    setScanErrFn(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
      streamRef.current = stream;
      if (videoRef.current) { videoRef.current.srcObject = stream; await videoRef.current.play(); }
      setScanFn(true);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const detector = new (window as any).BarcodeDetector({ formats: ['qr_code'] });
      intervalRef.current = window.setInterval(async () => {
        if (!videoRef.current) return;
        try {
          const codes = await (detector.detect(videoRef.current) as Promise<BarcodeResult[]>);
          if (codes.length > 0) {
            stopScan(streamRef, intervalRef, setScanFn);
            onDetect(codes[0].rawValue);
          }
        } catch { /* detection error — retry */ }
      }, 500);
    } catch (err) {
      setScanErrFn('No se pudo acceder a la cámara: ' + ((err as Error).message ?? 'usa el campo de texto para pegar el QR.'));
      setScanFn(false);
    }
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
    setEnvBusy(true); setEnvMsg(null);
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/wallets/transferencia', {
        idWalletOrigen:  demoInfo.idWallet,
        idWalletDestino: destId,
        valor:           Number(envValor),
        descripcion:     `Transferencia QA a wallet #${destId}${envDestUser ? ' (' + envDestUser + ')' : ''}`,
        creadoPor:       demoInfo.idUsuario,
      });
      setEnvMsg({ ok: r.success, text: r.message ?? (r.success ? 'Transferencia realizada.' : 'Error al transferir.') });
      if (r.success) { await loadCuenta(); }
    } catch (e) { setEnvMsg({ ok: false, text: (e as Error).message }); }
    finally { setEnvBusy(false); setEnvPin(''); }
  }

  // ── QR Payment handler ────────────────────────────────────────────────────
  async function handlePagarQr(e: FormEvent) {
    e.preventDefault();
    if (!demoInfo || !pagQrCode) return;
    const pinErr = validatePin(pagPin);
    if (pinErr) { setPagMsg({ ok: false, text: pinErr }); return; }
    setPagBusy(true); setPagMsg(null);
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/qr/pagar', {
        codigoQr:        pagQrCode,
        idWalletUsuario: demoInfo.idWallet,
        valor:           Number(pagValor),
        descripcion:     'Pago QR comercio demo QA desde UI',
        creadoPor:       demoInfo.idUsuario,
      });
      setPagMsg({ ok: r.success, text: r.message ?? (r.success ? 'Pago QR realizado.' : 'Error al pagar QR.') });
      if (r.success) { await loadCuenta(); }
    } catch (e) { setPagMsg({ ok: false, text: (e as Error).message }); }
    finally { setPagBusy(false); setPagPin(''); }
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  function resetEnviar() {
    setEnvDest(null); setEnvDestUser(''); setEnvValor(''); setEnvNeedValor(false);
    setEnvMsg(null); setEnvScanErr(null); setEnvPasted(''); setEnvManual(false); setEnvManualDest('');
    stopScan(envStreamRef, envIntervalRef, setEnvScanning);
  }
  function resetPagar() {
    setPagQrCode(''); setPagValor(''); setPagNeedValor(false);
    setPagMsg(null); setPagScanErr(null); setPagPasted('');
    stopScan(pagStreamRef, pagIntervalRef, setPagScanning);
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

      {/* Tab navigation */}
      <div className="wallet-tabs">
        {(['saldo', 'recibir', 'enviar', 'pagar', 'movimientos'] as Tab[]).map(t => {
          const labels: Record<Tab, string> = {
            saldo: 'Mi Saldo', recibir: 'Recibir', enviar: 'Enviar',
            pagar: 'Pagar QR', movimientos: 'Movimientos',
          };
          return (
            <button
              key={t}
              className={`wallet-tab-btn${tab === t ? ' wallet-tab-btn--active' : ''}`}
              onClick={() => {
                setTab(t);
                if (t !== 'enviar') stopScan(envStreamRef, envIntervalRef, setEnvScanning);
                if (t !== 'pagar')  stopScan(pagStreamRef, pagIntervalRef, setPagScanning);
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

          {/* Video element — always in DOM when on this tab (hidden until scanning) */}
          <video
            ref={envVideoRef}
            className="scan-video"
            playsInline
            muted
            style={{ display: envScanning ? 'block' : 'none', marginBottom: '0.75rem' }}
          />

          {/* Scan / paste / manual — hidden once destination is set */}
          {!envDest && !envManual && (
            <div className="scan-section">
              {!envScanning && (
                <>
                  {HAS_BARCODE_DETECTOR ? (
                    <button
                      className="btn-scan"
                      onClick={() => void startScan(envVideoRef, envStreamRef, envIntervalRef, setEnvScanning, setEnvScanErr, parseTransferQr)}
                    >
                      📷 Escanear QR
                    </button>
                  ) : (
                    <p className="scan-unavail">Escáner no disponible en este navegador.</p>
                  )}
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
                <button className="btn-secondary" onClick={() => stopScan(envStreamRef, envIntervalRef, setEnvScanning)}>
                  Cancelar cámara
                </button>
              )}
              {envScanErr && <div className="error-msg" style={{ marginTop: '0.5rem' }}>{envScanErr}</div>}
            </div>
          )}

          {/* Manual entry fallback */}
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

          {/* Transfer form — shown once destination is confirmed */}
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

          <video
            ref={pagVideoRef}
            className="scan-video"
            playsInline
            muted
            style={{ display: pagScanning ? 'block' : 'none', marginBottom: '0.75rem' }}
          />

          {!pagQrCode && (
            <div className="scan-section">
              {!pagScanning && (
                <>
                  {HAS_BARCODE_DETECTOR ? (
                    <button
                      className="btn-scan"
                      onClick={() => void startScan(pagVideoRef, pagStreamRef, pagIntervalRef, setPagScanning, setPagScanErr, parseMerchantQr)}
                    >
                      📷 Escanear QR del comercio
                    </button>
                  ) : (
                    <p className="scan-unavail">Escáner no disponible en este navegador.</p>
                  )}
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
                <button className="btn-secondary" onClick={() => stopScan(pagStreamRef, pagIntervalRef, setPagScanning)}>
                  Cancelar cámara
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
                      <td>{m.descripcion ?? '—'}</td>
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

      <p className="user-wallet-footer">
        Ambiente QA/Demo · saldos y transacciones ficticios · sin dinero real · sin producción
      </p>
    </div>
  );
}
