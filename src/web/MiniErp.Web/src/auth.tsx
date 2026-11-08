import React from 'react'
import Keycloak from 'keycloak-js'

const api = new URL((import.meta.env.VITE_API_URL ?? 'http://localhost:8080') || location.origin, location.origin)
const authority = new URL(import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:18090/realms/minierp-local')
const realmMarker = '/realms/'
const realmIndex = authority.pathname.lastIndexOf(realmMarker)
if (realmIndex < 0) throw new Error('OIDC realm adresi geçersiz.')
const keycloak = new Keycloak({
  url: authority.origin + authority.pathname.slice(0, realmIndex),
  realm: authority.pathname.slice(realmIndex + realmMarker.length),
  clientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'minierp-web'
})
const nativeFetch = window.fetch.bind(window)
const listeners = new Set<() => void>()
let refresh: Promise<boolean> | undefined
let initialized: Promise<boolean> | undefined

function isApiUrl(value: string) {
  const url = new URL(value, location.href)
  return url.origin === api.origin && url.pathname.startsWith('/api/')
}
function endSession() {
  keycloak.clearToken()
  for (const listener of listeners) listener()
}

// Existing API clients share one bearer transport. Tokens never enter URLs or storage.
window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = input instanceof Request ? input.url : String(input)
  if (!isApiUrl(url)) return nativeFetch(input, init)
  if (!keycloak.authenticated) throw new Error('Oturum açmanız gerekiyor.')
  try {
    refresh ??= keycloak.updateToken(30).finally(() => { refresh = undefined })
    await refresh
  } catch {
    endSession()
    throw new Error('Oturumunuz sona erdi. Yeniden giriş yapın.')
  }
  const request = new Request(input, init)
  const headers = new Headers(request.headers)
  headers.set('Authorization', `Bearer ${keycloak.token}`)
  // Reject redirects so an API redirect cannot forward credentials elsewhere.
  const response = await nativeFetch(new Request(request, { headers, redirect: 'error' }))
  if (response.status === 401) endSession()
  return response
}

async function audit(event: 'login' | 'logout') {
  const response = await fetch(new URL('/api/v1/auth/events', api), {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ event })
  })
  if (!response.ok) throw new Error('Oturum denetim kaydı alınamadı.')
}

function initialize() {
  // Preserve only the application path across the top-level SSO check; no tokens
  // or OIDC callback parameters are written to application storage.
  if (!initialized && location.pathname !== '/auth/callback')
    sessionStorage.setItem('minierp.returnTo', location.pathname)
  initialized ??= keycloak.init({
    onLoad: 'check-sso',
    checkLoginIframe: false, pkceMethod: 'S256', flow: 'standard', responseMode: 'query',
    redirectUri: `${location.origin}/auth/callback`
  }).then(async authenticated => {
    if (authenticated) {
      const response = await fetch(new URL('/api/v1/auth/session', api))
      if (!response.ok) throw new Error('ERP oturumu doğrulanamadı.')
      await audit('login')
    }
    if (location.pathname === '/auth/callback') {
      const saved = sessionStorage.getItem('minierp.returnTo')
      sessionStorage.removeItem('minierp.returnTo')
      const returnTo = saved?.startsWith('/') && !saved.startsWith('//') && !saved.startsWith('/auth/') ? saved : '/dashboard'
      history.replaceState(null, '', returnTo)
    }
    return authenticated
  })
  return initialized
}

