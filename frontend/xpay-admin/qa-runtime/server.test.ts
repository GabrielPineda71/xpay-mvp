// @vitest-environment node
// P0-B frontend — qa-runtime/server.cjs: cabeceras de seguridad, no-store en
// index.html/fallback SPA y paths mal codificados sin tumbar el proceso.
// Sin dependencias nuevas: http/fs/os/path de Node; el servidor escucha en
// 127.0.0.1 con puerto efímero sobre un directorio temporal sintético.
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createRequire } from 'node:module';
import { describe, it, expect, beforeAll, afterAll } from 'vitest';

const require = createRequire(import.meta.url);
const { crearManejador } = require('./server.cjs') as {
  crearManejador: (raiz: string) => http.RequestListener;
};

const INDICE = '<!doctype html><title>INDICE SINTETICO</title>';

interface Respuesta { status: number; headers: http.IncomingHttpHeaders; body: string }

function iniciar(raiz: string): Promise<http.Server> {
  return new Promise(resolve => {
    const servidor = http.createServer(crearManejador(raiz));
    servidor.listen(0, '127.0.0.1', () => resolve(servidor));
  });
}

function pedir(servidor: http.Server, ruta: string): Promise<Respuesta> {
  const { port } = servidor.address() as AddressInfo;
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, path: ruta, method: 'GET' }, res => {
      let body = '';
      res.setEncoding('utf8');
      res.on('data', parte => { body += parte; });
      res.on('end', () => resolve({ status: res.statusCode ?? 0, headers: res.headers, body }));
    });
    req.on('error', reject);
    req.end();
  });
}

function esperarCabecerasSeguridad(r: Respuesta) {
  expect(r.headers['referrer-policy']).toBe('no-referrer');
  expect(r.headers['x-content-type-options']).toBe('nosniff');
  expect(r.headers['x-frame-options']).toBe('DENY');
  expect(r.headers['content-security-policy']).toBe("frame-ancestors 'none'");
}

let raiz = '';
let raizVacia = '';
let servidor: http.Server;
let servidorSinIndice: http.Server;

beforeAll(async () => {
  raiz = mkdtempSync(join(tmpdir(), 'xpay-qa-runtime-'));
  mkdirSync(join(raiz, 'assets'));
  writeFileSync(join(raiz, 'index.html'), INDICE);
  writeFileSync(join(raiz, 'assets', 'app.js'), 'export const x = 1;\n');
  raizVacia = mkdtempSync(join(tmpdir(), 'xpay-qa-runtime-vacia-'));
  servidor = await iniciar(raiz);
  servidorSinIndice = await iniciar(raizVacia);
});

afterAll(async () => {
  await new Promise(resolve => servidor.close(resolve));
  await new Promise(resolve => servidorSinIndice.close(resolve));
  rmSync(raiz, { recursive: true, force: true });
  rmSync(raizVacia, { recursive: true, force: true });
});

describe('qa-runtime/server.cjs (P0-B frontend)', () => {
  it('index.html: 200, cabeceras de seguridad y Cache-Control no-store', async () => {
    const r = await pedir(servidor, '/');
    expect(r.status).toBe(200);
    expect(r.body).toBe(INDICE);
    expect(r.headers['content-type']).toBe('text/html; charset=utf-8');
    expect(r.headers['cache-control']).toBe('no-store');
    esperarCabecerasSeguridad(r);
  });

  it.each(['/verificar-email', '/restablecer-clave', '/recuperar-clave', '/reenviar-verificacion'])(
    'fallback SPA %s: index.html con no-store',
    async ruta => {
      const r = await pedir(servidor, ruta);
      expect(r.status).toBe(200);
      expect(r.body).toBe(INDICE);
      expect(r.headers['cache-control']).toBe('no-store');
      esperarCabecerasSeguridad(r);
    },
  );

  it('asset estático: cabeceras de seguridad, sin no-store', async () => {
    const r = await pedir(servidor, '/assets/app.js');
    expect(r.status).toBe(200);
    expect(r.headers['content-type']).toBe('application/javascript; charset=utf-8');
    expect(r.headers['cache-control']).toBeUndefined();
    esperarCabecerasSeguridad(r);
  });

  it('path mal codificado: 400 fijo, sin reflejar la entrada, y el proceso sigue vivo', async () => {
    const r = await pedir(servidor, '/%E0%A4%A?x=<script>');
    expect(r.status).toBe(400);
    expect(r.body).toBe('Solicitud inválida.');
    expect(r.body).not.toContain('script');
    expect(r.headers['cache-control']).toBe('no-store');
    esperarCabecerasSeguridad(r);

    const despues = await pedir(servidor, '/');
    expect(despues.status).toBe(200);
  });

  it('byte nulo codificado: 400 fijo y el proceso sigue vivo', async () => {
    const r = await pedir(servidor, '/%00');
    expect(r.status).toBe(400);
    expect(r.body).toBe('Solicitud inválida.');
    expect((await pedir(servidor, '/')).status).toBe(200);
  });

  it('path traversal: nunca sale de la raíz (devuelve index.html)', async () => {
    const r = await pedir(servidor, '/../../../../etc/passwd');
    expect(r.status).toBe(200);
    expect(r.body).toBe(INDICE);
  });

  it('sin index.html: 500 fijo en lugar de tumbar el proceso', async () => {
    const r = await pedir(servidorSinIndice, '/verificar-email');
    expect(r.status).toBe(500);
    expect(r.body).toBe('Error interno.');
    esperarCabecerasSeguridad(r);
    expect((await pedir(servidorSinIndice, '/')).status).toBe(500);
  });
});
