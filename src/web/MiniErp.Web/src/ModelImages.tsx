import React from 'react'
import { AuthenticatedImage } from './auth'

type ModelImage = {id:string;modelId:string;fileName:string;contentType:string;size:number;uploadedAt:string;url:string}
const allowedTypes = ['image/png','image/jpeg','image/webp']
const maximumSize = 5 * 1024 * 1024

export default function ModelImages({api,modelId}:{api:string;modelId:string}) {
 const [images,setImages]=React.useState<ModelImage[]>([]),[file,setFile]=React.useState<File|null>(null)
 const [loading,setLoading]=React.useState(true),[busy,setBusy]=React.useState(false),[error,setError]=React.useState(''),[notice,setNotice]=React.useState('')
 const generation=React.useRef(0),upload=React.useRef<AbortController|null>(null),input=React.useRef<HTMLInputElement>(null)
 const path=`/api/v1/plm/models/${encodeURIComponent(modelId)}/images`
 async function load(signal:AbortSignal):Promise<ModelImage[]> {
  const response=await fetch(api+path,{signal});const value=await response.json().catch(()=>({}))
  if(!response.ok)throw new Error(value.title||'Model görselleri alınamadı.')
  return (value.data??value) as ModelImage[]
 }
 React.useEffect(()=>{
  const current=++generation.current,controller=new AbortController()
  upload.current?.abort();upload.current=null;setImages([]);setFile(null);setError('');setNotice('');setBusy(false);setLoading(true)
  if(input.current)input.current.value=''
  void load(controller.signal).then(rows=>{if(current===generation.current)setImages(rows)}).catch(e=>{if(e.name!=='AbortError'&&current===generation.current)setError(e.message)}).finally(()=>{if(current===generation.current&&!controller.signal.aborted)setLoading(false)})
  return()=>{controller.abort();upload.current?.abort()}
 },[api,modelId])
 async function save(event:React.FormEvent) {
  event.preventDefault();if(upload.current)return;setError('');setNotice('')
  if(!file){setError('Yüklenecek görseli seçin.');return}
  if(!allowedTypes.includes(file.type)||file.size===0||file.size>maximumSize){setError('PNG, JPEG veya WebP biçiminde, en fazla 5 MB boyutunda bir görsel seçin.');return}
  const current=generation.current,controller=new AbortController();upload.current=controller;setBusy(true)
  try {
   const data=new FormData();data.append('file',file)
   const response=await fetch(api+path,{method:'POST',body:data,signal:controller.signal})
   const value=await response.json().catch(()=>({}))
   if(!response.ok)throw new Error(Object.values(value.errors||{}).flat().join(' ')||value.title||'Görsel yüklenemedi.')
   if(current!==generation.current)return
   setFile(null);if(input.current)input.current.value=''
   setNotice('Görsel kaydedildi.')
   try {const rows=await load(controller.signal);if(current===generation.current)setImages(rows)}
   catch(e){if((e as Error).name!=='AbortError'&&current===generation.current)setError('Görsel kaydedildi ancak liste yenilenemedi. Sayfayı yeniden açarak kontrol edin.')}
  }catch(e){if((e as Error).name!=='AbortError'&&current===generation.current)setError(e instanceof Error?e.message:'Görsel yüklenemedi.')}
  finally{if(upload.current===controller){upload.current=null;if(current===generation.current)setBusy(false)}}
 }
 return <section className="panel data-quality"><h2>Model görselleri</h2><p>Görseller modele aittir; revizyonun teknik onay kaydı yerine geçmez.</p>
  {error&&<p role="alert" className="feedback error">{error}</p>}{notice&&<p role="status" className="feedback success">{notice}</p>}
  <form aria-label="Model görseli yükle" onSubmit={event=>void save(event)}><fieldset className="product-form-fields" disabled={busy||loading}>
   <label className="definition-label">Görsel dosyası<input ref={input} type="file" aria-label="Model görsel dosyası" accept="image/png,image/jpeg,image/webp" onChange={event=>{setFile(event.target.files?.[0]??null);setError('');setNotice('')}}/></label>
   <small>PNG, JPEG, WebP · en fazla 5 MB</small><button type="submit" className="submit-button" disabled={!file}>{busy?'Görsel yükleniyor…':'Görsel yükle'}</button>
  </fieldset></form>
  {loading?<p role="status">Model görselleri yükleniyor…</p>:!images.length?<p>Bu modele henüz görsel eklenmedi.</p>:<div style={{display:'grid',gridTemplateColumns:'repeat(auto-fill,minmax(180px,1fr))',gap:16,marginTop:16}}>{images.map(image=>{
   const imageUrl=image.url.startsWith('/api/v1/plm/models/')?api+image.url:''
   return <figure key={image.id} style={{margin:0,border:'1px solid #d5dce4',borderRadius:10,padding:10}}>
    {!imageUrl?<p>Görsel yüklenemedi.</p>:<AuthenticatedImage url={imageUrl} alt={image.fileName}/>}
    <figcaption style={{overflowWrap:'anywhere'}}><b>{image.fileName}</b><br/><small>{(image.size/1024).toLocaleString('tr-TR',{maximumFractionDigits:1})} KB · {new Date(image.uploadedAt).toLocaleString('tr-TR')}</small></figcaption>
   </figure>
  })}</div>}
 </section>
}
