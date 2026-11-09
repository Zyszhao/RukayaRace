import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const exec = promisify(execFile);
// SteamCMD 不同版本/输出流的成功文本可能不同，以远端近期更新记录补充核验。
export async function confirmSteamUpload({ stdout = '', stderr = '', workshopId, notes, startedAt }) {
  const output = `${stdout}\n${stderr}`.replace(/\x1b\[[0-?]*[ -/]*[@-~]/g, '');
  const successLines = output.split(/[\r\n]+/).filter(line => /Success\.\s*(?:Uploaded item|Published File)/i.test(line));
  if (successLines.some(line => line.includes(workshopId))) return 'steamcmd';
  if (!/^[1-9]\d*$/.test(workshopId)) throw new Error('工坊 ID 无效。');
  // 使用本机 PowerShell 网络栈，沿用 Windows 的代理配置。
  const { stdout: html } = await exec('pwsh', ['-NoProfile', '-Command', '(Invoke-WebRequest -Uri $env:RUKAYA_CHANGELOG_URL -Headers @{"Cache-Control"="no-cache"} -TimeoutSec 20).Content'], {
    env: { ...process.env, RUKAYA_CHANGELOG_URL: `https://steamcommunity.com/sharedfiles/filedetails/changelog/${workshopId}` },
    timeout: 30000, maxBuffer: 4 * 1024 * 1024,
  });
  const entries = [...html.matchAll(/<p\s+id="(\d+)"[^>]*>([\s\S]*?)<\/p>/g)];
  const decode = value => value.replace(/<br\s*\/?>/gi, '\n').replace(/<[^>]+>/g, '')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&').trim();
  if (entries.some(([, timestamp, content]) => Number(timestamp) >= Math.floor(startedAt / 1000) - 5 && decode(content) === notes.trim())) {
    return 'workshop-changelog';
  }
  throw new Error('SteamCMD 成功文本和远端近期更新记录均未确认此次上传；核对工坊后再决定是否重试。');
}
