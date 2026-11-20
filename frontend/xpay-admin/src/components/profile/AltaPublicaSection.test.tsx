// Datos y transportes sintéticos: estas pruebas no consultan Veriff ni envían mensajes.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AltaPublicaSection, type EstadoAltaPublica } from './AltaPublicaSection.tsx';
import { ApiError, HttpUncertainError } from '../../api/client.ts';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockLogout = vi.fn();
vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});
vi.mock('../../auth/AuthContext.tsx', () => ({ useAuth: () => ({ logout: mockLogout }) }));

const STATE: EstadoAltaPublica = {
  esRegistroPublico: true, habilitadaPiloto: false, emailVerificado: true, whatsAppVerificado: false,
  identidadVerificada: false, estadoKyc: 'NO_INICIADO', estado: 'PENDIENTE', pendientes: ['Verifica tu WhatsApp.'],
};
const CONSENT = {
  documento: { version: 'TEST-C1', fecha: '2026-01-01', url: 'https://app.xpaycol.com/legal/prueba.pdf' },
  textoCasilla: 'Autorizo expresamente la verificación biométrica de prueba.', aceptado: false, habilitado: true,
};
const URL_SESION = 'https://magic.veriff.me/v/synthetic-test-session';
let estado = { ...STATE };
let consentimiento = { ...CONSENT, documento: { ...CONSENT.documento } };
let identidad: { estadoKyc: string; sessionUrl?: string | null };

function mostrar() {
  return render(<MemoryRouter initialEntries={['/mi-wallet/perfil']}><Routes><Route path="/mi-wallet/perfil" element={<AltaPublicaSection />} /><Route path="/login" element={<p>INGRESO_PRUEBA</p>} /></Routes></MemoryRouter>);
}

beforeEach(() => {
  estado = { ...STATE, pendientes: [...STATE.pendientes] };
  consentimiento = { ...CONSENT, documento: { ...CONSENT.documento } };
  identidad = { estadoKyc: 'NO_INICIADO' };
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/usuarios/alta-publica/estado') return Promise.resolve({ success: true, data: estado });
    if (path === '/api/usuarios/alta-publica/consentimiento-kyc') return Promise.resolve({ success: true, data: consentimiento });
    if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: identidad });
    return Promise.reject(new Error('GET no simulado.'));
  });
  mockPost.mockReset(); mockLogout.mockReset();
});

