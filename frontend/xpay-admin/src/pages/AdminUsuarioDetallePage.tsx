import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get, post } from '../api/client.ts';
import { fmtDate } from '../utils.ts';
import { useAuth } from '../auth/AuthContext.tsx';

interface RolDetalle {
  idRol:           number;
  codigo:          string;
  nombre:          string;
  fechaAsignacion: string;
}

interface RolAsignable {
  codigo: string;
  nombre: string;
}

interface UsuarioDetalle {
  idUsuario:           number;
  idPersona:           number;
  usuario:             string;
  nombreCompleto:      string;
  tipoDocumento:       string;
  numeroDocumento:     string;
  email:               string | null;
  celular:             string;
  direccion:           string | null;
  ciudad:              string | null;
  departamento:        string | null;
  pais:                string;
  estado:              string;
  emailVerificado:     boolean;
  celularVerificado:   boolean;
  intentosFallidos:    number;
  fechaBloqueo:        string | null;
  motivoBloqueo:       string | null;
  ultimoIngreso:       string | null;
  requiereCambioClave: boolean;
  fechaCreacion:       string;
  fechaActualizacion:  string | null;
  roles:               RolDetalle[];
}

interface ApiResp { success: boolean; data: UsuarioDetalle; }
interface AccionResp { success: boolean; message: string; data: UsuarioDetalle; }
interface RolesAsignablesResp { success: boolean; data: RolAsignable[]; }

type Accion = 'activar' | 'inactivar' | 'desbloquear';

const CONFIRMACION: Record<Accion, (usuario: string) => string> = {
  activar:      u => `¿Confirmas activar a ${u}? Podrá iniciar sesión nuevamente.`,
  inactivar:    u => `¿Confirmas inactivar a ${u}? No podrá iniciar sesión hasta que sea reactivado.`,
  desbloquear:  u => `¿Confirmas desbloquear a ${u}? Se reiniciarán sus intentos fallidos y podrá iniciar sesión nuevamente.`,
};

// Fase USUARIOS-ADMIN-4: el cambio de rol no invalida un JWT ya emitido —
// solo se refleja en el próximo login del usuario objetivo.
const AVISO_JWT = 'El cambio de rol se reflejará en la próxima sesión del usuario. Una sesión ya abierta puede conservar permisos anteriores hasta que expire el token.';

