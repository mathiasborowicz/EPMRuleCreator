const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { resolveVersion } = require('../../scripts/resolve-release-version.cjs');

// Exercise the actual inline Actions script, not a separate implementation.
const workflow = fs.readFileSync(path.join(__dirname, '../../.github/workflows/windows-release-build.yml'), 'utf8');
const script = workflow.match(/          script: \|\r?\n((?:            .*\r?\n?)+)/)[1]
  .split(/\r?\n/).map(line => line.replace(/^            /, '')).join('\n');
const execute = new (Object.getPrototypeOf(async function () {}).constructor)('github', 'context', 'core', 'require', 'process', script);

function harness({ existingRelease, existingAsset = false, failUpload = false, failRead = false, version = '0.9.0' } = {}) {
  const calls = [];
  const listReleases = () => {};
  const listReleaseAssets = () => {};
  const release = existingRelease || { id: 7, tag_name: 'v0.9.0', draft: true, html_url: 'https://github.com/owner/repo/releases/tag/v0.9.0' };
  const github = {
    paginate: async method => method === listReleases ? (existingRelease ? [existingRelease] : []) : (existingAsset ? [{ name: 'IntuneEpmRuleCreator-win-x64.zip' }, { name: 'LICENSE' }] : []),
    rest: { repos: {
      listReleases, listReleaseAssets,
      createRelease: async args => { calls.push(['create', args]); return { data: release }; },
      uploadReleaseAsset: async args => {
        calls.push(['upload', args]);
        if (failUpload) throw new Error('Upload failed');
      },
      updateRelease: async args => { calls.push(['publish', args]); return { data: { ...release, draft: false } }; }
    } }
  };
  const summary = {
    addHeading: () => summary,
    addLink: (label, url) => { calls.push(['link', url]); return summary; },
    write: async () => {}
  };
  return {
    calls,
    run: () => execute(github, { repo: { owner: 'owner', repo: 'repo' }, runNumber: 42, sha: 'tested-commit' },
      { summary, info: () => {} },
      name => {
        assert.equal(name, 'node:fs');
        return { readFileSync: file => {
          assert.ok(['release/IntuneEpmRuleCreator-win-x64.zip', 'release/LICENSE'].includes(file));
          if (failRead) throw new Error('Missing ZIP');
          return Buffer.from('tested archive');
        } };
      }, { env: { RELEASE_VERSION: version } })
  };
}

test('publishes a draft only after uploading the standalone ZIP and license', async () => {
  const h = harness();
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['create', 'upload', 'upload', 'publish', 'link']);
  assert.equal(h.calls[0][1].draft, true);
  assert.equal(h.calls[0][1].target_commitish, 'tested-commit');
  assert.equal(h.calls[0][1].tag_name, 'v0.9.0');
  assert.equal(h.calls[0][1].name, 'Intune EPM Rule Creator v0.9.0');
  assert.ok(!h.calls[0][1].name.includes('\u2014'));
  assert.match(h.calls[0][1].body, /GNU GPLv3/);
  assert.equal(h.calls[1][1].name, 'IntuneEpmRuleCreator-win-x64.zip');
  assert.equal(h.calls[1][1].headers['content-type'], 'application/zip');
  assert.match(h.calls[0][1].body, /extract it/);
  assert.ok(!h.calls.filter(call => call[0] === 'upload').some(call => call[1].name.endsWith('.exe')));
  assert.equal(h.calls[2][1].name, 'LICENSE');
  assert.equal(h.calls[3][1].make_latest, 'true');
  assert.equal(h.calls[4][1], 'https://github.com/owner/repo/releases/download/v0.9.0/IntuneEpmRuleCreator-win-x64.zip');
});

test('missing archive creates no release', async () => {
  const h = harness({ failRead: true });
  await assert.rejects(h.run(), /Missing ZIP/);
  assert.deepEqual(h.calls, []);
});

test('failed uploads do not publish a public release', async () => {
  const h = harness({ failUpload: true });
  await assert.rejects(h.run(), /Upload failed/);
  assert.deepEqual(h.calls.map(call => call[0]), ['create', 'upload']);
});

test('retry reuses an unpublished draft and uploads its missing asset', async () => {
  const h = harness({ existingRelease: { id: 7, tag_name: 'v0.9.0', draft: true, html_url: 'https://github.com/owner/repo/releases/tag/v0.9.0' } });
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['upload', 'upload', 'publish', 'link']);
});

test('retry preserves an already published ZIP without duplicate releases', async () => {
  const h = harness({ existingRelease: { id: 7, tag_name: 'v0.9.0', draft: false, html_url: 'https://github.com/owner/repo/releases/tag/v0.9.0' }, existingAsset: true });
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['link']);
});

test('release packaging includes only the EXE and license', () => {
  assert.match(workflow, /working-directory: release\r?\n\s+run: zip -9 IntuneEpmRuleCreator-win-x64\.zip IntuneEpmRuleCreator\.exe LICENSE/);
});

test('invalid release versions create no releases', async () => {
  const h = harness({ version: '' });
  await assert.rejects(h.run(), /Missing or invalid release version/);
  assert.deepEqual(h.calls, []);
});

test('first version uses project baseline and ignores old build tags', () => {
  assert.equal(resolveVersion('0.9.0', ['build-49', 'unrelated']), '0.9.0');
});

test('later merges increment the greatest published patch numerically', () => {
  assert.equal(resolveVersion('0.9.0', ['v0.9.2', 'v0.9.10', 'v0.9.9']), '0.9.11');
});

test('explicitly raising the project version starts a new version line', () => {
  assert.equal(resolveVersion('0.10.0', ['v0.9.9']), '0.10.0');
});

test('retrying the same tagged commit reuses its version', () => {
  assert.equal(resolveVersion('0.9.0', ['v0.9.0', 'v0.9.1'], ['v0.9.0']), '0.9.0');
});

test('invalid and overflowing assembly versions fail clearly', () => {
  assert.throws(() => resolveVersion('0.9', []), /Invalid release version/);
  assert.throws(() => resolveVersion('0.9.0', ['v0.9.65534']), /Windows assembly limits/);
});
