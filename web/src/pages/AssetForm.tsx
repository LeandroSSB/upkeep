// PLACEHOLDER (Task 3) — destino do FAB e do empty state da Home. O formulário
// real (novo/editar ativo) chega na Task 5.
import { Link } from "react-router-dom";

export default function AssetForm() {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>
      <main>
        <h1>Novo ativo</h1>
        <p className="loading">O cadastro chega na próxima etapa.</p>
        <p>
          <Link to="/">Voltar para a lista</Link>
        </p>
      </main>
    </div>
  );
}
