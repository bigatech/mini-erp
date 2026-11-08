// Runs only against the disposable CI/Codespaces stack; no customer fixtures.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const base = process.env.CI_BASE_URL || 'http://127.0.0.1:13000';
if (!/^http:\/\/(127\.0\.0\.1|localhost):\d+$/.test(base)) throw new Error('Smoke writes require a loopback disposable stack');
async function request(path, status = 200, options = {}) {
  const response = await fetch(base + path, options);
  assert.equal(response.status, status, `${options.method || 'GET'} ${path}`);
  return response;
}
const post = (path, body, status) => request(path, status, {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
});
(async () => {
  await request('/health/live');
  await request('/health/ready');
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
  const browser = await chromium.launch({headless:true, args:['--no-sandbox']});
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
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
    require('node:fs').mkdirSync('TestResults', {recursive:true});
    await page.screenshot({path:'TestResults/ci-ui.png', fullPage:true});
    assert.deepEqual(errors, []);
  } finally { await browser.close(); }
  console.log('PASS: production startup/schema, API 200/400/404/409, PostgreSQL persistence, MinIO image round trip, UI data/error/recovery');
  console.log('AUTH GAP: anonymous model access is currently allowed; public production release remains blocked.');
})().catch(error => { console.error(error); process.exitCode = 1; });
