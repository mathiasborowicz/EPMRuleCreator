const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

function parseVersion(value) {
  if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(value))
    throw new Error(`Invalid release version: ${value}`);
  const parts = value.split('.').map(Number);
  if (parts.some(part => !Number.isSafeInteger(part) || part > 65534))
    throw new Error(`Version components exceed Windows assembly limits: ${value}`);
  return parts;
}

function compareVersions(left, right) {
  const a = parseVersion(left);
  const b = parseVersion(right);
  for (let i = 0; i < 3; i++) {
    if (a[i] !== b[i]) return a[i] - b[i];
  }
  return 0;
}

function resolveVersion(baseVersion, tags, currentTags = []) {
  parseVersion(baseVersion);
  const versions = tags.filter(tag => /^v\d+\.\d+\.\d+$/.test(tag)).map(tag => tag.slice(1));
  versions.forEach(parseVersion);
  const current = currentTags.filter(tag => /^v\d+\.\d+\.\d+$/.test(tag)).map(tag => tag.slice(1))
    .filter(version => compareVersions(version, baseVersion) >= 0).sort(compareVersions).at(-1);
  // Retrying publication for the same commit must reuse its original version.
  if (current) return current;
  const latest = versions.sort(compareVersions).at(-1);
  if (!latest || compareVersions(baseVersion, latest) > 0) return baseVersion;
  const parts = parseVersion(latest);
  parts[2]++;
  const next = parts.join('.');
  parseVersion(next);
  return next;
}

if (require.main === module) {
  const project = fs.readFileSync(path.join(__dirname, '../src/EPMRuleCreator.UI/EPMRuleCreator.UI.csproj'), 'utf8');
  const baseVersion = project.match(/<Version>([^<]+)<\/Version>/)?.[1];
  parseVersion(baseVersion || '');
  const publishing = process.env.GITHUB_EVENT_NAME === 'push' && process.env.GITHUB_REF === 'refs/heads/main';
  const gitTags = args => execFileSync('git', args, { encoding: 'utf8' }).split(/\r?\n/).filter(Boolean);
  const version = publishing
    ? resolveVersion(baseVersion, gitTags(['tag', '--list']), gitTags(['tag', '--points-at', 'HEAD']))
    : baseVersion;
  console.log(`Application version: ${version}`);
  if (process.env.GITHUB_OUTPUT) fs.appendFileSync(process.env.GITHUB_OUTPUT, `version=${version}\n`);
}

module.exports = { resolveVersion };
