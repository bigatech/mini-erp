# P1-01: local OIDC session and API authentication

The existing modular monolith uses an external OIDC identity provider. This increment adds no business table, aggregate, context, user password store, or migration. It implements the authentication portion of ERP-10 section 21 and ERP-12 US-1.1. Role/action and company/location resource authorization remain P1-02; this is not production approval.

## Session and API contract

The local SPA uses the official Keycloak JavaScript adapter, Authorization Code and PKCE S256. Access, refresh and ID tokens remain in memory. The adapter stores only the short-lived authorization transaction in session storage; the application stores a local return path and an optional logout audit failure flag. Reload resumes through the provider's SSO using a fresh authorization flow. Neither implicit nor password grant is enabled. API calls carry a bearer header; tokens are never query parameters. Image and export downloads use authenticated fetch and revocable blob URLs. API redirects are rejected to prevent accidental credential forwarding.

`Auth:Authority` and `Auth:Audience` configure JWT validation. Production requires an explicit authority. HTTPS is required except for an explicit loopback development issuer. The API validates issuer, audience, RS256 signature, expiration/not-before and a nonempty `sub`, using discovery/JWKS rotation. Clock tolerance is 30 seconds. `Auth:BackchannelBaseUrl` optionally changes only the network destination of loopback issuer discovery/JWKS requests inside Docker; it does not change the accepted issuer. There is no unsigned token, mock identity, query token, or anonymous development bypass.

All business routes, including downloads and unknown API routes, inherit the authenticated-subject fallback policy. Anonymous or invalid bearer requests receive 401 problem+json and a Bearer challenge without validation internals. Public exceptions are `/` (static host status without business data), `/health/live` and `/health/ready` (container probes). CORS preflight does not return business data. Health does not prove UI acceptance.

| Endpoint | Behavior |
|---|---|
| GET `/api/v1/auth/session` | Authenticated session check, `{authenticated:true,subject}`; Cache-Control no-store. Does not grant a business permission. |
| POST `/api/v1/auth/events` | Authenticated `{event:"login"}` or `{event:"logout"}` only; 204. Invalid event 400. Subject comes from the verified token. |

Session audit is structured JSON (`MiniErp.Security.Audit`): action, SHA256 issuer/subject pseudonym, UTC log timestamp and correlation ID. No token, password or raw identity is logged. These are client-session observations, not proof of IdP revocation. Keycloak realm events additionally enable LOGIN/LOGIN_ERROR/LOGOUT. Central log retention and production revocation policy are deployment decisions. Audit failure blocks opening a newly authenticated business screen. Logout still closes the local session and attempts IdP logout when audit is unavailable; the UI reports the missing audit.

## Local provider and existing realm

`deploy/compose/oidc.yaml` retains the separate loopback Keycloak and persistent identity volume. `deploy/identity/minierp-realm.json` specifies the existing public `minierp-web` client, S256, API audience mapper `minierp-api`, and local callback/logout redirect. **Startup import skips an already-existing realm**: its audience mapper, logout attributes and event configuration require an authenticated administrator update. Do not delete the identity volume to apply configuration. No real user or role is provisioned by this change. User-to-existing-Kullanici and company/location membership mapping is P1-02.

Local application: `http://localhost:3000`, provider `http://localhost:18090/realms/minierp-local`. Compose API uses `host.docker.internal:18090` for backchannel transport. The UI requires an account in that realm; an empty provider is not a working real-user acceptance scenario.

## Verification and isolated demonstration

`ApiAuthenticationTests` exercises the real ASP.NET bearer/authz pipeline with independently generated ephemeral RSA keys: valid, absent, malformed, wrong issuer/audience/signature, expired, future, missing subject and disallowed algorithm tokens. Rejected writes never reach the business handler. Session/audit validation and missing/insecure remote configuration are covered.

`node scripts/ci/prepare-identity.cjs` creates random disposable credentials in ignored `deploy/environments/ci.env` and an ignored synthetic realm fixture in `TestResults/identity`. Never publish either file. The CI compose stack uses a separate empty database/storage and real Keycloak realm. `scripts/ci/smoke.cjs` exercises real browser Code/PKCE login/logout, bearer API/S3 roundtrip, validation/conflict responses, UI data/error/recovery, and authenticated persistence after API restart. CI emits only a synthetic screen artifact. These tests do not audit customer source data or prove P1-02 isolation.

Official references: [ASP.NET Core JWT validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0), [Keycloak JavaScript adapter](https://www.keycloak.org/securing-apps/javascript-adapter). Only the existing architecture's authentication adapter is implemented; external ERP data models are not copied.
