// PLACEHOLDER — destino das rotas que a Task 5 implementa (template, serviço,
// editar ativo). Mantém os links da Task 4 navegando em vez de 404 no router.
import { Link } from "react-router-dom";

export default function EmConstrucao({ titulo }: { titulo: string }) {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>
      <main>
        <h1>{titulo}</h1>
        <p className="loading">Em construção — chega na próxima etapa.</p>
        <p>
          <Link to="/">Voltar para a lista</Link>
        </p>
      </main>
    </div>
  );
}