function estadoBadge(estado: string) {
  const cls = estado === 'ACTIVO' ? 'badge-ok' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

function boolTexto(v: boolean) {
  return v ? 'Sí' : 'No';
}

export function AdminUsuarioDetallePage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const [data,        setData]        = useState<UsuarioDetalle | null>(null);
  const [loading,     setLoading]     = useState(false);
  const [error,       setError]       = useState('');
  const [actionBusy,  setActionBusy]  = useState(false);
  const [actionMsg,   setActionMsg]   = useState('');
  const [actionError, setActionError] = useState('');

  const [rolesAsignables,  setRolesAsignables]  = useState<RolAsignable[]>([]);
  const [rolSeleccionado,  setRolSeleccionado]  = useState('');
  const [rolBusy,          setRolBusy]          = useState(false);
  const [rolMsg,           setRolMsg]           = useState('');
  const [rolError,         setRolError]         = useState('');

  const cargarDetalle = useCallback(() => {
    if (!id) return;
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/admin/usuarios/${id}`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [id]);

  const cargarRolesAsignables = useCallback(() => {
    get<RolesAsignablesResp>('/api/admin/roles/asignables')
      .then(r => setRolesAsignables(r.data))
      .catch(() => setRolesAsignables([]));
  }, []);

  useEffect(() => { cargarDetalle(); }, [cargarDetalle]);
  useEffect(() => { cargarRolesAsignables(); }, [cargarRolesAsignables]);

  async function ejecutarAccion(accion: Accion) {
    if (!id || !data) return;
    if (!confirm(CONFIRMACION[accion](data.usuario))) return;
    setActionBusy(true);
    setActionMsg('');
    setActionError('');
    try {
      const r = await post<AccionResp>(`/api/admin/usuarios/${id}/${accion}`, {});
      setActionMsg(r.message);
      // No se actualiza el estado local de forma optimista — se vuelve a
      // consultar el detalle real para reflejar exactamente lo que el
      // backend confirmó (estado, intentosFallidos, fechaBloqueo, etc.).
      cargarDetalle();
    } catch (err) {
      setActionError((err as Error).message);
    } finally {
      setActionBusy(false);
    }
  }

  async function asignarRol() {
    if (!id || !data || !rolSeleccionado) return;
    if (!confirm(`¿Confirmas asignar el rol ${rolSeleccionado} a ${data.usuario}?\n\n${AVISO_JWT}`)) return;
    setRolBusy(true);
    setRolMsg('');
    setRolError('');
    try {
      const r = await post<AccionResp>(`/api/admin/usuarios/${id}/roles`, { rolCodigo: rolSeleccionado });
      setRolMsg(r.message);
      setRolSeleccionado('');
      cargarDetalle();
    } catch (err) {
      setRolError((err as Error).message);
    } finally {
      setRolBusy(false);
    }
  }

  async function revocarRol(rol: RolDetalle) {
    if (!id || !data) return;
    if (!confirm(`¿Confirmas revocar el rol ${rol.codigo} de ${data.usuario}?\n\n${AVISO_JWT}`)) return;
    setRolBusy(true);
    setRolMsg('');
    setRolError('');
    try {
      const r = await post<AccionResp>(`/api/admin/usuarios/${id}/roles/${rol.idRol}/revocar`, {});
      setRolMsg(r.message);
      cargarDetalle();
    } catch (err) {
      setRolError((err as Error).message);
    } finally {
      setRolBusy(false);
    }
  }

  const esPropiaCuenta = data != null && user != null && data.idUsuario === user.idUsuario;

  return (
    <div className="page">
      <h2>Detalle de usuario</h2>
      <button type="button" className="btn-link" onClick={() => navigate('/admin/usuarios')}>
        ← Volver al listado
      </button>

      {loading && <div className="loading">Cargando...</div>}
      {error   && <div className="error-msg">Error: {error}</div>}

      {data && !loading && (
        <div className="detail-grid">
          <div className="detail-section">
            <h3>Identificación</h3>
            <dl>
              <dt>ID usuario</dt><dd className="mono">{data.idUsuario}</dd>
              <dt>Usuario</dt><dd>{data.usuario}</dd>
              <dt>Nombre completo</dt><dd>{data.nombreCompleto}</dd>
              <dt>Documento</dt><dd className="mono">{data.tipoDocumento} {data.numeroDocumento}</dd>
              <dt>Email</dt><dd>{data.email ?? '—'} {data.email && `(${data.emailVerificado ? 'verificado' : 'no verificado'})`}</dd>
              <dt>Celular</dt><dd>{data.celular} ({data.celularVerificado ? 'verificado' : 'no verificado'})</dd>
              <dt>Dirección</dt><dd>{data.direccion ?? '—'}</dd>
              <dt>Ciudad / Departamento</dt><dd>{data.ciudad ?? '—'} / {data.departamento ?? '—'}</dd>
              <dt>País</dt><dd>{data.pais}</dd>
            </dl>
          </div>

          <div className="detail-section">
            <h3>Acceso</h3>
            <dl>
              <dt>Estado</dt><dd>{estadoBadge(data.estado)}</dd>
              <dt>Intentos fallidos</dt><dd className="mono">{data.intentosFallidos}</dd>
              <dt>Fecha de bloqueo</dt><dd className="mono">{fmtDate(data.fechaBloqueo)}</dd>
              <dt>Motivo de bloqueo</dt><dd>{data.motivoBloqueo ?? '—'}</dd>
              <dt>Último ingreso</dt><dd className="mono">{fmtDate(data.ultimoIngreso)}</dd>
              <dt>Requiere cambio de clave</dt><dd>{boolTexto(data.requiereCambioClave)}</dd>
              <dt>Fecha de creación</dt><dd className="mono">{fmtDate(data.fechaCreacion)}</dd>
              <dt>Última actualización</dt><dd className="mono">{fmtDate(data.fechaActualizacion)}</dd>
            </dl>

            <div className="detail-actions">
              {data.estado === 'ACTIVO' && (
                <button
                  type="button"
                  className="btn-search"
                  style={{ background: '#c53030' }}
                  disabled={actionBusy || esPropiaCuenta}
                  title={esPropiaCuenta ? 'No puedes inactivar tu propia cuenta.' : undefined}
                  onClick={() => ejecutarAccion('inactivar')}
                >
                  {actionBusy ? 'Procesando...' : 'Inactivar'}
                </button>
              )}
              {data.estado === 'INACTIVO' && (
                <button
                  type="button"
                  className="btn-search"
                  disabled={actionBusy || esPropiaCuenta}
                  title={esPropiaCuenta ? 'No puedes activar tu propia cuenta.' : undefined}
                  onClick={() => ejecutarAccion('activar')}
                >
                  {actionBusy ? 'Procesando...' : 'Activar'}
                </button>
              )}
              {data.estado === 'BLOQUEADO' && (
                <button
                  type="button"
                  className="btn-search"
                  disabled={actionBusy || esPropiaCuenta}
                  title={esPropiaCuenta ? 'No puedes desbloquear tu propia cuenta.' : undefined}
                  onClick={() => ejecutarAccion('desbloquear')}
                >
                  {actionBusy ? 'Procesando...' : 'Desbloquear'}
                </button>
              )}
              {esPropiaCuenta && (
                <span className="results-meta">No puedes ejecutar acciones sobre tu propia cuenta.</span>
              )}
            </div>

            {actionMsg   && <div className="breb-msg-ok">{actionMsg}</div>}
            {actionError && <div className="error-msg">Error: {actionError}</div>}
          </div>

          <div className="detail-section">
            <h3>Roles activos</h3>
            {data.roles.length === 0 ? (
              <div className="empty">Sin roles asignados.</div>
            ) : (
              <div className="table-wrapper">
                <table>
                  <thead>
                    <tr>
                      <th>Código</th>
                      <th>Nombre</th>
                      <th>Fecha de asignación</th>
                      <th>Acción</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.roles.map(r => (
                      <tr key={r.codigo}>
                        <td className="mono">{r.codigo}</td>
                        <td>{r.nombre}</td>
                        <td className="mono">{fmtDate(r.fechaAsignacion)}</td>
                        <td>
                          <button
                            type="button"
                            className="btn-link"
                            disabled={rolBusy || esPropiaCuenta}
                            title={esPropiaCuenta ? 'No puedes revocar roles de tu propia cuenta.' : undefined}
                            onClick={() => revocarRol(r)}
                          >
                            Revocar
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="detail-actions">
              <select
                value={rolSeleccionado}
                onChange={e => setRolSeleccionado(e.target.value)}
                disabled={rolBusy || esPropiaCuenta || rolesAsignables.length === 0}
              >
                <option value="">
                  {rolesAsignables.length === 0 ? 'Sin roles disponibles' : 'Seleccionar rol...'}
                </option>
                {rolesAsignables
                  // No ofrecer un rol que el usuario ya tiene activo.
                  .filter(ra => !data.roles.some(r => r.codigo === ra.codigo))
                  .map(ra => (
                    <option key={ra.codigo} value={ra.codigo}>{ra.nombre} ({ra.codigo})</option>
                  ))}
              </select>
              <button
                type="button"
                className="btn-search"
                disabled={rolBusy || esPropiaCuenta || !rolSeleccionado}
                title={esPropiaCuenta ? 'No puedes asignarte roles a ti mismo.' : undefined}
                onClick={asignarRol}
              >
                {rolBusy ? 'Procesando...' : 'Asignar rol'}
              </button>
            </div>

            {rolMsg   && <div className="breb-msg-ok">{rolMsg}</div>}
            {rolError && <div className="error-msg">Error: {rolError}</div>}
          </div>
        </div>
      )}
    </div>
  );
}
