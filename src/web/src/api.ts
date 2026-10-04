let etag = '';
export async function apiWithVersion<T = any>(path: string, body?: unknown, method = body === undefined ? 'GET' : 'POST', key?: string, expectedVersion?:string): Promise<{data:T,version:string}> {
  const response = await fetch('/api/v1' + path, { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-Learning-Request': '1', ...(body === undefined ? {} : { 'Idempotency-Key': key || crypto.randomUUID(), 'If-Match': expectedVersion??etag }) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  const next = response.headers.get('etag'); if (next) etag = next;
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.title || `请求失败（${response.status}）`);
  return {data,version:next||''};
}
export async function api<T = any>(path:string,body?:unknown,method=body===undefined?'GET':'POST',key?:string,expectedVersion?:string):Promise<T>{return (await apiWithVersion<T>(path,body,method,key,expectedVersion)).data;}

export function localDate(zone = 'Asia/Shanghai') {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  return `${parts.find(p=>p.type==='year')!.value}-${parts.find(p=>p.type==='month')!.value}-${parts.find(p=>p.type==='day')!.value}`;
}

// Preserve the response's numeric notation and payload bytes for local evaluation captures.
// A download does not replace the page's optimistic review version.
export async function readJsonText(path:string):Promise<string>{
  const response=await fetch('/api/v1'+path,{credentials:'same-origin',headers:{'X-Learning-Request':'1'}});
  const body=await response.text();
  if(!response.ok){let problem:{title?:string}={};try{problem=JSON.parse(body)}catch{}throw new Error(problem.title||`请求失败（${response.status}）`);}
  if(!response.headers.get('content-type')?.includes('application/json'))throw new Error('评测材料格式无法确认，请刷新后重试。');
  return body;
}
