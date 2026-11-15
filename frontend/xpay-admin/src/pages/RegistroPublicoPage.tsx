import { type FormEvent, useRef, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { ApiError, post } from '../api/client.ts';
import { BrandMark } from '../components/BrandMark.tsx';
import { ambienteXpay } from '../config/ambiente.ts';
import { mensajeErrorIdentidad } from '../utils/erroresIdentidad.ts';
import { LONGITUD_MAXIMA_CLAVE, MENSAJE_CONFIRMACION_DISTINTA, validarPoliticaClave } from '../utils/politicaClave.ts';
import './registro-publico.css';

export function RegistroPublicoPage() {
  return ambienteXpay() === 'QA' ? <FormularioRegistro /> : <Navigate to="/login" replace />;
}

function FormularioRegistro() {
  const enviandoRef = useRef(false);
  const [enviando, setEnviando] = useState(false);
  const [enviado, setEnviado] = useState(false);
  const [error, setError] = useState('');

  async function registrar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (enviandoRef.current) return;

    const formulario = evento.currentTarget;
    const campos = new FormData(formulario);
    const nombreUsuario = String(campos.get('nombreUsuario') ?? '').trim();
    const email = String(campos.get('email') ?? '').trim();
    const celular = String(campos.get('celular') ?? '').trim();
    const password = String(campos.get('password') ?? '');
    const confirmacion = String(campos.get('confirmacion') ?? '');

    if (!nombreUsuario || !email || !celular) {
      setError('Completa todos los campos.');
      return;
    }
    if (password !== confirmacion) {
      setError(MENSAJE_CONFIRMACION_DISTINTA);
      return;
    }
    const errorClave = validarPoliticaClave(password);
    if (errorClave) {
      setError(errorClave);
      return;
    }
    if (password.toLowerCase().includes(nombreUsuario.toLowerCase())) {
      setError('La contraseña no puede contener el nombre de usuario.');
      return;
    }

    enviandoRef.current = true;
    setEnviando(true);
    setError('');
    try {
      await post<unknown>('/api/usuarios/registro', { nombreUsuario, email, celular, password });
      formulario.reset();
      setEnviado(true);
    } catch (err) {
      setError(mensajeErrorIdentidad(err, {
        mostrar400: false,
        incierto: 'No pudimos confirmar el registro. Revisa tu correo antes de volver a intentarlo.',
        generico: err instanceof ApiError && err.status === 400
          ? 'Revisa los datos ingresados o prueba con otro nombre de usuario.'
          : 'No fue posible crear la cuenta. Intenta nuevamente más tarde.',
      }));
    } finally {
      enviandoRef.current = false;
      setEnviando(false);
    }
  }

  return (
    <main className="login-container registro-publico">
      <section className="login-card registro-publico__tarjeta" aria-labelledby="registro-titulo">
        <BrandMark variant="logo" theme="teal" className="registro-publico__marca" />
        <h1 id="registro-titulo">Crear cuenta</h1>
        <p className="registro-publico__nota">Ambiente de pruebas. Usa datos de prueba.</p>
        {enviado ? (
          <p role="status" className="session-message">Registro recibido. Revisa tu correo para activar la cuenta.</p>
        ) : (
          <form onSubmit={registrar} aria-busy={enviando}>
            <label>
              Usuario
              <input name="nombreUsuario" autoComplete="username" autoCapitalize="none" maxLength={100} required />
            </label>
            <label>
              Correo electrónico
              <input name="email" type="email" autoComplete="email" required />
            </label>
            <label>
              Celular
              <input name="celular" type="tel" autoComplete="tel" maxLength={30} required />
            </label>
            <label>
              Contraseña
              <input
                name="password" type="password" autoComplete="new-password"
                aria-describedby="registro-politica-clave" maxLength={LONGITUD_MAXIMA_CLAVE} required
              />
            </label>
            <p id="registro-politica-clave" className="registro-publico__ayuda">
              Usa al menos 8 caracteres, con una mayúscula, una minúscula, un número y un carácter especial.
            </p>
            <label>
              Confirmar contraseña
              <input name="confirmacion" type="password" autoComplete="new-password" maxLength={LONGITUD_MAXIMA_CLAVE} required />
            </label>
            {error && <p role="alert" className="error">{error}</p>}
            <button type="submit" disabled={enviando}>{enviando ? 'Creando cuenta…' : 'Crear cuenta'}</button>
          </form>
        )}
        <div className="registro-publico__enlaces">
          {enviado && <Link to="/reenviar-verificacion">Reenviar correo de verificación</Link>}
          <Link to="/login">Volver a iniciar sesión</Link>
        </div>
      </section>
    </main>
  );
}
