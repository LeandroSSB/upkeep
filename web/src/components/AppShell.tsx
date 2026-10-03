// Shell comum a todas as telas: coluna de bolso + header (wordmark + lamp).
// `lamp` sobrescreve o neutro (a Home passa o pior status global da lista);
// `after` entra depois do <main> (FAB da Home, toast do AssetDetail).
import type { ReactNode } from "react";

export function AppShell({
  lamp,
  children,
  after,
}: {
  lamp?: ReactNode;
  children?: ReactNode;
  after?: ReactNode;
}) {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        {lamp ?? <span className="lamp" aria-hidden="true" />}
      </header>
      <main>{children}</main>
      {after}
    </div>
  );
}
