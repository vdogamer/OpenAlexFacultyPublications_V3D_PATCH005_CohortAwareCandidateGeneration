(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const fileInput = $('publication-file');
  let generation = 0;
  function text(value) { return typeof value === 'string' ? value : ''; }
  function clear() {
    for (const id of ['dataset', 'styles', 'excluded', 'load-error']) $(id).hidden = true;
    $('styles').replaceChildren();
    $('excluded-body').replaceChildren();
    $('dataset-notices').replaceChildren();
    $('load-status').textContent = 'Reading publication data…';
  }
  function showError(error) {
    clear();
    $('load-status').textContent = 'The selected file was not loaded.';
    $('load-error').textContent = error && error.message ? error.message : 'The JSON could not be read.';
    $('load-error').hidden = false;
  }
  // CSL supports small spans of rich text. Copy only display tags and a short
  // formatting allowlist; never adopt source elements, URLs, event handlers, or CSS.
  function safeCslHtml(html) {
    const parsed = document.createElement('template');
    parsed.innerHTML = html;
    const output = document.createDocumentFragment();
    const allowed = new Set(['DIV', 'SPAN', 'I', 'B', 'EM', 'STRONG', 'SUP', 'SUB']);
    const classes = new Set(['csl-entry', 'csl-left-margin', 'csl-right-inline', 'csl-indent', 'csl-block']);
    function copy(source, target) {
      for (const child of source.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) target.append(document.createTextNode(child.textContent));
        else if (child.nodeType === Node.ELEMENT_NODE) {
          if (['SCRIPT', 'STYLE', 'IFRAME', 'OBJECT', 'EMBED', 'LINK', 'META', 'IMG'].includes(child.tagName)) continue;
          const element = allowed.has(child.tagName) ? document.createElement(child.tagName.toLowerCase()) : null;
          if (!element) { copy(child, target); continue; }
          for (const name of child.classList) if (classes.has(name)) element.classList.add(name);
          for (const [property, values] of Object.entries({
            'font-variant': ['small-caps', 'normal'], 'font-style': ['italic', 'normal'],
            'font-weight': ['bold', 'normal'], 'text-decoration': ['underline', 'none'],
            'vertical-align': ['super', 'sub', 'baseline']
          })) {
            const value = child.style.getPropertyValue(property).trim().toLowerCase();
            if (values.includes(value)) element.style.setProperty(property, value);
          }
          copy(child, element); target.append(element);
        }
      }
    }
    copy(parsed.content, output);
    return output;
  }
  function download(value, filename) {
    const url = URL.createObjectURL(new Blob([value], { type: 'text/plain;charset=utf-8' }));
    const a = document.createElement('a'); a.href = url; a.download = filename;
    document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 5000);
  }
  function appendStyle(result) {
    const card = document.createElement('section'); card.className = 'style-card'; card.dataset.style = result.key;
    const heading = document.createElement('h2'); heading.textContent = result.title; card.append(heading);
    const count = document.createElement('p'); count.className = 'citation-count';
    count.textContent = result.count + ' citation(s) · same exported records'; card.append(count);
    const actions = document.createElement('div'); actions.className = 'actions';
    const copy = document.createElement('button'); copy.type = 'button'; copy.textContent = 'Copy text';
    copy.dataset.action = 'copy'; copy.disabled = result.count === 0;
    const plain = result.textEntries.join('\n\n') + (result.count ? '\n' : '');
    copy.addEventListener('click', async () => {
      try {
        if (!navigator.clipboard || !navigator.clipboard.writeText) throw new Error('Clipboard unavailable');
        await navigator.clipboard.writeText(plain); $('load-status').textContent = result.title + ' copied as plain text.';
      } catch (_) { $('load-status').textContent = 'Clipboard access is unavailable. Use Download text for this citation style.'; }
    });
    const save = document.createElement('button'); save.type = 'button'; save.className = 'secondary';
    save.textContent = 'Download text'; save.dataset.action = 'download'; save.disabled = result.count === 0;
    save.addEventListener('click', () => download(plain, 'faculty-citations-' + result.key + '.txt'));
    actions.append(copy, save); card.append(actions);
    const bibliography = document.createElement('div'); bibliography.className = 'bibliography';
    result.htmlEntries.forEach((entry, index) => {
      const wrapper = document.createElement('div'); wrapper.dataset.cslId = result.entryIds[index];
      wrapper.append(safeCslHtml(entry)); bibliography.append(wrapper);
    });
    if (!result.count) { const p = document.createElement('p'); p.textContent = 'No metadata-complete records are eligible for this preview.'; bibliography.append(p); }
    card.append(bibliography); $('styles').append(card);
  }
  async function show(raw, filename, bytes) {
    const mine = ++generation; clear();
    try {
      const input = JSON.parse(raw);
      const prepared = CitationPilot.prepare(input);
      const results = CitationPilot.STYLES.map(style => CitationPilot.render(CSL, CITATION_RESOURCES, prepared, style.key));
      if (mine !== generation) return;
      const facultyName = prepared.faculty && (prepared.faculty.facultyName || prepared.faculty.name || prepared.faculty.userId);
      $('dataset-title').textContent = facultyName ? 'Publication data for ' + facultyName : 'Publication citation data';
      $('dataset-kind').textContent = prepared.kind === 'DatabaseExport'
        ? (prepared.isPartial
          ? 'PARTIAL OpenAlexIngestion_Test export: one or more sources failed. These previews cover only returned, eligible records. Missing source records remain unknown.'
          : 'Read-only OpenAlexIngestion_Test export. Available source checks completed. Existing source review statuses are preserved; this pilot performs no new authorship review.')
        : 'Public external example · NOT a local database export. This preview does not establish a stored faculty–publication association.';
      $('dataset-counts').textContent = prepared.publications.length + ' source association row(s) returned; ' + prepared.items.length + ' eligible citation(s); ' + prepared.excludedCount
        + ' source row(s) outside the preview, including ' + prepared.heldCount + ' held row(s).';
      const snapshot = prepared.snapshot;
      $('dataset-snapshot').textContent = snapshot
        ? 'Database: ' + text(snapshot.databaseName) + '. Available source read: ' + (prepared.isPartial ? 'PARTIAL' : 'completed')
          + '. This describes returned database records, not completeness of the faculty member’s career bibliography.'
        : 'No current local database snapshot is asserted by this example.';
      $('dataset-file').textContent = filename;
      $('dataset-hash').textContent = 'Calculating input SHA-256…';
      prepared.notices.forEach(notice => { const li = document.createElement('li'); li.textContent = notice; $('dataset-notices').append(li); });
      prepared.sourceChecks.forEach(check => {
        const li = document.createElement('li');
        li.textContent = check.source + ' — ' + check.status + (typeof check.error === 'string' && check.error ? ': ' + check.error : '');
        $('dataset-notices').append(li);
      });
      results.forEach(appendStyle);
      const excluded = prepared.publications.filter(row => row.previewEligible !== true);
      for (const row of excluded.slice(0, 2000)) {
        const tr = document.createElement('tr');
        for (const value of [row.rawDoi || '(DOI not supplied)', row.status, [...(Array.isArray(row.reviewReasons) ? row.reviewReasons : []), ...(Array.isArray(row.errors) ? row.errors : [])].join('; ')]) {
          const td = document.createElement('td'); td.textContent = typeof value === 'string' ? value : ''; tr.append(td);
        }
        $('excluded-body').append(tr);
      }
      if (excluded.length > 2000) { const tr = document.createElement('tr'); const td = document.createElement('td'); td.colSpan = 3; td.textContent = 'The first 2,000 excluded rows are shown here. The JSON retains every exported association row.'; tr.append(td); $('excluded-body').append(tr); }
      $('excluded').hidden = excluded.length === 0;
      $('dataset').hidden = false; $('styles').hidden = false;
      $('load-status').textContent = 'Loaded ' + prepared.items.length + ' eligible citation(s). All three previews use this one input.';
      if (window.crypto && window.crypto.subtle) {
        const digest = await window.crypto.subtle.digest('SHA-256', bytes || new TextEncoder().encode(raw));
        if (mine === generation) $('dataset-hash').textContent = 'Input SHA-256: ' + Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('');
      } else $('dataset-hash').textContent = 'SHA-256 display is unavailable in this browser; the preview still uses one in-memory data set.';
    } catch (error) { if (mine === generation) showError(error); }
  }
  fileInput.addEventListener('change', async () => {
    const file = fileInput.files && fileInput.files[0];
    const ticket = ++generation; clear();
    if (!file) { $('load-status').textContent = 'No file loaded.'; return; }
    if (file.size > 25 * 1024 * 1024) { showError(new Error('Choose a JSON file no larger than 25 MB.')); return; }
    try {
      const bytes = await file.arrayBuffer();
      if (ticket !== generation) return;
      await show(new TextDecoder('utf-8', { fatal: true }).decode(bytes).replace(/^\uFEFF/, ''), file.name, bytes);
    } catch (error) { showError(error); }
  });
  const embedded = $('citation-export');
  if (embedded && /^[\s\uFEFF]*[\[{]/.test(embedded.textContent))
    show(embedded.textContent.replace(/^\uFEFF/, ''), 'Embedded read-only publication export');
})();
