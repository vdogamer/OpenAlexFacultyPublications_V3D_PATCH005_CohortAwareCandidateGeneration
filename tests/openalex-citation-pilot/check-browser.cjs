'use strict';
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require('playwright');

async function main() {
  const root = path.resolve(process.env.CITATION_RENDERER_ROOT || 'openalex-citation-preview');
  const app = path.join(root, 'app');
  const fixture = await fs.readFile(path.join(root, 'tests/browser-fixture.json'));
  const input = JSON.parse(fixture.toString('utf8'));
  assert.equal(input.testFixture, true, 'Browser input must be explicitly synthetic test data.');
  const expectedIds = input.cslItems.map(item => String(item.id)).sort();
  assert.equal(expectedIds.length, 2, 'The browser fixture should exercise two eligible records.');
  const checks = [];
  const failures = [];
  const consoleErrors = [];
  const outsideRequests = [];
  async function waitForProof(target, label, predicate, argument) {
    try { await target.waitForFunction(predicate, argument, { timeout: 10000 }); }
    catch (error) {
      const details = await target.evaluate(() => ({
        loadError: document.querySelector('#load-error')?.textContent || '',
        loadStatus: document.querySelector('#load-status')?.textContent || '',
        cards: document.querySelectorAll('.style-card').length,
        engine: typeof CitationPilot, processor: typeof CSL, resources: typeof CITATION_RESOURCES
      })).catch(() => ({ loadError: '', loadStatus: 'The page could not be inspected.' }));
      console.error('BROWSER DIAGNOSTIC ' + JSON.stringify({ label, ...details, failures, consoleErrors }));
      const primary = details.loadError || failures[0] || consoleErrors[0] || details.loadStatus || error.message;
      throw new Error(label + ': ' + primary.replace(/\s+/g, ' ') + ' (cards=' + details.cards + ').');
    }
  }
  function observe(target) {
    target.on('pageerror', error => failures.push(error.message));
    target.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
  }
  const contentTypes = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.txt': 'text/plain' };
  const server = http.createServer(async (req, res) => {
    try {
      const parsed = new URL(req.url, 'http://localhost');
      const relative = parsed.pathname === '/' ? 'index.html' : decodeURIComponent(parsed.pathname.slice(1));
      const local = path.resolve(app, relative);
      if (!local.startsWith(app + path.sep)) { res.writeHead(403).end(); return; }
      const bytes = await fs.readFile(local);
      res.setHeader('Content-Type', (contentTypes[path.extname(local)] || 'application/octet-stream') + ';charset=utf-8');
      res.end(bytes);
    } catch (_) { res.writeHead(404).end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = 'http://127.0.0.1:' + server.address().port;
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'citation-offline-browser-'));
  let browser;
  try {
    browser = await chromium.launch({ headless: true, channel: process.env.UI_CHECK_BROWSER_CHANNEL || 'chrome' });
    const context = await browser.newContext({ acceptDownloads: true, viewport: { width: 1366, height: 1000 } });
    context.setDefaultTimeout(10000);
    context.setDefaultNavigationTimeout(15000);
    await context.route('**/*', route => {
      const url = new URL(route.request().url());
      if ((url.protocol === 'http:' || url.protocol === 'https:') && url.origin !== base) {
        outsideRequests.push(url.origin); route.abort();
      } else route.continue();
    });
    const page = await context.newPage();
    observe(page);
    await page.goto(base);
    await page.locator('#publication-file').setInputFiles({ name: 'synthetic-citation-fixture.json', mimeType: 'application/json', buffer: fixture });
    await waitForProof(page, 'Folder upload', () => document.querySelectorAll('.style-card').length === 3);
    assert.equal(await page.locator('#load-error').isVisible(), false);
    assert.match(await page.locator('#dataset-kind').textContent(), /NOT a local database export/);
    assert.match(await page.locator('#dataset-title').textContent(), /Synthetic Faculty/);
    checks.push('Public example remains distinct from a local database export');

    async function checkStyleSets(target) {
      const texts = [];
      for (const key of ['ama', 'vancouver', 'apa']) {
        const card = target.locator(`.style-card[data-style="${key}"]`);
        const ids = await card.locator('.bibliography > [data-csl-id]').evaluateAll(elements => elements.map(element => element.dataset.cslId).sort());
        assert.deepEqual(ids, expectedIds, key + ' must use exactly the same eligible record IDs.');
        const text = await card.locator('.bibliography').textContent();
        assert.match(text, /synthetic journal article/i);
        assert.doesNotMatch(text, /held-record|10\.5555\/incomplete/);
        assert.equal(await card.locator('.bibliography script,.bibliography img,.bibliography iframe,.bibliography a[href]').count(), 0);
        texts.push(text.replace(/\s+/g, ' ').trim());
      }
      assert.equal(new Set(texts).size, 3, 'The chosen styles must produce three independently formatted bibliographies.');
      assert.equal(await target.evaluate(() => window.__citationXss), undefined);
    }
    await checkStyleSets(page);
    checks.push('AMA/Vancouver/APA contain the same eligible records with distinct formatting');
    assert.match(await page.locator('#excluded-body').textContent(), /10\.5555\/held-record/);
    assert.match(await page.locator('#excluded-body').textContent(), /MetadataIncomplete/);
    checks.push('Held and incomplete records remain visible outside ordinary citations');
    checks.push('Source HTML and script text cannot create executable citation elements');

    const expectedHash = crypto.createHash('sha256').update(fixture).digest('hex');
    await waitForProof(page, 'Input hash', hash => document.querySelector('#dataset-hash').textContent.includes(hash), expectedHash);
    checks.push('Displayed input SHA-256 identifies the original uploaded bytes');

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.locator('.style-card[data-style="ama"] [data-action="download"]').click()
    ]);
    assert.equal(download.suggestedFilename(), 'faculty-citations-ama.txt');
    const stream = await download.createReadStream();
    const chunks = []; for await (const chunk of stream) chunks.push(Buffer.from(chunk));
    const downloaded = Buffer.concat(chunks).toString('utf8');
    assert.match(downloaded, /synthetic journal article/i);
    assert.doesNotMatch(downloaded, /held-record|10\.5555\/incomplete/);
    checks.push('Downloaded plain-text bibliography contains only the selected eligible citations');

    for (const width of [320, 390, 768, 1366]) {
      await page.setViewportSize({ width, height: 1000 });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'Horizontal overflow at ' + width + ' px.');
    }
    checks.push('Preview fits 320, 390, 768, and 1366 px viewports');

    const partial = await fs.readFile(path.join(root, 'tests/partial-fixture.json'));
    assert.equal(JSON.parse(partial.toString('utf8')).testFixture, true);
    await page.locator('#publication-file').setInputFiles({ name: 'synthetic-partial-export.json', mimeType: 'application/json', buffer: partial });
    await waitForProof(page, 'Partial source export', () => document.querySelectorAll('.style-card').length === 3);
    await checkStyleSets(page);
    assert.match(await page.locator('#dataset-kind').textContent(), /PARTIAL OpenAlexIngestion_Test export/);
    assert.match(await page.locator('#dataset-snapshot').textContent(), /source read: PARTIAL/);
    assert.doesNotMatch(await page.locator('#dataset-snapshot').textContent(), /source read: completed/);
    assert.match(await page.locator('#dataset-notices').textContent(), /AcceptedOpenAlex.*Failed/s);
    checks.push('A failed source remains prominently PARTIAL while successful eligible records stay usable');

    await page.locator('#publication-file').setInputFiles({ name: 'invalid.json', mimeType: 'application/json', buffer: Buffer.from('{ invalid JSON') });
    await waitForProof(page, 'Invalid replacement', () => !document.querySelector('#load-error').hidden);
    assert.equal(await page.locator('.style-card').count(), 0);
    assert.equal(await page.locator('#styles').isVisible(), false);
    checks.push('Invalid replacement input clears the previous bibliography');

    const template = await fs.readFile(path.join(root, 'preview-template.html'), 'utf8');
    assert.equal(template.split('__CITATION_EXPORT_JSON__').length, 2);
    const escaped = fixture.toString('utf8').replace(/</g, '\\u003c').replace(/>/g, '\\u003e').replace(/&/g, '\\u0026');
    const output = path.join(temporary, 'synthetic-preview.html');
    await fs.writeFile(output, template.replace('__CITATION_EXPORT_JSON__', () => escaped));
    const offline = await context.newPage();
    observe(offline);
    await offline.goto(pathToFileURL(output).href);
    await waitForProof(offline, 'Offline file', () => document.querySelectorAll('.style-card').length === 3);
    await checkStyleSets(offline);
    checks.push('Self-contained exported preview loads the same data from a local file');
    assert.deepEqual(outsideRequests, [], 'The preview must not attempt external metadata or image requests.');
    assert.deepEqual(failures, [], 'The preview must not generate browser JavaScript errors.');
    checks.push('Folder and self-contained previews make no external requests or JavaScript errors');
    console.log(`Citation browser checks passed: ${checks.length}.`);
    checks.forEach(check => console.log('PASS: ' + check));
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
    await fs.rm(temporary, { recursive: true, force: true });
  }
}

main().catch(error => { console.error('FAIL: ' + error.message); process.exitCode = 1; });
