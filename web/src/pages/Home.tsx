// PLACEHOLDER (Task 1) — só valida shell, tokens e fontes com os 3 status.
// A Home real (fetch /assets + componente Sticker) chega na Task 3 e APAGA isto.
export default function Home() {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot dot--vencido" aria-label="pior status: vencido" />
      </header>

      <main>
        <p className="eyebrow">Mais urgente</p>
        <article className="sticker sticker--vencido sticker--hero">
          <h2 className="sticker__title">Corsa 2012 · troca de óleo</h2>
          <p className="sticker__display">1.500 km</p>
          <p className="sticker__due">vence em 1.500 km</p>
        </article>

        <p className="eyebrow">Vence em breve</p>
        <article className="sticker sticker--vence_em_breve">
          <h2 className="sticker__title">Casa · contrato da luz</h2>
          <p className="sticker__due">vence 21 mar 2027</p>
        </article>

        <p className="eyebrow">Em dia</p>
        <article className="sticker sticker--ok">
          <h2 className="sticker__title">Geladeira · revisão</h2>
          <p className="sticker__due">em dia até 14 set 2027</p>
        </article>
      </main>
    </div>
  );
}
