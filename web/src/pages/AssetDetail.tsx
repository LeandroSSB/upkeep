// PLACEHOLDER (Task 3) — só mantém a rota /ativos/:id viva para os links da Home.
// A tela real (ordem de serviço, odômetro, stickers, logbook) chega na Task 4.
import { Link } from "react-router-dom";

export default function AssetDetail() {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>
      <main>
        <h1>Ativo</h1>
        <p className="loading">Detalhe do ativo chega na próxima etapa.</p>
        <p>
          <Link to="/">Voltar para a lista</Link>
        </p>
      </main>
    </div>
  );
}
