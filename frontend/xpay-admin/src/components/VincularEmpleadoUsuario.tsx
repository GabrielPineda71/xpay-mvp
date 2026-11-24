import { Link } from 'react-router-dom';

interface Props {
  idConvenio: number;
  estadoEmpleado: string;
  idUsuarioVinculado: number | null | undefined;
}

// La lista de empleados lleva a una sola bandeja por convenio: no consulta
// candidatos por fila ni pide al administrador identificadores internos.
export function VincularEmpleadoUsuario({ idConvenio, estadoEmpleado, idUsuarioVinculado }: Props) {
  if (idUsuarioVinculado != null) return <span className="badge badge-ok">Vinculado</span>;
  return <div style={{ display: 'flex', gap: '.4rem', alignItems: 'center', flexWrap: 'wrap' }}>
    <span className="badge badge-warn">Sin vincular</span>
    {estadoEmpleado === 'ACTIVO' && <Link to={`/admin/nomina-vinculaciones?idConvenio=${idConvenio}`} style={{ fontSize: '.8rem' }}>Revisar coincidencias</Link>}
  </div>;
}
