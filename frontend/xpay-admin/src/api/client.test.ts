// XPAY-402 — api/client.ts: ApiError preserva status HTTP de forma
// retrocompatible. CERO red real: se mockea `fetch` global (no `client.ts`
// en sí — aquí se prueba el código REAL de handleResponse/ApiError).
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { get, post, ApiError, HttpUncertainError } from './client.ts';

const originalFetch = global.fetch;

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  localStorage.clear();
  global.fetch = vi.fn();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe('ApiError (XPAY-402 requisito A)', () => {
  it('es instanceof Error', () => {
    const err = new ApiError('mensaje', 400);
    expect(err).toBeInstanceOf(Error);
  });

  it('conserva message', () => {
    const err = new ApiError('mensaje específico', 400);
    expect(err.message).toBe('mensaje específico');
  });

  it('conserva status', () => {
    const err = new ApiError('mensaje', 429);
    expect(err.status).toBe(429);
  });

  it('nunca almacena Authorization/token/body de la petición (solo name+status como own properties enumerables)', () => {
    const err = new ApiError('mensaje', 400);
    const claves = Object.keys(err).sort();
    // `message` lo define el constructor de Error como own property NO
    // enumerable (spec ECMA-262) — no aparece en Object.keys. `name` y
    // `status` sí son enumerables (asignación directa). Ninguna otra clave
    // (Authorization/token/body) existe en la instancia.
    expect(claves).toEqual(['name', 'status']);
  });
});

describe('get/post — status HTTP preservado (XPAY-402 requisito B)', () => {
  it('una respuesta 429 produce ApiError con status 429', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(429, { error: 'rate_limit_exceeded', message: 'Too many requests. Please try again later.', correlationId: 'abc' }),
    );

    try {
      await get('/api/algo');
      expect.fail('debía lanzar');
    } catch (err) {
      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).status).toBe(429);
      expect((err as ApiError).message).toBe('Too many requests. Please try again later.');
    }
  });

  it('una respuesta 400 conserva message + status 400 (misma extracción de mensaje de siempre)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(400, { success: false, message: 'La contraseña actual no coincide.' }),
    );

    try {
      await post('/api/auth/cambiar-clave', { claveActual: 'x', claveNueva: 'y' });
      expect.fail('debía lanzar');
    } catch (err) {
      expect(err).toBeInstanceOf(ApiError);
      expect(err).toBeInstanceOf(Error);
      expect((err as ApiError).status).toBe(400);
      expect((err as ApiError).message).toBe('La contraseña actual no coincide.');
    }
  });

  it('una respuesta sin cuerpo JSON parseable usa el mensaje de fallback "Error HTTP {status}"', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      new Response('no es json', { status: 500 }),
    );

    try {
      await get('/api/algo');
      expect.fail('debía lanzar');
    } catch (err) {
      expect((err as ApiError).status).toBe(500);
      expect((err as ApiError).message).toBe('Error HTTP 500');
    }
  });

  it('401 también produce ApiError con status 401 (mismo mensaje de siempre)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(401, { message: 'no autorizado' }),
    );

    try {
      await get('/api/algo');
      expect.fail('debía lanzar');
    } catch (err) {
      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).status).toBe(401);
      expect((err as ApiError).message).toBe('Sesión expirada o no autorizada. Inicia sesión nuevamente.');
    }
  });

  it('éxito (2xx) continúa igual: devuelve el JSON parseado, sin lanzar', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(200, { success: true, data: { foo: 'bar' } }),
    );

    const r = await get<{ success: boolean; data: { foo: string } }>('/api/algo');
    expect(r).toEqual({ success: true, data: { foo: 'bar' } });
  });

  it('un consumidor que solo hace `err instanceof Error` / `err.message` sigue funcionando igual (retrocompatibilidad)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(400, { message: 'algo salió mal' }),
    );

    let mensajeCapturado: string | null = null;
    try {
      await get('/api/algo');
    } catch (err) {
      mensajeCapturado = err instanceof Error ? err.message : null;
    }
    expect(mensajeCapturado).toBe('algo salió mal');
  });

  it('HttpUncertainError (timeout de POST) NO se ve afectado por este cambio — sigue siendo su propio tipo, no ApiError', () => {
    const err = new HttpUncertainError('timeout');
    expect(err).toBeInstanceOf(Error);
    expect(err).not.toBeInstanceOf(ApiError);
  });
});
