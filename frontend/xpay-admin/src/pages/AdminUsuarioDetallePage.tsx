import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate } from '../utils.ts';

interface RolDetalle {
  codigo:          string;
  nombre:          string;
  fechaAsignacion: string;
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
  const [data,    setData]    = useState<UsuarioDetalle | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/admin/usuarios/${id}`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [id]);

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
                    </tr>
                  </thead>
                  <tbody>
                    {data.roles.map(r => (
                      <tr key={r.codigo}>
                        <td className="mono">{r.codigo}</td>
                        <td>{r.nombre}</td>
                        <td className="mono">{fmtDate(r.fechaAsignacion)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
