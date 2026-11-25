// Runs only against the disposable CI/Codespaces stack; no customer fixtures.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const fs = require('node:fs');
const base = process.env.CI_BASE_URL || 'http://127.0.0.1:13000';
if (!/^http:\/\/(127\.0\.0\.1|localhost):\d+$/.test(base)) throw new Error('Smoke writes require a loopback disposable stack');
const credentials = Object.fromEntries(fs.readFileSync(process.env.CI_ENV_FILE || 'deploy/environments/ci.env', 'utf8').split(/\r?\n/).filter(line => line.includes('=')).map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
let token;
const adapterTokens = new Set();
async function request(path, status = 200, options = {}) {
  const response = await fetch(base + path, {...options, headers:{...(token ? {Authorization:`Bearer ${token}`} : {}), ...options.headers}});
  assert.equal(response.status, status, `${options.method || 'GET'} ${path}`);
  return response;
}
const post = (path, body, status) => request(path, status, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
});
(async () => {
  await request('/health/live');
  await request('/health/ready');
  await request('/api/v1/plm/models', 401);
  await request('/api/v1/plm/models', 401, {headers:{Authorization:'Bearer malformed-token'}});
  const browser = await chromium.launch({headless:true, args:['--no-sandbox'], ...(process.env.PLAYWRIGHT_CHANNEL ? {channel:process.env.PLAYWRIGHT_CHANNEL} : {})});
  try {
  const page = await browser.newPage();
  const errors = [];
  const tokenReads = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (!response.url().endsWith('/protocol/openid-connect/token') || response.status() !== 200) return;
    tokenReads.push(response.json().then(body => {
      for (const key of ['access_token', 'refresh_token', 'id_token']) {
        if (typeof body[key] === 'string') adapterTokens.add(body[key]);
      }
    }).catch(() => { errors.push('OIDC token response inspection failed'); }));
  });
  async function assertMemoryOnlyTokens() {
    await Promise.all(tokenReads);
    assert.ok(adapterTokens.size >= 2, 'Inspect adapter access and refresh tokens from real OIDC response');
    const stored = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)]);
    assert.ok(stored.every(value => !/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/.test(value)), 'JWT must not persist in browser storage');
    assert.ok(stored.every(value => [...adapterTokens].every(secret => !value.includes(secret))), 'Adapter access/refresh/ID tokens must remain in memory');
  }
  const authenticated = page.waitForRequest(req => req.url().startsWith(base + '/api/') && /^Bearer /.test(req.headers().authorization || ''), {timeout:60000});
  await page.goto(base + '/plm-models');
  await page.getByRole('button', {name:'Oturum aç', exact:true}).click();
  // Authorization Code + PKCE against the real, disposable Keycloak realm.
  // The access token is observed from transport and remains in this process memory only.
  await page.locator('#username').fill(credentials.CI_USER, {timeout:60000});
  await page.locator('#password').fill(credentials.CI_PASSWORD);
  await page.locator('#kc-login').click();
  token = (await authenticated).headers().authorization.slice(7);
  adapterTokens.add(token);
  const claims = JSON.parse(Buffer.from(token.split('.')[1], 'base64url'));
  assert.equal(claims.iss, 'http://127.0.0.1:18091/realms/minierp-ci');
  assert.ok([claims.aud].flat().includes('minierp-api'));
  if (process.argv.includes('--persistence-only')) {
    const persisted = await (await request('/api/v1/plm/models')).json();
    assert.ok(persisted.data.some(model => model.code === 'CI-MODEL'), 'Persistence after API restart');
    console.log('PASS: authenticated API persistence after restart');
    return;
  }
  const products = await (await request('/api/v1/plm/products')).json();
  assert.equal(products.data.length, 0, 'Must start with an empty disposable database');
  const model = await (await post('/api/v1/plm/models', {code: 'CI-MODEL', name: 'CI synthetic model'}, 201)).json();
  await post('/api/v1/plm/models', {code: 'CI-MODEL', name: 'duplicate'}, 409);
  await post('/api/v1/plm/models', {code: '', name: ''}, 400);
  const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64');
  const form = new FormData();
  form.append('file', new Blob([image], {type:'image/png'}), 'ci.png');
  const uploaded = await (await request(`/api/v1/plm/models/${model.id}/images`, 201, {method:'POST', body:form})).json();
  const downloaded = await request(uploaded.url);
  assert.deepEqual(Buffer.from(await downloaded.arrayBuffer()), image, 'S3 round trip');
  assert.equal((await (await request(`/api/v1/plm/models/${model.id}/images`)).json()).data.length, 1);
  await request('/api/v1/does-not-exist', 404);
    await page.goto(base + '/plm-models');
    await page.getByLabel('PLM model seçimi').selectOption(model.id);
    await page.getByText('CI-MODEL · CI synthetic model', {exact:true}).waitFor();
    // API error state and recovery must be visible, with no unhandled JS errors.
    await page.route('**/api/v1/plm/models', route => route.fulfill({status:503, contentType:'application/json', body:'{"title":"CI unavailable"}'}));
    await page.reload();
    await page.getByRole('alert').filter({hasText:'CI unavailable'}).waitFor();
    await page.unroute('**/api/v1/plm/models');
    await page.reload();
    await page.getByLabel('PLM model seçimi').selectOption(model.id);
    await page.getByText('CI-MODEL · CI synthetic model', {exact:true}).waitFor();
    fs.mkdirSync('TestResults', {recursive:true});
    await page.screenshot({path:'TestResults/ci-ui.png', fullPage:true});
    assert.deepEqual(errors, []);
    // Auth tokens may only exist in the running SPA, never browser persistent stores.
    await assertMemoryOnlyTokens();
    await page.getByRole('button', {name:'Oturumu kapat', exact:true}).click();
    await page.getByRole('button', {name:'Oturum aç', exact:true}).click();
    await page.locator('#username').waitFor({timeout:30000});
    token = undefined;
    await request('/api/v1/plm/models', 401);
    // A second genuine login proves the provider session ended; simulate only the
    // ERP audit failure so logout must still clear the real Keycloak session.
    const secondAuthenticated = page.waitForRequest(req => req.url().startsWith(base + '/api/') && /^Bearer /.test(req.headers().authorization || ''), {timeout:60000});
    await page.locator('#username').fill(credentials.CI_USER);
    await page.locator('#password').fill(credentials.CI_PASSWORD);
    await page.locator('#kc-login').click();
    token = (await secondAuthenticated).headers().authorization.slice(7);
    adapterTokens.add(token);
    await page.getByRole('button', {name:'Oturumu kapat', exact:true}).waitFor();
    await assertMemoryOnlyTokens();
    let failedLogoutAudit = false;
    await page.route('**/api/v1/auth/events', async route => {
      if (route.request().postDataJSON()?.event !== 'logout') return route.continue();
      failedLogoutAudit = true;
      await route.fulfill({status:503, contentType:'application/json', body:'{"title":"CI audit unavailable"}'});
    });
    await page.getByRole('button', {name:'Oturumu kapat', exact:true}).click();
    await page.getByRole('button', {name:'Oturum aç', exact:true}).waitFor();
    assert.ok(failedLogoutAudit, 'Logout audit 503 fixture must execute');
    await page.getByRole('button', {name:'Oturum aç', exact:true}).click();
    await page.locator('#username').waitFor({timeout:30000});
    token = undefined;
    await request('/api/v1/plm/models', 401);
    assert.deepEqual(errors, []);
  } finally { await browser.close(); }
  console.log('PASS: real OIDC Code/PKCE login/logout and audit-503 logout, memory-only tokens, anonymous 401, API 200/400/404/409, S3 image round trip, UI data/error/recovery');
})().catch(error => {
  // Playwright timeout call logs can include fill() arguments. Never emit the
  // disposable password, other env values, or observed tokens in CI logs.
  let message = String(error.message);
  for (const secret of [...Object.values(credentials), ...adapterTokens])
    if (secret) message = message.split(secret).join('[REDACTED]');
  console.error(message);
  process.exitCode = 1;
});
