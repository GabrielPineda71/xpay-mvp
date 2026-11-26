import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
import { SolicitarCupoPanel } from './SolicitarCupoPanel.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('../../api/client.ts', async () => ({
  ...await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts'),
  get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args),
}));
const BASE = '/api/cartera-ordinaria';
const TEXTO = { version: 'V1-SINTETICA', texto: 'Texto vigente recibido del servidor.\n\nAUTORIZACIÓN SINTÉTICA.', hash: 'a'.repeat(64) };
const RECIBIDA = { idSolicitud: 71, estadoSolicitud: 'RECIBIDA', decisionCrediticia: 'PENDIENTE', consentimientoRegistrado: false };
let actual: Record<string, unknown> | null;
let disponibilidad: { disponible: boolean; habilitado: boolean; requierePrimerApellido: boolean; estado: string; mensaje: string };

beforeEach(() => {
  mockGet.mockReset(); mockPost.mockReset(); sessionStorage.clear(); localStorage.clear();
  actual = null;
  disponibilidad = { disponible: true, habilitado: true, requierePrimerApellido: false, estado: 'DISPONIBLE', mensaje: 'Puedes solicitar la evaluación de tu cupo.' };
  mockGet.mockImplementation((url: string) => {
    if (url === `${BASE}/disponibilidad`) return Promise.resolve(disponibilidad);
    if (url === `${BASE}/mi-solicitud-actual`) return Promise.resolve({ solicitud: actual });
    if (url === `${BASE}/autorizacion-consulta-riesgo/texto-vigente`) return Promise.resolve(TEXTO);
    return Promise.reject(new Error('GET fuera del contrato de pruebas.'));
  });
});
const renderPanel = (onCupoActualizado = vi.fn(), actor = '11:22') => render(
  <MemoryRouter><SolicitarCupoPanel actor={actor} onCupoActualizado={onCupoActualizado} /></MemoryRouter>,
);
async function autorizar() {
  const user = userEvent.setup();
  await user.click(await screen.findByRole('checkbox', { name: 'AUTORIZO' }));
  await user.click(screen.getByRole('button', { name: 'Aceptar y consultar' }));
}

