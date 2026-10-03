// A plaqueta: card chanfrado, título em condensed caps, linha do vencimento em
// mono e — quando o intervalo é conhecido — a régua do mostrador (assinatura v2).
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
      <h2 className="tag__title">{titulo}</h2>
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
