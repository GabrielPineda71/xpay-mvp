import { SolicitudEnlaceNeutra } from '../components/identidad/SolicitudEnlaceNeutra.tsx';

// Mismo texto que RegistroPublicoController.MensajeReenvioRecibido.
const MENSAJE_REENVIO_NEUTRO = 'Si la cuenta está pendiente de verificación, enviaremos un nuevo enlace a su correo registrado.';

// P0-B frontend — /reenviar-verificacion (pública): POST
// /api/usuarios/reenviar-verificacion con { usuario }. Respuesta neutra: no
// revela si el usuario existe ni si está pendiente de verificación.
export function ReenviarVerificacionPage() {
  return (
    <SolicitudEnlaceNeutra
      subtitulo="Reenviar verificación de correo"
      descripcion="Ingresa tu usuario para recibir un nuevo enlace de verificación en tu correo registrado."
      endpoint="/api/usuarios/reenviar-verificacion"
      mensajeNeutro={MENSAJE_REENVIO_NEUTRO}
      textoBoton="Reenviar enlace"
    />
  );
}
