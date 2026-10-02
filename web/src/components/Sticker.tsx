// A etiqueta: card branco com a barra do status à esquerda, título display e a
// linha do vencimento. O herói é a versão grande/rotacionada — o mais urgente da Home.
import type { Status } from "../lib/types";

export interface StickerItem {
  titulo: string;
  subtitulo?: string;
  status: Status;
  destaque?: string;
}

export function Sticker({ item, hero = false }: { item: StickerItem; hero?: boolean }) {
  const { titulo, subtitulo, status, destaque } = item;
  return (
    <article className={`sticker sticker--${status}${hero ? " sticker--hero" : ""}`}>
      <h2 className="sticker__title">{titulo}</h2>
      {subtitulo && <p className="sticker__sub">{subtitulo}</p>}
      {destaque && <p className={hero ? "display-xl sticker__display" : "sticker__due"}>{destaque}</p>}
    </article>
  );
}
