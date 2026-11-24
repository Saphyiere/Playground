// The technique drills (Scales, Arpeggios, Finger independence) played by the preview's stand-in piano: a run In time, a looped
// run and a run through the keys; then reading drills with their Loop on: a set played right, and the next begins by itself.
// A line for each with what it came to, and pictures. See README.md.
// node Music/tools/checks/drills.mjs <folder for pictures>
import { open, sleep } from './drive.mjs';

const out = process.argv[2] ?? 'artifacts/checks/drills';
const page = await open({ width: 1557, height: 973, dpr: 1 });
console.log(await page.get('keys/connect'));
const state = `(() => {
  const m = document.querySelector('.sheet-music');
  const readouts = Object.fromEntries([...document.querySelectorAll('.play-bar .mu-readout, .reading-bar .mu-readout')].map(r => [r.querySelector('.mu-readout-label')?.textContent.trim(), r.querySelector('.mu-readout-value')?.textContent.trim()]));
  return { loop: document.querySelector('.play-loop .play-mode-option')?.getAttribute('aria-checked') ?? null, through: document.querySelector('.exercise-through')?.getAttribute('aria-pressed') ?? null,
    bars: m?.querySelectorAll('g.measure').length ?? -1, notes: m?.querySelectorAll('g.note').length ?? -1, readouts,
    figures: [...document.querySelectorAll('.summary-figures .mu-readout')].map(r => r.textContent.replace(/\\s+/g, ' ').trim()),
    summary: [...document.querySelectorAll('.sum-point, .summary-words li')].map(e => e.textContent.replace(/\\s+/g, ' ').trim()).slice(0, 4), go: document.querySelector('[data-piano=go]')?.textContent.trim() ?? null };
})()`;
const mode = async name => { await page.evaluate(`[...document.querySelectorAll('.play-mode:not(.play-loop) .play-mode-option')].find(b => b.textContent.trim() === ${JSON.stringify(name)})?.click()`); await sleep(300); };
const loop = async on => { if (((await page.evaluate(state)).loop === 'true') !== on) { await page.tapSel('.play-loop .play-mode-option'); await sleep(600); } };
const through = async on => { const now = (await page.evaluate(state)).through; if (now !== null && (now === 'true') !== on) { await page.tapSel('.exercise-through'); await sleep(900); } };
const tempo = async bpm => {
  await page.evaluate(`document.querySelector('.play-tempo-figure')?.click()`);
  await sleep(250);
  await page.evaluate(`(() => { const e = document.querySelector('.tempo-entry'); if (!e) return; e.value = '${bpm}'; e.dispatchEvent(new Event('change', { bubbles: true })); })()`);
  await sleep(250);
  await page.evaluate(`[...document.querySelectorAll('.tempo-pop .mu-btn')].find(b => b.textContent.trim() === 'Done')?.click()`);
  await sleep(250);
};
const start = async () => { await page.tapSel('.play-bar [data-piano=go]'); await sleep(800); };
const passes = n => page.until(`(() => { const r = [...document.querySelectorAll('.play-bar .mu-readout')].find(r => r.querySelector('.mu-readout-label')?.textContent.trim() === 'Pass'); return Number(r?.querySelector('.mu-readout-value')?.textContent) >= ${n}; })()`, 120000, 100);

for (const drill of ['scales', 'arpeggios', 'independence']) {
  page.logs.length = 0;
  await page.go(`drills/${drill}`);
  await page.until(`document.querySelector('.sheet-music svg')`, 10000);
  await loop(false); await through(false);
  const opened = await page.evaluate(state);
  // In time, once: evenness is measured.
  await mode('In time');
  await tempo(100);
  await start();
  await page.get('keys/perform?right=1&spread=3&lean=0');
  await sleep(1200);
  const once = await page.evaluate(state);
  // Looped: two passes, Stop; the evenness is the last pass's.
  await loop(true);
  const looped = await page.evaluate(state);
  await start();
  await page.get('keys/perform?live=true&right=1&spread=3&lean=0');
  await passes(3);
  const during = await page.evaluate(state);
  await page.tap('Stop');
  await sleep(1300);
  const after = await page.evaluate(state);
  await page.shot(`${out}/${drill}-looped.png`);
  await loop(false);
  // Through the keys: one piece, played once.
  await through(true);
  const all = await page.evaluate(state);
  await page.shot(`${out}/${drill}-through.png`);
  await start();
  await page.get('keys/perform?right=1&spread=3&lean=0');
  await sleep(1500);
  const gone = await page.evaluate(state);
  await through(false);
  const logs = page.logs.filter(l => !l.includes('No header'));
  console.log(`${drill}: ${opened.bars} bars ${opened.notes} notes | once: ${JSON.stringify(once.figures)} | loop: ${looped.bars} bars; pass ${during.readouts.Pass}, last ${during.readouts['Last pass']}; after ${JSON.stringify(after.figures)} run key "${after.go}" | through: ${all.bars} bars ${all.notes} notes; ${JSON.stringify(gone.summary.slice(0, 2))}${logs.length ? ' LOGS ' + logs.slice(0, 2).join(' || ') : ''}`);
}

// A reading drill with its Loop on: a set played right, and the next begins by itself.
for (const drill of ['intervals', 'chords', 'keys']) {
  page.logs.length = 0;
  await page.go(`drills/${drill}`);
  await page.until(`document.querySelector('.sheet-music svg')`, 10000);
  if ((await page.evaluate(`document.querySelector('.drill-loop .play-mode-option')?.getAttribute('aria-checked')`)) !== 'true') { await page.tapSel('.drill-loop .play-mode-option'); await sleep(400); }
  const seen = [];
  for (let i = 0; i < 12; i++) {
    const before = (await page.evaluate(state)).readouts.Item;
    if (seen.at(-1) !== before) seen.push(before);
    const said = await page.get('keys/answer?right=true');
    if (said !== 'answered') { seen.push(said); break; }
    try { await page.until(`[...document.querySelectorAll('.reading-bar .mu-readout')].find(r => r.querySelector('.mu-readout-label')?.textContent.trim() === 'Item')?.querySelector('.mu-readout-value')?.textContent.trim() !== ${JSON.stringify(before)}`, 4000, 80); } catch { seen.push('stuck'); break; }
    // The next item is listened for once its music is painted.
    await sleep(500);
  }
  const end = await page.evaluate(state);
  await page.shot(`${out}/drill-${drill}-loop.png`);
  console.log(`${drill} looped: items ${seen.join(', ')} | now ${JSON.stringify(end.readouts)} | another-set key: ${end.go}`);
  await page.tapSel('.drill-loop .play-mode-option');
  await sleep(300);
}
page.close();
