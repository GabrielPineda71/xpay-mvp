export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

const NET_ERR = 'No fue posible conectar con el backend XPAY. Verifica la URL del API o la conexión.';

// XPAY-377 — antes de este ticket, ningún fetch() de este cliente tenía
// timeout/AbortController: una petición POST que nunca recibiera respuesta
// (red, proxy, backend colgado) dejaba la promesa pendiente PARA SIEMPRE.
// Eso es lo que causó que "Procesando..." quedara indefinido en el primer
// intento real de Gabriel (XPAY-376) — demostrado por inspección de código,
// no asumido: los 5 métodos de este archivo llamaban fetch() sin `signal`.
//
// DEFAULT_TIMEOUT_MS es genérico (usado por get/put/patch/postForm y por
// post() cuando no se pasa uno explícito). post() acepta un timeoutMs
// propio porque las llamadas financieras (retiros/real) necesitan un valor
// mayor (el backend puede tardar más esperando a Passport) y, sobre todo,
// necesitan que el llamador pueda distinguir un HttpUncertainError de
// cualquier otro error — ver comentario de esa clase.
const DEFAULT_TIMEOUT_MS = 20_000;

// XPAY-377 FASE 4 — error DISTINGUIBLE de cualquier otro fallo de red/HTTP.
// Un timeout NUNCA debe presentarse como "la operación falló": el servidor
// pudo haber recibido y procesado la solicitud aunque el navegador no haya
// recibido la respuesta a tiempo. El llamador (UserWalletPage.tsx) debe
// capturar este tipo específico y mostrar un mensaje de INCERTIDUMBRE, no
// de fallo ni de éxito.
export class HttpUncertainError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'HttpUncertainError';
  }
}

function getToken(): string | null {
  return localStorage.getItem('xpay_token');
}

function authHeaders(): Record<string, string> {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (res.status === 401) {
    localStorage.removeItem('xpay_token');
    localStorage.removeItem('xpay_user');
    window.dispatchEvent(new Event('xpay:unauthorized'));
    throw new Error('Sesión expirada o no autorizada. Inicia sesión nuevamente.');
  }
  if (!res.ok) {
    let msg: string;
    try {
      const body = await res.json() as { message?: string };
      msg = body.message ?? `Error HTTP ${res.status}`;
    } catch {
      msg = `Error HTTP ${res.status}`;
    }
    throw new Error(msg);
  }
  try {
    return await res.json() as T;
  } catch {
    throw new Error('Respuesta inválida del servidor.');
  }
}

export async function get<T>(path: string, timeoutMs: number = DEFAULT_TIMEOUT_MS): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      headers: { ...authHeaders() },
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch {
    // GET es idempotente/sin efecto secundario — un timeout aquí no tiene
    // la misma ambigüedad financiera que un POST, así que no necesita
    // HttpUncertainError.
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

// XPAY-377 — timeoutMs es explícito (no opcional silencioso) para que cada
// llamador decida conscientemente cuánto esperar. Las llamadas financieras
// (retiros/real) deben pasar un valor propio, mayor al default, y están
// obligadas a manejar HttpUncertainError de forma distinta a cualquier
// otro error — ver UserWalletPage.tsx handleConfirmarRetiroReal.
export async function post<T>(
  path: string,
  body: unknown,
  extraHeaders?: Record<string, string>,
  timeoutMs: number = DEFAULT_TIMEOUT_MS,
): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders(), ...extraHeaders },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch (err) {
    if (isTimeoutError(err)) {
      throw new HttpUncertainError(
        'Se agotó el tiempo de espera esperando la respuesta del servidor. ' +
        'No sabemos todavía si la operación se completó del lado del servidor.',
      );
    }
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

export async function postForm<T>(path: string, form: FormData, timeoutMs: number = DEFAULT_TIMEOUT_MS): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: 'POST',
      headers: { ...authHeaders() },
      body: form,
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch {
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

export async function put<T>(path: string, body: unknown, timeoutMs: number = DEFAULT_TIMEOUT_MS): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch {
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

export async function patch<T>(path: string, body: unknown, timeoutMs: number = DEFAULT_TIMEOUT_MS): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch {
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

// AbortSignal.timeout() rechaza con DOMException("TimeoutError") — nunca
// con el propio AbortError genérico que produciría una cancelación manual.
function isTimeoutError(err: unknown): boolean {
  return err instanceof DOMException && err.name === 'TimeoutError';
}
