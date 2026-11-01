import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const candidates = [new URL('../install-online.sh', import.meta.url), new URL('../lib/macos-install.sh', import.meta.url)];
const source = fs.readFileSync(candidates.find(p => fs.existsSync(p)), 'utf8');
const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const shellPath = p => p.replaceAll('\\', '/').replace(/^([A-Za-z]):/, (_, d) => `/${d.toLowerCase()}`);
const payload = Buffer.from('VibeCodingLight online installer fixture');
const digest = crypto.createHash('sha256').update(payload).digest('hex');
const helper = `#!/usr/bin/env node
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const native=p=>process.platform==='win32'?p.replace(/^\\/([a-z])\\//i,(_,d)=>d.toUpperCase()+':/'):p;
const tool=path.basename(process.argv[1]),a=process.argv.slice(2),o=JSON.parse(process.env.MOCK_OPTIONS);
const log=s=>fs.appendFileSync(process.env.MOCK_LOG,s+'\\n');
log(tool+' '+a.join(' '));
if(tool==='uname')console.log(a[0]==='-s'?(o.os||'Darwin'):(o.arch||'arm64'));
if(tool==='sysctl')console.log(o.arm||0);
if(tool==='sw_vers')console.log(o.version||'14.6');
if(tool==='curl'){
 if(o.network)process.exit(22);
 const target=native(a[a.indexOf('--output')+1]);
 const manifest=a.includes('https://www.vibecodinglight.com/releases/macos/installer.json');
 if(manifest)fs.writeFileSync(target,JSON.stringify({version:'0.1.53',platform:'osx-arm64',archiveSha256:process.env.MOCK_HASH,sourceCommit:'b'.repeat(40),...o.metadata}));
 else {if(!a.includes('https://github.com/barancavusoglu/vibecodinglight-desktop/releases/download/macos-v0.1.53/VibeCodingLight-macos-v0.1.53-macos-arm64.zip'))process.exit(24);fs.copyFileSync(process.env.MOCK_ARCHIVE,target);}
}
if(tool==='plutil'){
 const file=native(a.at(-1)),key=a[a.indexOf('-extract')+1];
 const info=file.endsWith('Info.plist')?{CFBundleIdentifier:'com.vibecodinglight.desktop',CFBundleShortVersionString:'0.1.53',VCLSourceRevision:'b'.repeat(40),...o.bundle}:JSON.parse(fs.readFileSync(file,'utf8'));
 if(!(key in info))process.exit(1); console.log(info[key]);
}
if(tool==='shasum'){const p=native(a.at(-1));console.log(crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')+'  '+p);}
if(tool==='unzip')console.log(o.unsafe?'../outside':'VibeCodingLight.app/Contents/Info.plist\\nInstall VibeCodingLight.command');
if(tool==='codesign'&&o.signature)process.exit(1);
if(tool==='ditto'){
 const target=native(a.at(-1)),resources=path.join(target,'VibeCodingLight.app/Contents/Resources');
 fs.mkdirSync(path.join(resources,'licenses'),{recursive:true});
 fs.writeFileSync(path.join(target,'VibeCodingLight.app/Contents/Info.plist'),'{}');
 for(const n of ['LICENSE','NOTICE','THIRD_PARTY_NOTICES.md'])if(!o.missingLicense)fs.writeFileSync(path.join(resources,n),'fixture');
 fs.writeFileSync(path.join(target,'Install VibeCodingLight.command'),'echo executed >> "$MOCK_LOG"\\n');
}
`;

