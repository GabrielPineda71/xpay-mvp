const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.env.PORT || 8080;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
};

// P0-B frontend — cabeceras en TODA respuesta. Anti-framing con CSP
// frame-ancestors (navegadores actuales) + X-Frame-Options (anteriores). No
// se añade una CSP amplia: solo frame-ancestors, que no restringe scripts,
// estilos ni conexiones del bundle.
const CABECERAS_SEGURIDAD = {
  'Referrer-Policy': 'no-referrer',
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Content-Security-Policy': "frame-ancestors 'none'",
};

// Respuesta de error con texto FIJO: nunca refleja la URL recibida.
function responderError(res, status, texto) {
  res.writeHead(status, {
    ...CABECERAS_SEGURIDAD,
    'Content-Type': 'text/plain; charset=utf-8',
    'Cache-Control': 'no-store',
  });
  res.end(texto);
}

function crearManejador(root) {
  const raiz = path.resolve(root);
  const indice = path.join(raiz, 'index.html');

  return (req, res) => {
    let urlPath;
    try {
      urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
    } catch {
      // Path mal codificado (URIError): antes tumbaba el proceso Node.
      responderError(res, 400, 'Solicitud inválida.');
      return;
    }
    // Un byte nulo hace lanzar a fs.stat de forma síncrona.
    if (urlPath.includes('\0')) {
      responderError(res, 400, 'Solicitud inválida.');
      return;
    }

    let filePath = path.normalize(path.join(raiz, urlPath));
    if (filePath !== raiz && !filePath.startsWith(raiz + path.sep)) {
      filePath = indice;
    }

    fs.stat(filePath, (err, stat) => {
      if (err || !stat.isFile()) {
        filePath = indice;
      }
      const ext = path.extname(filePath);
      const cabeceras = { ...CABECERAS_SEGURIDAD, 'Content-Type': MIME[ext] || 'application/octet-stream' };
      // index.html (directo o fallback SPA de /verificar-email,
      // /restablecer-clave, etc.) nunca se cachea.
      if (filePath === indice) {
        cabeceras['Cache-Control'] = 'no-store';
      }
      const flujo = fs.createReadStream(filePath);
      flujo.on('error', () => {
        if (!res.headersSent) responderError(res, 500, 'Error interno.');
        else res.destroy();
      });
      flujo.once('open', () => {
        res.writeHead(200, cabeceras);
        flujo.pipe(res);
      });
    });
  };
}

module.exports = { crearManejador, CABECERAS_SEGURIDAD };

if (require.main === module) {
  http.createServer(crearManejador(__dirname))
    .listen(PORT, () => console.log(`XPAY Admin static server listening on ${PORT}`));
}
