import { useRef, useState, type FormEvent } from 'react';
import { ApiError, post } from '../../api/client.ts';
import type { MiPerfil } from '../../hooks/useMiPerfil.ts';

export function CompletarPrimerApellidoSection({ perfil, onActualizado }: {
  perfil: MiPerfil;
  onActualizado: () => void;
}) {
  const [apellido, setApellido] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [requiereActualizar, setRequiereActualizar] = useState(false);
  const [guardado, setGuardado] = useState(false);
  const enviando = useRef(false);
  if (perfil.puedeCompletarPrimerApellido !== true || perfil.primerApellido?.trim()
    || !perfil.emailVerificado || !perfil.identidadVerificada || perfil.estadoKycActual !== 'APROBADO') return null;

  async function guardar(event: FormEvent) {
    event.preventDefault();
    if (enviando.current || requiereActualizar || guardado) return;
    const normalizado = apellido.normalize('NFC').trim();
    const contieneControles = Array.from(apellido).some(c => {
      const codigo = c.charCodeAt(0);
      return codigo <= 31 || (codigo >= 127 && codigo <= 159);
    });
    if (!normalizado || normalizado.length > 100 || contieneControles) {
      setError('Escribe tu primer apellido de hasta 100 caracteres, tal como aparece en tu documento.');
      return;
    }
    enviando.current = true; setOcupado(true); setError('');
    try {
      const response = await post<{ success: boolean; data: MiPerfil }>('/api/usuarios/mi-perfil/primer-apellido', {
        primerApellido: normalizado,
      });
      if (!response.success || !response.data?.primerApellido?.trim()
        || response.data.puedeCompletarPrimerApellido !== false) throw new Error('Estado no confirmado.');
      setGuardado(true);
      onActualizado();
    } catch (err) {
      const corregible = err instanceof ApiError && err.status === 400;
      setRequiereActualizar(!corregible);
      setError(corregible ? err.message : 'No pudimos confirmar el guardado. Actualiza tu perfil antes de volver a intentarlo.');
    } finally { enviando.current = false; setOcupado(false); }
  }

  return <div className="profile-first-surname">
    <h4 className="profile-section-subtitle">Completa tu primer apellido</h4>
    {guardado ? <p role="status">Primer apellido guardado.</p> : <form className="profile-edit-form" onSubmit={e => void guardar(e)}>
      <p>Confirma tu primer apellido tal como aparece en tu documento. Si es compuesto, escríbelo completo.</p>
      <label>Primer apellido
        <input value={apellido} onChange={e => setApellido(e.target.value)} maxLength={100} required
          autoComplete="family-name" disabled={ocupado || requiereActualizar} />
      </label>
      {error && <p role="alert">{error}</p>}
      {requiereActualizar
        ? <button key="actualizar" type="button" className="wallet-send-link-btn" onClick={onActualizado}>Actualizar perfil</button>
        : <button key="guardar" type="submit" className="btn-breb" disabled={ocupado || !apellido.trim()}>{ocupado ? 'Guardando...' : 'Guardar primer apellido'}</button>}
    </form>}
  </div>;
}
