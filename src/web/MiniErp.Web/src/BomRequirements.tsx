import React from 'react'

type BomLine={id:string;componentCode:string;componentName:string;unit:string;quantity:number;parentLineId?:string|null;variantOptionId?:string|null;sizeOptionId?:string|null;setPartOptionId?:string|null;label?:string|null;attributes?:Record<string,string>|null}
type Option={id:string;kind:string;code:string;name:string}
type RequirementRow={lineId:string;parentLineId?:string|null;level:number;componentCode:string;componentName:string;unit:string;recipeQuantity:number;wasteRate:number;netQuantity:number|null;grossQuantity:number|null;perTopUnit:number|null;isLeaf:boolean}
type Requirements={rootLineId:string;netQuantity:number;method:string;complete:boolean;rows:RequirementRow[];warnings:string[]}

export default function BomRequirements({api,modelId,revisionId}:{api:string;modelId:string;revisionId:string}){
 const [lines,setLines]=React.useState<BomLine[]>([]),[options,setOptions]=React.useState<Option[]>([])
 const [rootId,setRootId]=React.useState(''),[amount,setAmount]=React.useState(''),[variant,setVariant]=React.useState(''),[size,setSize]=React.useState(''),[part,setPart]=React.useState('')
 const [loading,setLoading]=React.useState(true),[busy,setBusy]=React.useState(false),[error,setError]=React.useState(''),[result,setResult]=React.useState<Requirements|null>(null)
 const posting=React.useRef(false),calculation=React.useRef<AbortController|null>(null),generation=React.useRef(0)
 function invalidate(){generation.current++;calculation.current?.abort();posting.current=false;setBusy(false);setResult(null);setError('')}
 React.useEffect(()=>{
  const c=new AbortController();generation.current++;calculation.current?.abort();posting.current=false;setBusy(false);setLoading(true);setResult(null);setError('');setRootId('');setAmount('');setVariant('');setSize('');setPart('');setLines([]);setOptions([])
  async function read<T>(path:string):Promise<T>{const r=await fetch(api+path,{signal:c.signal});const b=await r.json();if(!r.ok)throw new Error(b.title||'BOM bilgileri alınamadı.');return b.data as T}
  void Promise.all([read<BomLine[]>(`/api/v1/plm/revisions/${revisionId}/bom`),read<Option[]>(`/api/v1/plm/models/${modelId}/options`)]).then(([l,o])=>{if(!c.signal.aborted){setLines(l);setOptions(o)}}).catch(e=>{if(!c.signal.aborted)setError(e.message)}).finally(()=>{if(!c.signal.aborted)setLoading(false)})
  return()=>{c.abort();generation.current++;calculation.current?.abort();posting.current=false}
 },[api,modelId,revisionId])
 const roots=lines.filter(l=>!l.parentLineId),root=roots.find(l=>l.id===rootId)
 const label=(id?:string|null)=>options.find(o=>o.id===id)?.name||'Ortak'
 function selectRoot(id:string){invalidate();setRootId(id);const r=roots.find(l=>l.id===id);setAmount(r?String(r.quantity):'');setVariant(r?.variantOptionId||'');setSize(r?.sizeOptionId||'');setPart(r?.setPartOptionId||'')}
 async function calculate(e:React.FormEvent){
  e.preventDefault();if(posting.current)return;setError('');setResult(null)
  const quantity=Number(amount);if(!root||!amount.trim()||!Number.isFinite(quantity)||quantity<=0){setError('Üst malzemeyi seçin ve pozitif net miktar girin.');return}
  posting.current=true;setBusy(true);const c=new AbortController();calculation.current=c;const token=++generation.current
  try{const r=await fetch(`${api}/api/v1/plm/revisions/${revisionId}/bom/requirements`,{method:'POST',signal:c.signal,headers:{'Content-Type':'application/json'},body:JSON.stringify({rootLineId:rootId,netQuantity:quantity,variantOptionId:variant||null,sizeOptionId:size||null,setPartOptionId:part||null})});const b=await r.json().catch(()=>({}));if(!r.ok)throw new Error(Object.values(b.errors||{}).flat().join(' ')||b.title||'Malzeme ihtiyacı hesaplanamadı.');if(generation.current===token)setResult((b.data??b) as Requirements)}catch(e){if(!c.signal.aborted&&generation.current===token)setError(e instanceof Error?e.message:'Hesaplama tamamlanamadı.')}finally{if(generation.current===token){posting.current=false;setBusy(false)}}
 }
 const fmt=(value:number|null|undefined)=>value==null?'Hesaplanamadı':new Intl.NumberFormat('tr-TR',{maximumFractionDigits:6}).format(value)
 function scope(title:string,kind:string,value:string,set:(v:string)=>void,fixed?:string|null){return <label className="definition-label">{title}<select aria-label={title} disabled={Boolean(fixed)} value={value} onChange={e=>{invalidate();set(e.target.value)}}><option value="">Seçin / ortak kapsam</option>{options.filter(o=>o.kind===kind).map(o=><option key={o.id} value={o.id}>{o.code} — {o.name}</option>)}</select></label>}
 return <section className="panel data-quality" aria-label="Çok seviyeli BOM ihtiyaç hesabı"><h2>Çok seviyeli BOM ihtiyaç hesabı</h2><p>Her seviyede brüt ihtiyaç = net ihtiyaç ÷ (1 − fire oranı). Alt seviyenin net ihtiyacı, üst seviyenin brüt miktarı × kendi reçete miktarıdır.</p><p>Reçete miktarları değiştirilmez. Bu hesap stok hareketi veya üretim emri oluşturmaz; set çarpanı uygulanmaz.</p>
  {loading&&<p role="status">Reçete ve model kapsamları yükleniyor…</p>}{error&&<p role="alert" className="feedback error">{error}</p>}
  <form onSubmit={e=>void calculate(e)}><fieldset disabled={loading||busy}><label className="definition-label">Üst malzeme<select aria-label="İhtiyaç hesabı üst malzeme" value={rootId} required onChange={e=>selectRoot(e.target.value)}><option value="">Kök malzeme seçin</option>{roots.map(l=><option value={l.id} key={l.id}>{l.componentCode} — {l.label||l.componentName} #{l.id.slice(0,8)} [{label(l.variantOptionId)} / {label(l.sizeOptionId)} / {label(l.setPartOptionId)}]</option>)}</select></label><label className="definition-label">Üst malzeme net miktarı {root?`(${root.unit})`:''}<input aria-label="İhtiyaç hesabı üst net miktar" type="number" min="0" step="any" required value={amount} onChange={e=>{invalidate();setAmount(e.target.value)}}/></label>
   {root&&<><p>Kaydedilmiş üst reçete miktarı: {fmt(root.quantity)} {root.unit}. Hesap miktarı bu değerden başlatılır; reçeteye yazılmaz.</p>{scope('İhtiyaç hesabı varyantı','variant',variant,setVariant,root.variantOptionId)}{scope('İhtiyaç hesabı bedeni','size',size,setSize,root.sizeOptionId)}{scope('İhtiyaç hesabı set parçası','set-part',part,setPart,root.setPartOptionId)}</>}
   <button className="submit-button" disabled={!rootId} type="submit">{busy?'Hesaplanıyor…':'Malzeme ihtiyacını hesapla'}</button></fieldset></form>
  {!loading&&!roots.length&&<p>Hesaplamak için revizyona kök malzeme ekleyin.</p>}
  {result&&<><p role="status">{result.complete?'Hesap tamamlandı.':'Hesap eksik; hesaplanamayan satırlar aşağıda belirtilmiştir.'} Üst net miktar: {fmt(result.netQuantity)} {root?.unit}.</p>{result.warnings.map((warning,index)=><p className="feedback error" role="alert" key={index}>{warning}</p>)}
   <div className="table-wrap"><table><thead><tr><th>Seviye / malzeme</th><th>Reçete miktarı</th><th>Kendi firesi</th><th>Net ihtiyaç</th><th>Fire dahil brüt ihtiyaç</th><th>Üst malzemenin 1 {root?.unit||'birimi'} için brüt</th></tr></thead><tbody>{result.rows.map(row=><tr key={row.lineId} style={row.isLeaf?{background:'#eef6ee'}:undefined}><td style={{paddingLeft:12+Math.min(row.level,20)*18}}>{row.level>0?'↳ ':''}{row.componentCode} — {row.componentName}{row.isLeaf?' · En alt seviye':''}</td><td>{fmt(row.recipeQuantity)} {row.unit}</td><td>{fmt(row.wasteRate*100)}%</td><td>{fmt(row.netQuantity)} {row.unit}</td><td><b>{fmt(row.grossQuantity)}</b> {row.unit}</td><td>{fmt(row.perTopUnit)} {row.unit}</td></tr>)}</tbody></table></div><p>Birimleri farklı malzemeler toplanmaz. Gösterim en fazla 6 ondalıktır; hesapta ara yuvarlama yapılmaz. %100 fireli veya hesaplanamayan bir satır sıfır olarak gösterilmez.</p></>}
 </section>
}
