// Runs only against the disposable CI/Codespaces stack; no customer fixtures.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const fs = require('node:fs');
const base = process.env.CI_BASE_URL || 'http://127.0.0.1:13000';
if (!/^http:\/\/(127\.0\.0\.1|localhost):\d+$/.test(base)) throw new Error('Smoke writes require a loopback disposable stack');
const credentials = Object.fromEntries(fs.readFileSync(process.env.CI_ENV_FILE || 'deploy/environments/ci.env', 'utf8').split(/\r?\n/).filter(line => line.includes('=')).map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
let token;
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
  const browser = await chromium.launch({headless:true, args:['--no-sandbox']});
  try {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const authenticated = page.waitForRequest(req => req.url().startsWith(base + '/api/') && /^Bearer /.test(req.headers().authorization || ''), {timeout:60000});
  await page.goto(base + '/plm-models');
  await page.getByRole('button', {name:'Oturum aç', exact:true}).click();
  // Authorization Code + PKCE against the real, disposable Keycloak realm.
  // The access token is observed from transport and remains in this process memory only.
  await page.locator('#username').fill(credentials.CI_USER, {timeout:60000});
  await page.locator('#password').fill(credentials.CI_PASSWORD);
  await page.locator('#kc-login').click();
  token = (await authenticated).headers().authorization.slice(7);
  const claims = JSON.parse(Buffer.from(token.split('.')[1], 'base64url'));
  assert.equal(claims.iss, 'http://127.0.0.1:18090/realms/minierp-ci');
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
    const stored = await page.evaluate(() => [...Object.values(localStorage), ...Object.values(sessionStorage)]);
    assert.ok(stored.every(value => !value.includes(token)), 'Access token must not persist in browser storage');
    await page.getByRole('button', {name:'Oturumu kapat', exact:true}).click();
    await page.getByRole('button', {name:'Oturum aç', exact:true}).click();
    await page.locator('#username').waitFor({timeout:30000});
    token = undefined;
    await request('/api/v1/plm/models', 401);
  } finally { await browser.close(); }
  console.log('PASS: real OIDC Code/PKCE login/logout, anonymous 401, API 200/400/404/409, S3 image round trip, UI data/error/recovery');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
