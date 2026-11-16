'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

async function main() {
  const root = path.resolve(process.env.CITATION_RENDERER_ROOT || 'citation-preview');
  const manifest = JSON.parse(await fs.readFile(path.join(root, 'vendor-manifest.json'), 'utf8'));
  if (manifest.schemaVersion !== 'CitationPilot.VendorManifest/1' || !Array.isArray(manifest.files))
    throw new Error('Unsupported public renderer vendor manifest.');
  if (manifest.files.length < 1 || manifest.files.length > 30)
    throw new Error('Unexpected public renderer vendor file count.');
  for (const file of manifest.files) {
    const url = new URL(file.url);
    if (url.protocol !== 'https:' || url.hostname !== 'raw.githubusercontent.com'
      || url.username || url.password || url.search || url.hash
      || !/^[0-9a-f]{40}$/.test(file.revision)
      || !/^[0-9a-f]{40}$/.test(file.gitBlobSha)
      || !/^[0-9a-f]{64}$/.test(file.sha256)
      || !['Juris-M/citeproc-js', 'citation-style-language/styles', 'citation-style-language/locales'].includes(file.repository)
      || !url.pathname.startsWith('/' + file.repository + '/' + file.revision + '/'))
      throw new Error('A vendor source is not an immutable approved public GitHub URL.');
    if (typeof file.path !== 'string' || path.posix.isAbsolute(file.path) || path.win32.isAbsolute(file.path))
      throw new Error('A vendor destination must be relative.');
    const destination = path.resolve(root, file.path);
    if (!destination.startsWith(root + path.sep)) throw new Error('A vendor path escapes the renderer directory.');
    const response = await fetch(url, {
      redirect: 'error', signal: AbortSignal.timeout(45000),
      headers: { 'User-Agent': 'AcademicOnline-CitationPilot-Validation/1' }
    });
    if (!response.ok) throw new Error(`Public vendor fetch failed: ${file.path} HTTP ${response.status}.`);
    const body = Buffer.from(await response.arrayBuffer());
    if (body.length > 8_000_000) throw new Error('A public vendor source exceeded the file-size limit.');
    const sha256 = crypto.createHash('sha256').update(body).digest('hex');
    const gitBlobSha = crypto.createHash('sha1').update(`blob ${body.length}\0`).update(body).digest('hex');
    if (sha256 !== file.sha256 || gitBlobSha !== file.gitBlobSha)
      throw new Error(`Public vendor integrity mismatch: ${file.path}.`);
    await fs.mkdir(path.dirname(destination), { recursive: true });
    await fs.writeFile(destination, body);
    console.log(`Verified pinned public vendor: ${file.path} (${body.length} bytes)`);
  }
  console.log(`Public vendor bootstrap passed: ${manifest.files.length} immutable sources checked.`);
}

main().catch(error => { console.error('FAIL: ' + error.message); process.exitCode = 1; });
