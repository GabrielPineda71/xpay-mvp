import { useEffect, useState, type FormEvent } from 'react';
import { useMiPerfil, type MiPerfil } from '../../hooks/useMiPerfil.ts';

// XPAY-401 — fecha_nacimiento es una columna DATE pura (sin componente
// horario) — NO debe pasar por fmtDate (utils.ts), que asume timestamps con
// "Z" y convierte a hora de Bogotá (ver comentario de fmtDate: exactamente
// el caso que advierte no usar, porque un desplazamiento de huso horario
// podría mover la fecha calendario un día). Formateo puramente de texto,
// sin construir ningún Date.
function fmtFechaNacimiento(iso: string | null): string {
  if (!iso) return '—';
  const datePart = iso.split('T')[0];
  const [y, m, d] = datePart.split('-');
  if (!y || !m || !d) return '—';
  return `${d}/${m}/${y}`;
}

function nombreCompleto(p: MiPerfil): string {
  const partes = [p.primerNombre, p.segundoNombre, p.primerApellido, p.segundoApellido]
    .filter((v): v is string => !!v && v.trim().length > 0);
  return partes.length > 0 ? partes.join(' ') : '—';
}

function textoDocumento(p: MiPerfil): string {
  if (!p.tipoDocumento && !p.numeroDocumento) return '—';
  return [p.tipoDocumento, p.numeroDocumento].filter(Boolean).join(' ');
}

// Estado de edición: solo los 5 campos editables (XPAY-399). Celular nunca
// se envía vacío (columna NOT NULL) — el resto puede limpiarse ("").
interface FormEditable {
  celular: string;
  email: string;
  direccion: string;
  ciudad: string;
  departamento: string;
}

function formDesdePerfil(p: MiPerfil): FormEditable {
  return {
    celular: p.celular,
    email: p.email ?? '',
    direccion: p.direccion ?? '',
    ciudad: p.ciudad ?? '',
    departamento: p.departamento ?? '',
  };
}

