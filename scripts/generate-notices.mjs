import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const all=new Map();
for(const p of ['windows/App/obj/project.assets.json','macos/obj/project.assets.json']) {
  const a=JSON.parse(fs.readFileSync(path.join(root,p),'utf8'));
  for(const[k,v]of Object.entries(a.libraries)) if(v.type==='package') all.set(k,{...v,folders:Object.keys(a.packageFolders)});
}
const cache=Object.keys(JSON.parse(fs.readFileSync(path.join(root,'windows/App/obj/project.assets.json'),'utf8')).packageFolders)[0];
for(const folder of ['microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64','microsoft.netcore.app.runtime.osx-arm64','microsoft.netcore.app.runtime.osx-x64']) {
  const p=path.join(cache,folder,'10.0.11');
  if(fs.existsSync(p))all.set(`${folder}/10.0.11`,{path:folder+'/10.0.11',folders:[cache]});
}
const unescape=s=>s.replaceAll('&amp;','&').replaceAll('&lt;','<').replaceAll('&gt;','>').replaceAll('&quot;','"').replaceAll('&apos;',"'");
const rows=[],inventory=[];
for(const[key,v]of [...all].sort((a,b)=>a[0].localeCompare(b[0]))) {
  const dir=v.folders.map(p=>path.join(p,v.path)).find(fs.existsSync);
  if(!dir)throw new Error('Missing package '+key);
  const spec=fs.readdirSync(dir).find(n=>n.endsWith('.nuspec'));
  const xml=fs.readFileSync(path.join(dir,spec),'utf8');
  const field=name=>unescape(xml.match(new RegExp(`<${name}[^>]*>([^<]*)</${name}>`))?.[1]||'');
  const id=field('id'),version=field('version'),license=field('license');
  if(!license)throw new Error('Missing license '+id);
  const dest=path.join(root,'licenses/notices');
  fs.mkdirSync(dest,{recursive:true});
  const texts=[];
  function collect(p,rel='') {
    for(const e of fs.readdirSync(p,{withFileTypes:true})) {
      if(e.isDirectory() && !['lib','ref','runtimes','tools','analyzers','build','buildTransitive'].includes(e.name)) collect(path.join(p,e.name),path.join(rel,e.name));
      else if(e.isFile()&&/licen[sc]e|notice|copying/i.test(e.name)&&!e.name.endsWith('.nuspec')) {
        const data=fs.readFileSync(path.join(p,e.name));
        const digest=crypto.createHash('sha256').update(data).digest('hex').slice(0,16);
        const target=path.join(dest,digest+'-'+e.name);fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,data);
        texts.push(path.relative(root,target).replaceAll('\\','/'));
      }
    }
  }
  collect(dir);
  const repo=xml.match(/<repository[^>]*url="([^"]+)"/);const commit=xml.match(/<repository[^>]*commit="([^"]+)"/);
  const record={id,version,license,authors:field('authors'),copyright:field('copyright'),repository:repo?unescape(repo[1]):'',commit:commit?.[1]||'',noticeFiles:texts};
  inventory.push(record);
  rows.push(`| ${id} | ${version} | ${license.replaceAll('|','\\|')} | [NuGet](https://www.nuget.org/packages/${id}/${version}) |`);
}
fs.mkdirSync(path.join(root,'licenses'),{recursive:true});
fs.writeFileSync(path.join(root,'licenses/dependencies.json'),JSON.stringify(inventory,null,2)+'\n');
fs.writeFileSync(path.join(root,'THIRD_PARTY_NOTICES.md'),`# Third-party notices\n\nThe desktop projects use the following NuGet dependency closure, including platform-specific dependencies that may not be loaded on every OS and build-only components. Versions come from the resolved project assets and pinned lock files. .NET 10.0.11 runtime licensing is included for the currently tested self-contained builds. Regenerate this inventory when the SDK or dependency versions change.\n\nThe source code in this repository is Apache-2.0; that license does not replace the separate licenses of these components. NuGet dependencies are restored from their publishers, not vendored as source here. Copyright, author and repository metadata are recorded in [licenses/dependencies.json](licenses/dependencies.json). Original license/third-party notice files shipped inside the packages are preserved under [licenses/notices](licenses/notices); upstream license texts for packages without bundled files are preserved under [licenses/upstream](licenses/upstream). Native libraries (including Skia, HarfBuzz and ANGLE) may contain multiple notices: retain the full supplied notice files, not just their top-level MIT entry.\n\n| Package | Version | Declared license | Source package |\n| --- | --- | --- | --- |\n${rows.join('\n')}\n\n## Redistribution\n\nDistribute LICENSE, NOTICE, this file and the complete licenses/ directory with release archives. Verify the actual platform/runtime dependency set for each new build; an unchanged source inventory does not cover arbitrary future package updates. This repository does not claim ownership of third-party components. The initial source publication does not publish a binary release.\n`);
console.log(JSON.stringify({packages:inventory.length,bundledNoticeFiles:inventory.flatMap(r=>r.noticeFiles).length}));
