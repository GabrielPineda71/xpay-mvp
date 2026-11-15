import { useState, type FormEvent } from 'react';
import { post, ApiError } from '../../api/client.ts';

interface Msg { ok: boolean; text: string; }

const soloDigitos = (v: string) => v.replace(/\D/g, '').slice(0, 6);

function textoError(err: unknown, porDefecto: string): string {
  if (err instanceof ApiError && err.status === 429 && err.message.startsWith('Too many'))
    return 'Has realizado varios intentos. Intenta nuevamente más tarde.';
  return (err instanceof Error && err.message) || porDefecto;
}

// Verificación de un contacto con código de un solo uso:
//   email   -> POST /api/contactos/email/codigo   (correo desde XPAY)
//   celular -> POST /api/contactos/celular/codigo (plantilla de WhatsApp)
// y POST /api/contactos/{canal}/verificar con el código. El backend decide
// todo (vigencia, intentos, reenvíos); el código nunca se guarda en storage.
// Para WhatsApp se pide la autorización de mensajes de SERVICIO; la
// publicidad se gestiona aparte en «Comunicaciones».
export function VerificarContacto({ canal, onVerificado }: { canal: 'email' | 'celular'; onVerificado: () => void }) {
  const [abierto, setAbierto] = useState(false);
  const [enviado, setEnviado] = useState(false);
  const [aceptaWhatsApp, setAceptaWhatsApp] = useState(false);
  const [codigo, setCodigo] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [mensaje, setMensaje] = useState<Msg | null>(null);

  const esCelular = canal === 'celular';

  async function solicitar() {
    if (esCelular && !aceptaWhatsApp) {
      setMensaje({ ok: false, text: 'Para enviarte el código por WhatsApp debes autorizar los mensajes de servicio.' });
      return;
    }
    setEnviando(true);
    setMensaje(null);
    try {
      const r = await post<{ success: boolean; message?: string }>(`/api/contactos/${canal}/codigo`, {
        aceptaServicioWhatsApp: esCelular && aceptaWhatsApp,
      });
      setEnviado(true);
      setCodigo('');
      setMensaje({ ok: true, text: r.message ?? 'Te enviamos un código.' });
    } catch (err) {
      setMensaje({ ok: false, text: textoError(err, 'No pudimos enviar el código. Intenta nuevamente.') });
    } finally {
      setEnviando(false);
    }
  }

  async function confirmar(e: FormEvent) {
    e.preventDefault();
    if (codigo.length !== 6) {
      setMensaje({ ok: false, text: 'Escribe el código de 6 dígitos.' });
      return;
    }
    setEnviando(true);
    setMensaje(null);
    try {
      await post<{ success: boolean; message?: string }>(`/api/contactos/${canal}/verificar`, { codigo });
      setCodigo('');
      setAbierto(false);
      setEnviado(false);
      onVerificado();
    } catch (err) {
      setCodigo('');
      setMensaje({ ok: false, text: textoError(err, 'No se pudo verificar el código. Intenta nuevamente.') });
    } finally {
      setEnviando(false);
    }
  }

  function cerrar() {
    setAbierto(false);
    setEnviado(false);
    setCodigo('');
    setMensaje(null);
  }

  if (!abierto) {
    return (
      <button type="button" className="wallet-send-link-btn" onClick={() => setAbierto(true)}>
        {esCelular ? 'Verificar celular' : 'Verificar correo'}
      </button>
    );
  }

  return (
    <div className="profile-edit-form" data-testid={`verificar-${canal}`}>
      {esCelular && !enviado && (
        <label className="profile-checkbox">
          <input type="checkbox" checked={aceptaWhatsApp} onChange={e => setAceptaWhatsApp(e.target.checked)} />
          Autorizo a XPAY a enviarme por WhatsApp mensajes de servicio (códigos de verificación y avisos de mi cuenta). No incluye publicidad.
        </label>
      )}

      {!enviado ? (
        <div className="profile-edit-actions">
          <button type="button" className="btn-breb" onClick={() => void solicitar()} disabled={enviando}>
            {enviando ? 'Enviando...' : esCelular ? 'Enviar código por WhatsApp' : 'Enviar código al correo'}
          </button>
          <button type="button" className="wallet-send-link-btn" onClick={cerrar} disabled={enviando}>Cancelar</button>
        </div>
      ) : (
        <form onSubmit={e => void confirmar(e)} autoComplete="off" noValidate>
          <label>
            Código de verificación
            <input
              type="text" inputMode="numeric" maxLength={6} autoComplete="one-time-code"
              value={codigo}
              onChange={e => setCodigo(soloDigitos(e.target.value))}
            />
          </label>
          <div className="profile-edit-actions">
            <button type="submit" className="btn-breb" disabled={enviando}>
              {enviando ? 'Verificando...' : 'Confirmar código'}
            </button>
            <button type="button" className="wallet-send-link-btn" onClick={() => void solicitar()} disabled={enviando}>
              Reenviar código
            </button>
            <button type="button" className="wallet-send-link-btn" onClick={cerrar} disabled={enviando}>Cancelar</button>
          </div>
        </form>
      )}

      {mensaje && (
        mensaje.ok
          ? <p className="profile-save-feedback" role="status">{mensaje.text}</p>
          : <span className="breb-msg-err">{mensaje.text}</span>
      )}
    </div>
  );
}
