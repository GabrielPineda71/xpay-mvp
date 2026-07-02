import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';

export function MiEmpresaPage() {
  const { user } = useAuth();

  return (
    <div className="page">
      <h2>Mi Empresa — Libranza</h2>
      <p className="dashboard-subtitle">
        {user?.usuario ?? '—'}
        {' · '}<span className="badge badge-info">QA / Demo</span>
        {' · '}<span className="badge badge-warn">Módulo en preparación</span>
      </p>

      <div className="info-section">
        <h3>Empresa demo</h3>
        <div className="info-grid">
          <div className="info-item">
            <span className="label">Nombre</span>
            <span className="value">Empresa Demo Libranza QA</span>
          </div>
          <div className="info-item">
            <span className="label">NIT ficticio</span>
            <span className="value">900888001-0</span>
          </div>
          <div className="info-item">
            <span className="label">Tipo</span>
            <span className="value">EMPRESA_LIBRANZA</span>
          </div>
          <div className="info-item">
            <span className="label">Estado módulo</span>
            <span className="value"><span className="badge badge-warn">En preparación</span></span>
          </div>
          <div className="info-item">
            <span className="label">Ambiente</span>
            <span className="value">QA / Demo</span>
          </div>
        </div>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem' }}>
        <h3>Flujo previsto — Libranza XPAY</h3>
        <ol style={{ paddingLeft: '1.25rem', fontSize: '0.9rem', color: '#4a5568', lineHeight: '2' }}>
          <li>
            <strong>Carga de empleados / autorizados</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              La empresa sube el padrón de empleados con cupo de libranza autorizado.
            </span>
          </li>
          <li>
            <strong>Validación de cupo XPAY</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              XPAY valida el cupo disponible por empleado en tiempo real.
            </span>
          </li>
          <li>
            <strong>Uso de wallet / QR por el empleado</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              El empleado paga en comercios aliados con su wallet XPAY; el monto se registra como libranza.
            </span>
          </li>
          <li>
            <strong>Consulta de estado</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              La empresa consulta el estado de la libranza por empleado y período.
            </span>
          </li>
          <li>
            <strong>Recaudo y conciliación</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              La empresa realiza el recaudo periódico; XPAY concilia y actualiza saldos.
            </span>
          </li>
        </ol>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem', borderLeft: '3px solid #3b82f6', background: '#eff6ff' }}>
        <h3 style={{ color: '#1e40af' }}>Módulo activo</h3>
        <p style={{ fontSize: '0.9rem', color: '#1d4ed8' }}>
          El módulo de empleados ya está disponible.{' '}
          <Link to="/mi-empresa/libranza" style={{ fontWeight: 600 }}>Ver empleados y carga de padrón →</Link>
        </p>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem', borderLeft: '3px solid #f59e0b', background: '#fffbeb' }}>
        <h3 style={{ color: '#92400e' }}>Estado actual del módulo</h3>
        <p style={{ fontSize: '0.9rem', color: '#78350f', lineHeight: '1.6' }}>
          El módulo de libranza no está implementado en esta versión del MVP XPAY.
          Esta vista es informativa y refleja el flujo previsto para la integración futura
          con empresas que ofrecen libranza como beneficio a sus empleados.
        </p>
        <p style={{ fontSize: '0.9rem', color: '#78350f', marginTop: '0.75rem', lineHeight: '1.6' }}>
          <strong>No se realizarán transacciones financieras</strong> desde esta vista.
          Cuando el módulo esté disponible, se integrará con los endpoints de libranza del backend XPAY.
        </p>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem' }}>
        <h3>Capacidades planificadas para la empresa</h3>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={{ textAlign: 'left', padding: '0.5rem', fontSize: '0.78rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>Capacidad</th>
              <th style={{ textAlign: 'left', padding: '0.5rem', fontSize: '0.78rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>Estado</th>
            </tr>
          </thead>
          <tbody>
            {[
              ['Carga de empleados autorizados', 'Planificado'],
              ['Consulta de cupos por empleado', 'Planificado'],
              ['Reporte de libranzas por período', 'Planificado'],
              ['Conciliación y recaudo', 'Planificado'],
              ['Panel de estado de convenio', 'Planificado'],
            ].map(([cap, est]) => (
              <tr key={cap}>
                <td style={{ padding: '0.5rem', fontSize: '0.875rem', borderBottom: '1px solid #f0f0f0' }}>{cap}</td>
                <td style={{ padding: '0.5rem', fontSize: '0.875rem', borderBottom: '1px solid #f0f0f0' }}>
                  <span className="badge badge-info">{est}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="user-wallet-footer">
        Ambiente QA/Demo · módulo libranza en preparación · sin transacciones financieras · sin producción
      </p>
    </div>
  );
}
