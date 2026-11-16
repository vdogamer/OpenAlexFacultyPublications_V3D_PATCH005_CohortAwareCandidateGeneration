'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '..');
const Engine = require(path.join(root, 'app/assets/citation-engine.js'));
// Execute only the pinned upstream processor source. Export records are data,
// never executable source. A wrapper lets the unchanged browser bundle run in Node.
const CSL = vm.runInThisContext('(function(){\n' + fs.readFileSync(path.join(root, 'app/vendor/citeproc.js'), 'utf8') + '\nreturn CSL;})()', { filename: 'citeproc.js' });
const resources = {
  locale: fs.readFileSync(path.join(root, 'app/styles/locales-en-US.xml'), 'utf8'),
  styles: Object.fromEntries(Engine.STYLES.map(style => [style.key, fs.readFileSync(path.join(root, 'app/styles', style.source), 'utf8')]))
};
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'browser-fixture.json'), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));
let checks = 0;
function check(name, action) { action(); checks++; console.log('PASS ' + name); }
check('one immutable data set renders all three genuine CSL styles', () => {
  const before = JSON.stringify(fixture);
  const data = Engine.prepare(fixture);
  const ids = data.items.map(item => item.id).sort();
  const outputs = Engine.STYLES.map(style => Engine.render(CSL, resources, data, style.key));
  for (const result of outputs) {
    assert.equal(result.count, 2);
    assert.deepEqual([...result.entryIds].sort(), ids);
    assert.equal(result.htmlEntries.length, result.textEntries.length);
    assert(!result.textEntries.join('').includes('held-record'));
    assert(!result.textEntries.join('').includes('10.5555/incomplete'));
  }
  assert.equal(JSON.stringify(fixture), before);
  assert.equal(data.items[0].author.length, 13);
  assert(outputs.find(value => value.key === 'apa').textEntries.join('').includes('AuthorM'));
  assert(outputs.find(value => value.key === 'ama').textEntries.join('').includes('et al.'));
  assert.notEqual(outputs[0].textEntries.join(''), outputs[2].textEntries.join(''));
  assert.equal(data.excludedCount, 2);
  assert.equal(data.heldCount, 1);
});
check('public example arrays never assert a local database export', () => {
  const data = Engine.prepare([fixture.cslItems[0]]);
  assert.equal(data.kind, 'ExternalExample');
  assert.equal(data.snapshot, null);
  assert(data.notices.some(notice => notice.includes('no local database association')));
});
check('held records cannot be marked eligible', () => {
  const bad = clone(fixture); bad.publications[2].previewEligible = true;
  assert.throws(() => Engine.prepare(bad), /ineligible or held/);
});
check('held metadata cannot be smuggled into the citation list', () => {
  const bad = clone(fixture);
  bad.cslItems.push({ ...bad.cslItems[0], id: 'doi:10.5555/held-record', DOI: '10.5555/held-record' });
  assert.throws(() => Engine.prepare(bad), /differs from the eligible/);
});
check('the eligible wrapper and CSL item cannot disagree', () => {
  const bad = clone(fixture); bad.cslItems[0].title = 'Changed only in the citation list';
  assert.throws(() => Engine.prepare(bad), /differs from the eligible/);
});
check('DOI association mismatches are rejected', () => {
  const bad = clone(fixture); bad.publications[0].normalizedDoi = '10.5555/other';
  assert.throws(() => Engine.prepare(bad), /does not match/);
});
check('incomplete database snapshots cannot masquerade as complete reads', () => {
  const bad = clone(fixture); delete bad.sourceKind;
  assert.throws(() => Engine.prepare(bad), /complete one-faculty/);
});
check('truncated association rows cannot retain a complete snapshot claim', () => {
  const bad = clone(fixture); delete bad.sourceKind;
  bad.databaseSnapshot = { databaseName:'AcademicOnline_Rebuild', complete:true, facultyRows:1,
    doiRows:bad.publications.length + 1, endMarker:'CompleteOneFacultyRead' };
  assert.throws(() => Engine.prepare(bad), /row counts/);
});
check('database, end marker and summary must agree with the exported row set', () => {
  const good = clone(fixture); delete good.sourceKind;
  good.databaseSnapshot = { databaseName:'AcademicOnline_Rebuild', complete:true, facultyRows:1,
    doiRows:good.publications.length, endMarker:'CompleteOneFacultyRead' };
  assert.equal(Engine.prepare(good).kind, 'DatabaseExport');
  for (const change of [value => value.databaseSnapshot.databaseName='OtherDatabase',
    value => value.databaseSnapshot.facultyRows=2, value => value.databaseSnapshot.endMarker='Missing',
    value => value.summary.readyCitationCount=999, value => value.readOnly=false]) {
    const bad = clone(good); change(bad); assert.throws(() => Engine.prepare(bad));
  }
});
check('metadata must have authors, title, type and date rather than invented placeholders', () => {
  for (const field of ['author', 'title', 'type', 'issued']) {
    const item = clone(fixture.cslItems[0]); delete item[field];
    assert.throws(() => Engine.prepare([item]));
  }
});
check('duplicate item IDs are rejected rather than silently merged', () => {
  assert.throws(() => Engine.prepare([fixture.cslItems[0], fixture.cslItems[0]]), /Duplicate citation/);
});
check('invalid calendar dates are rejected instead of formatted', () => {
  for (const parts of [[2025,13,1],[2025,0],[2025,2,29],[2025,4,31],[2025,1,1,4]]) {
    const item=clone(fixture.cslItems[0]); item.issued={'date-parts':[parts]};
    assert.throws(() => Engine.prepare([item]), /invalid/);
  }
});
check('malformed optional contributors are rejected before entering the CSL processor', () => {
  for (const role of ['editor', 'translator', 'container-author', 'collection-editor']) {
    const item = clone(fixture.cslItems[0]); item.type = 'chapter';
    item[role] = [{ family:{bad:true}, given:'Editor' }];
    assert.throws(() => Engine.prepare([item]), new RegExp('invalid ' + role + ' name field'));
    item[role] = 'Unstructured Display Name';
    assert.throws(() => Engine.prepare([item]), new RegExp('invalid ' + role + ' contributor list'));
  }
  const author = clone(fixture.cslItems[0]); author.author[0].given={bad:true};
  assert.throws(() => Engine.prepare([author]), /invalid author name field/);
});
check('source text with HTML-like payloads is escaped by the CSL HTML output', () => {
  const result = Engine.render(CSL, resources, Engine.prepare(fixture), 'apa');
  const html = result.htmlEntries.join('');
  assert(!html.includes('<script>'));
  assert(!html.includes('<img src='));
  assert(html.includes('window.__citationXss=true'));
});
check('empty eligible sets render explicit empty previews without a citation', () => {
  const input = clone(fixture);
  input.publications = input.publications.filter(row => !row.previewEligible); input.cslItems = [];
  input.summary = { sourceAssociationRows:input.publications.length, readyCitationCount:0 };
  const result = Engine.render(CSL, resources, Engine.prepare(input), 'ama');
  assert.equal(result.count, 0); assert.deepEqual(result.entryIds, []);
});
check('the same known citation produces distinct standard punctuation', () => {
  const item = { id:'doi:10.5555/example', type:'article-journal', title:'Synthetic article',
    author:[{family:'Example',given:'Alex B'}], 'container-title':'Synthetic Journal',
    'container-title-short':'Synth J', issued:{'date-parts':[[2025,11,10]]}, volume:'12', page:'123', DOI:'10.5555/example' };
  const data = Engine.prepare([item]);
  const ama = Engine.render(CSL, resources, data, 'ama').textEntries[0];
  const van = Engine.render(CSL, resources, data, 'vancouver').textEntries[0];
  const apa = Engine.render(CSL, resources, data, 'apa').textEntries[0];
  assert(ama.includes('Example AB.')); assert(ama.includes('2025;12:123.'));
  assert(van.includes('2025 Nov 10;12:123.'));
  assert(apa.includes('Example, A. B. (2025).')); assert(apa.includes('https://doi.org/10.5555/example'));
});
console.log('Citation rendering checks passed: ' + checks + '.');
