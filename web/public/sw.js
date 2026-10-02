// Service worker do upkeep — escrito à mão (sem Workbox), escopo mínimo:
// app-shell em precache, /api/* SEMPRE rede, assets stale-while-revalidate.
// Nada de background sync/push (fora de escopo). Bump no VERSION a cada deploy
// → activate apaga os caches da versão anterior.
const VERSION = "upkeep-v1";
const SHELL = ["/", "/index.html", "/favicon.svg", "/manifest.webmanifest"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(SHELL)));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(
        names.filter((n) => n !== VERSION).map((n) => caches.delete(n)),
      );
      await self.clients.claim();
    })(),
  );
});

// Fluxo de update: a UI manda SKIP_WAITING quando o usuário aceita recarregar.
self.addEventListener("message", (event) => {
  if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting();
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  // API: dados sempre frescos — passthrough puro (sem respondWith, sem cache).
  if (url.pathname.startsWith("/api/")) return;

  // Navegação: network-first; offline (ou erro) → index.html em cache.
  if (event.request.mode === "navigate") {
    event.respondWith(
      (async () => {
        try {
          const fresh = await fetch(event.request);
          // Mantém o shell em cache atualizado para o próximo modo offline —
          // só resposta ok: um 503/504 do nginx não pode virar o shell offline.
          const cache = await caches.open(VERSION);
          if (fresh.ok) await cache.put("/index.html", fresh.clone());
          return fresh;
        } catch {
          const cache = await caches.open(VERSION);
          return (await cache.match("/index.html")) || Response.error();
        }
      })(),
    );
    return;
  }

  // Demais GETs same-origin (js/css/fontes/imagens): stale-while-revalidate.
  if (event.request.method === "GET" && url.origin === self.location.origin) {
    event.respondWith(
      (async () => {
        const cache = await caches.open(VERSION);
        const cached = await cache.match(event.request);
        const fresh = fetch(event.request)
          .then((res) => {
            if (res && res.ok) cache.put(event.request, res.clone());
            return res;
          })
          .catch(() => undefined);
        return cached || fresh;
      })(),
    );
  }
});
