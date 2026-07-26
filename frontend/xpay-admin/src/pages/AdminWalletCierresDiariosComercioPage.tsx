import { useEffect, useState, useCallback } from 'react';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';
import { generarComprobantePdfCierre } from '../utils/comprobanteCierrePdf.ts';

interface CierreAdminResumen {
  idCierre:                number;
  idComercio:              number;
  nombreComercio:          string | null;
  fechaCierre:             string;
  estado:                  string;
  codigoUnico:             string;
  cantidadRecargas:        number;
  valorTotalRecaudado:     number;
  valorLiquidadoAlGenerar: number;
  valorPendienteAlGenerar: number;
  valorLiquidadoActual:    number;
  valorPendienteActual:    number;
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

interface CierreAdminDetalle {
  idCierre:                number;
  idComercio:              number;
  nombreComercio:          string | null;
  idComercioAliado:        number | null;
  fechaCierre:             string;
  fechaHoraCorteUtc:       string;
  codigoUnico:             string;
  estado:                  string;
  cantidadRecargas:        number;
  valorTotalRecaudado:     number;
  valorLiquidadoAlGenerar: number;
  valorPendienteAlGenerar: number;
  valorLiquidadoActual:    number;
  valorPendienteActual:    number;
  generadoPorUsuario:      number;
  nombreGeneradoPor:       string | null;
  fechaGeneracion:         string;
  revisadoPorUsuario:      number | null;
  nombreRevisadoPor:       string | null;
  fechaRevision:           string | null;
  cerradoPorUsuario:       number | null;
  nombreCerradoPor:        string | null;
  fechaCerrado:            string | null;
  observacionesAdmin:      string | null;
  recargas:                RecargaEnCierre[];
}

interface ListarResp  { success: boolean; data: CierreAdminResumen[]; }
interface DetalleResp { success: boolean; data: CierreAdminDetalle; }

export function AdminWalletCierresDiariosComercioPage() {
  const [fechaDesde, setFechaDesde] = useState('');
  const [fechaHasta, setFechaHasta] = useState('');
  const [idComercio, setIdComercio] = useState('');
  const [estado, setEstado]         = useState('');

  const [cierres, setCierres] = useState<CierreAdminResumen[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState('');

  const [detalle, setDetalle]           = useState<CierreAdminDetalle | null>(null);
  const [detalleBusy, setDetalleBusy]   = useState(false);

  const buildQuery = useCallback(() => {
    const params = new URLSearchParams();
    if (fechaDesde) params.set('fechaDesde', fechaDesde);
    if (fechaHasta) params.set('fechaHasta', fechaHasta);
    if (idComercio) params.set('idComercio', idComercio);
    if (estado)     params.set('estado', estado);
    return params.toString();
  }, [fechaDesde, fechaHasta, idComercio, estado]);

  const cargar = useCallback(() => {
    setLoading(true);
    setError('');
    const qs = buildQuery();
    get<ListarResp>(`/api/admin/wallet-cierres-comercio?${qs}`)
      .then(r => setCierres(r.data))
      .catch(err => { setCierres([]); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [buildQuery]);

  useEffect(cargar, [cargar]);

  async function verDetalle(idCierre: number) {
    setDetalleBusy(true);
    try {
      const r = await get<DetalleResp>(`/api/admin/wallet-cierres-comercio/${idCierre}`);
      setDetalle(r.data);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setDetalleBusy(false);
    }
  }

  return (
    <div className="page">
      <h2>Cierres Diarios de Comercio</h2>
      <p style={{ color: '#718096', marginBottom: '1.5rem', fontSize: '0.9rem' }}>
        QA · consolidación diaria de recargas en efectivo por comercio · no crea movimientos de
        Wallet ni Ledger · sin producción
      </p>

      {error && <div className="error-msg" style={{ marginBottom: '1rem' }}>Error: {error}</div>}

      {/* Filtros */}
      <div style={{
        display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end',
        marginBottom: '1.25rem', padding: '0.75rem 1rem',
        background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
      }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Desde
          <input type="date" value={fechaDesde} onChange={e => setFechaDesde(e.target.value)} style={{ maxWidth: '160px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Hasta
          <input type="date" value={fechaHasta} onChange={e => setFechaHasta(e.target.value)} style={{ maxWidth: '160px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Comercio
          <input type="number" value={idComercio} onChange={e => setIdComercio(e.target.value)} placeholder="idComercio" style={{ maxWidth: '110px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Estado
          <select value={estado} onChange={e => setEstado(e.target.value)} style={{ maxWidth: '160px' }}>
            <option value="">Todos</option>
            <option value="GENERADO">GENERADO</option>
            <option value="REVISADO">REVISADO</option>
            <option value="CERRADO">CERRADO</option>
          </select>
        </label>
        <button className="btn-secondary" onClick={cargar}>Actualizar</button>
      </div>

      {loading && <div className="loading">Cargando...</div>}

      {!loading && (
        cierres.length === 0 ? (
          <div className="empty">No hay cierres diarios con los filtros actuales.</div>
        ) : (
          <div className="table-wrapper" style={{ marginBottom: '1.5rem' }}>
            <table>
              <thead>
                <tr>
                  <th>#Cierre</th><th>Comercio</th><th>Fecha</th><th>Estado</th>
                  <th>Recargas</th><th>Total recaudado</th>
                  <th>Liquidado al generar</th><th>Pendiente al generar</th>
                  <th>Liquidado actual</th><th>Pendiente actual</th><th></th>
                </tr>
              </thead>
              <tbody>
                {cierres.map(c => (
                  <tr key={c.idCierre}>
                    <td className="mono">{c.idCierre}</td>
                    <td>{c.nombreComercio ?? `comercio #${c.idComercio}`}</td>
                    <td className="mono">{c.fechaCierre}</td>
                    <td><span className="badge badge-ok">{c.estado}</span></td>
                    <td className="mono">{c.cantidadRecargas}</td>
                    <td style={{ fontWeight: 600 }}>{fmtMoney(c.valorTotalRecaudado)}</td>
                    <td>{fmtMoney(c.valorLiquidadoAlGenerar)}</td>
                    <td>{fmtMoney(c.valorPendienteAlGenerar)}</td>
                    <td style={{ color: c.valorLiquidadoActual !== c.valorLiquidadoAlGenerar ? '#2f855a' : undefined }}>
                      {fmtMoney(c.valorLiquidadoActual)}
                    </td>
                    <td style={{ color: c.valorPendienteActual !== c.valorPendienteAlGenerar ? '#c05621' : undefined }}>
                      {fmtMoney(c.valorPendienteActual)}
                    </td>
                    <td>
                      <button className="btn-secondary" style={{ fontSize: '0.78rem', padding: '0.25rem 0.7rem' }}
                        onClick={() => void verDetalle(c.idCierre)}>
                        Ver detalle
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      )}

      {detalleBusy && <div className="loading">Cargando detalle...</div>}

      {detalle && (
        <div className="table-wrapper" style={{ padding: '1rem' }}>
          <h3 style={{ marginBottom: '0.5rem' }}>
            Cierre #{detalle.idCierre} — {detalle.nombreComercio ?? `comercio #${detalle.idComercio}`} — {detalle.fechaCierre}
          </h3>
          <p style={{ fontSize: '0.82rem', color: '#718096' }}>
            Código único: <span className="mono">{detalle.codigoUnico}</span> · Corte: <span className="mono">{fmtDate(detalle.fechaHoraCorteUtc)}</span>
          </p>

          <div style={{ display: 'flex', gap: '2rem', flexWrap: 'wrap', margin: '0.75rem 0' }}>
            <div>
              <div style={{ fontSize: '0.8rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                Snapshot al generar
              </div>
              <table style={{ fontSize: '0.85rem' }}>
                <tbody>
                  <tr><td>Recargas</td><td style={{ textAlign: 'right', paddingLeft: '1rem' }}>{detalle.cantidadRecargas}</td></tr>
                  <tr><td>Total recaudado</td><td style={{ textAlign: 'right', paddingLeft: '1rem', fontWeight: 700 }}>{fmtMoney(detalle.valorTotalRecaudado)}</td></tr>
                  <tr><td>Liquidado</td><td style={{ textAlign: 'right', paddingLeft: '1rem' }}>{fmtMoney(detalle.valorLiquidadoAlGenerar)}</td></tr>
                  <tr><td>Pendiente</td><td style={{ textAlign: 'right', paddingLeft: '1rem' }}>{fmtMoney(detalle.valorPendienteAlGenerar)}</td></tr>
                </tbody>
              </table>
            </div>
            <div>
              <div style={{ fontSize: '0.8rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                Situación actual (recalculada en vivo)
              </div>
              <table style={{ fontSize: '0.85rem' }}>
                <tbody>
                  <tr><td>Liquidado</td><td style={{ textAlign: 'right', paddingLeft: '1rem' }}>{fmtMoney(detalle.valorLiquidadoActual)}</td></tr>
                  <tr><td>Pendiente</td><td style={{ textAlign: 'right', paddingLeft: '1rem' }}>{fmtMoney(detalle.valorPendienteActual)}</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          <p style={{ fontSize: '0.85rem' }}>
            Generado por {detalle.nombreGeneradoPor ?? `#${detalle.generadoPorUsuario}`} el {fmtDate(detalle.fechaGeneracion)}
            {detalle.fechaRevision && <> · Revisado por {detalle.nombreRevisadoPor ?? `#${detalle.revisadoPorUsuario}`} el {fmtDate(detalle.fechaRevision)}</>}
            {detalle.fechaCerrado && <> · Cerrado por {detalle.nombreCerradoPor ?? `#${detalle.cerradoPorUsuario}`} el {fmtDate(detalle.fechaCerrado)}</>}
          </p>

          <div className="table-wrapper" style={{ marginTop: '0.75rem', marginBottom: '1rem' }}>
            <table>
              <thead>
                <tr><th>#Recarga</th><th>Sede</th><th>Cajero</th><th>Usuario wallet</th><th>Valor</th><th>Liquidada al generar</th><th>Fecha</th></tr>
              </thead>
              <tbody>
                {detalle.recargas.map(r => (
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

          <div style={{
            display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'center',
            padding: '1rem', background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
          }}>
            <button className="btn-secondary" onClick={() => generarComprobantePdfCierre({
              idCierre: detalle.idCierre,
              idComercio: detalle.idComercio,
              nombreComercio: detalle.nombreComercio,
              fechaCierre: detalle.fechaCierre,
              fechaHoraCorteUtc: detalle.fechaHoraCorteUtc,
              codigoUnico: detalle.codigoUnico,
              estado: detalle.estado,
              cantidadRecargas: detalle.cantidadRecargas,
              valorTotalRecaudado: detalle.valorTotalRecaudado,
              valorLiquidadoAlGenerar: detalle.valorLiquidadoAlGenerar,
              valorPendienteAlGenerar: detalle.valorPendienteAlGenerar,
            })}>
              Descargar comprobante PDF
            </button>

            {/* Flujo autogestionado: ADMIN_COMERCIO genera y cierra en un solo paso —
                XPAY consulta y audita, no aprueba cierres normales. Sin acciones aquí. */}
            <span style={{ fontSize: '0.82rem', color: '#718096' }}>
              Cierre autogestionado por el comercio — consulta y auditoría, sin acciones administrativas.
            </span>
          </div>
        </div>
      )}
    </div>
  );
}
