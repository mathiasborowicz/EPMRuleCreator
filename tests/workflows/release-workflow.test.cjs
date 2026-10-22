const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Exercise the actual inline Actions script, not a separate implementation.
const workflow = fs.readFileSync(path.join(__dirname, '../../.github/workflows/windows-release-build.yml'), 'utf8');
const script = workflow.match(/          script: \|\r?\n((?:            .*\r?\n?)+)/)[1]
  .split(/\r?\n/).map(line => line.replace(/^            /, '')).join('\n');
const execute = new (Object.getPrototypeOf(async function () {}).constructor)('github', 'context', 'core', 'require', script);

function harness({ existingRelease, existingAsset = false, failUpload = false, failRead = false } = {}) {
  const calls = [];
  const listReleases = () => {};
  const listReleaseAssets = () => {};
  const release = existingRelease || { id: 7, tag_name: 'build-42', draft: true, html_url: 'https://github.com/owner/repo/releases/tag/build-42' };
  const github = {
    paginate: async method => method === listReleases ? (existingRelease ? [existingRelease] : []) : (existingAsset ? [{ name: 'IntuneEpmRuleCreator.exe' }] : []),
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
          assert.equal(file, 'release/IntuneEpmRuleCreator.exe');
          if (failRead) throw new Error('Missing EXE');
          return Buffer.from('tested executable');
        } };
      })
  };
}

test('publishes a draft only after uploading the tested standalone EXE', async () => {
  const h = harness();
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['create', 'upload', 'publish', 'link']);
  assert.equal(h.calls[0][1].draft, true);
  assert.equal(h.calls[0][1].target_commitish, 'tested-commit');
  assert.equal(h.calls[0][1].tag_name, 'build-42');
  assert.equal(h.calls[1][1].name, 'IntuneEpmRuleCreator.exe');
  assert.equal(h.calls[2][1].make_latest, 'true');
  assert.equal(h.calls[3][1], 'https://github.com/owner/repo/releases/download/build-42/IntuneEpmRuleCreator.exe');
});

test('missing executable creates no release', async () => {
  const h = harness({ failRead: true });
  await assert.rejects(h.run(), /Missing EXE/);
  assert.deepEqual(h.calls, []);
});

test('failed uploads do not publish a public release', async () => {
  const h = harness({ failUpload: true });
  await assert.rejects(h.run(), /Upload failed/);
  assert.deepEqual(h.calls.map(call => call[0]), ['create', 'upload']);
});

test('retry reuses an unpublished draft and uploads its missing asset', async () => {
  const h = harness({ existingRelease: { id: 7, tag_name: 'build-42', draft: true, html_url: 'https://github.com/owner/repo/releases/tag/build-42' } });
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['upload', 'publish', 'link']);
});

test('retry preserves an already published EXE without duplicate releases', async () => {
  const h = harness({ existingRelease: { id: 7, tag_name: 'build-42', draft: false, html_url: 'https://github.com/owner/repo/releases/tag/build-42' }, existingAsset: true });
  await h.run();
  assert.deepEqual(h.calls.map(call => call[0]), ['link']);
});