function run(options = {}, downloadOnly = true) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'vcl-mac-online-test-'));
  const bin = path.join(root, 'bin'), tmp = path.join(root, 'tmp'), log = path.join(root, 'commands.log');
  try {
    fs.mkdirSync(bin); fs.mkdirSync(tmp);
    for (const command of ['uname', 'sysctl', 'sw_vers', 'curl', 'plutil', 'shasum', 'unzip', 'ditto', 'codesign']) {
      const file = path.join(bin, command); fs.writeFileSync(file, helper); fs.chmodSync(file, 0o755);
    }
    const archive = path.join(root, 'fixture.zip'); fs.writeFileSync(archive, payload);
    const guard = "export PATH='/usr/bin:/bin:/usr/sbin:/sbin'";
    assert.equal(source.split(guard).length, 2, 'The production script must use the standard system PATH');
    const script = path.join(root, 'install.sh');
    fs.writeFileSync(script, source.replace(guard, `export PATH='${shellPath(bin)}:${shellPath(path.dirname(process.execPath))}':"$PATH"`));
    const result = spawnSync(bash, [shellPath(script), ...(downloadOnly ? ['--download-only'] : [])], {
      env: { ...process.env, TMPDIR: shellPath(tmp), MOCK_OPTIONS: JSON.stringify(options), MOCK_LOG: log,
        MOCK_HASH: options.hash || digest, MOCK_ARCHIVE: archive }, encoding: 'utf8', timeout: 20_000,
    });
    return { ...result, log: fs.existsSync(log) ? fs.readFileSync(log, 'utf8') : '', retained: fs.readdirSync(tmp) };
  } finally {
    assert.equal(path.dirname(root), path.resolve(os.tmpdir())); assert.match(path.basename(root), /^vcl-mac-online-test-/);
    fs.rmSync(root, { recursive: true, force: true });
  }
}
test('download-only verifies an official package without executing its installer', () => {
  const r = run(); assert.equal(r.status, 0, r.stderr); assert.match(r.stdout, /Verified package:/);
  assert.match(r.log, /codesign --verify --deep --strict/); assert.doesNotMatch(r.log, /executed/);
  assert.equal(r.retained.length, 1);
});
test('normal installation runs only after validation and removes its temporary directory', () => {
  const r = run({}, false); assert.equal(r.status, 0, r.stderr); assert.match(r.log, /executed/);
  assert.equal(r.retained.length, 0);
});
test('checksum mismatch prevents extraction and installation', () => {
  const r = run({ hash: 'c'.repeat(64) }, false); assert.notEqual(r.status, 0);
  assert.match(r.stderr, /Checksum mismatch/); assert.doesNotMatch(r.log, /ditto|executed/); assert.equal(r.retained.length, 0);
});
test('unsupported operating systems, Intel hardware and older macOS stop before downloading', () => {
  for (const options of [{ os: 'Linux' }, { arch: 'x86_64' }, { version: '13.7' }]) {
    const r = run(options); assert.notEqual(r.status, 0); assert.doesNotMatch(r.log, /curl/);
  }
});
test('Apple silicon remains supported from a Rosetta terminal', () => {
  const r = run({ arch: 'x86_64', arm: 1 }); assert.equal(r.status, 0, r.stderr);
});
test('invalid release metadata never becomes a download command', () => {
  for (const metadata of [{ version: '1.2.3; touch outside' }, { platform: 'osx-x64' }, { sourceCommit: 'bad' }]) {
    const r = run({ metadata }, false); assert.notEqual(r.status, 0);
    assert.doesNotMatch(r.log, /github.com|executed/);
  }
});
test('unsafe archive paths and failed signatures stop the installer', () => {
  for (const options of [{ unsafe: true }, { signature: true }, { bundle: { VCLSourceRevision: 'c'.repeat(40) } }, { missingLicense: true }]) {
    const r = run(options, false); assert.notEqual(r.status, 0); assert.doesNotMatch(r.log, /executed/);
    assert.equal(r.retained.length, 0);
  }
});
test('network errors stop cleanly without running any installer', () => {
  const r = run({ network: true }, false); assert.notEqual(r.status, 0); assert.doesNotMatch(r.log, /executed/);
  assert.equal(r.retained.length, 0);
});
test('real Mac archive passes the online download validation', {
  skip: process.platform !== 'darwin' || !process.env.VCL_MAC_REAL_ARCHIVE,
}, () => {
  const archive = path.resolve(process.env.VCL_MAC_REAL_ARCHIVE);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'vcl-mac-online-test-'));
  try {
    const bin = path.join(root, 'bin'), tmp = path.join(root, 'tmp');
    fs.mkdirSync(bin); fs.mkdirSync(tmp);
    const metadata = path.join(root, 'manifest.json');
    fs.writeFileSync(metadata, JSON.stringify({ version: process.env.VCL_MAC_REAL_VERSION, platform: 'osx-arm64',
      sourceCommit: process.env.VCL_MAC_REAL_COMMIT,
      archiveSha256: crypto.createHash('sha256').update(fs.readFileSync(archive)).digest('hex') }));
    const curl = path.join(bin, 'curl');
    fs.writeFileSync(curl, `#!/usr/bin/env node\nconst fs=require('node:fs');const a=process.argv.slice(2);fs.copyFileSync(a.includes('https://www.vibecodinglight.com/releases/macos/installer.json')?process.env.REAL_METADATA:process.env.REAL_ARCHIVE,a[a.indexOf('--output')+1]);\n`);
    fs.chmodSync(curl, 0o755);
    const script = path.join(root, 'install.sh');
    fs.writeFileSync(script, source.replace("export PATH='/usr/bin:/bin:/usr/sbin:/sbin'",
      `export PATH='${bin}:${path.dirname(process.execPath)}':/usr/bin:/bin:/usr/sbin:/sbin`));
    const result = spawnSync('/bin/bash', [script, '--download-only'], { encoding: 'utf8', timeout: 60_000,
      env: { ...process.env, TMPDIR: tmp, REAL_METADATA: metadata, REAL_ARCHIVE: archive } });
    assert.equal(result.status, 0, result.stderr); assert.match(result.stdout, /Verified package:/);
  } finally {
    assert.equal(path.dirname(root), path.resolve(os.tmpdir())); assert.match(path.basename(root), /^vcl-mac-online-test-/);
    fs.rmSync(root, { recursive: true, force: true });
  }
});
