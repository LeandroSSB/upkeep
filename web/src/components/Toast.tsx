// Aviso de sucesso: rodapé central, some sozinho em 3s. O timer depende só da
// mensagem (onHide via ref) — re-renders do pai não reiniciam a contagem.
import { useEffect, useRef } from "react";

export function Toast({ message, onHide }: { message: string; onHide: () => void }) {
  const hide = useRef(onHide);
  hide.current = onHide;

  useEffect(() => {
    const timer = window.setTimeout(() => hide.current(), 3000);
    return () => window.clearTimeout(timer);
  }, [message]);

  return (
    <div className="toast" role="status">
      {message}
    </div>
  );
}