// XPAY-401 — sección "Mi información" de Perfil. Consume EXCLUSIVAMENTE
// GET/PATCH /api/usuarios/mi-perfil (XPAY-399, sin cambios de backend).
// Identidad legal (nombre/apellido/documento/fecha de nacimiento/país) es
// SIEMPRE de solo lectura aquí — no existe ningún camino en este componente
// para enviarla al backend (el estado de edición ni siquiera la incluye).
export function MiInformacionSection() {
  const { perfil, loading, loadError, guardando, mensaje, setMensaje, cargar, actualizar } = useMiPerfil();
  const [editando, setEditando] = useState(false);
  const [form, setForm] = useState<FormEditable | null>(null);
  const [errorLocal, setErrorLocal] = useState<string | null>(null);

  useEffect(() => { void cargar(); }, [cargar]);

  function iniciarEdicion() {
    if (!perfil) return;
    setForm(formDesdePerfil(perfil));
    setErrorLocal(null);
    setMensaje(null);
    setEditando(true);
  }

  function cancelarEdicion() {
    // XPAY-401 PASO 5 — Cancelar restaura los valores cargados y NO llama
    // backend: simplemente se descarta `form` y se sale del modo edición.
    setForm(null);
    setErrorLocal(null);
    setEditando(false);
  }

  async function handleGuardar(e: FormEvent) {
    e.preventDefault();
    if (!form) return;

    if (!form.celular.trim()) {
      setErrorLocal('El celular no puede quedar vacío.');
      return;
    }
    setErrorLocal(null);

    // Se envían los 5 campos editables tal como están en el formulario — el
    // backend ya es idempotente ante valores sin cambio real (XPAY-399:
    // no genera auditoría engañosa si nada cambió). Nunca se agrega ningún
    // campo adicional (identidad/KYC/IDs) a este objeto.
    const ok = await actualizar({
      celular: form.celular,
      email: form.email,
      direccion: form.direccion,
      ciudad: form.ciudad,
      departamento: form.departamento,
    });
    if (ok) {
      setEditando(false);
      setForm(null);
    }
    // En error: se mantiene `editando=true` y `form` intacto (XPAY-401 PASO 7).
  }

  if (loading) {
    return (
      <section className="profile-section" aria-label="Mi información">
        <h3 className="profile-section-title">Mi información</h3>
        <div className="loading">Cargando tu información...</div>
      </section>
    );
  }

  if (loadError || !perfil) {
    return (
      <section className="profile-section" aria-label="Mi información">
        <h3 className="profile-section-title">Mi información</h3>
        <div className="profile-load-error">
          <p>{loadError ?? 'No se pudo cargar tu información.'}</p>
          <button type="button" className="wallet-send-link-btn" onClick={() => void cargar()}>
            Reintentar
          </button>
        </div>
      </section>
    );
  }

  return (
    <section className="profile-section" aria-label="Mi información">
      <h3 className="profile-section-title">Mi información</h3>

      {/* Identidad legal — SIEMPRE solo lectura, sin excepción. */}
      <dl className="profile-data-list">
        <dt>Nombre</dt><dd>{nombreCompleto(perfil)}</dd>
        <dt>Documento</dt><dd>{textoDocumento(perfil)}</dd>
        <dt>Fecha de nacimiento</dt><dd>{fmtFechaNacimiento(perfil.fechaNacimiento)}</dd>
        <dt>País</dt><dd>{perfil.pais || '—'}</dd>
      </dl>
      <p className={perfil.identidadVerificada ? 'profile-identity-verified' : 'profile-identity-pending'}>
        {perfil.identidadVerificada ? 'Identidad verificada' : 'Identidad pendiente de verificación'}
      </p>

      <h4 className="profile-section-subtitle">Datos de contacto</h4>

      {!editando ? (
        <>
          <dl className="profile-data-list">
            <dt>Celular</dt>
            <dd>
              {perfil.celular}{' '}
              <span className="profile-verificado-badge">{perfil.celularVerificado ? 'Verificado' : 'No verificado'}</span>
            </dd>
            <dt>Email</dt>
            <dd>
              {perfil.email ?? '—'}{' '}
              <span className="profile-verificado-badge">{perfil.emailVerificado ? 'Verificado' : 'No verificado'}</span>
            </dd>
            <dt>Dirección</dt><dd>{perfil.direccion ?? '—'}</dd>
            <dt>Ciudad</dt><dd>{perfil.ciudad ?? '—'}</dd>
            <dt>Departamento</dt><dd>{perfil.departamento ?? '—'}</dd>
          </dl>
          <button type="button" className="btn-breb" onClick={iniciarEdicion}>
            Editar información
          </button>
        </>
      ) : (
        <form className="profile-edit-form" onSubmit={e => void handleGuardar(e)}>
          <label>
            Celular
            <input
              type="text"
              value={form!.celular}
              onChange={e => setForm(f => f && { ...f, celular: e.target.value })}
              required
            />
          </label>
          <label>
            Email
            <input
              type="email"
              value={form!.email}
              onChange={e => setForm(f => f && { ...f, email: e.target.value })}
              placeholder="Dejar vacío para quitar tu email"
            />
          </label>
          <label>
            Dirección
            <input
              type="text"
              value={form!.direccion}
              onChange={e => setForm(f => f && { ...f, direccion: e.target.value })}
            />
          </label>
          <label>
            Ciudad
            <input
              type="text"
              value={form!.ciudad}
              onChange={e => setForm(f => f && { ...f, ciudad: e.target.value })}
            />
          </label>
          <label>
            Departamento
            <input
              type="text"
              value={form!.departamento}
              onChange={e => setForm(f => f && { ...f, departamento: e.target.value })}
            />
          </label>

          {errorLocal && <span className="breb-msg-err">{errorLocal}</span>}
          {mensaje && !mensaje.ok && <span className="breb-msg-err">{mensaje.text}</span>}

          <div className="profile-edit-actions">
            <button type="submit" className="btn-breb" disabled={guardando}>
              {guardando ? 'Guardando...' : 'Guardar cambios'}
            </button>
            <button type="button" className="wallet-send-link-btn" onClick={cancelarEdicion} disabled={guardando}>
              Cancelar
            </button>
          </div>
        </form>
      )}

      {mensaje && mensaje.ok && (
        <p className="profile-save-feedback" role="status">{mensaje.text}</p>
      )}
    </section>
  );
}