describe('Alta pública desde Perfil', () => {
  it('espera respuestas reconocidas y no inicia verificaciones al cargar', async () => {
    mockGet.mockReturnValue(new Promise(() => {}));
    mostrar();
    expect(screen.getByText('Consultando tu activación...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('oculta la nueva sección cuando el servicio está cerrado por configuración', async () => {
    mockGet.mockRejectedValue(new ApiError('No disponible.', 404));
    mostrar();
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Activación de mi Wallet' })).not.toBeInTheDocument());
    expect(mockPost).not.toHaveBeenCalled();
    expect(mockGet).toHaveBeenCalledExactlyOnceWith('/api/usuarios/alta-publica/estado');
  });

  it('no muestra el recorrido a una cuenta que no es de autorregistro', async () => {
    estado.esRegistroPublico = false;
    mostrar();
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Activación de mi Wallet' })).not.toBeInTheDocument());
  });

  it('distingue cuenta y Wallet y exige el acto explícito de la casilla C', async () => {
    mostrar();
    const casilla = await screen.findByRole('checkbox', { name: CONSENT.textoCasilla });
    expect(casilla).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Guardar autorización' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(screen.getByText('Verifica tu WhatsApp.')).toBeInTheDocument();
    expect(screen.getByText(/Registrarte o verificar tu identidad no asigna un cupo automáticamente/)).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('guarda solo la autorización vigente sin iniciar Veriff por consecuencia', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, data: { ...CONSENT, aceptado: true } });
    mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    await user.click(screen.getByRole('button', { name: 'Guardar autorización' }));
    expect(await screen.findByText('Autorización biométrica registrada.')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/alta-publica/consentimiento-kyc', { autorizaBiometria: true, versionDocumento: 'TEST-C1' });
    expect(screen.getByRole('button', { name: 'Iniciar verificación de identidad' })).toBeEnabled();
  });

  it('con consentimiento y correo verificado inicia solo por el botón y ofrece la sesión recibida', async () => {
    const user = userEvent.setup();
    consentimiento.aceptado = true;
    mockPost.mockResolvedValue({ success: true, data: { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION } });
    mostrar();
    await user.click(await screen.findByRole('button', { name: 'Iniciar verificación de identidad' }));
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/kyc/veriff/session', {});
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', URL_SESION);
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
  });

  it('sin correo verificado conserva cerrada la creación de sesión aunque C esté aceptada', async () => {
    consentimiento.aceptado = true; estado.emailVerificado = false;
    mostrar();
    await screen.findByText('Autorización biométrica registrada.');
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('retoma la misma sesión pendiente sin crear una nueva', async () => {
    consentimiento.aceptado = true; identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION };
    mostrar();
    const enlace = await screen.findByRole('link', { name: 'Continuar verificación' });
    expect(enlace).toHaveAttribute('href', URL_SESION);
    expect(enlace).toHaveAttribute('target', '_blank');
    expect(enlace).toHaveAttribute('rel', 'noopener noreferrer');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each(['javascript:alert(1)', 'https://magic.veriff.me.evil.test/v/prueba', 'http://saas.veriff.com/v/prueba', 'https://user@magic.veriff.me/v/prueba', 'https://magic.veriff.me:444/v/prueba'])('rechaza enlaces de sesión inseguros: %s', async sessionUrl => {
    identidad = { estadoKyc: 'PENDIENTE', sessionUrl };
    mostrar();
    expect(await screen.findByText(/El enlace para continuar no está disponible/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each(['EN_REVISION', 'APROBADO'])('no permite iniciar otra sesión en %s', async estadoKyc => {
    consentimiento.aceptado = true; identidad = { estadoKyc, sessionUrl: URL_SESION };
    mostrar();
    await screen.findByText('Autorización biométrica registrada.');
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('desmarca la casilla si una actualización trae una nueva versión', async () => {
    const user = userEvent.setup();
    mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    consentimiento = { ...consentimiento, documento: { ...consentimiento.documento, version: 'TEST-C2' } };
    await user.click(screen.getByRole('button', { name: 'Actualizar estado de activación' }));
    expect(await screen.findByRole('link', { name: /Leer autorizaciones.*TEST-C2/ })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: CONSENT.textoCasilla })).not.toBeChecked();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('un timeout de inicio pide recuperar estado y no repite el POST', async () => {
    const user = userEvent.setup(); consentimiento.aceptado = true;
    mockPost.mockRejectedValue(new HttpUncertainError('Timeout simulado.'));
    mostrar();
    await user.click(await screen.findByRole('button', { name: 'Iniciar verificación de identidad' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No sabemos todavía si se creó tu sesión.');
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
    identidad = { estadoKyc: 'PENDIENTE', sessionUrl: URL_SESION };
    await user.click(screen.getByRole('button', { name: 'Actualizar estado de activación' }));
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', URL_SESION);
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('con configuración incompleta no ofrece casilla ni inicio', async () => {
    consentimiento.habilitado = false;
    mostrar();
    await screen.findByText('La verificación de identidad estará disponible cuando XPAY complete su configuración.');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación de identidad' })).not.toBeInTheDocument();
  });

  it('si falla una actualización no permite usar la autorización guardada en una vista antigua', async () => {
    const user = userEvent.setup();
    mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    mockGet.mockRejectedValue(new Error('Fallo de lectura simulado.'));
    await user.click(screen.getByRole('button', { name: 'Actualizar estado de activación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos consultar tu proceso de activación.');
    expect(screen.getByRole('button', { name: 'Guardar autorización' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: CONSENT.textoCasilla })).toBeDisabled();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('tras un timeout de consentimiento exige consultar otra vez sin volver a enviarlo', async () => {
    const user = userEvent.setup();
    mockPost.mockRejectedValue(new HttpUncertainError('Timeout simulado.'));
    mostrar();
    await user.click(await screen.findByRole('checkbox', { name: CONSENT.textoCasilla }));
    await user.click(screen.getByRole('button', { name: 'Guardar autorización' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar si se guardó la autorización.');
    expect(screen.getByRole('checkbox', { name: CONSENT.textoCasilla })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Guardar autorización' })).toBeDisabled();
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('la cuenta habilitada vuelve al ingreso sin inventar un token ni modificar permisos locales', async () => {
    const user = userEvent.setup(); estado.habilitadaPiloto = true; estado.idWallet = 901;
    mostrar();
    await user.click(await screen.findByRole('button', { name: 'Ingresar a mi Wallet' }));
    expect(await screen.findByText('INGRESO_PRUEBA')).toBeInTheDocument();
    expect(mockLogout).toHaveBeenCalledExactlyOnceWith();
    expect(mockPost).not.toHaveBeenCalled();
  });
});
