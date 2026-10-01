const PREFIX='learning-shell-';
let cacheName;
self.addEventListener('install', event=>event.waitUntil((async()=>{
  const response=await fetch('/',{cache:'no-store'}),html=await response.clone().text();
  const assets=[...html.matchAll(/(?:src|href)="(\/assets\/[^" ]+)"/g)].map(m=>m[1]);
  cacheName=PREFIX+assets.join('|');const cache=await caches.open(cacheName);
  await cache.put('/',response);await cache.addAll(assets.concat(['/manifest.webmanifest','/icon.svg']));
  await self.skipWaiting();
})()));
self.addEventListener('activate', event=>event.waitUntil((async()=>{
  const names=await caches.keys();
  // On a restarted worker, infer the newest shell cache by insertion order.
  cacheName=cacheName||names.filter(n=>n.startsWith(PREFIX)).at(-1);
  await Promise.all(names.filter(n=>n.startsWith(PREFIX)&&n!==cacheName).map(n=>caches.delete(n)));
  await self.clients.claim();
})()));
self.addEventListener('fetch',event=>{
  const url=new URL(event.request.url);
  if(url.origin!==self.location.origin||event.request.method!=='GET'||url.pathname.startsWith('/api/'))return;
  if(event.request.mode==='navigate')event.respondWith(fetch(event.request).catch(()=>caches.match('/')));
  else if(url.pathname.startsWith('/assets/')||['/icon.svg','/manifest.webmanifest'].includes(url.pathname))event.respondWith(caches.match(event.request).then(cached=>cached||fetch(event.request)));
});
