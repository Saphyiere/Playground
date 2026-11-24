// Every exercise to play, played by the preview's stand-in piano in its first key and hands: once through In time, once in
// Wait for me, looped In time until a third pass, and through the keys. A line for each with what every run came to, BAD (and a
// picture) for anything that didn't draw, didn't finish or wasn't all right, and a count at the end. See README.md.
// node Music/tools/checks/exercises.mjs <folder for pictures> [one exercise's id] [what's chosen of its variations] [hands]
// With a family's id and a choice ("interval=octave;quick=3") it's that form that's played, in C, with its first hands or the
// ones named (Right, Left, Together).
import { open, sleep } from './drive.mjs';

const out = process.argv[2] ?? 'artifacts/checks/exercises';
const only = process.argv[3];
const variation = process.argv[4];
const hands = process.argv[5];
const page = await open({ width: 1557, height: 973, dpr: 1 });
console.log(await page.get('keys/connect'));
// Every exercise to play, type by type as the catalogue lists them: each card that opens an exercise (a drill's opens drills/,
// and one to read has no shape to play).
await page.go('exercises');
const types = await page.evaluate(`[...document.querySelectorAll('.lib-list a.col-item')].map(a => a.getAttribute('href')).filter(h => h.includes('type='))`);
let ids = [];
for (const type of types) {
  await page.go(type);
  await page.until(`document.querySelector('.lib-list[aria-busy=false]')`);
  for (const id of await page.evaluate(`[...document.querySelectorAll('a.ex-card:not(:has(.ex-shape-read))')].map(a => a.getAttribute('href')).filter(h => h.startsWith('exercises/')).map(h => h.slice(10))`))
    if (!ids.includes(id)) ids.push(id);
}
if (only) ids = ids.filter(i => i === only);
console.log(`${ids.length} exercises`);

