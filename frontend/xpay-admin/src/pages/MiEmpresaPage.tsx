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
        {' · '}<span className="badge badge-ok">Módulo activo</span>
      </p>

      <div className="info-section" style={{ borderLeft: '3px solid #3b82f6', background: '#eff6ff' }}>
        <h3 style={{ color: '#1e40af' }}>Acceso rápido</h3>
        <p style={{ fontSize: '0.9rem', color: '#1d4ed8' }}>
          <Link to="/mi-empresa/libranza" style={{ fontWeight: 600, fontSize: '1rem' }}>
            Ir a gestión de empleados Libranza →
          </Link>
        </p>
        <p style={{ fontSize: '0.85rem', color: '#1d4ed8', marginTop: '0.5rem' }}>
          Administre empleados, cargue padrón CSV, consulte cobros y aplique pagos de nómina.
        </p>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem' }}>
        <h3>Flujo Anticipo de Nómina — XPAY</h3>
        <ol style={{ paddingLeft: '1.25rem', fontSize: '0.9rem', color: '#4a5568', lineHeight: '2' }}>
          <li>
            <strong>Carga de empleados / autorizados</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              La empresa sube el padrón de empleados con sus cortes de pago y cupo de anticipo.
            </span>
          </li>
          <li>
            <strong>Solicitud de anticipo por el empleado</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              El empleado solicita un anticipo dentro de su cupo disponible para el corte vigente.
            </span>
          </li>
          <li>
            <strong>Desembolso a wallet XPAY</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              XPAY desembolsa el monto neto a la wallet del empleado; se registra en el ledger.
            </span>
          </li>
          <li>
            <strong>Cobro en el corte de pago</strong><br />
            <span style={{ color: '#718096', fontSize: '0.85rem' }}>
              La empresa aplica el pago en la fecha de corte; se concilia automáticamente.
            </span>
          </li>
        </ol>
      </div>

      <div className="info-section" style={{ marginTop: '1.25rem' }}>
        <h3>Capacidades disponibles</h3>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={{ textAlign: 'left', padding: '0.5rem', fontSize: '0.78rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>Capacidad</th>
              <th style={{ textAlign: 'left', padding: '0.5rem', fontSize: '0.78rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>Estado</th>
            </tr>
          </thead>
          <tbody>
            {[
              ['Carga de empleados (CSV)', 'Activo'],
              ['Periodicidades MENSUAL / QUINCENAL / DECADAL', 'Activo'],
              ['Cupo por corte de pago', 'Activo'],
              ['Anticipo de nómina (solicitud + desembolso)', 'Activo'],
              ['Cobros y aplicación de pago empresa', 'Activo'],
              ['Ledger contable integrado', 'Activo'],
            ].map(([cap, est]) => (
              <tr key={cap}>
                <td style={{ padding: '0.5rem', fontSize: '0.875rem', borderBottom: '1px solid #f0f0f0' }}>{cap}</td>
                <td style={{ padding: '0.5rem', fontSize: '0.875rem', borderBottom: '1px solid #f0f0f0' }}>
                  <span className="badge badge-ok">{est}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="user-wallet-footer">
        Ambiente QA/Demo · libranza activo · sin transacciones en producción
      </p>
    </div>
  );
}
