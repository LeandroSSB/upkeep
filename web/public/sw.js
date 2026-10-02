// Service worker do upkeep — escrito à mão (sem Workbox), escopo mínimo:
// app-shell em precache, /api/* SEMPRE rede, assets stale-while-revalidate.
// Nada de background sync/push (fora de escopo).
//
// bumpar VERSION a cada release (assets com hash antigo acumulam até aqui) —
// o activate apaga os caches de toda versão anterior.
const VERSION = "upkeep-v1";
// "/" fica DE FORA do precache (peso morto): o fallback offline e o re-cache
// das navegações usam "/index.html" — nada no fetch handler pede "/".
const SHELL = ["/index.html", "/favicon.svg", "/manifest.webmanifest"];

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
          // try/catch PRÓPRIO: QuotaExceeded no put não pode derrubar o respond.
          if (fresh.ok) {
            try {
              const cache = await caches.open(VERSION);
              await cache.put("/index.html", fresh.clone());
            } catch {
              // sem espaço em cache — seguimos com a resposta fresca na mesma
            }
          }
          // Trade-off deliberado: resposta non-ok (502/503 do nginx em deploy,
          // 404...) é devolvida ASSIM, não trocada pelo shell em cache — o
          // contrário mascararia a falha com um app cujas chamadas de /api
          // falhariam de qualquer jeito. Só falha de REDE (catch) cai pro shell.
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
            // cache.put isolado com .catch: QuotaExceeded (ou cache indisponível)
            // não derruba o respond — o item continua válido no cache atual.
            if (res && res.ok) cache.put(event.request, res.clone()).catch(() => {});
            return res;
          })
          .catch(() => undefined);
        return cached || fresh;
      })(),
    );
  }
});
