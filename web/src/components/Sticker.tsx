// O verbete: entrada do caderno — título serifado, carimbo do status no
// cabeçalho, linha do vencimento em mono e a régua (linha de tinta) quando o
// intervalo é conhecido.
import { statusLabel } from "../lib/format";
import type { Status } from "../lib/types";

export interface StickerItem {
  titulo: string;
  subtitulo?: string;
  status: Status;
  destaque?: string;
  /** fração 0..1 consumida do intervalo (gaugeFraction); null/undefined = sem régua */
  gauge?: number | null;
}

export function Sticker({ item }: { item: StickerItem }) {
  const { titulo, subtitulo, status, destaque, gauge } = item;
  const pct = gauge != null ? `${Math.round(gauge * 100)}%` : null;
  return (
    <article className={`tag tag--${status}`}>
      <div className="tag__head">
        <h2 className="tag__title">{titulo}</h2>
        <span className={`stamp stamp--${status}`}>{statusLabel(status)}</span>
      </div>
      {subtitulo && <p className="tag__sub">{subtitulo}</p>}
      {destaque && <p className="tag__due">{destaque}</p>}
      {pct && (
        <div className="gauge" aria-hidden="true">
          <div className="gauge__fill" style={{ width: pct }} />
        </div>
      )}
    </article>
  );
}
