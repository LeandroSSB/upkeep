// Gera os PNGs do PWA (192/512, "any" e maskable) a partir do favicon.svg v2
// (plaqueta chanfrada — régua + "U" como path, sem <text>: determinístico em
// qualquer host, mesmo sem fontes). Rodar no megalan (sharp é devDep e NUNCA
// vai para o bundle):
//   docker run --rm -v ~/upkeep/web:/src -w /src -v upkeep-npm-cache:/root/.npm \
//     node:22-alpine sh -c "npm ci && node scripts/gen-icons.mjs"
import { mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const webRoot = dirname(dirname(fileURLToPath(import.meta.url))); // .../web
const favicon = await readFile(join(webRoot, "public", "favicon.svg"), "utf8");
const outDir = join(webRoot, "public", "icons");

// sanidade: o favicon v2 tem o traço do "U" e a régua vermelha — sem isso o
// ícone sai errado (guard contra um favicon revertido/alterado por engano)
for (const needle of ['stroke="#16232A"', 'fill="#B3372A"']) {
  if (!favicon.includes(needle)) {
    throw new Error(`favicon.svg sem ${needle} — plaqueta mudou? Revise o gen-icons.mjs.`);
  }
}

const svgOpen = favicon.match(/<svg[^>]*>/)[0];
const inner = favicon.slice(svgOpen.length, favicon.lastIndexOf("</svg>"));

// maskable: fundo porcelana full-bleed + conteúdo a 84% (safe zone do launcher)
const SCALE = 0.84;
const TRANSLATE = 16 * (1 - SCALE);
const maskableSvg =
  `${svgOpen}<rect width="32" height="32" fill="#E9EEEF"/>` +
  `<g transform="translate(${TRANSLATE} ${TRANSLATE}) scale(${SCALE})">${inner}</g></svg>`;

// width/height nativos no <svg>: librsvg rasteriza no tamanho alvo sem blur.
function sized(svg, px) {
  return svg.replace("<svg ", `<svg width="${px}" height="${px}" `);
}

await mkdir(outDir, { recursive: true });
const targets = [
  [favicon, 192, "icon-192.png"],
  [favicon, 512, "icon-512.png"],
  [maskableSvg, 192, "icon-192-maskable.png"],
  [maskableSvg, 512, "icon-512-maskable.png"],
];
for (const [svg, px, name] of targets) {
  await sharp(Buffer.from(sized(svg, px))).png().toFile(join(outDir, name));
  console.log(`ok ${name} (${px}x${px})`);
}
