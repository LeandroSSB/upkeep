// Cabeçalho do ativo como ordem de serviço: meta (tipo), nome em display,
// notas e — para veículo — o odômetro em mono grande com "atualizar km" inline.
import { useState } from "react";
import type { Asset } from "../api/assets";
import { formatKm, typeLabel } from "../lib/format";
import { OdometerForm } from "./OdometerForm";

export function WorkOrderHeader({
  asset,
  onOdometerSaved,
}: {
  asset: Asset;
  onOdometerSaved: (km: number) => void;
}) {
  const [editingKm, setEditingKm] = useState(false);

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
        </div>
      )}
    </header>
  );
}
