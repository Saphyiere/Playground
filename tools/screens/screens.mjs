// Drives headless Chromium over the DevTools protocol, for checking an app's screens from a cloud session: go to pages, tap and
// type like a finger would, read text, and take screenshots. The page is a preview host's (e.g. Workout/tools/AdaptiveWorkout.Preview).
//
// usage: node tools/screens/screens.mjs steps.json out-dir [base-url, default http://127.0.0.1:5199/]
//
// steps.json is a list of steps, each an object with one of:
//   {go: "path"}                     load base-url + path
//   {size: [w, h]}                   the viewport, in CSS pixels (390 x 844 to start, a phone)
//   {tap: "text"}                    click the button or link with this text, aria-label or name ({within: selector} narrows it)
//   {tapSel: "selector"}             click the first element matching a selector
//   {realTap: "selector or text"}    a finger-like tap at the element's center, so a field loses focus first, as on a phone
//   {type: "selector", value: "…"}   focus a field, select what's there and type over it
//   {fill: "selector", value: "…"}   set a field's value and fire change
//   {select: "selector", option: "text"}, {enterIn: "selector"}
//   {wait: ms}, {eval: "js expression"}, {text: "selector"} (prints the matches' text)
//   {shot: "name", height?: px}      a PNG at out-dir/name.png, the page that tall
// It uses the Chromium that comes with the machine, not Playwright.
import { spawn } from 'node:child_process';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';

const [stepsFile, shots, base = 'http://127.0.0.1:5199/'] = process.argv.slice(2);
if (!stepsFile || !shots) throw new Error('usage: node tools/screens/screens.mjs steps.json out-dir [base-url]');
const steps = JSON.parse(readFileSync(stepsFile, 'utf8'));
mkdirSync(shots, { recursive: true });
const shell = process.env.CHROME ?? `/opt/pw-browsers/${readdirSync('/opt/pw-browsers').filter(d => d.startsWith('chromium_headless_shell-')).sort().at(-1)}/chrome-linux/headless_shell`;
const browser = spawn(shell, ['--no-sandbox', '--disable-gpu', '--hide-scrollbars', '--remote-debugging-port=9333', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let size = { width: 390, height: 844 };

let targets;
for (let i = 0; i < 50; i++) {
  try { targets = await (await fetch('http://127.0.0.1:9333/json')).json(); if (targets.length) break; } catch { }
  await sleep(200);
}
const page = targets.find(t => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.onopen = r);
let id = 0;
const pending = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const evaluate = async expr => (await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true })).result?.result?.value;

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { ...size, deviceScaleFactor: 2, mobile: true });

