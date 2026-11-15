import { type FormEvent, useRef, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { ApiError, post } from '../api/client.ts';
import { BrandMark } from '../components/BrandMark.tsx';
import { ambienteXpay } from '../config/ambiente.ts';
import { type ConfiguracionRegistroPublico, type DocumentoRegistro, useConfiguracionRegistroPublico } from '../hooks/useConfiguracionRegistroPublico.ts';
import { mensajeErrorIdentidad } from '../utils/erroresIdentidad.ts';
import { LONGITUD_MAXIMA_CLAVE, MENSAJE_CONFIRMACION_DISTINTA, validarPoliticaClave } from '../utils/politicaClave.ts';
import './registro-publico.css';

export function RegistroPublicoPage() {
  const configuracion = useConfiguracionRegistroPublico();
  if (ambienteXpay() !== 'QA') return <Navigate to="/login" replace />;
  if (configuracion.estado === 'disponible') return <FormularioRegistro documentos={configuracion.documentos} />;

  return (
    <main className="login-container registro-publico">
      <section className="login-card registro-publico__tarjeta" aria-labelledby="registro-titulo">
        <BrandMark variant="logo" theme="teal" className="registro-publico__marca" />
        <h1 id="registro-titulo">Crear cuenta</h1>
        <p role="status">
          {configuracion.estado === 'cargando' ? 'Consultando disponibilidad del registro…' : 'El registro no está disponible en este momento.'}
        </p>
        <div className="registro-publico__enlaces"><Link to="/login">Volver a iniciar sesión</Link></div>
      </section>
    </main>
  );
}

const fechaDocumento = new Intl.DateTimeFormat('es-CO', { dateStyle: 'long', timeZone: 'UTC' });

function EnlaceDocumento({ nombre, documento }: { nombre: string; documento: DocumentoRegistro }) {
  return (
    <p>
      <a href={documento.url} target="_blank" rel="noopener noreferrer">{nombre}</a>
      <span>Versión {documento.version} · <time dateTime={documento.fecha}>{fechaDocumento.format(new Date(documento.fecha))}</time></span>
    </p>
  );
}

function FormularioRegistro({ documentos }: { documentos: ConfiguracionRegistroPublico }) {
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
    if (!campos.has('aceptaTerminos') || !campos.has('autorizaTratamientoDatos')) {
      setError('Acepta los términos y autoriza el tratamiento de datos para crear la cuenta.');
      return;
    }

    enviandoRef.current = true;
    setEnviando(true);
    setError('');
    try {
      await post<unknown>('/api/usuarios/registro', {
        nombreUsuario, email, celular, password,
        consentimiento: {
          aceptaTerminos: true,
          versionTerminos: documentos.terminos.version,
          autorizaTratamientoDatos: true,
          versionPoliticaDatos: documentos.politicaDatos.version,
        },
      });
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
        <p className="registro-publico__nota">Ambiente de pruebas. Usa datos de prueba y un correo autorizado de pruebas.</p>
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
            <fieldset className="registro-publico__consentimientos">
              <legend>Consentimientos de registro</legend>
              <div className="registro-publico__documentos">
                <EnlaceDocumento nombre="Términos y Condiciones" documento={documentos.terminos} />
                <EnlaceDocumento nombre="Política de Tratamiento de Datos Personales" documento={documentos.politicaDatos} />
              </div>
              <label>
                <input name="aceptaTerminos" type="checkbox" required />
                <span>
                  Declaro que tengo dieciocho (18) años cumplidos, que la información suministrada es veraz y que he leído
                  y acepto los Términos y Condiciones de XPAY, versión {documentos.terminos.version}.
                </span>
              </label>
              <label>
                <input name="autorizaTratamientoDatos" type="checkbox" required />
                <span>
                  He leído la Política de Tratamiento de Datos Personales, versión {documentos.politicaDatos.version} publicada,
                  y autorizo de manera previa, expresa e informada a X PAY S.A.S. para tratar mis datos de identificación,
                  contacto, cuenta y seguridad con el fin de crear y administrar mi acceso, validar mi mayoría de edad,
                  verificar mi correo, prevenir fraude, atender solicitudes y gestionar el proceso de verificación de identidad,
                  en los términos descritos en dicha política.
                </span>
              </label>
              <p>
                Conozco mis derechos de acceso, actualización, rectificación, revocatoria y supresión cuando procedan,
                y puedo ejercerlos mediante <a href="mailto:info@xpaycol.com">info@xpaycol.com</a>.
              </p>
            </fieldset>
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
