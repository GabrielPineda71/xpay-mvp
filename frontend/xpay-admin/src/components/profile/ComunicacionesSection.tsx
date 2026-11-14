import { useCallback, useEffect, useState } from 'react';
import { get, put } from '../../api/client.ts';

interface EstadoContactos {
  emailVerificado: boolean;
  celularVerificado: boolean;
  canalEmailDisponible: boolean;
  canalWhatsAppDisponible: boolean;
  requeridoParaOperar: boolean;
  servicioWhatsApp: boolean;
  publicidadEmail: boolean;
  publicidadWhatsApp: boolean;
}

type Preferencia = 'servicioWhatsApp' | 'publicidadEmail' | 'publicidadWhatsApp';

// Preferencias de comunicación (GET /api/contactos/estado, PUT
// /api/contactos/preferencias). Separadas de la verificación: verificar un
// contacto no autoriza publicidad, y retirar una autorización no revoca la
// verificación. La publicidad está desactivada mientras el usuario no la active.
export function ComunicacionesSection() {
  const [estado, setEstado] = useState<EstadoContactos | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const r = await get<{ success: boolean; data: EstadoContactos }>('/api/contactos/estado');
      setEstado(r.data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudieron consultar tus preferencias.');
    }
  }, []);

  useEffect(() => { void cargar(); }, [cargar]);

  async function cambiar(campo: Preferencia, valor: boolean) {
    setGuardando(true);
    try {
      const r = await put<{ success: boolean; data: EstadoContactos }>('/api/contactos/preferencias', { [campo]: valor });
      setEstado(r.data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo guardar la preferencia.');
    } finally {
      setGuardando(false);
    }
  }

  return (
    <section className="profile-section" aria-label="Comunicaciones">
      <h3 className="profile-section-title">Comunicaciones</h3>
      {error && <span className="breb-msg-err">{error}</span>}
      {estado && (
        <>
          {estado.requeridoParaOperar && (!estado.emailVerificado || !estado.celularVerificado) && (
            <p className="profile-placeholder-note" data-testid="contactos-requeridos">
              Para enviar dinero, pagar con QR, solicitar anticipos y retirar debes verificar tu correo y tu celular en Mi información.
            </p>
          )}
          <label className="profile-checkbox">
            <input type="checkbox" checked={estado.servicioWhatsApp} disabled={guardando}
              onChange={e => void cambiar('servicioWhatsApp', e.target.checked)} />
            Mensajes de servicio por WhatsApp (códigos y avisos de mi cuenta)
          </label>
          <label className="profile-checkbox">
            <input type="checkbox" checked={estado.publicidadEmail} disabled={guardando}
              onChange={e => void cambiar('publicidadEmail', e.target.checked)} />
            Ofertas y novedades de XPAY por correo
          </label>
          <label className="profile-checkbox">
            <input type="checkbox" checked={estado.publicidadWhatsApp} disabled={guardando}
              onChange={e => void cambiar('publicidadWhatsApp', e.target.checked)} />
            Ofertas y novedades de XPAY por WhatsApp
          </label>
        </>
      )}
    </section>
  );
}
