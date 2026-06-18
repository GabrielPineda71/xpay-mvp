export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

const NET_ERR = 'No fue posible conectar con el backend XPAY. Verifica la URL del API o la conexión.';

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

export async function get<T>(path: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      headers: { ...authHeaders() },
    });
  } catch {
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}

export async function post<T>(path: string, body: unknown): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify(body),
    });
  } catch {
    throw new Error(NET_ERR);
  }
  return handleResponse<T>(res);
}
