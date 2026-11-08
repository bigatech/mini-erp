import React from 'react'
import ModelConfiguration from './ModelConfiguration'
import ProductionRoute from './ProductionRoute'
import RevisionApproval from './RevisionApproval'
import ModelOrderHistory from './ModelOrderHistory'
import ModelImages from './ModelImages'

type Model = {id:string;code:string;name:string;status:string}
type Revision = {id:string;modelId:string;revisionNumber:string;status:string}
type Product = {id:string;code:string;name:string;stockUnit:string;isActive:boolean}
type BomLine = {id:string;revisionId:string;componentProductId:string;componentCode:string;componentName:string;unit:string;quantity:number;wasteRate:number;parentLineId?:string|null;variantOptionId?:string|null;sizeOptionId?:string|null;setPartOptionId?:string|null}

export default function PlmModels({api}:{api:string}) {
 const [models,setModels]=React.useState<Model[]>([]),[products,setProducts]=React.useState<Product[]>([])
 const [modelId,setModelId]=React.useState(''),[revisionId,setRevisionId]=React.useState('')
 const [revisions,setRevisions]=React.useState<Revision[]>([]),[lines,setLines]=React.useState<BomLine[]>([])
 const [model,setModel]=React.useState({code:'',name:''}),[revisionNumber,setRevisionNumber]=React.useState('')
 const [componentId,setComponentId]=React.useState(''),[quantity,setQuantity]=React.useState(''),[waste,setWaste]=React.useState('0')
 const [error,setError]=React.useState(''),[notice,setNotice]=React.useState(''),[busy,setBusy]=React.useState(false)
 const [loading,setLoading]=React.useState(true),[revisionLoading,setRevisionLoading]=React.useState(false),[bomLoading,setBomLoading]=React.useState(false)
 const posting=React.useRef(false)
 const currentModelId=React.useRef(modelId);currentModelId.current=modelId
 const [bomNonce,setBomNonce]=React.useState(0)
 const [orderNonce,setOrderNonce]=React.useState(0)
 async function request<T>(path:string,signal?:AbortSignal,body?:object):Promise<T> {
  const response=await fetch(api+path,{signal,...(body?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:{})})
  const value=await response.json().catch(()=>({}))
  if(!response.ok)throw new Error(Object.values(value.errors||{}).flat().join(' ')||value.title||'İşlem tamamlanamadı.')
  return (value.data??value) as T
 }
 React.useEffect(()=>{const c=new AbortController();setLoading(true);void Promise.all([request<Model[]>('/api/v1/plm/models',c.signal),request<Product[]>('/api/v1/plm/products',c.signal)]).then(([m,p])=>{setModels(m);setProducts(p)}).catch(e=>{if(e.name!=='AbortError')setError(e.message)}).finally(()=>{if(!c.signal.aborted)setLoading(false)});return()=>c.abort()},[api])
 React.useEffect(()=>{const c=new AbortController();setRevisions([]);setRevisionId('');setLines([]);if(!modelId){setRevisionLoading(false);return()=>c.abort()}setRevisionLoading(true);void request<Revision[]>(`/api/v1/plm/models/${encodeURIComponent(modelId)}/revisions`,c.signal).then(setRevisions).catch(e=>{if(e.name!=='AbortError')setError(e.message)}).finally(()=>{if(!c.signal.aborted)setRevisionLoading(false)});return()=>c.abort()},[api,modelId])
 React.useEffect(()=>{const c=new AbortController();setLines([]);if(!revisionId){setBomLoading(false);return()=>c.abort()}setBomLoading(true);void request<BomLine[]>(`/api/v1/plm/revisions/${encodeURIComponent(revisionId)}/bom`,c.signal).then(setLines).catch(e=>{if(e.name!=='AbortError')setError(e.message)}).finally(()=>{if(!c.signal.aborted)setBomLoading(false)});return()=>c.abort()},[api,revisionId,bomNonce])
 async function save(event:React.FormEvent,kind:'model'|'revision'|'line') {
  event.preventDefault();if(posting.current)return;setError('');setNotice('')
  const q=Number(quantity),w=Number(waste)
  if(kind==='line'&&(!revisionId||!componentId||!quantity.trim()||!waste.trim()||!Number.isFinite(q)||q<=0||!Number.isFinite(w)||w<0||w>1)){setError('Bileşen seçin; miktar pozitif, fire oranı 0–1 arasında olmalıdır.');return}
  if(kind==='model'&&(!model.code.trim()||!model.name.trim())){setError('Model kodu ve adı zorunludur.');return}
  if(kind==='revision'&&(!modelId||!revisionNumber.trim())){setError('Model seçin ve revizyon numarası girin.');return}
  posting.current=true;setBusy(true)
  try {
   if(kind==='model') {const created=await request<Model>('/api/v1/plm/models',undefined,{code:model.code.trim(),name:model.name.trim()});setModels(current=>[...current,created]);setModel({code:'',name:''});setModelId(created.id);setNotice('Model kaydedildi. Revizyon açabilirsiniz.')}
   else if(kind==='revision') {const created=await request<Revision>(`/api/v1/plm/models/${encodeURIComponent(modelId)}/revisions`,undefined,{revisionNumber:revisionNumber.trim()});setRevisions(current=>[...current,created]);setRevisionNumber('');setRevisionId(created.id);setNotice('Taslak revizyon kaydedildi. Ürün ağacı kalemi ekleyebilirsiniz.')}
   else {const created=await request<BomLine>(`/api/v1/plm/revisions/${encodeURIComponent(revisionId)}/bom`,undefined,{componentProductId:componentId,quantity:q,wasteRate:w});setLines(current=>[...current,created]);setComponentId('');setQuantity('');setWaste('0');setNotice('Ürün ağacı kalemi kaydedildi.')}
  }catch(e){setError(e instanceof Error?e.message:'Kayıt sonucu alınamadı. Listeyi kontrol edin.')}finally{posting.current=false;setBusy(false)}
 }
 const selectedModel=models.find(m=>m.id===modelId),selectedRevision=revisions.find(r=>r.id===revisionId)
 function refreshRevisionStatus(){const expected=modelId;void request<Revision[]>(`/api/v1/plm/models/${encodeURIComponent(expected)}/revisions`).then(r=>{if(currentModelId.current===expected)setRevisions(r)}).catch(e=>setError(e.message))}
 function acceptClone(created:Revision){if(created.modelId!==currentModelId.current)return;setRevisions(r=>[...r,created]);setRevisionId(created.id);setBomNonce(n=>n+1);setNotice('BOM ve rota yeni taslak revizyona kopyalandı.')}
 const number=(value:number)=>new Intl.NumberFormat('tr-TR',{minimumFractionDigits:3,maximumFractionDigits:3}).format(value)
 return <>
  <p className="quality-note">Model → taslak revizyon → ürün ağacı. Bu ekrandaki taslaklar üretime açılamaz; Teknik içerik onaydan sonra kilitlenir; üretime açma ve firma/yetki entegrasyonu henüz tamamlanmadı.</p>
  {error&&<p role="alert" className="feedback error">{error}</p>}{notice&&<p role="status" className="feedback success">{notice}</p>}
  {loading&&<p role="status">Modeller ve bileşen ürünler yükleniyor…</p>}
  <div className="definitions-grid">
   <section className="panel data-quality"><h2>Modeller</h2><p>{models.length} model</p>
    <label className="definition-label">Model<select aria-label="PLM model seçimi" value={modelId} disabled={loading||busy} onChange={e=>{setModelId(e.target.value);setError('');setNotice('')}}><option value="">Model seçin</option>{models.map(m=><option key={m.id} value={m.id}>{m.code} — {m.name}</option>)}</select></label>
    {!loading&&!models.length&&<p>Henüz model yok. İlk modeli tanımlayın.</p>}
    <form onSubmit={e=>void save(e,'model')} aria-label="Yeni model"><h3>Yeni model</h3><fieldset disabled={loading||busy} className="product-form-fields"><label className="definition-label">Model kodu<input aria-label="Model kodu" required value={model.code} onChange={e=>setModel(v=>({...v,code:e.target.value}))}/></label><label className="definition-label">Model adı<input aria-label="Model adı" required value={model.name} onChange={e=>setModel(v=>({...v,name:e.target.value}))}/></label><button type="submit" className="submit-button">Model oluştur</button></fieldset></form>
   </section>
   <section className="panel data-quality"><h2>Model revizyonları</h2>{!modelId?<p>Revizyonları görmek için model seçin.</p>:<><p>{selectedModel?.code} · {selectedModel?.name}</p>{revisionLoading&&<p role="status">Revizyonlar yükleniyor…</p>}<label className="definition-label">Revizyon<select aria-label="PLM revizyon seçimi" value={revisionId} disabled={busy||revisionLoading} onChange={e=>{setRevisionId(e.target.value);setError('');setNotice('')}}><option value="">Revizyon seçin</option>{revisions.map(r=><option key={r.id} value={r.id}>{r.revisionNumber} · {r.status==='Draft'?'Taslak':r.status}</option>)}</select></label>{!revisionLoading&&!revisions.length&&<p>Bu modelde henüz revizyon yok.</p>}<form onSubmit={e=>void save(e,'revision')} aria-label="Yeni taslak revizyon"><h3>Taslak revizyon aç</h3><fieldset disabled={busy||revisionLoading} className="product-form-fields"><label className="definition-label">Revizyon numarası<input aria-label="Revizyon numarası" required value={revisionNumber} onChange={e=>setRevisionNumber(e.target.value)}/></label><button type="submit" className="submit-button">Revizyon oluştur</button></fieldset></form></>}</section>
  </div>
  {modelId&&<ModelImages api={api} modelId={modelId}/>}
  {modelId&&<ModelOrderHistory api={api} modelId={modelId} revisions={revisions} refresh={orderNonce}/>}
  {selectedRevision&&<RevisionApproval api={api} revisionId={revisionId} status={selectedRevision.status} onChanged={refreshRevisionStatus} onCloned={acceptClone}/>}
  <section className="panel data-quality"><h2>Ürün ağacı — ortak kök malzemeler</h2>{!revisionId?<p>Ürün ağacını görmek için model ve revizyon seçin.</p>:<><p>{selectedModel?.code} / {selectedRevision?.revisionNumber} · {selectedRevision?.status==='Draft'?'Taslak':selectedRevision?.status}</p>{bomLoading?<p role="status">Ürün ağacı yükleniyor…</p>:<><div className="table-wrap"><table><thead><tr><th>Bileşen kodu</th><th>Bileşen adı</th><th>Miktar</th><th>Birim</th><th>Fire oranı</th></tr></thead><tbody>{lines.filter(line=>!line.parentLineId&&!line.variantOptionId&&!line.sizeOptionId&&!line.setPartOptionId).map(line=><tr key={line.id}><td>{line.componentCode}</td><td>{line.componentName}</td><td>{number(line.quantity)}</td><td>{line.unit}</td><td>{number(line.wasteRate)}</td></tr>)}</tbody></table></div>{!lines.length&&<p>Bu revizyona henüz bileşen eklenmedi.</p>}</>}<form onSubmit={e=>void save(e,'line')} aria-label="Ürün ağacı kalemi"><h3>Ortak kök bileşen ekle</h3><fieldset className="product-form-fields" disabled={busy||bomLoading||selectedRevision?.status!=='Draft'}><label className="definition-label">Bileşen ürün<select aria-label="BOM bileşen ürünü" required value={componentId} onChange={e=>setComponentId(e.target.value)}><option value="">Ürün seçin</option>{products.filter(p=>p.isActive).map(p=><option key={p.id} value={p.id}>{p.code} — {p.name} ({p.stockUnit})</option>)}</select></label><label className="definition-label">Miktar<input aria-label="BOM miktarı" type="number" step="any" required min="0" value={quantity} onChange={e=>setQuantity(e.target.value)}/></label><label className="definition-label">Fire oranı (0–1)<input aria-label="BOM fire oranı" type="number" step="any" required min="0" max="1" value={waste} onChange={e=>setWaste(e.target.value)}/></label><p>Birim, bileşen ürünün stok biriminden alınır.</p><button type="submit" className="submit-button">Kalem ekle</button></fieldset></form></>}</section>
 {modelId&&<ModelConfiguration key={`${modelId}:${revisionId}:${lines.length}`} api={api} modelId={modelId} revisionId={revisionId} onBomChanged={()=>setBomNonce(n=>n+1)} onOrderChanged={()=>setOrderNonce(n=>n+1)} readOnly={Boolean(revisionId)&&selectedRevision?.status!=='Draft'}/>}
 {revisionId&&<ProductionRoute key={revisionId} api={api} revisionId={revisionId} readOnly={Boolean(revisionId)&&selectedRevision?.status!=='Draft'}/>}
 </>
}

