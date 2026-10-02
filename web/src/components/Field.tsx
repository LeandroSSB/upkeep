// Esqueleto comum dos campos de formulário: label + controle + erro, em cima
// das classes .field/.field__error do app.css. O controle (children) fica a
// cargo de quem usa — precisa casar o htmlFor com o id do input.
import type { ReactNode } from "react";

export function Field({
  htmlFor,
  label,
  error,
  children,
}: {
  htmlFor: string;
  label: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div className="field">
      <label htmlFor={htmlFor}>{label}</label>
      {children}
      {error && <p className="field__error">{error}</p>}
    </div>
  );
}
