import { requireUser } from './sharedAuth'

type CacheEntry={expiresAt:number;items:any[]}
const searchCache=new Map<string,CacheEntry>()

function error(c:any,message:string,status:number){return c.json({ok:false,error:message},{status,headers:{'Cache-Control':'no-store'}})}
function clean(value:unknown,max:number){return String(value??'').trim().slice(0,max)}

export function registerMediaRoutes(app:any){
  app.get('/me/media/youtube/search',async(c:any)=>{
    const user=await requireUser(c)
    if(!user)return error(c,'Sessão inválida ou expirada.',401)
    const query=clean(c.req.query('q'),120)
    if(query.length<2)return error(c,'Digite pelo menos 2 caracteres para pesquisar.',400)
    const apiKey=clean(c.env.YOUTUBE_API_KEY,500)
    if(!apiKey)return error(c,'Pesquisa do YouTube ainda não está configurada no servidor.',503)

    const cacheKey=query.toLocaleLowerCase('pt-BR')
    const now=Date.now(),cached=searchCache.get(cacheKey)
    if(cached&&cached.expiresAt>now)return c.json({ok:true,items:cached.items,cached:true},{headers:{'Cache-Control':'private, max-age=60'}})

    try{
      const url=new URL('https://www.googleapis.com/youtube/v3/search')
      url.searchParams.set('part','snippet');url.searchParams.set('type','video');url.searchParams.set('videoEmbeddable','true')
      url.searchParams.set('maxResults','8');url.searchParams.set('q',query);url.searchParams.set('key',apiKey)
      const response=await fetch(url.toString(),{headers:{'Accept':'application/json'}})
      if(!response.ok){console.error('youtube_search_upstream_error',response.status);return error(c,'Não foi possível pesquisar no YouTube agora.',502)}
      const payload:any=await response.json()
      const items=(Array.isArray(payload?.items)?payload.items:[]).map((item:any)=>({
        provider:'YOUTUBE',id:clean(item?.id?.videoId,80),title:clean(item?.snippet?.title,300),artist:clean(item?.snippet?.channelTitle,200),
        artwork:clean(item?.snippet?.thumbnails?.medium?.url||item?.snippet?.thumbnails?.default?.url,600),playbackId:clean(item?.id?.videoId,80)
      })).filter((item:any)=>item.id&&item.title)
      searchCache.set(cacheKey,{expiresAt:now+15*60*1000,items})
      if(searchCache.size>100){for(const [key,value] of searchCache)if(value.expiresAt<=now)searchCache.delete(key);if(searchCache.size>100)searchCache.delete(searchCache.keys().next().value)}
      return c.json({ok:true,items,cached:false},{headers:{'Cache-Control':'private, max-age=60'}})
    }catch(err){console.error('youtube_search_error',err);return error(c,'Não foi possível pesquisar no YouTube agora.',502)}
  })
}
