const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { requiresRelease, changedFiles } = require('../../scripts/release-change-filter.cjs');

const root = path.resolve(__dirname, '../..');
const workflow = fs.readFileSync(path.join(root, '.github/workflows/windows-release-build.yml'), 'utf8');

test('documentation and issue forms do not require a release', () => {
  for (const file of [
    'README.md', 'CONTRIBUTING.md', '.github/PULL_REQUEST_TEMPLATE.md',
    'docs/Installation.md', 'docs/assets/App.png', 'docs/assets/Context Menu.png',
    'docs/assets/screenshots/example.png', 'docs/RuleEditorMockup.html',
    '.github/ISSUE_TEMPLATE/feature_request.yml', '.github/ISSUE_TEMPLATE/config.yml'
  ]) assert.equal(requiresRelease([file]), false, file);
});

test('a push containing only documentation and issue forms is skipped', () => {
  assert.equal(requiresRelease(['README.md', 'docs/UserGuide.md', '.github/ISSUE_TEMPLATE/feature_request.yml']), false);
  assert.equal(requiresRelease([]), false);
});

test('application, embedded resources, license, and build changes still require a release', () => {
  for (const file of [
    'src/EPMRuleCreator.UI/MainWindow.xaml', 'src/EPMRuleCreator.Core/Services/FileAttributeService.cs',
    'src/EPMRuleCreator.UI/EPMRuleCreator.UI.csproj', 'docs/assets/epm-rule-creator.png',
    'LICENSE', 'scripts/Install-AppUpdate.ps1', '.github/workflows/windows-release-build.yml',
    'releases/EPMRuleCreator-win-x64.msi'
  ]) assert.equal(requiresRelease([file]), true, file);
});

test('mixed pushes and source-to-docs moves are not skipped', () => {
  assert.equal(requiresRelease(['README.md', 'src/Application.cs']), true);
  assert.equal(requiresRelease(['src/Application.cs', 'docs/Application.cs']), true);
});

test('first branch pushes and missing comparison SHAs are handled explicitly', () => {
  for (const before of [undefined, '', '0'.repeat(40), 'not-a-sha', '--help'])
    assert.equal(changedFiles(before, root), null);
});

test('comparison uses committed trees rather than unrelated local MSI edits', () => {
  const head = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
  assert.deepEqual(changedFiles(head, root), []);
});

test('CLI emits the Actions output used to gate release publication', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'epm-release-filter-'));
  const output = path.join(directory, 'output');
  try {
    const head = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
    execFileSync(process.execPath, ['scripts/release-change-filter.cjs'], {
      cwd: root, env: { ...process.env, RELEASE_BEFORE_SHA: head, GITHUB_OUTPUT: output }
    });
    assert.equal(fs.readFileSync(output, 'utf8'), 'release_required=false\n');
  } finally {
    if (fs.existsSync(output)) fs.unlinkSync(output);
    fs.rmdirSync(directory);
  }
});

test('workflow keeps PR checks and gates only the release job on the change filter', () => {
  assert.match(workflow, /pull_request:\s+branches:\s+- main/);
  assert.match(workflow, /release_required: \$\{\{ steps\.changes\.outputs\.release_required \}\}/);
  assert.match(workflow, /id: changes\s+env:\s+RELEASE_BEFORE_SHA: \$\{\{ github\.event\.before \}\}\s+run: node scripts\/release-change-filter\.cjs/);
  assert.match(workflow, /if: github\.event_name == 'push' && github\.ref == 'refs\/heads\/main' && needs\.test-and-publish\.outputs\.release_required == 'true'/);
  assert.doesNotMatch(workflow, /paths-ignore:/);
  assert.match(workflow, /node --test tests\/workflows\/release-workflow\.test\.cjs tests\/workflows\/release-change-filter\.test\.cjs/);
});