const state = `(() => {
  const m = document.querySelector('.sheet-music');
  const readouts = Object.fromEntries([...document.querySelectorAll('.play-bar .mu-readout')].map(r => [r.querySelector('.mu-readout-label')?.textContent.trim(), r.querySelector('.mu-readout-value')?.textContent.trim()]));
  return { loop: document.querySelector('.play-loop .play-mode-option')?.getAttribute('aria-checked') ?? null, mode: [...document.querySelectorAll('.play-mode:not(.play-loop) .play-mode-option')].find(b => b.getAttribute('aria-checked') === 'true')?.textContent.trim() ?? null,
    bars: m?.querySelectorAll('g.measure').length ?? -1, notes: m?.querySelectorAll('g.note').length ?? -1, running: !!document.querySelector('.play-bar .mu-btn') && [...document.querySelectorAll('.play-bar .mu-btn')].some(b => b.textContent.trim() === 'Stop'),
    readouts, summary: [...document.querySelectorAll('.sum-point, .summary-words li')].map(e => e.textContent.replace(/\\s+/g, ' ').trim()) };
})()`;
const mode = async name => { await page.evaluate(`[...document.querySelectorAll('.play-mode:not(.play-loop) .play-mode-option')].find(b => b.textContent.trim() === ${JSON.stringify(name)})?.click()`); await sleep(250); };
const loop = async on => { if (((await page.evaluate(state)).loop === 'true') !== on) { await page.tapSel('.play-loop .play-mode-option'); await sleep(500); } };
const through = async on => { const now = await page.evaluate(`document.querySelector('.exercise-through')?.getAttribute('aria-pressed')`); if (now !== null && (now === 'true') !== on) { await page.tapSel('.exercise-through'); await sleep(700); } };
const tempo = async bpm => {
  await page.evaluate(`document.querySelector('.play-tempo-figure')?.click()`);
  await sleep(250);
  await page.evaluate(`(() => { const e = document.querySelector('.tempo-entry'); if (!e) return; e.value = '${bpm}'; e.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  await sleep(250);
  await page.evaluate(`[...document.querySelectorAll('.tempo-pop .mu-btn')].find(b => b.textContent.trim() === 'Done')?.click()`);
  await sleep(250);
};
const start = async () => { await page.tapSel('.play-bar [data-piano=go]'); await sleep(700); };
let bad = 0;
const fail = async (id, what) => { bad++; console.log(`  BAD ${id}: ${what}`); await page.shot(`${out}/bad-${id}-${bad}.png`); };

for (const id of ids) {
  page.logs.length = 0;
  if (variation) console.log(await page.get(`seed/exercise/${id}?variation=${encodeURIComponent(variation)}${hands ? `&hands=${hands}` : ''}`));
  await page.go(`exercises/${id}`);
  try { await page.until(`document.querySelector('.sheet-music svg')`, 10000); } catch { await fail(id, 'no sheet drawn'); continue; }
  await loop(false);
  await through(false);
  const opened = await page.evaluate(state);
  if (opened.notes <= 0) await fail(id, `no notes on the sheet (${opened.notes})`);

  // In time, once through: all at once, every note right.
  await mode('In time');
  await start();
  const performed = await page.get('keys/perform?right=1&spread=4&lean=0');
  await sleep(900);
  const timed = await page.evaluate(state);
  const notesLine = timed.summary.find(s => s.startsWith('Notes')) ?? '';
  if (!/All \d+ right/.test(notesLine)) await fail(id, `In time: ${performed}; ${JSON.stringify(timed.summary)}`);

  // Wait for me, once through: each step answered.
  await mode('Wait for me');
  await start();
  let steps = 0;
  // Each step answered once it's the one waited for: the cursor has moved on from the step before (or the run has ended).
  const cursor = `(() => { const c = document.querySelector('.sheet-cursor'); return !c || c.hidden ? 'gone' : c.style.transform; })()`;
  let before = '';
  for (; steps < 900; steps++) {
    let at = '';
    for (let tries = 0; tries < 80; tries++) { at = await page.evaluate(cursor); if (at !== before) break; await sleep(15); }
    if (at === 'gone' || at === before) break;
    before = at;
    const said = await page.get('keys/answer?right=true');
    if (said !== 'answered') break;
  }
  await sleep(700);
  const waited = await page.evaluate(state);
  const first = waited.summary.find(s => /right first time/.test(s)) ?? '';
  const m = first.match(/(\d+) of (\d+) notes right first time/);
  if (!m || m[1] !== m[2] || waited.running) await fail(id, `Wait for me after ${steps} steps: ${JSON.stringify(waited.summary)} running ${waited.running}`);

  // Looped, In time: the stand-in plays as it's due; two passes in, Stop.
  await mode('In time');
  await tempo(240);
  await loop(true);
  const looped = await page.evaluate(state);
  await start();
  await page.get('keys/perform?live=true&right=1&spread=4&lean=0');
  let passes = '';
  try { passes = await page.until(`(() => { const r = [...document.querySelectorAll('.play-bar .mu-readout')].find(r => r.querySelector('.mu-readout-label')?.textContent.trim() === 'Pass'); const n = Number(r?.querySelector('.mu-readout-value')?.textContent); return n >= 3 ? String(n) : null; })()`, 150000, 100); }
  catch { await fail(id, `looped: never reached a third pass (${JSON.stringify((await page.evaluate(state)).readouts)})`); }
  const during = await page.evaluate(state);
  await page.tap('Stop');
  await sleep(1100);
  const after = await page.evaluate(state);
  const passLine = after.summary.find(s => s.startsWith('Passes')) ?? '';
  if (!/passes? of/.test(passLine)) await fail(id, `looped: no passes in the summary: ${JSON.stringify(after.summary)}`);
  await loop(false);
  // Through the keys: one long piece, drawn and played once In time.
  await through(true);
  const all = await page.evaluate(state);
  let throughLine = '';
  if (all.bars <= opened.bars || all.notes <= opened.notes) await fail(id, `through the keys: ${all.bars} bars, ${all.notes} notes`);
  else {
    await tempo(120);
    await start();
    await page.get('keys/perform?right=1&spread=4&lean=0');
    await sleep(1200);
    throughLine = ((await page.evaluate(state)).summary.find(s => s.startsWith('Notes')) ?? '').replace('Notes', '');
    if (!/All \d+ right/.test(throughLine)) await fail(id, `through the keys In time: ${throughLine}`);
  }
  await through(false);
  // The engraver saying a line is squeezed (a bar of sixteenths with a finger on each, at this width) is noted, not counted:
  // the line still reads. Anything else it or the page says is a problem.
  const squeezed = /Justification is highly compressed|System full width|ustifiable width/;
  const logs = page.logs.filter(l => !l.includes('No header') && !squeezed.test(l));
  const tight = page.logs.find(l => /highly compressed/.test(l))?.match(/: (0\.\d+)/)?.[1];
  if (logs.length) await fail(id, `console: ${logs.slice(0, 3).join(' || ')}`);
  console.log(`${id.padEnd(18)} bars ${String(opened.bars).padStart(2)} notes ${String(opened.notes).padStart(3)} | in time: ${notesLine.replace('Notes', '')} | wait: ${m ? m[1] + '/' + m[2] : '?'} in ${steps} steps | loop: ${looped.bars} bars shown, last pass ${during.readouts['Last pass'] ?? '-'}; ${passLine.replace('Passes', '').slice(0, 44)} | through: ${all.bars} bars ${throughLine}${tight ? ` | a line squeezed to ${tight}` : ''}`);
}
console.log(`${ids.length} exercises, ${bad} problems`);
page.close();
