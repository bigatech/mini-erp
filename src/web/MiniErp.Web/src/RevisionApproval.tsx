import React from 'react'

type Revision = {id:string;modelId:string;revisionNumber:string;status:string}
type Props = {api:string;revisionId:string;status:string;onChanged:()=>void;onCloned:(revision:Revision)=>void}

export default function RevisionApproval({api,revisionId,status,onChanged,onCloned}:Props) {
 const [busy,setBusy]=React.useState(false),[error,setError]=React.useState(''),[notice,setNotice]=React.useState(''),[number,setNumber]=React.useState('')
 const posting=React.useRef(false)
 React.useEffect(()=>{setError('');setNotice('');setNumber('')},[revisionId])
 async function act(action:'submit'|'approve'|'reject'|'clone') {
  if(posting.current||!revisionId)return
  setError('');setNotice('')
  if(action==='clone'&&!number.trim()){setError('Yeni revizyon numarası girin.');return}
  posting.current=true;setBusy(true)
  try {
   const response=await fetch(`${api}/api/v1/plm/revisions/${encodeURIComponent(revisionId)}/${action}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(action==='clone'?{revisionNumber:number.trim()}:{})})
   const value=await response.json().catch(()=>({}))
   if(!response.ok)throw new Error(Object.values(value.errors||{}).flat().join(' ')||value.title||'Revizyon işlemi tamamlanamadı.')
   if(action==='clone') {
    const created=(value.data??value) as Revision
    if(!created.id)throw new Error('Yeni revizyon yanıtı alınamadı. Revizyon listesini kontrol edin.')
    setNumber('');onCloned(created)
   } else onChanged()
   setNotice(action==='clone'?'Yeni taslak revizyon oluşturuldu.':action==='submit'?'Revizyon onaya gönderildi.':action==='approve'?'Revizyon onaylandı.':'Revizyon taslağa geri gönderildi.')
  }catch(e){setError(e instanceof Error?e.message:'Revizyon işlemi tamamlanamadı.')}
  finally{posting.current=false;setBusy(false)}
 }
 const label=status==='Draft'?'Taslak':status==='PendingApproval'?'Onay bekliyor':status==='Approved'?'Onaylı':status
 return <section className="panel data-quality" aria-label="Revizyon onayı">
  <h2>Revizyon onayı</h2><p>Durum: <strong>{label}</strong></p>
  <p className="quality-note">Onay yetkilendirmesi yerel geliştirme aşamasındadır.</p>
  {error&&<p role="alert" className="feedback error">{error}</p>}
  {notice&&<p role="status" className="feedback success">{notice}</p>}
  {busy&&<p role="status">Revizyon işlemi sürüyor…</p>}
  {status==='Draft'&&<button type="button" className="submit-button" disabled={busy||!revisionId} onClick={()=>void act('submit')}>Onaya gönder</button>}
  {status==='PendingApproval'&&<div><button type="button" className="submit-button" disabled={busy} onClick={()=>void act('approve')}>Onayla</button>{' '}<button type="button" disabled={busy} onClick={()=>void act('reject')}>Taslağa geri gönder</button></div>}
  {status==='Approved'&&<>
   <p>Onaylı revizyonun ürün ağacı ve rotası salt okunurdur. Değişiklik için yeni taslak revizyona kopyalayın.</p>
   <form aria-label="Onaylı revizyonu kopyala" onSubmit={event=>{event.preventDefault();void act('clone')}}>
    <fieldset disabled={busy} className="product-form-fields"><label className="definition-label">Yeni revizyon numarası<input aria-label="Kopya revizyon numarası" required value={number} onChange={event=>setNumber(event.target.value)}/></label><button type="submit" className="submit-button">Yeni taslak revizyona kopyala</button></fieldset>
   </form>
  </>}
 </section>
}
