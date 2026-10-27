const fs = require('node:fs');
const { execFileSync } = require('node:child_process');

function isDocumentationOnly(file) {
  // This image lives in docs, but is embedded in the application by the UI project.
  if (file === 'docs/assets/epm-rule-creator.png') return false;
  return file.startsWith('docs/')
    || file.startsWith('.github/ISSUE_TEMPLATE/')
    || /\.md$/i.test(file);
}

function requiresRelease(files) {
  return files.some(file => !isDocumentationOnly(file));
}

function changedFiles(before, cwd = process.cwd()) {
  if (!/^[a-f0-9]{40}(?:[a-f0-9]{24})?$/i.test(before || '') || /^0+$/.test(before))
    return null; // Initial branch pushes and non-push events have no usable before SHA.
  // Compare the entire push, not only the final commit. Disable rename detection
  // so moving a source file into docs still counts as removing application code.
  return execFileSync('git', ['diff', '--no-renames', '--name-only', '-z', before, 'HEAD', '--'],
    { cwd, encoding: 'utf8' }).split('\0').filter(Boolean);
}

if (require.main === module) {
  const files = changedFiles(process.env.RELEASE_BEFORE_SHA);
  const required = files === null || requiresRelease(files);
  console.log(`Release required: ${required}`);
  if (process.env.GITHUB_OUTPUT)
    fs.appendFileSync(process.env.GITHUB_OUTPUT, `release_required=${required}\n`);
}

module.exports = { requiresRelease, changedFiles };
