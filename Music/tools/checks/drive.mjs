// A driver for Prima's preview in a headless Chrome that's left running (its DevTools on SHOTS_PORT, default 9341; README.md
// has how to start one): a script imports `open` and gets a page to go to, tap, read, wait on and photograph. The preview is at
// BASE (default http://127.0.0.1:5299/).
import { mkdirSync, writeFileSync } from 'node:fs';

export const base = process.env.BASE ?? 'http://127.0.0.1:5299/';
const port = process.env.SHOTS_PORT ?? '9341';
export const sleep = ms => new Promise(r => setTimeout(r, ms));

export async function open({ width = 1280, height = 800, dpr = 1, fresh = false } = {}) {
  let targets;
  for (let i = 0; i < 50; i++) {
    try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (targets.length) break; } catch { }
    await sleep(200);
  }
  let target = targets.find(t => t.type === 'page');
  if (fresh || !target) target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let id = 0;
  const pending = new Map();
  const logs = [];
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
    else if (m.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(m.params.type)) logs.push(`${m.params.type}: ${m.params.args.map(a => a.value ?? a.description ?? '').join(' ').slice(0, 600)}`);
    else if (m.method === 'Runtime.exceptionThrown') logs.push(`exception: ${(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text).slice(0, 600)}`);
  };
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async expr => {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.result?.exceptionDetails) throw new Error(`eval failed: ${r.result.exceptionDetails.exception?.description ?? r.result.exceptionDetails.text}\n  in: ${expr.slice(0, 200)}`);
    return r.result?.result?.value;
  };
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });
  let metrics = { width, height, deviceScaleFactor: dpr, mobile: true, screenWidth: width, screenHeight: height };
  await send('Emulation.setDeviceMetricsOverride', metrics);
  await send('Emulation.setTouchEmulationEnabled', { enabled: false });

  const page = {
    send, evaluate, logs,
    async size(w, h, scale = metrics.deviceScaleFactor) {
      metrics = { width: w, height: h, deviceScaleFactor: scale, mobile: true, screenWidth: w, screenHeight: h };
      await send('Emulation.setDeviceMetricsOverride', metrics);
      await sleep(400);
    },
    // Waits until an expression is truthy; throws with what it was after `timeout` ms.
    async until(expr, timeout = 15000, every = 100) {
      const t0 = Date.now();
      for (;;) {
        const v = await evaluate(`(() => { try { return (${expr}); } catch (e) { return false; } })()`);
        if (v) return v;
        if (Date.now() - t0 > timeout) throw new Error(`timed out waiting for: ${expr}`);
        await sleep(every);
      }
    },
    // Loads a page of the preview and waits for Blazor to have it.
    async go(path) {
      await send('Page.navigate', { url: base + path });
      await page.until(`window.Blazor && document.querySelector('.shell-main, .shell, main') && document.readyState === 'complete'`, 30000);
      await sleep(600);
    },
    // Moves within the app, as a link does.
    async nav(path) {
      await evaluate(`Blazor.navigateTo(${JSON.stringify(path)})`);
      await sleep(500);
    },
    async tap(text, within = null) {
      const ok = await evaluate(`(() => {
        const scope = ${JSON.stringify(within)} ? document.querySelector(${JSON.stringify(within ?? '')}) : document;
        const el = [...(scope?.querySelectorAll('button, a') ?? [])].find(b => !b.disabled && (b.textContent.replace(/\\s+/g, ' ').trim() === ${JSON.stringify(text)} || b.getAttribute('aria-label') === ${JSON.stringify(text)}));
        if (!el) return false; el.click(); return true; })()`);
      if (!ok) throw new Error(`tap "${text}": not found`);
      await sleep(300);
    },
    async tapSel(selector) {
      const ok = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return false; el.click(); return true; })()`);
      if (!ok) throw new Error(`tap ${selector}: not found`);
      await sleep(300);
    },
    has: selector => evaluate(`!!document.querySelector(${JSON.stringify(selector)})`),
    text: selector => evaluate(`[...document.querySelectorAll(${JSON.stringify(selector)})].map(e => e.textContent.replace(/\\s+/g, ' ').trim())`),
    // A route of the preview itself (/seed/…, /keys/…), from here.
    async get(route) {
      return await (await fetch(base + route)).text();
    },
    async shot(file, clip = null) {
      mkdirSync(file.replace(/[\\/][^\\/]*$/, ''), { recursive: true });
      const shot = await send('Page.captureScreenshot', { format: 'png', ...(clip ? { clip: { ...clip, scale: 1 } } : {}) });
      writeFileSync(file, Buffer.from(shot.result.data, 'base64'));
    },
    close() { ws.close(); },
  };
  return page;
}
