// Servidor de pruebas que imita la parte de la API de GitHub que usa tools/release.sh (releases y assets).
// Solo escucha en 127.0.0.1 y lo guarda todo en memoria. Lo usa tools/release-test.js; nunca toca GitHub.
//
// Fallos a propósito (para comprobar que nada queda publicado a medias):
//   mock.failUpload = 'nombre'   las subidas de ese archivo devuelven 500 hasta que se ponga a null
//   mock.corrupt = 'nombre'      al descargar ese asset devuelve bytes distintos
//   mock.extra = true            el próximo borrador aparece con un archivo de más
'use strict';
const http = require('http');

function createMock() {
  const mock = { repos: {}, log: [], nextId: 1000, failUpload: null, corrupt: null, port: 0 };
  const repo = (name) => (mock.repos[name] ??= { releases: [] });

  // una versión ya publicada (como las de verdad): el script no debe tocarla nunca
  mock.seed = (name, tag) => {
    const r = { id: mock.nextId++, tag_name: tag, draft: false, assets: [], published: true };
    repo(name).releases.push(r);
    return r;
  };

  const send = (res, code, body, type = 'application/json') => {
    const data = Buffer.isBuffer(body) ? body : Buffer.from(typeof body === 'string' ? body : JSON.stringify(body));
    res.writeHead(code, { 'Content-Type': type, 'Content-Length': data.length });
    res.end(data);
  };
  const pub = (r) => ({ id: r.id, tag_name: r.tag_name, draft: r.draft, name: r.name, body: r.body,
    target_commitish: r.target_commitish, assets: r.assets.map(assetJson) });
  const assetJson = (a) => ({ id: a.id, name: a.name, size: a.data.length, state: 'uploaded',
    url: `http://127.0.0.1:${mock.port}/repos/${a.repo}/releases/assets/${a.id}` });
  const find = (id) => {
    for (const [name, r] of Object.entries(mock.repos))
      for (const rel of r.releases) {
        if (rel.id === id) return { repoName: name, rel };
        const a = rel.assets.find((x) => x.id === id);
        if (a) return { repoName: name, rel, asset: a };
      }
    return null;
  };

  const server = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', (c) => chunks.push(c));
    req.on('end', () => {
      const body = Buffer.concat(chunks);
      const u = new URL(req.url, 'http://127.0.0.1');
      const p = u.pathname;
      mock.log.push({ method: req.method, path: p + u.search });
      if (req.headers.authorization !== 'token prueba') return send(res, 401, { message: 'Bad credentials' });
      let m;
      if ((m = p.match(/^\/repos\/([^/]+\/[^/]+)\/releases\/tags\/([^/]+)$/)) && req.method === 'GET') {
        const r = repo(m[1]).releases.find((x) => x.tag_name === decodeURIComponent(m[2]) && !x.draft);
        return r ? send(res, 200, pub(r)) : send(res, 404, { message: 'Not Found' });
      }
      if ((m = p.match(/^\/repos\/([^/]+\/[^/]+)\/releases$/))) {
        if (req.method === 'GET') return send(res, 200, repo(m[1]).releases.map(pub));
        if (req.method === 'POST') {
          const j = JSON.parse(body.toString('utf8'));
          const r = { id: mock.nextId++, tag_name: j.tag_name, draft: j.draft === true, name: j.name, body: j.body,
            target_commitish: j.target_commitish, assets: [] };
          if (mock.extra) { r.assets.push({ id: mock.nextId++, name: 'sobrante.txt', data: Buffer.from('x'), repo: m[1] }); mock.extra = false; }
          repo(m[1]).releases.push(r);
          return send(res, 201, pub(r));
        }
      }
      if ((m = p.match(/^\/repos\/([^/]+\/[^/]+)\/releases\/(\d+)\/assets$/))) {
        const f = find(+m[2]);
        if (!f || f.asset) return send(res, 404, { message: 'Not Found' });
        if (req.method === 'GET') return send(res, 200, f.rel.assets.map(assetJson));
        if (req.method === 'POST') {
          const name = u.searchParams.get('name');
          if (!f.rel.draft) return send(res, 422, { message: 'release ya publicada' });
          // falla siempre (curl --retry reintenta los 500; un fallo de verdad que no se arregla solo)
          if (mock.failUpload === name) return send(res, 500, { message: 'fallo de prueba' });
          if (f.rel.assets.some((a) => a.name === name)) return send(res, 422, { message: 'already_exists' });
          const a = { id: mock.nextId++, name, data: body, repo: m[1] };
          f.rel.assets.push(a);
          return send(res, 201, assetJson(a));
        }
      }
      if ((m = p.match(/^\/repos\/([^/]+\/[^/]+)\/releases\/assets\/(\d+)$/))) {
        const f = find(+m[2]);
        if (!f || !f.asset) return send(res, 404, { message: 'Not Found' });
        if (req.method === 'DELETE') {
          if (!f.rel.draft) return send(res, 422, { message: 'release ya publicada' });
          f.rel.assets = f.rel.assets.filter((a) => a !== f.asset);
          res.writeHead(204); return res.end();
        }
        if (req.method === 'GET') {
          let data = f.asset.data;
          if (mock.corrupt === f.asset.name) data = Buffer.concat([data, Buffer.from(' ')]);
          return send(res, 200, data, 'application/octet-stream');
        }
      }
      if ((m = p.match(/^\/repos\/([^/]+\/[^/]+)\/releases\/(\d+)$/)) && req.method === 'PATCH') {
        const f = find(+m[2]);
        if (!f || f.asset) return send(res, 404, { message: 'Not Found' });
        if (!f.rel.draft) return send(res, 422, { message: 'release ya publicada: no se cambia' });
        const j = JSON.parse(body.toString('utf8'));
        if (j.draft === false) f.rel.draft = false;
        return send(res, 200, pub(f.rel));
      }
      send(res, 404, { message: 'Not Found: ' + req.method + ' ' + p });
    });
  });

  mock.start = () => new Promise((ok) => server.listen(0, '127.0.0.1', () => { mock.port = server.address().port; ok(mock.port); }));
  mock.stop = () => new Promise((ok) => server.close(ok));
  // cambios que el script hizo en algo que no era un borrador suyo
  mock.touchedPublished = (ids) => mock.log.filter((l) => l.method !== 'GET'
    && ids.some((id) => l.path.includes('/releases/' + id) || l.path.includes('/assets/' + id)));
  return mock;
}

module.exports = { createMock };
