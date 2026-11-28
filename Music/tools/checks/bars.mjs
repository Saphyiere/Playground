// Choosing bars on a piece's music where marks run on over bar lines, each drawn by Verovio with the bar it starts in: an 8va
// over bars 21 to 24 (the owner's case, 28 Nov 2026) and one carried on to the next line, an 8vb, slurs, hairpins, a pedal line
// and a trill's line. For each mark, with real mouse taps and drags: a tap on the bar after its first, and one on the mark itself
// over a bar under it, chooses that bar alone; the loop of its first bar, its end dragged a bar on, two bars on and onto the next
// line, loops just those bars; and the strip under the piece's bar says what the music lights. A line for each mark, BAD with a
// picture for anything else. The piece is written here as MusicXML and imported each time. See README.md.
// node Music/tools/checks/bars.mjs <folder for pictures> [studio|vinyl] [width] [height]
import { mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { open, sleep } from './drive.mjs';

const [out = 'artifacts/checks/bars', look = 'studio', width = '1280', height = '800'] = process.argv.slice(2);

// The marks, by their first bar: what each is, and where the piece below draws it.
const marks = [[2, 'a slur over bars 2-4'], [6, 'an 8va over bars 6-12, onto the next line'], [9, 'a hairpin under bars 9-11'],
  [13, 'an 8vb under bars 13-16'], [17, "a trill's line over bars 17-19"], [21, 'an 8va over bars 21-24'],
  [25, 'a pedal line under bars 25-28'], [29, 'a hairpin under bars 29-31']];

// A piano piece of 32 bars in 4/4 with those marks; pitches are sounding pitches, as MuseScore writes them under an octave line.
function piece() {
  const steps = 'CDEFGAB';
  const pitch = (step, octave) => `<pitch><step>${steps[step % 7]}</step><octave>${octave}</octave></pitch>`;
  const note = (p, duration, type, staff, voice, notations = '', tie = null) => `<note>${p}<duration>${duration}</duration>${tie ? `<tie type="${tie}"/>` : ''}`
    + `<voice>${voice}</voice><type>${type}</type><staff>${staff}</staff>${notations || tie ? `<notations>${tie ? `<tied type="${tie}"/>` : ''}${notations}</notations>` : ''}</note>`;
  const direction = (inner, staff, placement) => `<direction placement="${placement}"><direction-type>${inner}</direction-type><staff>${staff}</staff></direction>`;
  const shift = (type, staff) => direction(`<octave-shift type="${type}" size="8"/>`, staff, type === 'down' ? 'above' : 'below');
  const bars = [];
  for (let bar = 1; bar <= 32; bar++) {
    let xml = bar === 1 ? '<attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time><staves>2</staves>'
      + '<clef number="1"><sign>G</sign><line>2</line></clef><clef number="2"><sign>F</sign><line>4</line></clef></attributes>' : '';
    const up8 = (bar >= 6 && bar <= 12) || (bar >= 21 && bar <= 24);
    if (bar === 6 || bar === 21) xml += shift('down', 1);
    if (bar === 9) xml += direction('<wedge type="crescendo"/>', 1, 'below');
    if (bar === 29) xml += direction('<wedge type="diminuendo"/>', 1, 'below');
    const base = (bar * 2) % 5;
    for (let beat = 0; beat < 4; beat++) {
      let notations = '';
      if (beat === 0 && (bar === 2 || bar === 26)) notations += '<slur type="start" number="1"/>';
      if (beat === 3 && (bar === 4 || bar === 28)) notations += '<slur type="stop" number="1"/>';
      if (beat === 0 && bar === 17) notations += '<ornaments><trill-mark/><wavy-line type="start" number="1"/></ornaments>';
      if (beat === 3 && bar === 19) notations += '<ornaments><wavy-line type="stop" number="1"/></ornaments>';
      let [step, octave, tie] = [base + beat, up8 ? 6 : base + beat >= 5 ? 5 : 4, null];
      if (bar === 19 && beat === 3) [step, octave, tie] = [4, 5, 'start'];
      if (bar === 20 && beat === 0) [step, octave, tie] = [4, 5, 'stop'];
      xml += note(pitch(step, octave), 1, 'quarter', 1, 1, notations, tie);
    }
    if (bar === 11 || bar === 31) xml += direction('<wedge type="stop"/>', 1, 'below');
    if (bar === 12 || bar === 24) xml += shift('stop', 1);
    xml += '<backup><duration>4</duration></backup>';
    if (bar === 13) xml += shift('up', 2);
    if (bar === 25) xml += direction('<pedal type="start" line="yes"/>', 2, 'below');
    for (const half of [0, 1]) xml += note(pitch(bar + half * 4, bar >= 13 && bar <= 16 ? 1 : 2), 2, 'half', 2, 5);
    if (bar === 16) xml += shift('stop', 2);
    if (bar === 28) xml += direction('<pedal type="stop" line="yes"/>', 2, 'below');
    if (bar === 32) xml += '<barline location="right"><bar-style>light-heavy</bar-style></barline>';
    bars.push(`<measure number="${bar}">${xml}</measure>`);
  }
  return '<?xml version="1.0" encoding="UTF-8"?>\n<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">\n'
    + '<score-partwise version="4.0"><work><work-title>Octave lines</work-title></work><identification><creator type="composer">Prima check</creator></identification>'
    + `<part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1">${bars.join('')}</part></score-partwise>\n`;
}

const page = await open({ width: +width, height: +height });
mkdirSync(out, { recursive: true });
const file = join(tmpdir(), 'prima-checks', 'octave-lines.musicxml');
mkdirSync(join(tmpdir(), 'prima-checks'), { recursive: true });
writeFileSync(file, piece());
await page.get('seed/fresh');
await page.go(`settings?tab=look`);
await page.tapSel(`.look-option[data-theme=${look}]`);
await page.go('library');
await sleep(1500);
console.log(await page.get(`files/next?path=${encodeURIComponent(file)}`));
await page.tap('Import files');
await page.until(`location.pathname.includes('/library/import-')`, 30000);
const id = (await page.evaluate('location.pathname')).split('/').pop();
console.log(`imported as library/${id}, ${look} at ${width} x ${height}`);

// A bar's staff lines and staves, in the viewport's px, and its line.
const bar = n => page.evaluate(`(() => {
  const m = document.querySelectorAll('.sheet-music .measure[data-id]')[${n - 1}];
  if (!m) return null;
  const staves = [...m.querySelectorAll(':scope > .staff')].map(s => [...s.querySelectorAll(':scope > path')].map(p => p.getBoundingClientRect()));
  const lines = staves.flat();
  return { left: Math.min(...lines.map(r => r.left)), right: Math.max(...lines.map(r => r.right)),
    gap: (Math.max(...staves[0].map(r => r.bottom)) + Math.min(...(staves[1] ?? staves[0]).map(r => r.top))) / 2,
    line: [...document.querySelectorAll('.sheet-music .system')].indexOf(m.closest('.system')) };
})()`);
// What's lit: the tab's words, the bars the bands cover (by their staff lines' middles), the strip's loop.
const lit = () => page.evaluate(`(() => {
  const bars = [...document.querySelectorAll('.sheet-music .measure[data-id]')].map((m, i) => {
    const ls = [...m.querySelectorAll(':scope > .staff > path')].map(p => p.getBoundingClientRect());
    return { n: i + 1, x: (Math.min(...ls.map(r => r.left)) + Math.max(...ls.map(r => r.right))) / 2, y: (Math.min(...ls.map(r => r.top)) + Math.max(...ls.map(r => r.bottom))) / 2 };
  });
  const covered = [...document.querySelectorAll('.bar-marks .bar-band')].map(b => b.getBoundingClientRect())
    .flatMap(r => bars.filter(b => b.x >= r.left && b.x <= r.right && b.y >= r.top && b.y <= r.bottom).map(b => b.n));
  const tab = document.querySelector('.bar-marks .bar-tab');
  const cells = document.querySelector('.bar-strip .bar-cells'), count = cells?.querySelectorAll('.bar-cell').length ?? 0;
  const loop = cells?.querySelector('.bar-strip-loop');
  const from = loop && !loop.hidden ? Math.round(parseFloat(loop.style.left) / 100 * count + 1) : null;
  return { tab: tab && !tab.hidden ? tab.querySelector('.bar-tab-label').textContent.trim() : null,
    lit: covered.length ? [Math.min(...covered), Math.max(...covered), covered.length] : null,
    strip: from == null ? null : [from, Math.round(from + parseFloat(loop.style.width) / 100 * count - 1)] };
})()`);
const mouse = (type, x, y) => page.send('Input.dispatchMouseEvent', { type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1 });
const tap = async (x, y) => {
  for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased']) await mouse(type, x, y);
  await sleep(700);
};
const drag = async (to, end = 'to') => {
  const at = await page.evaluate(`(() => { const e = document.querySelector('.bar-marks .bar-handle[data-end=${end}]'); if (!e || e.hidden) return null; const r = e.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()`);
  if (!at) return false;
  await mouse('mouseMoved', ...at);
  await mouse('mousePressed', ...at);
  for (let i = 1; i <= 12; i++) { await mouse('mouseMoved', at[0] + (to[0] - at[0]) * i / 12, at[1] + (to[1] - at[1]) * i / 12); await sleep(30); }
  await mouse('mouseReleased', ...to);
  await sleep(900);
  return true;
};
// Nothing chosen: the tab's Clear (its tab can be off the screen, so not by a finger).
const clear = async () => {
  if (await page.evaluate(`(() => { const c = document.querySelector('.bar-marks .bar-tab:not([hidden]) .bar-tab-clear'); c?.click(); return !!c; })()`)) await sleep(600);
};

// The piece in Practice, once: every mark is checked in the one session.
await page.go(`library/${id}`);
await sleep(600);
await page.tap('Practice');
await page.until(`document.querySelectorAll('.sheet-music .measure').length > 0 && document.querySelector('.bar-strip .bar-cell')`, 30000);
await sleep(1500);
let bad = 0;
const all = await page.evaluate(`document.querySelectorAll('.sheet-music .measure[data-id]').length`);
for (const [first, what] of marks) {
  const wrong = [];
  // Each time, what's lit is checked against what was meant: the tab, the bands and (for a loop) the strip, all one choice.
  const expect = async (step, from, to) => {
    const s = await lit();
    const words = to == null ? `From bar ${from}` : from === to ? `Loop bar ${from}` : `Loop bars ${from}–${to}`;
    const lastLit = to ?? from;
    if (s.tab !== words || !s.lit || s.lit[0] !== from || s.lit[1] !== lastLit || (to != null && (s.strip?.[0] !== from || s.strip?.[1] !== to))) {
      wrong.push(`${step}: meant ${words}, the tab says ${s.tab ?? 'nothing'}, lit ${s.lit ? `${s.lit[0]}-${s.lit[1]}` : 'nothing'}${to != null ? `, the strip ${s.strip ? s.strip.join('-') : 'nothing'}` : ''}`);
      await page.shot(`${out}/${look}-${width}x${height}-bar${first}-${step.replace(/\W+/g, '-')}.png`);
    }
  };
  // Nothing chosen, the mark's line a little under the frame's top.
  await clear();
  const start = await bar(first);
  await page.evaluate(`(() => { const f = document.querySelector('.piece-frame'); f.scrollTop += ${start.gap} - f.getBoundingClientRect().top - 260; })()`);
  await sleep(600);
  // A tap on the bar after the first.
  let b = await bar(first + 1);
  await tap((b.left + b.right) / 2, b.gap);
  await expect(`a tap on bar ${first + 1}`, first + 1, null);
  // A tap on the mark's own stroke, where the browser finds it under a finger, over a bar under it after its first.
  await clear();
  const on = await page.evaluate(`(() => {
    const bars = document.querySelectorAll('.sheet-music .measure[data-id]');
    const mark = bars[${first - 1}].querySelector(':scope > .octave, :scope > .slur, :scope > .hairpin, :scope > .pedal, :scope > .trill');
    if (!mark) return null;
    const r = mark.getBoundingClientRect();
    for (const n of [${first + 2}, ${first + 1}]) {
      const ls = [...(bars[n - 1]?.querySelectorAll(':scope > .staff > path') ?? [])].map(p => p.getBoundingClientRect());
      if (ls.length === 0) continue;
      const left = Math.min(...ls.map(l => l.left)) + 3, right = Math.max(...ls.map(l => l.right)) - 3;
      for (let x = Math.max(left, r.left); x <= Math.min(right, r.right); x++) for (let y = Math.floor(r.top) - 1; y <= Math.ceil(r.bottom) + 1; y++) {
        const e = document.elementFromPoint(x, y);
        if (e && mark.contains(e)) return { x, y, n };
      }
    }
    return null;
  })()`);
  if (on) {
    await tap(on.x, on.y);
    await expect(`a tap on the mark over bar ${on.n}`, on.n, null);
  }
  // The first bar looped from its tab, and the loop's end dragged.
  b = await bar(first);
  await tap((b.left + b.right) / 2, b.gap);
  await expect(`a tap on bar ${first}`, first, null);
  if (!await page.evaluate(`(() => { const k = document.querySelector('.bar-marks .bar-tab:not([hidden]) .bar-tab-key'); k?.click(); return !!k; })()`)) {
    console.log(`BAD  bar ${first} ${what}\n       no tab to loop bar ${first} from${wrong.map(w => `\n       ${w}`).join('')}`);
    bad++;
    continue;
  }
  await sleep(700);
  await expect('Loop on its tab', first, first);
  const startLine = b.line;
  let next = null;
  for (let n = first + 1; n <= all && next == null; n++) if ((await bar(n)).line > startLine) next = Math.min(n + 1, all);
  for (const to of [first + 1, first + 2, next].filter(n => n != null && n <= all)) {
    b = await bar(to);
    if (!await drag([(b.left + b.right) / 2, b.gap])) { wrong.push(`no handle to drag to bar ${to}`); break; }
    await expect(`its end dragged to bar ${to}`, first, to);
  }
  bad += wrong.length ? 1 : 0;
  console.log(`${wrong.length ? 'BAD' : 'ok '}  bar ${String(first).padEnd(3)} ${what}${on ? '' : ' (no point on the mark itself to tap)'}${wrong.map(w => `\n       ${w}`).join('')}`);
}
console.log(bad ? `${bad} of ${marks.length} marks BAD` : `all ${marks.length} marks ok`);
if (page.logs.length) console.log(page.logs.filter(l => !l.includes('No header found')).join('\n'));
page.close();
process.exit(bad ? 1 : 0);
