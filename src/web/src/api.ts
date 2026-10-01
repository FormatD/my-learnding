let etag = '';
export async function api<T = any>(path: string, body?: unknown, method = body === undefined ? 'GET' : 'POST', key?: string): Promise<T> {
  const response = await fetch('/api/v1' + path, { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-Learning-Request': '1', ...(body === undefined ? {} : { 'Idempotency-Key': key || crypto.randomUUID(), 'If-Match': etag }) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  const next = response.headers.get('etag'); if (next) etag = next;
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.title || `请求失败（${response.status}）`);
  return data;
}
export function localDate(zone = 'Asia/Shanghai') {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  return `${parts.find(p=>p.type==='year')!.value}-${parts.find(p=>p.type==='month')!.value}-${parts.find(p=>p.type==='day')!.value}`;
}
