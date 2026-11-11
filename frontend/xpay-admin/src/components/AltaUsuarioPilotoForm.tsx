import { FormEvent, useState } from 'react';
import { ApiError, HttpUncertainError, post } from '../api/client.ts';

// D1 — alta controlada de usuario del piloto (POST api/admin/usuarios/piloto).
// Las versiones legales y la referencia de evidencia NO las propone XPAY: las
// define la asesoría legal del propietario. El backend solo valida formato
// (sin catálogo de versiones), así que este formulario no sugiere valores.
export interface AltaPilotoResultado {
  idUsuario: number;
  idWallet:  number;
  siguiente: string;
}

interface AltaPilotoResp { success: boolean; data: AltaPilotoResultado }

interface Campos {
  nombreUsuario:          string;
  email:                  string;
  celular:                string;
  versionTerminos:        string;
  versionPoliticaDatos:   string;
  versionAutorizacionKyc: string;
  fechaAceptacion:        string; // datetime-local (hora local del navegador)
  referenciaEvidencia:    string;
}

const VACIO: Campos = {
  nombreUsuario: '', email: '', celular: '',
  versionTerminos: '', versionPoliticaDatos: '', versionAutorizacionKyc: '',
  fechaAceptacion: '', referenciaEvidencia: '',
};

// Mismas reglas que AltaUsuarioPilotoService.Validar (el backend es la autoridad).
const PATRON_REFERENCIA = /^[A-Za-z0-9._:/-]+$/;
const MAX_VERSION       = 50;
const MAX_REFERENCIA    = 200;

export const MENSAJE_ALTA_CERRADA =
  'El alta de usuarios del piloto está cerrada en el servidor. Debe habilitarse por configuración antes de usar este formulario.';
export const MENSAJE_SOLO_SUPERUSUARIO = 'Solo un SUPERUSUARIO puede dar de alta usuarios del piloto.';
export const MENSAJE_INCIERTO =
  'No se recibió respuesta a tiempo y no sabemos si el alta se completó. Busca el usuario en la lista antes de intentarlo de nuevo.';

export function validarAltaPiloto(c: Campos, ahora: Date): string | null {
  if (!c.nombreUsuario.trim()) return 'El nombre de usuario es obligatorio.';
  if (!c.email.trim()) return 'El email es obligatorio.';
  if (!c.celular.trim()) return 'El celular es obligatorio.';
  const versiones: [string, string][] = [
    [c.versionTerminos, 'términos y condiciones'],
    [c.versionPoliticaDatos, 'política de tratamiento de datos'],
    [c.versionAutorizacionKyc, 'autorización KYC'],
  ];
  for (const [v, doc] of versiones) {
    const t = v.trim();
    if (!t || t.length > MAX_VERSION || !PATRON_REFERENCIA.test(t))
      return `La versión aceptada de ${doc} es obligatoria (máx. ${MAX_VERSION} caracteres: letras, dígitos y . _ : / -).`;
  }
  if (!c.fechaAceptacion) return 'La fecha de aceptación es obligatoria.';
  const fecha = new Date(c.fechaAceptacion);
  if (Number.isNaN(fecha.getTime())) return 'La fecha de aceptación no es válida.';
  if (fecha.getTime() > ahora.getTime() + 5 * 60_000) return 'La fecha de aceptación no puede ser futura.';
  const ref = c.referenciaEvidencia.trim();
  if (!ref || ref.length > MAX_REFERENCIA || !PATRON_REFERENCIA.test(ref))
    return `La referencia de evidencia es obligatoria (máx. ${MAX_REFERENCIA} caracteres: letras, dígitos y . _ : / -).`;
  return null;
}

