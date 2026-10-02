// Registro do service worker (só em build de produção) + fluxo de update.
// Quando um worker novo fica "waiting", dispara o evento window
// 'upkeep-update' — a UpdateBar oferece o recarregamento. O registration
// vive neste módulo para o botão achar o worker em waiting.
export const UPDATE_EVENT = "upkeep-update";

let registration: ServiceWorkerRegistration | null = null;

export function getSwRegistration(): ServiceWorkerRegistration | null {
  return registration;
}

function notifyUpdate(): void {
  window.dispatchEvent(new Event(UPDATE_EVENT));
}

export function registerServiceWorker(): void {
  if (!import.meta.env.PROD || !("serviceWorker" in navigator)) return;

  navigator.serviceWorker
    .register("/sw.js")
    .then((reg) => {
      registration = reg;

      // Worker já esperando quando a página abriu (update baixado antes).
      if (reg.waiting && navigator.serviceWorker.controller) notifyUpdate();

      reg.addEventListener("updatefound", () => {
        const worker = reg.installing;
        if (!worker) return;
        worker.addEventListener("statechange", () => {
          if (
            worker.state === "installed" &&
            navigator.serviceWorker.controller
          ) {
            notifyUpdate();
          }
        });
      });

      // Poll leve: pega deploys feitos com a aba aberta por horas.
      window.setInterval(() => {
        void reg.update().catch(() => undefined);
      }, 60_000);
    })
    .catch(() => {
      /* SW é aprimoramento progressivo — sem ele o app funciona igual. */
    });
}
