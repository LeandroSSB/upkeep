// Barreira de erros de render: sem ela, um throw em qualquer componente derruba
// a árvore inteira (tela branca). Classe é obrigatório — getDerivedStateFromError
// e componentDidCatch não têm equivalente em hooks.
import { Component, type ErrorInfo, type ReactNode } from "react";

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // Diagnóstico de dev: sem isso o erro original some atrás do fallback.
    console.error("Erro não tratado na UI:", error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return (
        <div className="shell" role="alert">
          <main className="empty-state">
            <p>Algo quebrou aqui.</p>
            <button className="btn btn--primary" type="button" onClick={() => window.location.reload()}>
              Recarregar
            </button>
          </main>
        </div>
      );
    }
    return this.props.children;
  }
}