export function AltaUsuarioPilotoForm({ onCreado }: { onCreado: (r: AltaPilotoResultado) => void }) {
  const [campos,     setCampos]     = useState<Campos>(VACIO);
  const [confirmado, setConfirmado] = useState(false);
  const [enviando,   setEnviando]   = useState(false);
  const [error,      setError]      = useState('');

  function set<K extends keyof Campos>(k: K, v: string) {
    setCampos(c => ({ ...c, [k]: v }));
  }

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setError('');
    const invalido = validarAltaPiloto(campos, new Date());
    if (invalido) { setError(invalido); return; }
    if (!confirmado) { setError('Debes confirmar que la aceptación y su evidencia existen.'); return; }

    setEnviando(true);
    try {
      const r = await post<AltaPilotoResp>('/api/admin/usuarios/piloto', {
        nombreUsuario:          campos.nombreUsuario.trim(),
        email:                  campos.email.trim(),
        celular:                campos.celular.trim(),
        versionTerminos:        campos.versionTerminos.trim(),
        versionPoliticaDatos:   campos.versionPoliticaDatos.trim(),
        versionAutorizacionKyc: campos.versionAutorizacionKyc.trim(),
        fechaAceptacionUtc:     new Date(campos.fechaAceptacion).toISOString(),
        referenciaEvidencia:    campos.referenciaEvidencia.trim(),
      });
      setCampos(VACIO);
      setConfirmado(false);
      onCreado(r.data);
    } catch (err) {
      if (err instanceof HttpUncertainError) setError(MENSAJE_INCIERTO);
      else if (err instanceof ApiError && err.status === 404) setError(MENSAJE_ALTA_CERRADA);
      else if (err instanceof ApiError && err.status === 403) setError(MENSAJE_SOLO_SUPERUSUARIO);
      else setError(err instanceof Error ? err.message : 'Error en el alta del usuario.');
    } finally {
      setEnviando(false);
    }
  }

  const campo = (k: keyof Campos, etiqueta: string, tipo = 'text', ayuda?: string) => (
    <div className="filter-field" style={{ minWidth: '240px' }}>
      <label htmlFor={`alta-${k}`}>{etiqueta}</label>
      <input id={`alta-${k}`} type={tipo} value={campos[k]} onChange={e => set(k, e.target.value)} />
      {ayuda && <span style={{ fontSize: '0.75rem', color: '#718096' }}>{ayuda}</span>}
    </div>
  );

  return (
    <form onSubmit={enviar} aria-label="Alta de usuario del piloto"
      style={{ background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '1rem 1.25rem', marginBottom: '1.5rem' }}>
      <h3 style={{ marginTop: 0 }}>Alta de usuario del piloto</h3>
      <p style={{ fontSize: '0.85rem', color: '#4a5568' }}>
        Crea el usuario, su persona y su wallet. Nace sin clave utilizable y con KYC no iniciado: después emite la
        clave temporal desde su detalle y el usuario debe completar la verificación de identidad.
      </p>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem' }}>
        {campo('nombreUsuario', 'Nombre de usuario')}
        {campo('email', 'Email', 'email', 'Debe ser distinto de cualquier email ya registrado.')}
        {campo('celular', 'Celular', 'tel')}
      </div>

      <h4 style={{ margin: '1rem 0 0.25rem' }}>Aceptación de documentos legales</h4>
      <p role="note" style={{ fontSize: '0.8rem', color: '#92400e', background: '#fffbeb', border: '1px solid #fcd34d', borderRadius: '4px', padding: '0.5rem 0.75rem' }}>
        Decisión de la asesoría legal: las versiones, la fecha y la referencia de evidencia deben corresponder a
        documentos aprobados y aceptados por el usuario. XPAY no propone valores ni los contrasta con un catálogo;
        solo valida su formato y los registra en auditoría.
      </p>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem' }}>
        {campo('versionTerminos', 'Versión de términos y condiciones')}
        {campo('versionPoliticaDatos', 'Versión de política de datos')}
        {campo('versionAutorizacionKyc', 'Versión de autorización KYC')}
        {campo('fechaAceptacion', 'Fecha y hora de aceptación', 'datetime-local')}
        {campo('referenciaEvidencia', 'Referencia de la evidencia', 'text', 'Identificador del documento firmado. Sin datos personales.')}
      </div>

      <label style={{ display: 'block', margin: '0.75rem 0', fontSize: '0.85rem' }}>
        <input type="checkbox" checked={confirmado} onChange={e => setConfirmado(e.target.checked)} />{' '}
        Confirmo que el usuario aceptó estos documentos y que la evidencia indicada existe.
      </label>

      {error && <div className="error-msg" role="alert" style={{ marginBottom: '0.75rem' }}>{error}</div>}
      <button type="submit" className="btn-search" disabled={enviando}>
        {enviando ? 'Creando...' : 'Crear usuario del piloto'}
      </button>
    </form>
  );
}
