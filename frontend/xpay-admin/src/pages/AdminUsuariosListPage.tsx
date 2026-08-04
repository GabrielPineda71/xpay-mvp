import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate } from '../utils.ts';

interface UsuarioItem {
  idUsuario:        number;
  usuario:          string;
  nombreCompleto:   string;
  tipoDocumento:    string;
  numeroDocumento:  string;
  email:            string | null;
  celular:          string;
  estado:           string;
  intentosFallidos: number;
  ultimoIngreso:    string | null;
  roles:            string[];
}

interface ListaData {
  items:    UsuarioItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = { texto: string; estado: string; rol: string; soloBloqueados: boolean };

const EMPTY: Filters = { texto: '', estado: '', rol: '', soloBloqueados: false };
const PAGE_SIZE = 20;

function estadoBadge(estado: string) {
  const cls = estado === 'ACTIVO' ? 'badge-ok' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function AdminUsuariosListPage() {
  const navigate = useNavigate();
  const [form,    setForm]    = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [page,    setPage]    = useState(1);
  const [data,    setData]    = useState<ListaData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    const params = new URLSearchParams();
    if (applied.texto)          params.set('texto', applied.texto);
    if (applied.estado)         params.set('estado', applied.estado);
    if (applied.rol)            params.set('rol', applied.rol);
    if (applied.soloBloqueados) params.set('soloBloqueados', 'true');
    params.set('page', String(page));
    params.set('pageSize', String(PAGE_SIZE));
    const url = `/api/admin/usuarios?${params.toString()}`;

    setLoading(true);
    setError('');
    get<ApiResp>(url)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [applied, page]);

  function handleBuscar(e: FormEvent) {
    e.preventDefault();
    setPage(1);
    setApplied({ ...form });
  }

  function handleLimpiar() {
    setForm(EMPTY);
    setPage(1);
    setApplied(EMPTY);
  }

  const totalPaginas = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="page">
      <h2>Usuarios</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Texto</label>
          <input
            type="text"
            value={form.texto}
            onChange={e => setForm(f => ({ ...f, texto: e.target.value }))}
            placeholder="usuario, documento, nombre, email"
          />
        </div>
        <div className="filter-field">
          <label>Estado</label>
          <select value={form.estado} onChange={e => setForm(f => ({ ...f, estado: e.target.value }))}>
            <option value="">Todos</option>
            <option value="ACTIVO">Activo</option>
            <option value="BLOQUEADO">Bloqueado</option>
          </select>
        </div>
        <div className="filter-field">
          <label>Rol</label>
          <select value={form.rol} onChange={e => setForm(f => ({ ...f, rol: e.target.value }))}>
            <option value="">Todos</option>
            <option value="ADMIN_XPAY">ADMIN_XPAY</option>
            <option value="SUPERUSUARIO">SUPERUSUARIO</option>
            <option value="OPERADOR_XPAY">OPERADOR_XPAY</option>
            <option value="COMERCIO">COMERCIO</option>
            <option value="USUARIO_FINAL">USUARIO_FINAL</option>
          </select>
        </div>
        <div className="filter-field filter-checkbox">
          <label>
            <input
              type="checkbox"
              checked={form.soloBloqueados}
              onChange={e => setForm(f => ({ ...f, soloBloqueados: e.target.checked }))}
            />
            {' '}Solo bloqueados
          </label>
        </div>
        <button type="submit" className="btn-search">Buscar</button>
        <button type="button" className="btn-search" style={{ background: '#718096' }} onClick={handleLimpiar}>
          Limpiar
        </button>
      </form>

      {loading && <div className="loading">Cargando...</div>}
      {error   && <div className="error-msg">Error: {error}</div>}

      {data && !loading && (
        <>
          <div className="results-meta">
            {data.total === 0
              ? 'Sin resultados para los filtros aplicados.'
              : `${data.total} usuario${data.total !== 1 ? 's' : ''} encontrado${data.total !== 1 ? 's' : ''} — página ${data.page} de ${totalPaginas}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay usuarios que coincidan con los filtros.</div>
          ) : (
            <>
              <div className="table-wrapper">
                <table>
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Usuario</th>
                      <th>Nombre completo</th>
                      <th>Documento</th>
                      <th>Email</th>
                      <th>Celular</th>
                      <th>Estado</th>
                      <th>Intentos fallidos</th>
                      <th>Último ingreso</th>
                      <th>Roles</th>
                      <th>Acción</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map(u => (
                      <tr key={u.idUsuario}>
                        <td className="mono">{u.idUsuario}</td>
                        <td>{u.usuario}</td>
                        <td>{u.nombreCompleto}</td>
                        <td className="mono">{u.tipoDocumento} {u.numeroDocumento}</td>
                        <td>{u.email ?? '—'}</td>
                        <td>{u.celular}</td>
                        <td>{estadoBadge(u.estado)}</td>
                        <td className="mono">{u.intentosFallidos}</td>
                        <td className="mono">{fmtDate(u.ultimoIngreso)}</td>
                        <td>{u.roles.length > 0 ? u.roles.join(', ') : '—'}</td>
                        <td>
                          <button
                            className="btn-link"
                            onClick={() => navigate(`/admin/usuarios/${u.idUsuario}`)}
                          >
                            Ver detalle
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="pagination">
                <button
                  type="button"
                  className="btn-search"
                  disabled={data.page <= 1}
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                >
                  ← Anterior
                </button>
                <span className="pagination-info">Página {data.page} de {totalPaginas}</span>
                <button
                  type="button"
                  className="btn-search"
                  disabled={data.page >= totalPaginas}
                  onClick={() => setPage(p => p + 1)}
                >
                  Siguiente →
                </button>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
