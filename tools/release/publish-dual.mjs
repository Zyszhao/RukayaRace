import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile, writeFile, readdir, lstat, mkdtemp, rm, access } from 'node:fs/promises';
import { join, resolve, relative } from 'node:path';
import { pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';
import { createHash } from 'node:crypto';

const exec = promisify(execFile);
const args = process.argv.slice(2);
const value = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const steamRoot = resolve(value('--steam-root', 'D:/SteamCMD'));
const version = value('--version', 'preview');
const target = value('--target', 'Both');
const live = args.includes('--publish');
if (!['Both', 'GitHub', 'Steam'].includes(target)) throw new Error('Invalid target');
if (live && !/^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) throw new Error('正式发布须指定语义版本，例如 v0.2.4。');
const tag = version === 'preview' ? version : version.startsWith('v') ? version : `v${version}`;
const github = target !== 'Steam';
const steam = target !== 'GitHub';
const config = JSON.parse(await readFile(join(steamRoot, 'workshop.config.json'), 'utf8'));
if (config.mods?.length !== 1) throw new Error('此入口要求恰好一个模组。');
const mod = config.mods[0];
const repo = resolve(steamRoot, mod.path);
const git = async (...argv) => (await exec('git', ['-C', repo, ...argv], { maxBuffer: 16 * 1024 * 1024 })).stdout.trim();
const head = await git('rev-parse', 'HEAD');
const branch = await git('branch', '--show-current');
if (!config.branchTargets[branch]) throw new Error(`分支未配置：${branch}`);
const workshopId = mod.workshopIds[config.branchTargets[branch]];
if (!/^[1-9]\d*$/.test(workshopId)) throw new Error('必须配置已有工坊 ID。');
if ((await readFile(join(repo, 'About/PublishedFileId.txt'), 'utf8')).trim() !== workshopId) throw new Error('工坊 ID 与 About 不一致。');
const origin = await git('remote', 'get-url', 'origin');
const match = origin.match(/github\.com[:/]([^/]+\/[^/]+?)(?:\.git)?$/);
if (!match) throw new Error('origin 必须指向 GitHub。');
const githubRepo = match[1];
const notesFile = value('--notes-file');
if (live && !notesFile) throw new Error('正式发布须传入 --notes-file（UTF-8 更新说明）。');
const notes = notesFile ? await readFile(resolve(notesFile), 'utf8') : '发布预演';
if (!notes.trim()) throw new Error('更新说明不能为空。');
const status = await git('status', '--porcelain', '--untracked-files=all');
if (live && status) throw new Error('统一工作区存在未提交文件；请先提交、推送并记录开发区摘要。');
console.log(JSON.stringify({ mode: live ? 'publish' : 'preview', repo, head, branch, target, tag, githubRepo, workshopId, dirty: Boolean(status) }, null, 2));

const env = { ...process.env, STEAM_USERNAME: process.env.STEAM_USERNAME || 'williamxiaozhao',
  STEAMCMD_PATH: join(steamRoot, 'steamcmd.exe'), STEAM_CONFIG_VDF: join(steamRoot, 'config/config.vdf') };
if (steam && live) {
  await access(env.STEAM_CONFIG_VDF);
  if (!env.STEAM_PASSWORD && !env.STEAM_REFRESH_TOKEN) throw new Error('标签认证需 STEAM_REFRESH_TOKEN 或 STEAM_PASSWORD；未配置时停止，避免半途上传。');
}
if (!live && !env.STEAM_PASSWORD && !env.STEAM_REFRESH_TOKEN) env.STEAM_REFRESH_TOKEN = 'offline-preview-only';
const { stageModContent } = await import(pathToFileURL(join(steamRoot, 'tools/windows-steam-plugin.mjs')));
const { publish } = await import(pathToFileURL(join(steamRoot, 'semantic-release-steam/index.mjs')));
const options = { ...config, outputReadme: false, mods: [{ ...mod, path: repo }] };
const context = { cwd: repo, branch: { name: branch }, env, options: { dryRun: true },
  nextRelease: { version: tag, notes }, logger: console };
let stage;
const work = await mkdtemp(join(tmpdir(), 'rukaya-dual-'));
try {
  await access(join(repo, '.steamignore'));
  stage = await stageModContent({ modPath: repo });
  const tracked = new Set((await git('ls-files', '-z')).split('\0'));
  const manifest = [];
  async function walk(dir) {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      const name = relative(stage, path).replaceAll('\\', '/');
      if ((await lstat(path)).isSymbolicLink()) throw new Error(`不允许符号链接：${name}`);
      if (entry.isDirectory()) { await walk(path); continue; }
      if (!/^(About|1\.7|Content|StoryTellerFullBody)\//.test(name) && name !== 'LoadFolders.xml') throw new Error(`白名单外文件：${name}`);
      if (/(^|\/)(\.git|node_modules|bin|obj)(\/|$)|(^|\/)\.env(?:\.|$)|\.(pdb|log|cs|csproj|sln|tmp|bak)$/i.test(name)) throw new Error(`开发文件混入：${name}`);
      if (!tracked.has(name)) throw new Error(`运行文件未入 Git：${name}`);
      const bytes = await readFile(path);
      manifest.push({ path: name, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
    }
  }
  await walk(stage);
  for (const required of ['LoadFolders.xml', 'About/About.xml', 'About/PublishedFileId.txt', '1.7/Assemblies/RukayaRace.dll']) {
    if (!manifest.some(x => x.path === required)) throw new Error(`缺少运行文件：${required}`);
  }
  manifest.sort((a, b) => a.path.localeCompare(b.path));
  const manifestPath = join(work, `RukayaRace-${tag}.manifest.json`);
  await writeFile(manifestPath, JSON.stringify({ tag, commit: head, files: manifest }, null, 2));
  console.log(`暂存校验通过：${manifest.length} 个文件，${manifest.reduce((n, x) => n + x.bytes, 0)} 字节；源码、Git、调试符号、发布工具均已排除。`);
  const zip = join(work, `RukayaRace-${tag}.zip`);
  if (github) {
    await exec('pwsh', ['-NoProfile', '-Command', 'Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory($env:RUKAYA_STAGE, $env:RUKAYA_ZIP); $z = [IO.Compression.ZipFile]::OpenRead($env:RUKAYA_ZIP); try { Write-Output ("ZIP entries: " + $z.Entries.Count) } finally { $z.Dispose() }'],
      { env: { ...process.env, RUKAYA_STAGE: stage, RUKAYA_ZIP: zip }, maxBuffer: 1024 * 1024 }).then(result => console.log(result.stdout.trim()));
  }
  if (steam) await publish(options, context);
  if (!live) { console.log('预演完成；未登录 Steam、未推送、未创建标签或 GitHub Release。'); }
  else {
    // 先验证远端精确提交，避免将尚未推送的源码和运行包关联。
    const remoteHead = (await git('ls-remote', 'origin', `refs/heads/${branch}`)).split(/\s/)[0];
    if (remoteHead !== head) throw new Error('远端分支与本地 HEAD 不一致，请先推送。');
    if (await git('status', '--porcelain', '--untracked-files=all') || await git('rev-parse', 'HEAD') !== head) throw new Error('预检期间工作区发生变化。');
    // 构建后再次比对，防止暂存期间运行文件被其他进程修改。
    for (const file of manifest) {
      const bytes = await readFile(join(repo, file.path));
      if (createHash('sha256').update(bytes).digest('hex') !== file.sha256) throw new Error(`暂存后文件变化：${file.path}`);
    }
    if (github) {
      const existingTag = await git('ls-remote', 'origin', `refs/tags/${tag}`);
      if (existingTag) throw new Error(`标签已存在：${tag}；请先核对已有发布，禁止覆盖。`);
      await exec('gh', ['api', `repos/${githubRepo}`]);
      const body = join(work, 'notes.md');
      await writeFile(body, notes);
      const releaseArgs = ['release', 'create', tag, zip, manifestPath, '--repo', githubRepo, '--target', head,
        '--title', `Rukaya Race ${tag}`, '--notes-file', body];
      if (tag.includes('-')) releaseArgs.push('--prerelease');
      const result = await exec('gh', releaseArgs);
      console.log(`GitHub Release 完成：${result.stdout.trim()}`);
    }
    if (steam) {
      const { createWorkshopVdf } = await import(pathToFileURL(join(steamRoot, 'semantic-release-steam/lib/vdf.mjs')));
      // 上游将 workshop.vdf 写入 contentfolder；此处移到包外，避免上传控制文件。
      await publish(options, { ...context, options: { dryRun: false }, stageModContent: async () => stage,
        uploadWorkshopItem: async data => {
          const vdf = join(work, 'workshop.vdf');
          await writeFile(vdf, createWorkshopVdf({ ...data, contentFolder: stage }));
          const result = await exec(env.STEAMCMD_PATH, ['+@ShutdownOnFailedCommand', '1', '+@NoPromptForPassword', '1',
            '+login', env.STEAM_USERNAME, '+workshop_build_item', vdf, '+quit'],
            { cwd: steamRoot, env, timeout: 600000, maxBuffer: 4 * 1024 * 1024 });
          if (!/Success\. (?:Uploaded item|Published File)/i.test(result.stdout)) throw new Error('SteamCMD 未返回上传成功标志；请检查 D:/SteamCMD/logs，不以退出码代替成功判断。');
          console.log(`SteamCMD 文件上传完成：${workshopId}；接下来更新标签。`);
          return vdf;
        } });
      console.log(`Steam 工坊发布完成：${workshopId}`);
    }
  }
} catch (error) {
  console.error('流程停止。GitHub、Steam 文件、Steam 标签分别执行，已成功的步骤不会自动回滚；根据上方完成记录选择单端重试。');
  throw error;
} finally {
  if (stage) await rm(stage, { recursive: true, force: true });
  await rm(work, { recursive: true, force: true });
}
