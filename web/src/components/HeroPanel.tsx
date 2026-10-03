// O mostrador aceso: o mais urgente da Home vira painel escuro retroiluminado
// — o único elemento "ligado" da prancheta. A régua e a cor da lâmpada contam
// o quanto do intervalo já foi consumido.
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
