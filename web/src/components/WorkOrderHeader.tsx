// Cabeçalho do ativo como ordem de serviço: meta (tipo), nome em display,
// notas e — para veículo — o odômetro em mono grande com "atualizar km" inline
// e o custo por km em mono pequeno logo abaixo.
import { useEffect, useState } from "react";
import type { Asset } from "../api/assets";
import { getCostReport } from "../api/reports";
import { formatBRL, formatKm, typeLabel } from "../lib/format";
import { OdometerForm } from "./OdometerForm";

export function WorkOrderHeader({
  asset,
  onOdometerSaved,
}: {
  asset: Asset;
  onOdometerSaved: (km: number) => void;
}) {
  const [editingKm, setEditingKm] = useState(false);
  const [custoPorKm, setCustoPorKm] = useState<number | null>(null);

  // Custo/km do próprio endpoint de relatórios (sem filtros = histórico total do
  // veículo). Linha é bônus: falha fica silenciosa. Refetcha quando o km muda —
  // kmRodados cresce junto com o odômetro atual.
  useEffect(() => {
    if (asset.tipo !== "veiculo") return;
    let cancelled = false;
    getCostReport({ assetId: asset.id })
      .then((r) => {
        if (!cancelled) setCustoPorKm(r.custoPorKm?.porKm ?? null);
      })
      .catch(() => {}); // sem custo/km hoje — a ordem de serviço segue normal
    return () => {
      cancelled = true;
    };
  }, [asset.id, asset.tipo, asset.odometroAtual]);

  return (
    <header className="workorder">
      <p className="workorder__meta">{typeLabel(asset.tipo)}</p>
      <h1 className="workorder__nome">{asset.nome}</h1>
      {asset.notas && <p className="workorder__notas">{asset.notas}</p>}

      {asset.tipo === "veiculo" && (
        <div className="workorder__km">
          {editingKm ? (
            <OdometerForm
              assetId={asset.id}
              current={asset.odometroAtual}
              onSaved={(km) => {
                setEditingKm(false);
                onOdometerSaved(km);
              }}
              onCancel={() => setEditingKm(false)}
            />
          ) : (
            <>
              {asset.odometroAtual != null ? (
                <span className="workorder__odo">{formatKm(asset.odometroAtual)}</span>
              ) : (
                <span className="workorder__odo workorder__odo--empty">odômetro não registrado</span>
              )}
              <button type="button" className="link workorder__km-toggle" onClick={() => setEditingKm(true)}>
                atualizar km
              </button>
            </>
          )}
          {custoPorKm != null && (
            <span className="workorder__custo-km">{formatBRL(custoPorKm)}/km</span>
          )}
        </div>
      )}
    </header>
  );
}
