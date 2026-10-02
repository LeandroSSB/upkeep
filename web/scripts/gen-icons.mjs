// Gera os PNGs do PWA (192/512, "any" e maskable) a partir do favicon.svg —
// fonte de verdade do sticker (card branco, barra vermelha, "U").
// Rodar UMA vez (ou quando o favicon mudar) no megalan — sharp é devDep e
// NUNCA vai para o bundle:
//   docker run --rm -v ~/upkeep:/src -v upkeep-npm-cache:/root/.npm \
//     -w /src/web node:22-alpine sh -c "npm ci && node scripts/gen-icons.mjs"
//
// Por que não renderizar o favicon direto: o "U" lá é <text>, que depende de
// fonte do host — em node:22-alpine (sem fontconfig/fonts) o texto simplesmente
// não renderiza e o ícone sai sem letra. Trocamos o <text> por um <path> de
// traço equivalente (mesma métrica: centro x≈19, baseline ~22, cap ~9) para o
// resultado ser determinístico em qualquer ambiente.
import { mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const webRoot = dirname(dirname(fileURLToPath(import.meta.url))); // .../web
const favicon = await readFile(join(webRoot, "public", "favicon.svg"), "utf8");
const outDir = join(webRoot, "public", "icons");

// "U" condensado bold desenhado como traço (cores do favicon).
const U_PATH =
  '<path d="M15 9 V15.5 C15 19.3 16.7 21.3 19 21.3 C21.3 21.3 23 19.3 23 15.5 V9" ' +
  'fill="none" stroke="#1A211B" stroke-width="3.5"/>';

if (!/<text[\s\S]*?<\/text>/.test(favicon)) {
  throw new Error(
    "favicon.svg sem <text> — sticker mudou? Revise o gen-icons.mjs (cores/geometria).",
  );
}

// Variante "any": favicon com o U em path (cantos arredondados preservados).
const anySvg = favicon.replace(/<text[\s\S]*?<\/text>/, U_PATH);

// Variante "maskable": full-bleed (rx 0) e conteúdo a 84% centrado — o U fica
// dentro do safe zone (círculo central de 80%) e a barra vermelha sangra a
// borda esquerda, cortada pela máscara do launcher.
const SCALE = 0.84;
const TRANSLATE = 16 * (1 - SCALE); // 2.56 — mantém o centro do conteúdo em (16,16)
const svgOpen = anySvg.match(/<svg[^>]*>/)[0];
const inner = anySvg
  .slice(svgOpen.length, anySvg.lastIndexOf("</svg>"))
  .replace('rx="8"', 'rx="0"');
if (!inner.includes('rx="0"')) {
  throw new Error("favicon.svg sem rx=\"8\" no card — ajuste a variante maskable.");
}
const maskableSvg =
  `${svgOpen}<rect width="32" height="32" fill="#FFFFFF"/>` +
  `<g transform="translate(${TRANSLATE} ${TRANSLATE}) scale(${SCALE})">${inner}</g></svg>`;

// Injeta width/height no <svg> para o librsvg renderizar NATIVO no tamanho
// alvo (sem o resize pós-raster que deixaria tudo borrado).
function sized(svg, px) {
  return svg.replace("<svg ", `<svg width="${px}" height="${px}" `);
}

await mkdir(outDir, { recursive: true });
const targets = [
  [anySvg, 192, "icon-192.png"],
  [anySvg, 512, "icon-512.png"],
  [maskableSvg, 192, "icon-192-maskable.png"],
  [maskableSvg, 512, "icon-512-maskable.png"],
];
for (const [svg, px, name] of targets) {
  await sharp(Buffer.from(sized(svg, px))).png().toFile(join(outDir, name));
  console.log(`ok ${name} (${px}x${px})`);
}
