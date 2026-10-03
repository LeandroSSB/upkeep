// Ajustes da conta — e-mail (leitura), tópico ntfy para lembretes, backup
// (export/import) e sair. Toast reutilizado de components/Toast (mesmo do AssetDetail).
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { getMe, testNotification, updateNtfyTopic, type Me } from "../api/auth";
import { ApiError } from "../api/client";
import { exportUserData } from "../api/export";
import { importBackup, type Importados } from "../api/import";
import { AppShell } from "../components/AppShell";
import { Field } from "../components/Field";
import { Toast } from "../components/Toast";
import { fieldMessage, leftoverMessage } from "../lib/formErrors";
import { useSession } from "../state/session";

// espelho do validator do servidor (MeEndpoints) — inválido nem sai do browser
const TOPIC_PATTERN = /^[a-zA-Z0-9_-]{1,64}$/;
const TOPIC_INVALIDO = "Tópico inválido: use apenas letras, números, - e _ (máx 64)";

// "3 ativos, 1 manutenção, 0 serviços importados" — plural irregular
// (manutenção→manutenções) impede o naïve "+s" do singular
function contagemImportados(n: Importados): string {
  const parte = (qtd: number, um: string, varios: string) => `${qtd} ${qtd === 1 ? um : varios}`;
  return (
    `${parte(n.assets, "ativo", "ativos")}, ` +
    `${parte(n.templates, "manutenção", "manutenções")}, ` +
    `${parte(n.services, "serviço", "serviços")} importados`
  );
}

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "ready"; me: Me };

