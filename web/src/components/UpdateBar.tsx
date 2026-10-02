// Banner fixo "Nova versão disponível" — aparece quando o sw-register detecta
// um worker em waiting (evento 'upkeep-update'). "Recarregar" manda
// SKIP_WAITING pro worker em waiting e recarrega a página quando ele assume
// o controle (controllerchange, uma única vez).
import { useEffect, useState } from "react";
import { getSwRegistration, UPDATE_EVENT } from "../sw-register";

export function UpdateBar() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const show = () => setVisible(true);
    window.addEventListener(UPDATE_EVENT, show);
    return () => window.removeEventListener(UPDATE_EVENT, show);
  }, []);

  if (!visible) return null;

  const reload = () => {
    const waiting = getSwRegistration()?.waiting;
    if (!waiting) {
      window.location.reload(); // nada em waiting (caso raro) — recarrega direto
      return;
    }
    navigator.serviceWorker.addEventListener(
      "controllerchange",
      () => window.location.reload(),
      { once: true },
    );
    waiting.postMessage({ type: "SKIP_WAITING" });
  };

  return (
    <div className="update-bar" role="status">
      <span>Nova versão disponível</span>
      <button className="update-bar__btn" type="button" onClick={reload}>
        Recarregar
      </button>
    </div>
  );
}
