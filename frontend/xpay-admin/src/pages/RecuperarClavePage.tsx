import { SolicitudEnlaceNeutra } from '../components/identidad/SolicitudEnlaceNeutra.tsx';

// Mismo texto que RecuperacionClaveController.MensajeSolicitudRecibida.
const MENSAJE_RECUPERACION_NEUTRO = 'Si la cuenta puede recuperarse, enviaremos un enlace a su correo registrado.';

// P0-B frontend — /recuperar-clave (pública): POST /api/auth/recuperar-clave
// con { usuario }. Respuesta neutra: no revela si el usuario existe.
export function RecuperarClavePage() {
  return (
    <SolicitudEnlaceNeutra
      subtitulo="Recuperar contraseña"
      descripcion="Ingresa tu usuario para recibir un enlace de recuperación en tu correo registrado."
      endpoint="/api/auth/recuperar-clave"
      mensajeNeutro={MENSAJE_RECUPERACION_NEUTRO}
      textoBoton="Enviar enlace"
    />
  );
}