export default function Settings() {
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setPhase({ kind: "loading" });
    getMe()
      .then((me) => {
        if (!cancelled) setPhase({ kind: "ready", me });
      })
      .catch((err) => {
        if (!cancelled) {
          setPhase({
            kind: "error",
            message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  return (
    <AppShell>
      <h1>Ajustes</h1>

        {phase.kind === "loading" && <p className="loading">Carregando…</p>}

        {phase.kind === "error" && (
          <div className="load-error">
            <p className="form-error" role="alert">
              {phase.message}
            </p>
            <button type="button" className="btn" onClick={() => setAttempt((n) => n + 1)}>
              Tentar de novo
            </button>
          </div>
        )}

        {phase.kind === "ready" && <SettingsForm me={phase.me} />}
    </AppShell>
  );
}

function SettingsForm({ me: initialMe }: { me: Me }) {
  const { logout } = useSession();
  const navigate = useNavigate();
  // me muda só depois de um salvar bem-sucedido (resposta do PUT)
  const [me, setMe] = useState(initialMe);
  const [topic, setTopic] = useState(initialMe.ntfyTopic ?? "");
  const [topicError, setTopicError] = useState<string | undefined>();
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testError, setTestError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  // M7: import ADITIVO de um arquivo de export — arquivo lido e validado no
  // client só como preview (contagem); a validação de verdade é do backend
  const fileRef = useRef<HTMLInputElement>(null);
  const [pending, setPending] = useState<{ text: string; assets: number } | null>(null);
  const [importing, setImporting] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  function validate(value: string): string | undefined {
    const t = value.trim();
    if (t === "") return undefined; // vazio limpa — operação válida
    return TOPIC_PATTERN.test(t) ? undefined : TOPIC_INVALIDO;
  }

  async function onSubmit(ev: FormEvent) {
    ev.preventDefault();
    const error = validate(topic);
    setTopicError(error);
    setFormError(null);
    if (error) return;

    setSaving(true);
    try {
      const updated = await updateNtfyTopic(topic.trim());
      setMe(updated);
      setTopic(updated.ntfyTopic ?? "");
      setToast("Ajustes salvos");
    } catch (err) {
      if (err instanceof ApiError) {
        setTopicError(fieldMessage(err.errors, "ntfyTopic"));
        setFormError(leftoverMessage(err.errors, ["ntfyTopic"], err.title));
      } else {
        setFormError("Algo deu errado. Tente de novo.");
      }
    } finally {
      setSaving(false);
    }
  }

  // O teste dispara para o tópico SALVO no servidor — só faz sentido com o campo
  // preenchido (e válido), senão o usuário clicaria sem ter nada configurado.
  // Dirty-gate: campo editado e ainda não salvo desabilita o teste (ele testaria
  // o tópico ANTIGO, não o que está na tela) e mostra microcopy convidando a salvar.
  const topicTrimmed = topic.trim();
  const topicPreenchidoValido = topicTrimmed !== "" && TOPIC_PATTERN.test(topicTrimmed);
  const topicDirty = topicTrimmed !== (me.ntfyTopic ?? "");

  async function onTestNotification() {
    setTesting(true);
    setTestError(null);
    try {
      await testNotification();
      setToast("Notificação enviada — confira o celular");
    } catch (err) {
      setTestError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    } finally {
      setTesting(false);
    }
  }

  // M6: export dos dados próprios — blob → download via object URL + <a download>.
  // O nome do arquivo vem do content-disposition do backend (upkeep-export-yyyy-MM-dd.json).
  async function onExport() {
    setExporting(true);
    setExportError(null);
    try {
      const { blob, filename } = await exportUserData();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setToast("Download iniciado");
    } catch (err) {
      setExportError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    } finally {
      setExporting(false);
    }
  }

  // Leitura do arquivo: preview barato (JSON.parse só p/ contar assets) antes
  // de incomodar o servidor — arquivo sem assets[] é "Arquivo inválido" já aqui.
  async function onImportFile(ev: ChangeEvent<HTMLInputElement>) {
    setImportError(null);
    const file = ev.target.files?.[0];
    ev.target.value = ""; // re-selecionar o MESMO arquivo volta a disparar change
    if (!file) return;
    try {
      const text = await file.text();
      const parsed = JSON.parse(text) as { assets?: unknown };
      if (!Array.isArray(parsed.assets)) throw new Error();
      setPending({ text, assets: parsed.assets.length });
    } catch {
      setImportError("Arquivo inválido");
    }
  }

  async function onImport() {
    if (!pending) return;
    setImporting(true);
    setImportError(null);
    try {
      const result = await importBackup(pending.text);
      setPending(null);
      setToast(contagemImportados(result.importados));
    } catch (err) {
      setImportError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    } finally {
      setImporting(false);
    }
  }

  function onLogout() {
    logout();
    navigate("/entrar", { replace: true });
  }

  return (
    <>
      <form onSubmit={onSubmit} noValidate>
        <Field htmlFor="email" label="E-mail">
          <input id="email" type="email" value={me.email} readOnly />
        </Field>

        <Field htmlFor="ntfyTopic" label="Tópico ntfy (opcional)" error={topicError}>
          <input
            id="ntfyTopic"
            name="ntfyTopic"
            type="text"
            value={topic}
            maxLength={64}
            placeholder="ex.: meus-lembretes"
            onChange={(e) => {
              setTopic(e.target.value);
              setTopicError(undefined); // erro do servidor/validação não sobrevive à edição
              setTestError(null);
            }}
            aria-invalid={topicError !== undefined}
          />
        </Field>

        {me.ntfyTopic && (
          <div className="settings-topic">
            <a href={`https://ntfy.sh/${me.ntfyTopic}`} target="_blank" rel="noreferrer">
              abrir ntfy.sh/{me.ntfyTopic}
            </a>
            <p className="settings-topic__hint">
              Instale o app ntfy e assine o tópico para receber lembretes.
            </p>
          </div>
        )}

        {formError && (
          <p className="form-error" role="alert">
            {formError}
          </p>
        )}

        <div className="form-actions">
          <button type="submit" className="btn btn--primary" disabled={saving}>
            {saving ? "Salvando…" : "Salvar"}
          </button>
          <button
            type="button"
            className="btn"
            onClick={onTestNotification}
            disabled={!topicPreenchidoValido || topicDirty || testing}
          >
            {testing ? "Enviando…" : "Testar notificação"}
          </button>
        </div>

        {topicDirty && (
          <p className="settings-topic__hint">Salve o tópico antes de testar.</p>
        )}

        {testError && (
          <p className="form-error" role="alert">
            {testError}
          </p>
        )}
      </form>

      <div className="settings-data">
        <h2>Seus dados</h2>
        <p className="settings-topic__hint">
          Baixe um arquivo JSON com todos os seus ativos, planos de manutenção e serviços — ou
          importe um backup anterior para adicionar esses dados à conta atual.
        </p>
        <button type="button" className="btn" onClick={onExport} disabled={exporting}>
          {exporting ? "Preparando…" : "Exportar meus dados"}
        </button>
        {exportError && (
          <p className="form-error" role="alert">
            {exportError}
          </p>
        )}

        <input
          ref={fileRef}
          type="file"
          accept=".json,application/json"
          hidden
          onChange={onImportFile}
        />
        {pending ? (
          <div className="wo-confirm" role="alert">
            <p>
              Isso ADICIONA {pending.assets} {pending.assets === 1 ? "ativo" : "ativos"} do arquivo
              aos seus dados atuais. Não substitui nada. Continuar?
            </p>
            <div className="wo-confirm__actions">
              <button
                type="button"
                className="btn btn--primary"
                onClick={onImport}
                disabled={importing}
              >
                {importing ? "Importando…" : "Continuar"}
              </button>
              <button
                type="button"
                className="link"
                onClick={() => setPending(null)}
                disabled={importing}
              >
                Cancelar
              </button>
            </div>
          </div>
        ) : (
          <button
            type="button"
            className="btn"
            onClick={() => fileRef.current?.click()}
            disabled={importing}
          >
            Importar backup
          </button>
        )}
        {importError && (
          <p className="form-error" role="alert">
            {importError}
          </p>
        )}
      </div>

      <div className="settings-footer">
        <button type="button" className="link link--danger" onClick={onLogout}>
          Sair
        </button>
      </div>

      {toast && <Toast message={toast} onHide={() => setToast(null)} />}
    </>
  );
}
