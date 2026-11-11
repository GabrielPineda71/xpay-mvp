import { useState } from 'react';
import { post } from '../api/client.ts';

// D2 — vínculo empleado ↔ usuario (POST api/libranza/admin/empleados/{id}/asociar-usuario).
// El vínculo decide a QUIÉN se acredita el anticipo, así que todas las
// validaciones viven en el backend (LibranzaAnticipoService.AsociarEmpleadoUsuarioAsync):
// documento del empleado = documento de la persona (y el verificado por KYC),
// wallet PERSONA activa del mismo titular, sin vínculos cruzados.
export const AYUDA_VINCULO =
  'El documento del empleado debe coincidir con el del usuario. En un usuario del piloto el documento ' +
  'se establece al aprobarse su verificación de identidad: vincula después de que el KYC esté aprobado.';

interface Props {
  idEmpleado:         number;
  idConvenio:         number;
  estadoEmpleado:     string;
  idUsuarioVinculado: number | null | undefined;
  onVinculado:        () => void;
}

export function VincularEmpleadoUsuario({ idEmpleado, idConvenio, estadoEmpleado, idUsuarioVinculado, onVinculado }: Props) {
  const [abierto,  setAbierto]  = useState(false);
  const [idUsuario, setIdUsuario] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [error,    setError]    = useState('');

  const vinculado = idUsuarioVinculado != null;

  async function vincular() {
    setError('');
    const id = Number(idUsuario);
    if (!Number.isInteger(id) || id <= 0) { setError('Ingresa un ID de usuario válido.'); return; }
    if (vinculado && id === idUsuarioVinculado) { setError('El empleado ya está vinculado a ese usuario.'); return; }
    setEnviando(true);
    try {
      await post(`/api/libranza/admin/empleados/${idEmpleado}/asociar-usuario`, { idUsuario: id, idConvenio });
      setAbierto(false);
      setIdUsuario('');
      onVinculado();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error vinculando el usuario.');
    } finally {
      setEnviando(false);
    }
  }

  const estado = vinculado
    ? <span className="badge badge-ok">Usuario #{idUsuarioVinculado}</span>
    : <span className="badge badge-warn">Sin vincular</span>;

  if (estadoEmpleado !== 'ACTIVO') return estado;

  if (!abierto) return (
    <div style={{ display: 'flex', gap: '0.4rem', alignItems: 'center', flexWrap: 'wrap' }}>
      {estado}
      {!vinculado && (
        <button type="button" className="btn-link" style={{ fontSize: '0.8rem' }} onClick={() => setAbierto(true)}>
          Vincular usuario
        </button>
      )}
    </div>
  );

  return (
    <div style={{ minWidth: '240px' }}>
      <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 600 }}>
        ID del usuario
        <input type="number" min={1} value={idUsuario} onChange={e => setIdUsuario(e.target.value)}
          aria-label={`ID del usuario para el empleado ${idEmpleado}`}
          style={{ display: 'block', width: '100%', padding: '0.3rem 0.5rem', marginTop: '0.2rem' }} />
      </label>
      <p style={{ fontSize: '0.75rem', color: '#718096', margin: '0.3rem 0' }}>{AYUDA_VINCULO}</p>
      {error && <div className="error-msg" role="alert" style={{ fontSize: '0.8rem' }}>{error}</div>}
      <div style={{ display: 'flex', gap: '0.4rem', marginTop: '0.3rem' }}>
        <button type="button" className="btn-link" disabled={enviando} onClick={vincular}>
          {enviando ? 'Vinculando...' : 'Confirmar vínculo'}
        </button>
        <button type="button" className="btn-link" disabled={enviando}
          onClick={() => { setAbierto(false); setError(''); setIdUsuario(''); }}>
          Cancelar
        </button>
      </div>
    </div>
  );
}
