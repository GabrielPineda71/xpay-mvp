import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useComercioScope } from '../../auth/useComercioScope.ts';
import { listarCajas, type CajaResumenDto } from '../../api/caja.ts';
import { getApiErrorMessage } from '../../utils/caja-format.ts';
import { CajaFilters } from '../../components/caja/CajaFilters.tsx';
import { CajaListItem } from '../../components/caja/CajaListItem.tsx';
import { CajaStatusBadge } from '../../components/caja/CajaStatusBadge.tsx';
import { fmtMoney, fmtFechaOperativa } from '../../utils/caja-format.ts';

const PAGE_SIZE = 20;

// /comercio/cajas — ADMIN_SEDE_COMERCIO ("Cajas de mi sede", auto-acotado por
// el backend) y ADMIN_COMERCIO ("Cajas del comercio", todas las sedes con
// filtro). El filtro de sede se deriva de las sedes ya vistas en los
// resultados cargados — no existe (todavía) un endpoint de listado de sedes
// accesible desde el rol comercio para poblar un combo completo; ver informe.
export function CajasListaPage() {
  const navigate = useNavigate();
  const { scope } = useComercioScope();
  const esAdminComercio = scope?.rolComercio === 'ADMIN_COMERCIO';

  const [items, setItems]     = useState<CajaResumenDto[]>([]);
  const [page, setPage]       = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState<string | null>(null);

  const [idEstablecimiento, setIdEstablecimiento] = useState<number | ''>('');
  const [estado, setEstado]   = useState('');
  const [desde, setDesde]     = useState('');
  const [hasta, setHasta]     = useState('');
  const [sedesVistas, setSedesVistas] = useState<Map<number, string>>(new Map());

  const cargar = useCallback(async (p: number) => {
    setLoading(true); setError(null);
    try {
      const r = await listarCajas({
        page: p,
        pageSize: PAGE_SIZE,
        idEstablecimiento: esAdminComercio && idEstablecimiento !== '' ? idEstablecimiento : undefined,
        estado: estado || undefined,
        desde: desde || undefined,
        hasta: hasta || undefined,
      });
      setItems(r.items);
      setTotalPages(r.totalPages || 1);
      setPage(r.page || p);
      setSedesVistas(prev => {
        const next = new Map(prev);
        for (const it of r.items) {
          if (it.nombreEstablecimiento) next.set(it.idEstablecimiento, it.nombreEstablecimiento);
        }
        return next;
      });
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [esAdminComercio, idEstablecimiento, estado, desde, hasta]);

  useEffect(() => { void cargar(1); }, [cargar]);

  const sedesOpciones = Array.from(sedesVistas.entries())
    .map(([idEstablecimiento, nombreEstablecimiento]) => ({ idEstablecimiento, nombreEstablecimiento }));

  return (
    <div className="caja-page" style={{ maxWidth: 960 }}>
      <h1 className="caja-page-title">{esAdminComercio ? 'Cajas del comercio' : 'Cajas de mi sede'}</h1>
      <p className="caja-page-subtitle">
        {esAdminComercio ? 'Todas las sedes de tu comercio.' : 'Cajas operadas en tu sede.'}
      </p>

      <CajaFilters
        mostrarFiltroSede={esAdminComercio}
        sedes={sedesOpciones}
        idEstablecimiento={idEstablecimiento}
        estado={estado}
        desde={desde}
        hasta={hasta}
        onChange={next => {
          if (next.idEstablecimiento !== undefined) setIdEstablecimiento(next.idEstablecimiento);
          if (next.estado !== undefined) setEstado(next.estado);
          if (next.desde !== undefined) setDesde(next.desde);
          if (next.hasta !== undefined) setHasta(next.hasta);
        }}
      />

      {error && <div className="caja-error-banner">{error}</div>}

      {loading ? (
        <>
          <div className="caja-skeleton caja-skeleton-line" style={{ height: 70, marginBottom: '0.75rem' }} />
          <div className="caja-skeleton caja-skeleton-line" style={{ height: 70, marginBottom: '0.75rem' }} />
        </>
      ) : items.length === 0 ? (
        <div className="caja-page-empty">Sin cajas para los filtros seleccionados.</div>
      ) : (
        <>
          <div className="caja-list-cards">
            {items.map(c => (
              <CajaListItem key={c.idCaja} caja={c} onClick={() => navigate(`/comercio/cajas/${c.idCaja}`)} />
            ))}
          </div>
          <div className="caja-table-wrapper">
            <table className="caja-table">
              <thead>
                <tr>
                  <th>Fecha</th><th>Cajero</th>{esAdminComercio && <th>Sede</th>}
                  <th>Estado</th><th>Fondo</th><th>Esperado</th><th>Contado</th><th>Diferencia</th>
                </tr>
              </thead>
              <tbody>
                {items.map(c => (
                  <tr key={c.idCaja} onClick={() => navigate(`/comercio/cajas/${c.idCaja}`)}>
                    <td>{fmtFechaOperativa(c.fechaOperativa)}</td>
                    <td>{c.nombreUsuarioCajero ?? `#${c.idUsuarioCajero}`}</td>
                    {esAdminComercio && <td>{c.nombreEstablecimiento ?? '—'}</td>}
                    <td><CajaStatusBadge estado={c.estado} /></td>
                    <td>{fmtMoney(c.fondoInicial)}</td>
                    <td>{fmtMoney(c.efectivoEsperado)}</td>
                    <td>{fmtMoney(c.efectivoContado)}</td>
                    <td>{fmtMoney(c.diferencia)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div className="caja-pagination">
              <button type="button" className="caja-btn caja-btn-secondary" style={{ width: 'auto' }}
                disabled={page <= 1} onClick={() => void cargar(page - 1)}>
                Anterior
              </button>
              <span>Página {page} de {totalPages}</span>
              <button type="button" className="caja-btn caja-btn-secondary" style={{ width: 'auto' }}
                disabled={page >= totalPages} onClick={() => void cargar(page + 1)}>
                Siguiente
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
