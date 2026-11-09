// 同一份玩家更新说明分别生成两端正文，避免 Steam 漏版本或 GitHub 重复标题。
export function formatReleaseNotes(tag, text) {
  const lines = text.replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n').trim().split('\n');
  const header = /^\s*(?:#{1,6}\s+)?(?:Rukaya Race\s+)?v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\s*$/i;
  // 兼容以往手写的版本标题，防止重复拼接。
  if (header.test(lines[0] ?? '')) lines.shift();
  const github = lines.join('\n').trim();
  if (!github) throw new Error('更新说明必须包含版本标题以外的实际更新内容。');
  return { github, steam: `Rukaya Race ${tag}\n${github}` };
}
