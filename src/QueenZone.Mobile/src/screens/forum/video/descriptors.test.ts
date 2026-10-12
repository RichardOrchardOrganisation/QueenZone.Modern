import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { projectVideos, validateDescriptors, watchUrl } from './descriptors.ts';
const id = 'M7lc1UVf-VE';
const video = (anchorIndex = 0, startSeconds: number | null = null, videoId = id) => ({ provider: 'youtube', videoId, anchorIndex, startSeconds, watchUrl: watchUrl(videoId, startSeconds) });
const link = (href = `https://youtu.be/${id}`, label = 'Video') => `<a href="${href}">${label}</a>`;
describe('additive forum video descriptors', () => {
  it('keeps older servers and malformed payloads readable', () => {
    for (const payload of [undefined, null, [], {}, 'youtube', [null], [video(-1)], [{ ...video(), provider: 'vimeo' }], [{ ...video(), videoId: 'bad' }], [video(0, 86401)], [video(0, -1)], [video(0, 1.5)], [{ ...video(), watchUrl: 'https://evil.test/' }]]) {
      const html = `<p>Before</p><p>${link()}</p><p>After</p>`;
      assert.deepEqual(projectVideos(html, payload), { html, cards: [] });
    }
  });
  it('validates bounds, canonical URLs, duplicates and the server cap', () => {
    assert.equal(validateDescriptors([video(0, 86400)])[0].startSeconds, 86400);
    assert.equal(validateDescriptors([video(), video(1), video(2, 90), video(3, 50)]).length, 2);
    assert.equal(validateDescriptors([video(0), video(0, 90)]).length, 1);
    assert.equal(validateDescriptors([video(1.1)]).length, 0);
  });
  it('maps all-anchor positions with images, entity decoding, prose and duplicate links intact', () => {
    const html = `<p>Before ${link('https://qz.test/', 'source')}</p><p><a href="https://qz.test/photo"><img src="/photo.jpg"></a></p><p><strong>${link(`http://youtube.com/watch?v=${id}&amp;t=1m30s`, 'Original')}</strong></p><p>${link(`https://youtu.be/${id}?t=90`, 'Duplicate')}</p><p>After</p>`;
    const projected = projectVideos(html, [video(2, 90)]);
    assert.equal(projected.cards.length, 1);
    assert.match(projected.cards[0].originalHtml, /Original/);
    assert.match(projected.html, /Before/); assert.match(projected.html, /source/);
    assert.match(projected.html, /img/); assert.match(projected.html, /Duplicate/); assert.match(projected.html, /After/);
    assert.equal((projected.html.match(/qz-forum-video data-card/g) ?? []).length, 1);
  });
  it('does not promote inline/quoted/list/heading/signature-like blocks or mismatched positions', () => {
    for (const html of [`<p>Inline ${link()}</p>`, `<blockquote><p>${link()}</p></blockquote>`, `<div class="qz-bbcode-quote">${link()}</div>`, `<p>${link()}<img src="x"></p>`, `<ul><li>${link()}</li></ul>`, `<h2>${link()}</h2>`, `<code>${link()}</code>`, `<p>${link()} ${link()}</p>`]) assert.equal(projectVideos(html, [video()]).cards.length, 0);
    assert.equal(projectVideos(`<p>${link('https://youtu.be/abcdefghijk')}</p>`, [video()]).cards.length, 0);
    assert.equal(projectVideos(`<p>${link()}</p>`, [video(1)]).cards.length, 0);
    assert.equal(projectVideos(`<p>${link()}</p>`, [video(0, 90)]).cards.length, 0);
  });
  it('supports formatted plain lines and approved URLs while rejecting hostile normalization', () => {
    for (const url of [`https://youtube.com/watch?v=${id}`, `http://www.youtube.com/shorts/${id}`, `https://m.youtube.com/embed/${id}`, `https://www.youtu.be/${id}`]) assert.equal(projectVideos(`<p>Prose<br>${link(url)}<br>After</p>`, [video()]).cards.length, 1);
    for (const url of [`https://youtube.com.evil/watch?v=${id}`, `https://a@youtube.com/watch?v=${id}`, `https://youtube.com:444/watch?v=${id}`, `https://youtube.com/watch?v=${id}&v=${id}`, `https://youtu.be/${id}?v=${id}`, `//youtu.be/${id}`, `https://youtu.be/%4D7lc1UVf-VE`, `https://youtube.com/a/../watch?v=${id}`, `javascript:${id}`]) assert.equal(projectVideos(`<p>${link(url)}</p>`, [video()]).cards.length, 0);
  });
  it('ignores malformed and excessive starts just like the server', () => {
    for (const t of ['bad', '86401', '1m2h', '']) assert.equal(projectVideos(`<p>${link(`https://youtu.be/${id}?t=${t}`)}</p>`, [video()]).cards.length, 1);
    assert.equal(projectVideos(`<p>${link(`https://youtu.be/${id}?START=1h2m3s`)}</p>`, [video(0, 3723)]).cards.length, 1);
  });
  it('cannot fabricate cards through posted attributes or custom tags', () => {
    const projection = projectVideos(`<qz-forum-video data-card="0">Safe text</qz-forum-video><p>${link()}</p>`, [video()]);
    assert.match(projection.html, /Safe text/);
    assert.equal((projection.html.match(/qz-forum-video data-card/g) ?? []).length, 1);
  });
});
