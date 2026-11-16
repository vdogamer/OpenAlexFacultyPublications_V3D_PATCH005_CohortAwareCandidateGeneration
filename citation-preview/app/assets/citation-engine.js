/* CitationPilot rendering adapter. The bundled citeproc-js engine has its own license. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.CitationPilot = factory();
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const SCHEMA = 'AcademicOnline.CitationPilot/1';
  const MAX_ITEMS = 2000;
  const STYLES = Object.freeze([
    { key: 'ama', title: 'AMA · 11th edition', source: 'american-medical-association.csl' },
    { key: 'vancouver', title: 'Vancouver · NLM citation-sequence', source: 'nlm-citation-sequence.csl' },
    { key: 'apa', title: 'APA · 7th edition', source: 'apa.csl' }
  ]);
  const clean = value => typeof value === 'string' ? value.trim() : '';
  const clone = value => JSON.parse(JSON.stringify(value));
  const stable = value => {
    if (Array.isArray(value)) return '[' + value.map(stable).join(',') + ']';
    if (value && typeof value === 'object') return '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + stable(value[key])).join(',') + '}';
    return JSON.stringify(value);
  };
  function fail(message) { throw new Error(message); }
  function validateItem(item, index) {
    const name = 'Citation ' + (index + 1);
    if (!item || typeof item !== 'object' || Array.isArray(item)) fail(name + ' must be a CSL-JSON object.');
    if (!['string', 'number'].includes(typeof item.id) || !String(item.id).trim()) fail(name + ' has no identifier.');
    if (!clean(item.type) || !clean(item.title)) fail(name + ' is missing its publication type or title.');
    if (!Array.isArray(item.author) || item.author.length === 0) fail(name + ' has no ordered author list.');
    if (item.author.some(author => !author || typeof author !== 'object' || Array.isArray(author)
      || (!clean(author.literal) && !clean(author.family)))) fail(name + ' has an incomplete author name.');
    const dates = item.issued && item.issued['date-parts'];
    if (!Array.isArray(dates) || !Array.isArray(dates[0]) || !Number.isInteger(dates[0][0])
      || dates[0][0] < 1000 || dates[0][0] > 9999) fail(name + ' has no usable publication year.');
    const parts = dates[0];
    if (parts.length < 1 || parts.length > 3 || parts.some(value => !Number.isInteger(value))
      || (parts.length > 1 && (parts[1] < 1 || parts[1] > 12))
      || (parts.length > 2 && (parts[2] < 1 || parts[2] > 31))) fail(name + ' has an invalid publication date.');
    if (parts.length === 3) {
      const date = new Date(Date.UTC(parts[0], parts[1] - 1, parts[2]));
      if (date.getUTCFullYear() !== parts[0] || date.getUTCMonth() !== parts[1] - 1 || date.getUTCDate() !== parts[2])
        fail(name + ' has an invalid calendar date.');
    }
    if (item.type === 'article-journal' && !clean(item['container-title'])) fail(name + ' has no journal title.');
    return item;
  }
  function prepare(input) {
    let items, faculty = null, snapshot = null, summary = null, publications = [], notices = [];
    let kind = 'ExternalExample';
    if (Array.isArray(input)) {
      items = input;
      notices.push('Standalone CSL-JSON input: no local database association or authorship review is established.');
    } else {
      if (!input || input.schemaVersion !== SCHEMA) fail('Choose an AcademicOnline.CitationPilot/1 export, or an explicitly identified CSL-JSON example array.');
      if (!Array.isArray(input.publications) || !Array.isArray(input.cslItems)) fail('The export must contain publications and cslItems arrays.');
      if (input.publications.length > 20000) fail('This pilot accepts at most 20,000 source association rows.');
      if (input.readOnly !== true || input.sqlWrites !== 0 || input.authorshipReview !== 'NotPerformed') fail('The export is missing its read-only/authorship-review boundary.');
      publications = input.publications;
      faculty = input.faculty || null;
      snapshot = input.databaseSnapshot || null;
      summary = input.summary || null;
      kind = input.sourceKind === 'PublicExternalExample' ? 'PublicExternalExample' : 'DatabaseExport';
      const eligible = [];
      for (const row of publications) {
        if (!row || typeof row !== 'object') fail('A publication association row is malformed.');
        const allowed = row.previewEligible === true && row.status === 'Ready'
          && row.reviewDisposition === 'StoredAssociationNotIndependentlyReviewed';
        if (row.previewEligible === true && !allowed) fail('An ineligible or held publication was marked for preview.');
        if (allowed) {
          if (!row.csl || !clean(row.normalizedDoi)) fail('An eligible publication is missing its citation metadata or DOI.');
          if (clean(row.csl.DOI).toLowerCase() !== clean(row.normalizedDoi).toLowerCase()
            || row.csl.id !== 'doi:' + clean(row.normalizedDoi)) fail('A citation does not match its exported DOI association.');
          eligible.push(row.csl);
        }
      }
      if (stable(eligible) !== stable(input.cslItems)) fail('The cslItems list differs from the eligible publication rows. Held or incomplete records cannot be added to this preview.');
      items = input.cslItems;
      if (kind === 'DatabaseExport' && (!snapshot || snapshot.complete !== true)) fail('The export does not confirm a complete one-faculty database read.');
      if (kind === 'DatabaseExport' && (snapshot.databaseName !== 'AcademicOnline_Rebuild'
        || snapshot.facultyRows !== 1 || snapshot.doiRows !== publications.length
        || snapshot.endMarker !== 'CompleteOneFacultyRead')) fail('The database snapshot identity, row counts, or end marker do not match this one-faculty export.');
      if (summary && (summary.sourceAssociationRows !== publications.length
        || summary.readyCitationCount !== eligible.length)) fail('The export summary does not match the publication and citation counts.');
      if (kind === 'PublicExternalExample') notices.push('Public external example only. This file is not an export of the local SQL database.');
      if (Array.isArray(input.notices)) notices.push(...input.notices.filter(value => typeof value === 'string'));
    }
    if (items.length > MAX_ITEMS) fail('This pilot previews at most ' + MAX_ITEMS + ' citations at a time.');
    const ids = new Set();
    items.forEach((item, index) => {
      validateItem(item, index);
      if (ids.has(String(item.id))) fail('Duplicate citation identifier: ' + item.id);
      ids.add(String(item.id));
    });
    return Object.freeze({ items: clone(items), faculty, snapshot, summary, publications, notices, kind,
      heldCount: publications.filter(row => row.status === 'Held' || row.reviewDisposition === 'Held').length,
      excludedCount: publications.filter(row => row.previewEligible !== true).length });
  }
  function render(CSL, resources, prepared, styleKey) {
    const style = STYLES.find(value => value.key === styleKey);
    if (!style) fail('Unsupported citation style.');
    const original = stable(prepared.items);
    if (prepared.items.length === 0) return { key: styleKey, title: style.title, htmlEntries: [], textEntries: [], entryIds: [], count: 0 };
    function run(format) {
      const working = clone(prepared.items);
      const byId = new Map(working.map(item => [String(item.id), item]));
      const engine = new CSL.Engine({ retrieveLocale: () => resources.locale,
        retrieveItem: id => byId.get(String(id)) }, resources.styles[styleKey], 'en-US');
      engine.setOutputFormat(format);
      engine.updateItems(working.map(item => String(item.id)));
      const result = engine.makeBibliography();
      if (!result || result[0].bibliography_errors.length) fail('The citation processor could not render every item in ' + style.title + '.');
      const actual = result[0].entry_ids.flat().map(String);
      const expected = working.map(item => String(item.id));
      if (actual.length !== expected.length || actual.some(id => !byId.has(id)) || new Set(actual).size !== actual.length)
        fail('A style changed the exported citation set.');
      return { entries: result[1], ids: actual };
    }
    const html = run('html');
    const text = run('text');
    if (stable(html.ids) !== stable(text.ids)) fail('HTML and plain-text bibliography order differs.');
    if (stable(prepared.items) !== original) fail('The source citation data changed while formatting.');
    return { key: styleKey, title: style.title, htmlEntries: html.entries,
      textEntries: text.entries.map(value => value.trimEnd()), entryIds: html.ids, count: html.ids.length };
  }
  return Object.freeze({ SCHEMA, STYLES, MAX_ITEMS, prepare, render, stable });
});
