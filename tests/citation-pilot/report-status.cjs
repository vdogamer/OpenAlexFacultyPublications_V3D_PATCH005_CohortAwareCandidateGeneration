'use strict';
const fs = require('node:fs');

module.exports = async function report({ github, context, core }) {
  const file = process.env.CITATION_CHECK_LOG;
  const lines = file && fs.existsSync(file) ? fs.readFileSync(file, 'utf8').split(/\r?\n/) : [];
  const ok = process.env.CITATION_JOB_RESULT === 'success';
  const failures = lines.filter(line => /error [A-Z]+\d+:|AssertionError|^FAIL\b|^Error:|^Unhandled exception\b|^\s*at .*\.c[sm]?js:\d+/.test(line));
  const summaries = lines.filter(line => /\b(pass(?:ed)?|self.checks?|self.tests?|checks? completed)\b/i.test(line));
  const description = (ok ? summaries.at(-1) || 'Independent citation validation passed.' : failures[0] || 'Citation validation failed; inspect the workflow steps.')
    .replace(/\x1B\[[0-9;]*m/g, '').replace(/\s+/g, ' ').slice(0, 140);
  const target = `${context.serverUrl}/${context.repo.owner}/${context.repo.repo}/actions/runs/${context.runId}`;
  await github.rest.repos.createCommitStatus({
    ...context.repo,
    sha: context.payload.pull_request?.head.sha || context.sha,
    state: ok ? 'success' : 'failure',
    context: process.env.CITATION_CHECK_NAME || 'Citation pilot validation',
    description,
    target_url: target
  });
  core.info(description);
};