for (const step of steps) {
  // Pretends to be a phone drawing behind its status and navigation bars (env(safe-area-inset-*)).
  if (step.insets) console.log('insets:', JSON.stringify((await send('Emulation.setSafeAreaInsetsOverride', { insets: step.insets })).error ?? 'ok'));
  if (step.measure) console.log(`page height ${await evaluate('document.scrollingElement.scrollHeight')} vs viewport ${await evaluate('innerHeight')}, top inset div ${await evaluate("document.querySelector('.status-bar-safe-area')?.getBoundingClientRect().height")}`);
  if (step.size) { size = { width: step.size[0], height: step.size[1] }; await send('Emulation.setDeviceMetricsOverride', { ...size, deviceScaleFactor: 2, mobile: true }); }
  if ('go' in step) { await send('Page.navigate', { url: base + step.go }); await sleep(2500); }
  if (step.tap) {
    const ok = await evaluate(`(() => {
      const scope = ${JSON.stringify(step.within ?? null)} ? document.querySelector(${JSON.stringify(step.within ?? '')}) : document;
      const el = [...scope.querySelectorAll('button, a')].find(b => b.textContent.trim() === ${JSON.stringify(step.tap)} || b.getAttribute('aria-label') === ${JSON.stringify(step.tap)} || (b.querySelector('.body.strong') ?? b.querySelector('.name'))?.textContent.trim() === ${JSON.stringify(step.tap)});
      if (!el) return false; el.click(); return true; })()`);
    console.log(`tap "${step.tap}": ${ok ? 'ok' : 'NOT FOUND'}`);
    await sleep(700);
  }
  if (step.tapSel) {
    const ok = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(step.tapSel)}); if (!el) return false; el.click(); return true; })()`);
    console.log(`tap ${step.tapSel}: ${ok ? 'ok' : 'NOT FOUND'}`);
    await sleep(700);
  }
  if (step.wait) await sleep(step.wait);
  if (step.eval) console.log(`eval: ${JSON.stringify(await evaluate(step.eval))}`);
  if (step.select) {
    const ok = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(step.select)}); const option = [...(el?.options ?? [])].find(o => o.text === ${JSON.stringify(step.option)}); if (!option) return false; el.value = option.value; el.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`);
    console.log(`select ${step.option} in ${step.select}: ${ok ? 'ok' : 'NOT FOUND'}`);
    await sleep(400);
  }
  // Real typing: focus a field, select what's there, and type over it like a keyboard would.
  if (step.type) {
    await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(step.type)}); el.scrollIntoView({ block: 'center' }); el.focus(); el.select?.(); })()`);
    await send('Input.insertText', { text: step.value });
    console.log(`typed ${step.value} into ${step.type}`);
    await sleep(200);
  }
  // A real finger-like tap at the element's center, so focus leaves the field first, as on a phone.
  if (step.realTap) {
    const rect = await evaluate(`(() => { const el = [...document.querySelectorAll('button, a, input')].find(b => b.matches?.(${JSON.stringify(step.realTap)}) || b.textContent.trim() === ${JSON.stringify(step.realTap)}); if (!el) return null; el.scrollIntoView({ block: 'center' }); const r = el.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()`);
    if (rect) {
      for (const type of ['mousePressed', 'mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: rect.x, y: rect.y, button: 'left', clickCount: 1 });
    }
    console.log(`real tap ${step.realTap}: ${rect ? 'ok' : 'NOT FOUND'}`);
    await sleep(800);
  }
  if (step.enterIn) {
    await evaluate(`document.querySelector(${JSON.stringify(step.enterIn)})?.focus()`);
    for (const type of ['keyDown', 'char', 'keyUp']) await send('Input.dispatchKeyEvent', { type, key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13, text: '\r' });
    console.log(`enter in ${step.enterIn}`);
    await sleep(700);
  }
  if (step.fill) {
    const ok = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(step.fill)}); if (!el) return false; el.value = ${JSON.stringify(step.value)}; el.dispatchEvent(new Event('change', { bubbles: true })); return true; })()`);
    console.log(`fill ${step.fill} = ${step.value}: ${ok ? 'ok' : 'NOT FOUND'}`);
    await sleep(500);
  }
  // Taps a button by its text and reports how long until an element matching `until` is on the page.
  if (step.time) {
    const ms = await evaluate(`new Promise(done => {
      const el = [...document.querySelectorAll('button, a')].find(b => b.textContent.trim() === ${JSON.stringify(step.time)});
      if (!el) return done(-1);
      const old = document.querySelector(${JSON.stringify(step.until)});
      const t0 = performance.now(); el.click();
      const check = () => { const now = document.querySelector(${JSON.stringify(step.until)}); return now && now !== old ? done(performance.now() - t0) : setTimeout(check, 0); };
      check(); })`);
    console.log(`"${step.time}" -> ${step.until}: ${ms.toFixed(1)} ms`);
    await sleep(800);
  }
  if (step.debug) console.log(await evaluate(`location.href + " " + document.body?.innerHTML.length + " " + document.title`), JSON.stringify(await send("Runtime.evaluate", { expression: "1+1", returnByValue: true })));
  if (step.text) console.log(`text ${step.text}: ${await evaluate(`[...document.querySelectorAll(${JSON.stringify(step.text)})].map(e => e.textContent.replace(/\\s+/g, ' ').trim()).join(' | ')`)}`);
  if (step.shot) {
    const height = step.height ?? size.height;
    await send('Emulation.setDeviceMetricsOverride', { width: size.width, height, deviceScaleFactor: 2, mobile: true });
    await sleep(300);
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(`${shots}/${step.shot}.png`, Buffer.from(shot.result.data, 'base64'));
    await send('Emulation.setDeviceMetricsOverride', { ...size, deviceScaleFactor: 2, mobile: true });
    console.log(`shot ${step.shot}`);
  }
}
ws.close();
browser.kill();
