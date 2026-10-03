// A ficha de capa: o mais urgente da Home vira a página escura do caderno —
// o único bloco escuro da prancheta. O carimbo atravessa a borda superior e
// "carimba" ao entrar (a assinatura v3); a régua e a tinta do status contam
// o quanto do intervalo já foi consumido.
import { statusLabel } from "../lib/format";
import type { Status } from "../lib/types";

export function HeroPanel({
  titulo,
  subtitulo,
  status,
  destaque,
  gauge,
}: {
  titulo: string;
  subtitulo?: string;
  status: Status;
  destaque: string;
  gauge?: number | null;
}) {
  const pct = gauge != null ? `${Math.round(gauge * 100)}%` : null;
  return (
    <article className={`panel panel--${status}`}>
      <span className={`stamp stamp--${status}`}>{statusLabel(status)}</span>
      <p className="panel__eyebrow">Mais urgente</p>
      <h2 className="panel__title">{titulo}</h2>
      {subtitulo && <p className="panel__sub">{subtitulo}</p>}
      <p className="panel__display">{destaque}</p>
      {pct && (
        <div className="gauge gauge--panel" aria-hidden="true">
          <div className="gauge__fill" style={{ width: pct }} />
        </div>
      )}
    </article>
  );
}