export function AuthGate({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = React.useState<'loading' | 'anonymous' | 'authenticated'>('loading')
  const [error, setError] = React.useState('')
  const [busy, setBusy] = React.useState(false)
  React.useEffect(() => {
    let active = true
    const expired = () => { if (active) setStatus('anonymous') }
    listeners.add(expired)
    void initialize().then(authenticated => {
      if (active) {
        setStatus(authenticated ? 'authenticated' : 'anonymous')
        if (sessionStorage.getItem('minierp.logoutAuditFailed') === '1') {
          sessionStorage.removeItem('minierp.logoutAuditFailed')
          setError('Çıkış denetim kaydı alınamadı. Kimlik sağlayıcısından çıkış işlemi yürütüldü.')
        }
      }
    }).catch(() => {
      endSession()
      if (active) {
        setStatus('anonymous')
        setError('Kimlik sağlayıcısına veya ERP oturumuna erişilemedi. Yeniden deneyin.')
      }
    })
    return () => { active = false; listeners.delete(expired) }
  }, [])
  async function login() {
    setBusy(true); setError('')
    try {
      sessionStorage.setItem('minierp.returnTo', location.pathname)
      await keycloak.login({ redirectUri: `${location.origin}/auth/callback` })
    } catch { setBusy(false); setError('Giriş başlatılamadı. Kimlik sağlayıcısını kontrol edin.') }
  }
  async function logout() {
    setBusy(true); setError('')
    let auditTimeout: ReturnType<typeof setTimeout> | undefined
    try {
      // Audit failure or a stalled API must never prevent leaving the IdP session.
      await Promise.race([
        audit('logout'),
        new Promise<never>((_, reject) => { auditTimeout = setTimeout(() => reject(new Error('Audit timeout')), 5000) })
      ])
    } catch {
      setError('Çıkış denetim kaydı alınamadı. Kimlik sağlayıcısından çıkış yapılıyor.')
      try { sessionStorage.setItem('minierp.logoutAuditFailed', '1') } catch { /* Continue logout when browser storage is unavailable. */ }
    } finally {
      if (auditTimeout) clearTimeout(auditTimeout)
    }
    try {
      await keycloak.logout({ redirectUri: `${location.origin}/auth/callback` })
    } catch {
      setError('Yerel oturum kapatıldı. Kimlik sağlayıcısından çıkış tamamlanamadı; sağlayıcı oturumunuzu ayrıca kapatın.')
    } finally {
      endSession()
      setBusy(false)
    }
  }
  if (status === 'loading') return <main className="panel" style={{ margin: '10vh auto', maxWidth: 540, padding: 32 }}><h1>Mini ERP</h1><p role="status">Oturum kontrol ediliyor…</p></main>
  if (status === 'anonymous') return <main className="panel" style={{ margin: '10vh auto', maxWidth: 540, padding: 32 }}><h1>Mini ERP</h1><h2>Güvenli giriş</h2><p>Çalışma alanını açmak için kurumsal hesabınızla giriş yapın.</p>{error && <p role="alert" className="feedback error">{error}</p>}<button type="button" disabled={busy} onClick={() => void login()}>{busy ? 'Yönlendiriliyor…' : 'Oturum aç'}</button>{error && <button type="button" onClick={() => location.reload()}>Yeniden dene</button>}</main>
  return <>{error && <p role="alert" className="feedback error">{error}</p>}<div style={{ position: 'fixed', right: 20, bottom: 12, zIndex: 50 }}><button type="button" disabled={busy} onClick={() => void logout()}>Oturumu kapat</button></div>{children}</>
}

export function AuthenticatedImage({ url, alt }: { url: string; alt: string }) {
  const [src, setSrc] = React.useState('')
  const [error, setError] = React.useState(false)
  React.useEffect(() => {
    const controller = new AbortController()
    let objectUrl = ''
    setSrc(''); setError(false)
    void fetch(url, { signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error('Görsel alınamadı.')
      const blob = await response.blob()
      if (!controller.signal.aborted) { objectUrl = URL.createObjectURL(blob); setSrc(objectUrl) }
    }).catch(() => { if (!controller.signal.aborted) setError(true) })
    return () => { controller.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [url])
  return error ? <p>Görsel yüklenemedi.</p> : src ? <a href={src} target="_blank" rel="noreferrer"><img src={src} alt={alt} style={{ width: '100%', height: 170, objectFit: 'contain' }}/></a> : <p role="status">Görsel yükleniyor…</p>
}

export function AuthenticatedDownload({ url, fileName, children }: { url: string; fileName: string; children: React.ReactNode }) {
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState('')
  async function download() {
    setBusy(true); setError('')
    try {
      const response = await fetch(url)
      if (!response.ok) throw new Error('Dosya indirilemedi.')
      const blobUrl = URL.createObjectURL(await response.blob())
      const link = document.createElement('a'); link.href = blobUrl; link.download = fileName
      link.click(); setTimeout(() => URL.revokeObjectURL(blobUrl), 60_000)
    } catch { setError('Dosya indirilemedi. Yeniden deneyin.') }
    finally { setBusy(false) }
  }
  return <><button type="button" disabled={busy} onClick={() => void download()}>{children}</button>{error && <span role="alert">{error}</span>}</>
}
