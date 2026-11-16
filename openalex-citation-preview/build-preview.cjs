#!/usr/bin/env node
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = __dirname;
const args = process.argv.slice(2);
let output = path.join(root, 'preview-template.html');
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--output' && args[i + 1]) output = path.resolve(args[++i]);
  else throw new Error('Usage: node build-preview.cjs [--output PATH]');
}
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const manifest = JSON.parse(read('vendor-manifest.json'));
for (const file of manifest.files) {
  const bytes = fs.readFileSync(path.join(root, file.path));
  const hash = crypto.createHash('sha256').update(bytes).digest('hex');
  if (hash !== file.sha256) throw new Error('Vendored source hash mismatch: ' + file.path);
}
const resources = {
  locale: read('app/styles/locales-en-US.xml'),
  styles: {
    ama: read('app/styles/american-medical-association.csl'),
    vancouver: read('app/styles/nlm-citation-sequence.csl'),
    apa: read('app/styles/apa.csl')
  }
};
// Escaping angle brackets prevents style metadata from ever ending this script.
const resourcesJs = '/* Generated from pinned, unmodified CSL source files. */\nwindow.CITATION_RESOURCES = '
  + JSON.stringify(resources).replace(/</g, '\\u003c').replace(/>/g, '\\u003e').replace(/&/g, '\\u0026') + ';\n';
fs.writeFileSync(path.join(root, 'app/assets/resources.js'), resourcesJs);
let html = read('app/index.html');
html = html.replace("script-src 'self'; style-src 'self' 'unsafe-inline'", () => "script-src 'unsafe-inline'; style-src 'unsafe-inline'");
// Source text can contain JavaScript replacement metacharacters such as $'.
// Callback replacers preserve literal source bytes instead of expanding them.
html = html.replace('  <link rel="stylesheet" href="assets/preview.css">', () => '<style>\n' + read('app/assets/preview.css') + '\n</style>');
const scripts = ['vendor/citeproc.js', 'assets/resources.js', 'assets/citation-engine.js', 'assets/preview.js'];
for (const script of scripts) html = html.replace('  <script defer src="' + script + '"></script>\n', () => '');
const escapeHtml = value => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const notices = read('app/THIRD_PARTY_NOTICES.txt') + '\n\n'
  + read('app/vendor/LICENSE.citeproc') + '\n\n'
  + read('app/vendor/CPAL') + '\n\n'
  + read('app/vendor/AGPLv3');
html = html.replace('<a href="THIRD_PARTY_NOTICES.txt">Third-party notices</a>', () => '<a href="#third-party-notices">Third-party notices</a>');
html = html.replace('</footer>', () => '<details id="third-party-notices"><summary>Bundled source and license notices</summary><pre style="white-space:pre-wrap;font-size:.75rem">' + escapeHtml(notices) + '</pre></details>\n</footer>');
let inline = '\n<script type="application/json" id="citation-export">__CITATION_EXPORT_JSON__</script>\n';
for (const script of scripts) {
  const source = read('app/' + script);
  if (/<\/script/i.test(source)) throw new Error('An inline source contains a closing script tag: ' + script);
  inline += '<script>\n' + source + '\n</script>\n';
}
html = html.replace('</body>', () => inline + '</body>');
if (html.split('__CITATION_EXPORT_JSON__').length !== 2) throw new Error('The JSON placeholder must occur exactly once.');
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, html);
console.log('Built offline citation preview template: ' + output);

