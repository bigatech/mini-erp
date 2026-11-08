import React from 'react'

type Cell={variantOptionId:string;sizeOptionId:string;setPartOptionId?:string|null;quantity:number}
type Draft={id:string;number:string;revisionId:string;status:string;lines:Cell[];createdAt:string;version:number;hasMaterialRequests?:boolean;pendingApproval?:{id:string;type:string;proposedLines:Cell[]}|null}
type Option={id:string;name:string;code:string}
type Event={id:string;type:string;at:string;fromStatus:string;toStatus:string;version:number}
type Revision={id:string;revisionNumber:string}

export default function OrderDraftCard({api,id,options,revisions=[],onChanged}:{api:string;id:string;options:Option[];revisions?:Revision[];onChanged:()=>void}){
 const [draft,setDraft]=React.useState<Draft|null>(null),[values,setValues]=React.useState<string[]>([]),[events,setEvents]=React.useState<Event[]>([])
 const [etag,setEtag]=React.useState(''),[loading,setLoading]=React.useState(true),[busy,setBusy]=React.useState(false),[error,setError]=React.useState(''),[notice,setNotice]=React.useState(''),[reload,setReload]=React.useState(0),[confirmCancel,setConfirmCancel]=React.useState(false)
 const posting=React.useRef(false),activeId=React.useRef(id);activeId.current=id
 React.useEffect(()=>{const c=new AbortController();setLoading(true);setDraft(null);setValues([]);setEvents([]);setError('');setNotice('');setConfirmCancel(false)
  void fetch(`${api}/api/v1/sales/order-drafts/${encodeURIComponent(id)}`,{signal:c.signal}).then(async r=>{const b=await r.json();if(!r.ok)throw new Error(b.title||'Sipariş kartı alınamadı.');if(c.signal.aborted)return;const value=(b.data??b) as Draft;setDraft(value);setValues(value.lines.map(l=>String(l.quantity)));setEtag(r.headers.get('ETag')||`"${value.version}"`)}).catch(e=>{if(!c.signal.aborted)setError(e.message)}).finally(()=>{if(!c.signal.aborted)setLoading(false)})
  void fetch(`${api}/api/v1/sales/order-drafts/${encodeURIComponent(id)}/events`,{signal:c.signal}).then(async r=>{const b=await r.json();if(!r.ok)throw new Error(b.title||'İşlem geçmişi alınamadı.');if(!c.signal.aborted)setEvents(b.data??b)}).catch(e=>{if(!c.signal.aborted)setError(e.message)})
  return()=>c.abort()
 },[api,id,reload])
 const name=(value?:string|null)=>value?options.find(o=>o.id===value)?.name||value:'Parçasız'
 const status=(value:string)=>value==='Draft'?'Taslak':value==='Cancelled'?'İptal':value
 async function action(kind:'save'|'cancel'){
  if(posting.current||!draft)return;setError('');setNotice('')
  if(kind==='save'&&values.some(v=>!v.trim()||!Number.isFinite(Number(v))||Number(v)<=0)){setError('Her hücre için pozitif miktar girin.');return}
  posting.current=true;setBusy(true);const target=id
  try{
   const r=await fetch(`${api}/api/v1/sales/order-drafts/${encodeURIComponent(target)}${kind==='cancel'?'/cancel':''}`,{method:kind==='cancel'?'POST':'PUT',headers:{'Content-Type':'application/json','If-Match':etag},body:JSON.stringify(kind==='cancel'?{}:{lines:draft.lines.map((l,i)=>({...l,quantity:Number(values[i])}))})})
   const b=await r.json().catch(()=>({}));if(activeId.current!==target)return
   if(!r.ok){if(r.status===412)throw new Error('Bu kayıt başka bir işlemde değişti. Girdiğiniz miktarlar korundu; güncel kaydı yeniden yükleyerek kontrol edin.');throw new Error(Object.values(b.errors||{}).flat().join(' ')||b.title||'İşlem tamamlanamadı.')}
   const updated=(b.data??b) as Draft;setDraft(updated);setValues(updated.lines.map(l=>String(l.quantity)));setEtag(r.headers.get('ETag')||`"${updated.version}"`);setConfirmCancel(false);setNotice(updated.pendingApproval?'İşlem yetkili kullanıcı onayına gönderildi.':kind==='cancel'?'Sipariş taslağı iptal edildi. Kayıt geçmişte korunur.':'Miktarlar kaydedildi.');onChanged()
   const history=await fetch(`${api}/api/v1/sales/order-drafts/${encodeURIComponent(target)}/events`);if(history.ok){const h=await history.json();if(activeId.current===target)setEvents(h.data??h)}
  }catch(e){if(activeId.current===target)setError(e instanceof Error?e.message:'İşlem tamamlanamadı.')}finally{posting.current=false;if(activeId.current===target)setBusy(false)}
 }
 return <section className="panel data-quality" aria-label="Sipariş taslak kartı"><div className="panel-heading"><h2>{draft?`Sipariş kartı · ${draft.number}`:'Sipariş kartı'}</h2><button type="button" disabled={busy||loading} onClick={()=>setReload(n=>n+1)}>Kartı yeniden yükle</button></div>
  {loading&&<p role="status">Sipariş kartı yükleniyor…</p>}{error&&<p role="alert" className="feedback error">{error}</p>}{notice&&<p role="status" className="feedback success">{notice}</p>}
  {draft&&<><p><b>{status(draft.status)}</b> · Revizyon: {revisions.find(r=>r.id===draft.revisionId)?.revisionNumber||draft.revisionId} · Sürüm {draft.version} · {new Date(draft.createdAt).toLocaleString('tr-TR')}</p><p>Sipariş numarası ve model revizyonu bu kartta değiştirilemez. Miktarlar parça adedidir.</p>
   {draft.hasMaterialRequests&&<p>Bağlı malzeme talebi var. Değişiklik ve iptal işlemleri yetkili kullanıcı onayına gönderilir.</p>}{draft.pendingApproval&&<div role="status"><p>{draft.pendingApproval.type==='Cancel'?'İptal':'Değişiklik'} onayı bekleniyor. Mevcut sipariş onay kararı verilene kadar korunur.</p>{draft.pendingApproval.type==='Update'&&<ul>{draft.pendingApproval.proposedLines.map((l,i)=><li key={i}>{name(l.variantOptionId)} / {name(l.sizeOptionId)} / {name(l.setPartOptionId)}: {l.quantity}</li>)}</ul>}</div>}<form onSubmit={e=>{e.preventDefault();void action('save')}}><fieldset disabled={busy||draft.status!=='Draft'||!!draft.pendingApproval}><div className="table-wrap"><table><thead><tr><th>Varyant</th><th>Beden</th><th>Set parçası</th><th>Parça adedi</th></tr></thead><tbody>{draft.lines.map((l,i)=><tr key={`${l.variantOptionId}:${l.sizeOptionId}:${l.setPartOptionId??''}`}><td>{name(l.variantOptionId)}</td><td>{name(l.sizeOptionId)}</td><td>{name(l.setPartOptionId)}</td><td><input style={{minWidth:90,maxWidth:150}} aria-label={`${name(l.variantOptionId)} / ${name(l.sizeOptionId)} / ${name(l.setPartOptionId)} kart miktarı`} type="number" step="any" min="0" required value={values[i]??''} onChange={e=>setValues(current=>current.map((v,n)=>n===i?e.target.value:v))}/></td></tr>)}</tbody></table></div><p>Toplam: {new Intl.NumberFormat('tr-TR').format(values.reduce((n,v)=>n+(Number(v)||0),0))} parça</p><button type="submit" className="submit-button">{draft.hasMaterialRequests?'Değişikliği onaya gönder':'Miktarları kaydet'}</button></fieldset></form>
   {draft.status==='Draft'&&!draft.pendingApproval&&!confirmCancel&&<button type="button" disabled={busy} onClick={()=>setConfirmCancel(true)}>Taslağı iptal et</button>}{draft.status==='Draft'&&!draft.pendingApproval&&confirmCancel&&<div role="group" aria-label="Taslak iptalini doğrula"><p>{draft.hasMaterialRequests?'İptal talebi yetkili kullanıcı onayına gönderilsin mi?':'Bu taslağı iptal etmek istiyor musunuz? Kayıt silinmez; miktarlar düzenlemeye kapanır.'}</p><button type="button" disabled={busy} onClick={()=>void action('cancel')}>İptali uygula</button><button type="button" disabled={busy} onClick={()=>setConfirmCancel(false)}>Vazgeç</button></div>}
   {draft.status==='Cancelled'&&<p>İptal edilen taslak salt okunurdur.</p>}<h3>İşlem geçmişi</h3><div className="table-wrap"><table><thead><tr><th>Zaman</th><th>İşlem</th><th>Durum</th><th>Sürüm</th></tr></thead><tbody>{events.map(e=><tr key={e.id}><td>{new Date(e.at).toLocaleString('tr-TR')}</td><td>{({Updated:'Miktarlar güncellendi',Cancelled:'İptal edildi',ApprovalRequested:'Onaya gönderildi',ChangeApproved:'Değişiklik onaylandı',ChangeRejected:'Değişiklik reddedildi',MaterialRequestLinked:'Malzeme talebi bağlandı'} as Record<string,string>)[e.type]||e.type}</td><td>{status(e.fromStatus)} → {status(e.toStatus)}</td><td>{e.version}</td></tr>)}</tbody></table></div>{!events.length&&<p>Bu kayıt için henüz işlem geçmişi yok.</p>}
  </>}
 </section>
}
