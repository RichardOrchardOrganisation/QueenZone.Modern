import { chromium } from 'playwright';
import fs from 'node:fs';
const base = process.env.BASE || 'http://127.0.0.1:5199';
const tag = process.env.TAG || 'pr';
const out = `/workspace/qz-2276-proof`;
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1280, height: 1800 }, extraHTTPHeaders: { 'X-Test-User-Email': 'admin@test.local' } });
// Serve seeded thumbnails/originals for CDN URLs locally (the CDN has no such files).
await ctx.route(/cdn\.queenzone\.org|blob\.core\.windows\.net/, async (route) => {
  const name = new URL(route.request().url()).pathname.split('/').pop();
  const p = `${out}/img/${name}`;
  if (fs.existsSync(p)) return route.fulfill({ path: p, contentType: 'image/jpeg' });
  return route.fulfill({ status: 404, body: '' });
});
// Testing host has no publish step, so the bundled/minified CSS doesn't exist; serve the unminified sources.
await ctx.route(/\/(design-system\/tokens\/tokens|css\/site)\.min\.css/, (route) =>
  route.fulfill({ path: `${out}/${route.request().url().includes('tokens') ? 'tokens' : 'site'}.min.css`, contentType: 'text/css' }));
const page = await ctx.newPage();
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
page.on('response', (r) => { if (r.status() >= 400 && r.url().startsWith(base)) errors.push(`${r.status()} ${r.url()}`); });
await page.goto(`${base}/admin/news/1003/edit`);
await page.click('[data-gallery-picker-open]');
const dlg = page.locator('[data-gallery-picker-dialog]');
await dlg.locator('select[name=catId]').selectOption('9');
await dlg.locator('[data-gallery-picker-filter] button[type=submit]').click();
await page.waitForTimeout(1000);
const ids = async () => (await dlg.locator('[data-original-url]').evaluateAll(els => els.map(e => e.getAttribute('data-original-url').split('/').pop())));
const p1 = await ids();
await dlg.screenshot({ path: `${out}/${tag}-picker-page1.png` });
await dlg.locator('[data-gallery-picker-page]', { hasText: 'Next' }).click();
await page.waitForTimeout(1000);
const p2 = await ids();
await dlg.screenshot({ path: `${out}/${tag}-picker-page2.png` });
// page 3 for completeness
const all = [...p1, ...p2];
if (await dlg.locator('[data-gallery-picker-page]', { hasText: 'Next' }).count()) {
  await dlg.locator('[data-gallery-picker-page]', { hasText: 'Next' }).click(); await page.waitForTimeout(800);
  all.push(...await ids()); await dlg.screenshot({ path: `${out}/${tag}-picker-page3.png` });
}
const summary = { tag, page1: p1, page2: p2, allPages: all };
for (const bad of ['103', '530', '531']) summary[`contains_${bad}`] = all.includes(bad);
// pick first photo on current page -> crop
await dlg.locator('[data-original-url]').first().click();
await page.waitForSelector('[data-article-image-dialog][open]', { timeout: 10000 });
await page.waitForTimeout(1500);
await page.locator('[data-article-image-dialog]').screenshot({ path: `${out}/${tag}-crop-dialog.png` });
await page.click('[data-article-image-apply]');
await page.waitForTimeout(1000);
await page.locator('[data-gallery-picker-host]').screenshot({ path: `${out}/${tag}-crop-applied.png` });
summary.cropFields = await page.evaluate(() => Object.fromEntries(['cropX','cropY','cropWidth','cropHeight','imageGalleryPicId'].map(n => [n, document.querySelector(`[name=${n}]`)?.value])));
summary.errors = errors;
fs.writeFileSync(`${out}/${tag}-summary.json`, JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));
await browser.close();
