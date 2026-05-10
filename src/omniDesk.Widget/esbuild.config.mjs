import { build, context } from 'esbuild';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes('--watch');

const outDir = resolve(__dirname, 'dist');
mkdirSync(outDir, { recursive: true });

const apiBaseDefault = process.env.WIDGET_API_BASE_URL ?? 'https://api.omnicare.ia.br';
const cdnBaseDefault = process.env.WIDGET_CDN_BASE_URL ?? 'https://cdn.omnicare.ia.br/widget/v1';

const sharedOptions = {
  bundle: true,
  format: 'esm',
  target: ['es2022'],
  platform: 'browser',
  minify: !watch,
  sourcemap: 'external',
  legalComments: 'none',
  define: {
    __WIDGET_VERSION__: JSON.stringify('1.0.0'),
    __DEFAULT_API_BASE_URL__: JSON.stringify(apiBaseDefault),
  },
};

async function buildWidget() {
  const result = await build({
    ...sharedOptions,
    entryPoints: { widget: resolve(__dirname, 'src/widget.ts') },
    outdir: outDir,
    write: false,
    metafile: true,
    entryNames: '[name].[hash]',
  });

  const out = result.outputFiles.find((f) => f.path.endsWith('.js'));
  const map = result.outputFiles.find((f) => f.path.endsWith('.js.map'));
  if (!out || !map) throw new Error('Build did not emit expected outputs.');

  const hash = createHash('sha256').update(out.contents).digest('hex').slice(0, 12);
  const fileName = `widget.${hash}.js`;
  const filePath = resolve(outDir, fileName);
  writeFileSync(filePath, out.contents);
  writeFileSync(`${filePath}.map`, map.contents);

  const loaderTemplate = readFileSync(resolve(__dirname, 'public/loader.js'), 'utf-8');
  const loaderProcessed = loaderTemplate
    .replaceAll('__CDN_BASE_URL__', cdnBaseDefault)
    .replaceAll('__WIDGET_BUNDLE__', fileName);
  writeFileSync(resolve(outDir, 'loader.js'), loaderProcessed);

  const manifest = {
    bundle: fileName,
    map: `${fileName}.map`,
    cdnBase: cdnBaseDefault,
    apiBase: apiBaseDefault,
    builtAt: new Date().toISOString(),
  };
  writeFileSync(resolve(outDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

  console.log(`✓ widget bundle → dist/${fileName}`);
}

if (watch) {
  const ctx = await context({
    ...sharedOptions,
    entryPoints: { widget: resolve(__dirname, 'src/widget.ts') },
    outdir: outDir,
  });
  await ctx.watch();
  console.log('watching…');
} else {
  await buildWidget();
}
