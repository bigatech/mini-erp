// Disposable synthetic identities only; generated credentials never enter tracked files.
const fs = require('node:fs');
const crypto = require('node:crypto');
const envPath = process.argv[2] || 'deploy/environments/ci.env';
const values = fs.existsSync(envPath)
  ? Object.fromEntries(fs.readFileSync(envPath, 'utf8').split(/\r?\n/).filter(line => line.includes('=')).map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]))
  : {};
for (const key of ['POSTGRES_PASSWORD', 'OBJECT_STORAGE_ACCESS_KEY', 'OBJECT_STORAGE_SECRET_KEY', 'CI_USER', 'CI_PASSWORD']) {
  values[key] ||= crypto.randomBytes(24).toString('hex');
}
fs.mkdirSync(require('node:path').dirname(envPath), {recursive:true});
fs.writeFileSync(envPath, Object.entries(values).map(([key, value]) => `${key}=${value}`).join('\n') + '\n', {mode:0o600});
const origin = 'http://127.0.0.1:13000';
const realm = {
  realm:'minierp-ci', enabled:true, sslRequired:'none', registrationAllowed:false,
  loginWithEmailAllowed:false, resetPasswordAllowed:false,
  clients:[{
    clientId:'minierp-web', enabled:true, protocol:'openid-connect', publicClient:true,
    standardFlowEnabled:true, implicitFlowEnabled:false, directAccessGrantsEnabled:false,
    serviceAccountsEnabled:false, redirectUris:[`${origin}/auth/callback`], webOrigins:[origin],
    attributes:{'pkce.code.challenge.method':'S256', 'post.logout.redirect.uris':`${origin}/*`},
    protocolMappers:[{
      name:'minierp-api-audience', protocol:'openid-connect', protocolMapper:'oidc-audience-mapper',
      consentRequired:false, config:{'included.custom.audience':'minierp-api', 'id.token.claim':'false', 'access.token.claim':'true'}
    }]
  }],
  users:[{username:values.CI_USER, enabled:true, emailVerified:true,
    firstName:'CI', lastName:'Synthetic', email:'ci@example.invalid', requiredActions:[],
    credentials:[{type:'password', value:values.CI_PASSWORD, temporary:false}]}]
};
fs.mkdirSync('TestResults/identity', {recursive:true});
fs.writeFileSync('TestResults/identity/ci-realm.json', JSON.stringify(realm), {mode:0o600});
// Keycloak runs as uid 1000 inside the disposable container. The read-only bind
// fixture must be readable there; its parent remains private on the runner.
fs.chmodSync('TestResults/identity', 0o700);
fs.chmodSync('TestResults/identity/ci-realm.json', 0o644);
console.log('Prepared ignored disposable CI identity configuration.');