describe('Solicitud de cupo explícita', () => {
  it('al entrar solo lee disponibilidad y solicitud, sin crear ni consultar', async () => {
    renderPanel();
    expect(await screen.findByRole('button', { name: 'Solicitar mi cupo' })).toBeEnabled();
    expect(mockPost).not.toHaveBeenCalled();
    expect(mockGet.mock.calls.map(x => x[0])).toEqual([`${BASE}/disponibilidad`, `${BASE}/mi-solicitud-actual`]);
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('crea sin monto ni datos personales y exige el texto vigente sin casilla premarcada', async () => {
    disponibilidad = { ...disponibilidad, habilitado: false };
    mockPost.mockImplementation((url: string) => {
      if (url === `${BASE}/solicitar-cupo`) {
        actual = { ...RECIBIDA }; disponibilidad = { ...disponibilidad, habilitado: true };
        return Promise.resolve(actual);
      }
      return Promise.reject(new Error('No se autorizó otra operación.'));
    });
    renderPanel();
    const user = userEvent.setup();
    await user.dblClick(await screen.findByRole('button', { name: 'Solicitar mi cupo' }));
    const checkbox = await screen.findByRole('checkbox', { name: 'AUTORIZO' });
    expect(checkbox).not.toBeChecked();
    expect(screen.getByText(/Texto vigente recibido del servidor/).textContent).toBe(TEXTO.texto);
    expect(screen.getByRole('button', { name: 'Aceptar y consultar' })).toBeDisabled();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost.mock.calls[0][0]).toBe(`${BASE}/solicitar-cupo`);
    expect(mockPost.mock.calls[0][1]).toBeUndefined();
    expect(mockPost.mock.calls[0][2]['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i);
    expect(JSON.parse(sessionStorage.getItem('xpay_solicitud_cupo:11:22')!).key).toBe(mockPost.mock.calls[0][2]['Idempotency-Key']);
  });

  it('aceptación explícita registra la versión mostrada y después evalúa una sola vez', async () => {
    actual = { ...RECIBIDA };
    const cupo = vi.fn();
    mockPost.mockImplementation((url: string) => {
      if (url.endsWith('/autorizar-consulta-riesgo')) return Promise.resolve({ idSolicitud: 71, version: TEXTO.version, resultado: 'registrada' });
      if (url.endsWith('/iniciar-evaluacion')) {
        actual = { ...RECIBIDA, estadoSolicitud: 'APROBADA', decisionCrediticia: 'APROBADA', consentimientoRegistrado: true,
          score: 'NO_MOSTRAR_SCORE', codigoMotivoDecision: 'NO_MOSTRAR_MOTIVO', montoSugerido: 'NO_MOSTRAR_MONTO' };
        return Promise.resolve(actual);
      }
      return Promise.reject(new Error('POST fuera del contrato.'));
    });
    renderPanel(cupo);
    await autorizar();
    expect(await screen.findByRole('status')).toHaveTextContent('Tu solicitud fue aprobada');
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(mockPost.mock.calls[0]).toEqual([`${BASE}/solicitudes/71/autorizar-consulta-riesgo`, { version: TEXTO.version }]);
    expect(mockPost.mock.calls[1][0]).toBe(`${BASE}/solicitudes/71/iniciar-evaluacion`);
    expect(mockPost.mock.calls[1][1]).toBeUndefined();
    expect(mockPost.mock.calls[1][3]).toBe(120000);
    expect(cupo).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/NO_MOSTRAR/)).not.toBeInTheDocument();
  });

  it('una solicitud persistida se recupera sin POST ni consentimiento premarcado', async () => {
    actual = { ...RECIBIDA, consentimientoRegistrado: true };
    renderPanel();
    expect(await screen.findByRole('checkbox', { name: 'AUTORIZO' })).not.toBeChecked();
    expect(mockPost).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Solicitar mi cupo' })).not.toBeInTheDocument();
  });

  it('la solicitud legacy se incorpora por clic y un 409 se confirma por GET antes de leer V1', async () => {
    actual = { ...RECIBIDA };
    disponibilidad = { ...disponibilidad, habilitado: false };
    mockPost.mockImplementation((url: string) => {
      if (url === `${BASE}/solicitar-cupo`) {
        disponibilidad = { ...disponibilidad, habilitado: true };
        return Promise.reject(new ApiError('Solicitud activa sintética', 409));
      }
      return Promise.reject(new Error('Ninguna evaluación autorizada.'));
    });
    renderPanel();
    const continuar = await screen.findByRole('button', { name: 'Continuar mi solicitud' });
    expect(mockPost).not.toHaveBeenCalled();
    expect(mockGet.mock.calls.map(c => c[0])).toEqual([`${BASE}/disponibilidad`, `${BASE}/mi-solicitud-actual`]);
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    await userEvent.setup().click(continuar);
    expect(await screen.findByRole('checkbox', { name: 'AUTORIZO' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Aceptar y consultar' })).toBeDisabled();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost.mock.calls[0][0]).toBe(`${BASE}/solicitar-cupo`);
    expect(mockPost.mock.calls[0][1]).toBeUndefined();
    expect(mockPost.mock.calls[0][2]['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i);
    expect(mockGet.mock.calls.map(c => c[0])).toEqual([`${BASE}/disponibilidad`, `${BASE}/mi-solicitud-actual`,
      `${BASE}/disponibilidad`, `${BASE}/mi-solicitud-actual`, `${BASE}/autorizacion-consulta-riesgo/texto-vigente`]);
  });

  it('creación incierta conserva GUID y tras refresh no repite POST automáticamente', async () => {
    mockPost.mockRejectedValue(new HttpUncertainError('timeout sintético'));
    const vista = renderPanel();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Solicitar mi cupo' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar');
    const key = JSON.parse(sessionStorage.getItem('xpay_solicitud_cupo:11:22')!).key;
    vista.unmount();
    renderPanel();
    await screen.findByRole('button', { name: 'Actualizar estado' });
    expect(screen.queryByRole('button', { name: 'Solicitar mi cupo' })).not.toBeInTheDocument();
    expect(JSON.parse(sessionStorage.getItem('xpay_solicitud_cupo:11:22')!).key).toBe(key);
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('tras timeout y GET que confirma ausencia permite reintentar solo la creación con el mismo GUID', async () => {
    mockPost.mockRejectedValueOnce(new HttpUncertainError('timeout sintético'))
      .mockImplementationOnce(() => { actual = { ...RECIBIDA }; return Promise.resolve(actual); });
    renderPanel();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Solicitar mi cupo' }));
    await screen.findByRole('alert');
    const key = mockPost.mock.calls[0][2]['Idempotency-Key'];
    expect(screen.queryByRole('button', { name: 'Reintentar solicitud' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    const reintentar = await screen.findByRole('button', { name: 'Reintentar solicitud' });
    expect(mockPost).toHaveBeenCalledTimes(1);
    await user.click(reintentar);
    await screen.findByRole('checkbox', { name: 'AUTORIZO' });
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(mockPost.mock.calls.map(c => c[0])).toEqual([`${BASE}/solicitar-cupo`, `${BASE}/solicitar-cupo`]);
    expect(mockPost.mock.calls[1][1]).toBeUndefined();
    expect(mockPost.mock.calls[1][2]['Idempotency-Key']).toBe(key);
  });

  it('sin poder guardar la intención local no envía la creación', async () => {
    renderPanel();
    const boton = await screen.findByRole('button', { name: 'Solicitar mi cupo' });
    const guardar = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Almacenamiento sintético no disponible', 'QuotaExceededError');
    });
    try {
      await userEvent.setup().click(boton);
      expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar');
      expect(mockPost).not.toHaveBeenCalled();
      expect(screen.queryByRole('button', { name: 'Solicitar mi cupo' })).not.toBeInTheDocument();
    } finally { guardar.mockRestore(); }
  });

  it('una autorización que devuelve otra versión nunca inicia evaluación', async () => {
    actual = { ...RECIBIDA };
    mockPost.mockResolvedValue({ idSolicitud: 71, version: 'OTRA-VERSION', resultado: 'registrada' });
    renderPanel();
    await autorizar();
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(6));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: 'AUTORIZO' })).not.toBeChecked());
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost.mock.calls[0][0]).toMatch(/\/autorizar-consulta-riesgo$/);
  });

  it('timeout después de iniciar evaluación nunca reenvía, incluso si el GET aún dice RECIBIDA', async () => {
    actual = { ...RECIBIDA };
    mockPost.mockImplementation((url: string) => url.endsWith('/autorizar-consulta-riesgo')
      ? Promise.resolve({ idSolicitud: 71, version: TEXTO.version, resultado: 'registrada' })
      : Promise.reject(new HttpUncertainError('timeout sintético')));
    const vista = renderPanel();
    await autorizar();
    expect(await screen.findByRole('status')).toHaveTextContent('Estamos confirmando tu evaluación');
    expect(screen.queryByRole('button', { name: 'Aceptar y consultar' })).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    await screen.findByRole('status');
    vista.unmount(); renderPanel();
    await screen.findByRole('status');
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it.each(['CONSULTANDO_RIESGO', 'ERROR_PROVEEDOR', 'RECHAZADA', 'PENDIENTE_REVISION_MANUAL', 'APROBADA'])('%s ofrece solo actualizar estado', async estado => {
    actual = { ...RECIBIDA, estadoSolicitud: estado };
    disponibilidad = { ...disponibilidad, habilitado: false };
    renderPanel();
    await screen.findByRole('status');
    expect(screen.getByRole('button', { name: 'Actualizar estado' })).toBeEnabled();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Continuar|Reintentar/ })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each(['EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO'])('%s reanuda solo por clic, sin autorización ni creación nuevas', async estado => {
    actual = { ...RECIBIDA, estadoSolicitud: estado, consentimientoRegistrado: true };
    sessionStorage.setItem('xpay_solicitud_cupo:11:22', JSON.stringify({ key: '11111111-1111-4111-8111-111111111111',
      creacionPendiente: false, solicitudId: 71, evaluacionIntentada: true }));
    mockPost.mockImplementation(() => {
      actual = { ...RECIBIDA, estadoSolicitud: 'APROBADA', decisionCrediticia: 'APROBADA', consentimientoRegistrado: true };
      return Promise.resolve(actual);
    });
    renderPanel();
    const continuar = await screen.findByRole('button', { name: 'Continuar evaluación' });
    expect(mockPost).not.toHaveBeenCalled();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    await userEvent.setup().click(continuar);
    expect(await screen.findByRole('status')).toHaveTextContent('Tu solicitud fue aprobada');
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost.mock.calls[0][0]).toBe(`${BASE}/solicitudes/71/iniciar-evaluacion`);
    expect(mockPost.mock.calls[0][1]).toBeUndefined();
    expect(mockPost.mock.calls[0][2]['Idempotency-Key']).toBe('11111111-1111-4111-8111-111111111111');
    expect(mockGet.mock.calls.filter(c => c[0] === `${BASE}/mi-solicitud-actual`)).toHaveLength(3);
  });

  it('si falta apellido guía a Perfil sin intentar habilitación ni consulta', async () => {
    disponibilidad = { ...disponibilidad, disponible: false, requierePrimerApellido: true, estado: 'REQUIERE_PRIMER_APELLIDO', mensaje: 'Completa tu primer apellido en tu perfil.' };
    mockGet.mockImplementation((url: string) => url.endsWith('/disponibilidad')
      ? Promise.resolve(disponibilidad) : Promise.reject(new ApiError('KYC requerido', 403)));
    renderPanel();
    expect(await screen.findByRole('link', { name: 'Completar mi primer apellido' })).toHaveAttribute('href', '/mi-wallet/perfil');
    expect(screen.queryByRole('button', { name: 'Solicitar mi cupo' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('sin texto vigente válido no se puede aceptar ni enviar evaluación', async () => {
    actual = { ...RECIBIDA };
    mockGet.mockImplementation((url: string) => url.endsWith('/disponibilidad') ? Promise.resolve(disponibilidad)
      : url.endsWith('/mi-solicitud-actual') ? Promise.resolve({ solicitud: actual }) : Promise.resolve({ ...TEXTO, hash: '' }));
    renderPanel();
    expect(await screen.findByRole('alert')).toHaveTextContent('Actualiza el estado');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('al cambiar de actor una autorización pendiente no continúa con el nuevo titular', async () => {
    actual = { ...RECIBIDA };
    let resolver!: (value: unknown) => void;
    mockPost.mockImplementation(() => new Promise(resolve => { resolver = resolve; }));
    const vista = renderPanel();
    await autorizar();
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    vista.rerender(<MemoryRouter><SolicitarCupoPanel actor="33:44" /></MemoryRouter>);
    await screen.findByRole('checkbox', { name: 'AUTORIZO' });
    resolver({ idSolicitud: 71, version: TEXTO.version, resultado: 'registrada' });
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    expect(sessionStorage.getItem('xpay_solicitud_cupo:33:44')).toBeNull();
  });
});
