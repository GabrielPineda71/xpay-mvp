// Transportes sintéticos; sin sesiones ni mensajes reales.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VerificacionIdentidadPublicaSection } from './VerificacionIdentidadPublicaSection.tsx';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});
const CONSENT = {
  documento: { version: '1.2', fecha: '2026-11-18', url: 'https://app.xpaycol.com/legal/v1.2/autorizaciones.pdf' },
  textoCasilla: 'Autorizo expresamente la verificación biométrica de prueba.', aceptado: false, habilitado: true,
};
const URL_SESION = 'https://saas.veriff.com/v/synthetic-test-session';
let consentimiento = { ...CONSENT, documento: { ...CONSENT.documento } };
let identidad: { estadoKyc: string; sessionUrl?: string | null };
let correoVerificado = true;
beforeEach(() => {
  consentimiento = { ...CONSENT, documento: { ...CONSENT.documento } };
  identidad = { estadoKyc: 'NO_INICIADO' }; correoVerificado = true;
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/usuarios/alta-publica/consentimiento-kyc') return Promise.resolve({ success: true, data: consentimiento });
    if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: identidad });
    if (path === '/api/usuarios/mi-perfil') return Promise.resolve({ success: true, data: { emailVerificado: correoVerificado } });
    return Promise.reject(new Error('Consulta no simulada.'));
  });
  mockPost.mockReset();
});
function mostrar() { return render(<VerificacionIdentidadPublicaSection />); }
describe('Correo y Veriff para cuentas públicas limitadas', () => {
  it('no envía autorizaciones ni abre sesiones al entrar; WhatsApp no bloquea este recorrido', async () => {
    mostrar();
    expect(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Guardar autorización' })).toBeDisabled();
    expect(screen.getByText(/Verificación pendiente para una próxima etapa. Puedes continuar con Veriff/)).toBeInTheDocument();
    expect(screen.getByText(/Crear la cuenta o aprobar Veriff no activa una Wallet/)).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
    expect(mockGet.mock.calls.every(([path]) => !String(path).includes('/alta-publica/estado'))).toBe(true);
  });
  it('guarda C por acto explícito y versión vigente sin iniciar Veriff', async () => {
    const user = userEvent.setup(); mockPost.mockResolvedValue({ success: true, data: { ...CONSENT, aceptado: true } }); mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    await user.click(screen.getByRole('button', { name: 'Guardar autorización' }));
    expect(await screen.findByText('Autorización biométrica registrada.')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/alta-publica/consentimiento-kyc', { autorizaBiometria: true, versionDocumento: '1.2' });
    expect(screen.getByRole('button', { name: 'Iniciar verificación de identidad' })).toBeEnabled();
  });
  it('inicia con correo y C vigentes; presenta el enlace recibido', async () => {
    const user = userEvent.setup(); consentimiento.aceptado = true;
    mockPost.mockResolvedValue({ success: true, data: { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION } }); mostrar();
    await user.click(await screen.findByRole('button', { name: 'Iniciar verificación de identidad' }));
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/kyc/veriff/session', {});
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', URL_SESION);
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
  });
  it('sin correo confirmado no ofrece autorización ni inicio aunque C figure guardada', async () => {
    correoVerificado = false; consentimiento.aceptado = true; mostrar();
    await screen.findByText('Primero confirma tu correo electrónico en Mi información.');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('retoma la sesión pendiente sin crear otra solicitud', async () => {
    consentimiento.aceptado = true; identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION }; mostrar();
    const enlace = await screen.findByRole('link', { name: 'Continuar verificación' });
    expect(enlace).toHaveAttribute('href', URL_SESION); expect(enlace).toHaveAttribute('target', '_blank');
    expect(enlace).toHaveAttribute('rel', 'noopener noreferrer'); expect(mockPost).not.toHaveBeenCalled();
  });
  it('no recupera una sesión pendiente sin C vigente', async () => {
    identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION }; mostrar(); await screen.findByRole('checkbox');
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it.each(['javascript:alert(1)', 'https://saas.veriff.com.evil.test/v/prueba', 'http://saas.veriff.com/v/prueba', 'https://user@magic.veriff.me/v/prueba', 'https://magic.veriff.me:444/v/prueba'])('no abre una sesión de origen inseguro: %s', async sessionUrl => {
    consentimiento.aceptado = true; identidad = { estadoKyc: 'PENDIENTE', sessionUrl }; mostrar();
    expect(await screen.findByText(/El enlace para continuar no está disponible/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
  });
  it.each(['EN_REVISION', 'APROBADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'])('no ofrece un intento automático en %s', async estadoKyc => {
    consentimiento.aceptado = true; identidad = { estadoKyc, sessionUrl: URL_SESION }; mostrar();
    await screen.findByText('Autorización biométrica registrada.');
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('la aprobación no ofrece activación financiera', async () => {
    consentimiento.aceptado = true; identidad = { estadoKyc: 'APROBADO' }; mostrar();
    expect(await screen.findByText(/Tu identidad fue aprobada. La activación de Wallet/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Wallet|anticipo|cupo/i })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('el flag cerrado conserva cerrada la sesión', async () => {
    consentimiento.habilitado = false; consentimiento.aceptado = true; mostrar();
    await screen.findByText('La verificación de identidad estará disponible cuando XPAY complete su configuración.');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
  });
  it('no acepta un PDF diferente aunque esté en el mismo dominio', async () => {
    consentimiento.documento.url = 'https://app.xpaycol.com/legal/otro.pdf'; mostrar();
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos cargar el documento');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Leer autorizaciones/ })).not.toBeInTheDocument();
  });
  it('tras una lectura fallida no utiliza una sesión anterior', async () => {
    const user = userEvent.setup(); consentimiento.aceptado = true; identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION }; mostrar();
    await screen.findByRole('link', { name: 'Continuar verificación' }); mockGet.mockRejectedValue(new Error('Fallo simulado.'));
    await user.click(screen.getByRole('button', { name: 'Actualizar estado de verificación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos consultar tu verificación.');
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('un timeout de sesión se recupera consultando sin repetir el POST', async () => {
    const user = userEvent.setup(); consentimiento.aceptado = true; mockPost.mockRejectedValue(new HttpUncertainError('Timeout simulado.')); mostrar();
    await user.click(await screen.findByRole('button', { name: 'Iniciar verificación de identidad' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No sabemos todavía si se creó tu sesión.');
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION };
    await user.click(screen.getByRole('button', { name: 'Actualizar estado de verificación' }));
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', URL_SESION); expect(mockPost).toHaveBeenCalledTimes(1);
  });
  it('un timeout al guardar C exige consultar antes de reintentar', async () => {
    const user = userEvent.setup(); mockPost.mockRejectedValue(new HttpUncertainError('Timeout simulado.')); mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    await user.click(screen.getByRole('button', { name: 'Guardar autorización' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar si se guardó la autorización.');
    expect(screen.queryByRole('button', { name: 'Guardar autorización' })).not.toBeInTheDocument(); expect(mockPost).toHaveBeenCalledTimes(1);
  });
  it('un servicio aún no publicado conserva el Perfil sin inventar estado', async () => {
    mockGet.mockRejectedValue(new ApiError('No disponible.', 404)); mostrar();
    expect(await screen.findByRole('alert')).toHaveTextContent('Puedes conservar y actualizar tu Perfil');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
});
