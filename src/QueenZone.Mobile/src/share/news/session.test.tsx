import { ApiError } from '../../api/errors';
import type { NewsSuggestionCreated, NewsSuggestionWrite } from '../../api/newsSuggestions';
import { createMemoryStorage } from '../../cache/storage';
import { createNewsShareStore, newsShareSlotKey } from './draftStore';
import { createNewsShareController, type NewsShareControllerOptions } from './session';
import { newsShareStaleWindowMs } from './sharePolicy';

const httpsUrl = 'https://www.bbc.co.uk/news/example';
const created: NewsSuggestionCreated = {
  id: '11111111-1111-1111-1111-111111111111',
  status: 'Pending',
  url: httpsUrl,
  title: 'Queen announce dates',
  submittedAt: '2026-08-26T10:00:00Z',
};

function controller(
  submit?: (input: NewsSuggestionWrite, token: string) => Promise<NewsSuggestionCreated>,
  options?: NewsShareControllerOptions,
) {
  const store = createNewsShareStore(createMemoryStorage());
  const calls: NewsSuggestionWrite[] = [];
  const session = createNewsShareController(
    store,
    async (input, token) => {
      calls.push(input);
      if (submit) {
        return submit(input, token);
      }
      return created;
    },
    options,
  );
  return { session, store, calls };
}

function clockAt(iso: string) {
  let nowMs = Date.parse(iso);
  return {
    now: () => new Date(nowMs),
    set(isoValue: string) {
      nowMs = Date.parse(isoValue);
    },
    advance(ms: number) {
      nowMs += ms;
    },
  };
}

describe('news share session', () => {
  it('keeps a captured draft after remount', async () => {
    const storage = createMemoryStorage();
    const store = createNewsShareStore(storage);
    const first = createNewsShareController(store, async () => created);
    await first.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = first.view();
    expect(form.kind).toBe('form');
    if (form.kind === 'form') {
      form.patch({ title: 'Kept after kill' });
    }
    await first.flush();

    const remount = createNewsShareController(createNewsShareStore(storage), async () => created);
    await remount.hydrate();
    const restored = remount.view();
    expect(restored.kind).toBe('form');
    if (restored.kind !== 'form') {
      return;
    }
    expect(restored.draft.url).toBe(httpsUrl);
    expect(restored.draft.title).toBe('Kept after kill');
  });

  it('keeps edits when the same normalized URL is redelivered', async () => {
    const { session } = controller();
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = session.view();
    expect(form.kind).toBe('form');
    if (form.kind !== 'form') {
      return;
    }
    form.patch({ title: 'Member headline', notes: 'Worth a look' });
    await session.capture({ webUrl: `${httpsUrl}/`, text: 'Queen announce dates', hasFiles: false });
    const again = session.view();
    expect(again.kind).toBe('form');
    if (again.kind !== 'form') {
      return;
    }
    expect(again.draft.title).toBe('Member headline');
    expect(again.draft.notes).toBe('Worth a look');
  });

  it('replaces the draft when a new URL arrives', async () => {
    const { session } = controller();
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = session.view();
    if (form.kind === 'form') {
      form.patch({ title: 'Old' });
    }
    await session.capture({ webUrl: 'https://www.rollingstone.com/music/queen', hasFiles: false });
    const next = session.view();
    expect(next.kind).toBe('form');
    if (next.kind !== 'form') {
      return;
    }
    expect(next.draft.url).toBe('https://www.rollingstone.com/music/queen');
    expect(next.draft.title).toBe('');
  });

  it('no-ops a second submit while inFlight', async () => {
    let resolveSubmit!: (value: NewsSuggestionCreated) => void;
    const hung = new Promise<NewsSuggestionCreated>((resolve) => {
      resolveSubmit = resolve;
    });
    const { session, calls } = controller(() => hung);
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const first = session.view();
    expect(first.kind).toBe('form');
    if (first.kind !== 'form') {
      return;
    }

    const pending = first.submit('tok');
    expect(session.view().kind).toBe('submitting');
    await first.submit('tok');
    resolveSubmit(created);
    await pending;
    expect(calls).toHaveLength(1);
    expect(session.view().kind).toBe('submitted');
  });

  it('flushes the latest patch before a sign-in hop', async () => {
    const { session, store } = controller();
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = session.view();
    expect(form.kind).toBe('form');
    if (form.kind !== 'form') {
      return;
    }
    form.patch({ title: 'Flush me' });
    await session.flush();
    const persisted = await store.read();
    expect(persisted?.kind).toBe('form');
    if (persisted?.kind === 'form') {
      expect(persisted.draft.title).toBe('Flush me');
    }
  });

  it('observes a failed background patch, preserves flush rejection, and recovers on the next write', async () => {
    const store = createNewsShareStore(createMemoryStorage());
    const session = createNewsShareController(store, async () => created);
    await session.openBlank();
    const form = session.view();
    expect(form.kind).toBe('form');
    if (form.kind !== 'form') {
      return;
    }
    const error = new Error('Storage unavailable');
    const warning = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const write = jest.spyOn(store, 'write').mockRejectedValueOnce(error);
    try {
      form.patch({ title: 'Unsaved title' });
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      expect(warning).toHaveBeenCalledWith('Could not persist the news suggestion draft.', error);
      await expect(session.flush()).rejects.toBe(error);
      form.patch({ title: 'Recovered title' });
      await session.flush();
      const saved = await store.read();
      expect(saved?.kind === 'form' ? saved.draft.title : null).toBe('Recovered title');
      expect(write).toHaveBeenCalledTimes(2);
    } finally {
      warning.mockRestore();
      write.mockRestore();
    }
  });

  it('observes a failed cancel clear and notifies subscribers without an unhandled rejection', async () => {
    const store = createNewsShareStore(createMemoryStorage());
    const session = createNewsShareController(store, async () => created);
    await session.openBlank();
    const form = session.view();
    expect(form.kind).toBe('form');
    if (form.kind !== 'form') {
      return;
    }
    const error = new Error('Storage unavailable');
    const warning = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const clear = jest.spyOn(store, 'clear').mockRejectedValueOnce(error);
    const listener = jest.fn();
    const unsubscribe = session.subscribe(listener);
    try {
      form.cancel();
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      await expect(session.flush()).rejects.toBe(error);
      expect(warning).toHaveBeenCalledWith('Could not persist the news suggestion draft.', error);
      expect(listener).toHaveBeenCalledTimes(1);
      expect(session.view().kind).toBe('idle');
    } finally {
      unsubscribe();
      warning.mockRestore();
      clear.mockRestore();
    }
  });

  it('reports a failed chosen draft without leaving an unhandled rejection', async () => {
    const store = createNewsShareStore(createMemoryStorage());
    const session = createNewsShareController(store, async () => created);
    await session.capture({ text: 'https://example.com/a https://example.com/b', hasFiles: false });
    const chooser = session.view();
    expect(chooser.kind).toBe('choose');
    if (chooser.kind !== 'choose') {
      return;
    }
    const error = new Error('Storage unavailable');
    const warning = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const write = jest.spyOn(store, 'write').mockRejectedValueOnce(error);
    try {
      chooser.choose('https://example.com/a');
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      await expect(session.flush()).rejects.toBe(error);
      const failed = session.view();
      expect(failed.kind).toBe('failed');
      if (failed.kind === 'failed') {
        expect(failed.error.message).toBe('Storage unavailable');
        expect(failed.draft.url).toBe('https://example.com/a');
      }
      expect(warning).toHaveBeenCalledWith('Could not persist the news suggestion draft.', error);
    } finally {
      warning.mockRestore();
      write.mockRestore();
    }
  });

  it('maps submit errors for 400, 409, 429, 401, and network', async () => {
    const cases: { error: unknown; code: string }[] = [
      { error: new ApiError(400, 'Bad Request'), code: 'invalid' },
      { error: new ApiError(409, 'Conflict'), code: 'duplicate' },
      { error: new ApiError(429, 'Too Many Requests'), code: 'quota' },
      { error: new ApiError(401, 'Unauthorized'), code: 'unauthorized' },
      { error: new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'), code: 'network' },
    ];

    for (const example of cases) {
      const { session, store } = controller(async () => {
        throw example.error;
      });
      await session.capture({ webUrl: httpsUrl, hasFiles: false });
      const form = session.view();
      expect(form.kind).toBe('form');
      if (form.kind !== 'form') {
        return;
      }
      await form.submit('tok');
      const failed = session.view();
      expect(failed.kind).toBe('failed');
      if (failed.kind !== 'failed') {
        return;
      }
      expect(failed.error.code).toBe(example.code);
      expect(await store.read()).not.toBeNull();
    }
  });

  it('clears the slot on cancel and on 201', async () => {
    const cancelled = controller();
    await cancelled.session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = cancelled.session.view();
    if (form.kind === 'form') {
      form.cancel();
    }
    await cancelled.session.flush();
    expect(await cancelled.store.read()).toBeNull();
    expect(cancelled.session.view().kind).toBe('idle');

    const submitted = controller();
    await submitted.session.capture({ webUrl: httpsUrl, hasFiles: false });
    const again = submitted.session.view();
    if (again.kind === 'form') {
      await again.submit('tok');
    }
    expect(submitted.session.view().kind).toBe('submitted');
    expect(await submitted.store.read()).toBeNull();
  });

  it('discards a share older than 30 minutes on hydrate and stays idle', async () => {
    const storage = createMemoryStorage();
    const clock = clockAt('2026-09-08T12:00:00.000Z');
    const first = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await first.capture({ webUrl: httpsUrl, hasFiles: false });
    await first.flush();

    clock.advance(newsShareStaleWindowMs + 1);
    const remount = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await remount.hydrate();
    expect(remount.view().kind).toBe('idle');
    expect(await createNewsShareStore(storage).read()).toBeNull();
  });

  it('discards a slot with a missing or invalid savedAt on hydrate', async () => {
    const missing = createMemoryStorage({
      [newsShareSlotKey]: JSON.stringify({
        v: 1,
        kind: 'form',
        draft: { url: httpsUrl, title: 'Old', notes: '', origin: 'share' },
      }),
    });
    const missingSession = createNewsShareController(createNewsShareStore(missing), async () => created);
    await missingSession.hydrate();
    expect(missingSession.view().kind).toBe('idle');
    expect(await missing.getItem(newsShareSlotKey)).toBeNull();

    const invalid = createMemoryStorage({
      [newsShareSlotKey]: JSON.stringify({
        v: 1,
        kind: 'form',
        draft: { url: httpsUrl, title: 'Old', notes: '', origin: 'share' },
        savedAt: 'not-a-date',
      }),
    });
    const invalidSession = createNewsShareController(createNewsShareStore(invalid), async () => created);
    await invalidSession.hydrate();
    expect(invalidSession.view().kind).toBe('idle');
    expect(await invalid.getItem(newsShareSlotKey)).toBeNull();
  });

  it('restores an unfinished share within 30 minutes', async () => {
    const storage = createMemoryStorage();
    const clock = clockAt('2026-09-08T12:00:00.000Z');
    const first = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await first.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = first.view();
    if (form.kind === 'form') {
      form.patch({ title: 'Still working' });
    }
    await first.flush();

    clock.advance(newsShareStaleWindowMs);
    const remount = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await remount.hydrate();
    const restored = remount.view();
    expect(restored.kind).toBe('form');
    if (restored.kind !== 'form') {
      return;
    }
    expect(restored.draft.url).toBe(httpsUrl);
    expect(restored.draft.title).toBe('Still working');
  });

  it('does not reopen a replayed same URL or fingerprint after cancel', async () => {
    const { session, store } = controller();
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = session.view();
    if (form.kind === 'form') {
      form.cancel();
    }
    await session.flush();
    expect(session.view().kind).toBe('idle');

    await session.capture({ webUrl: `${httpsUrl}/`, hasFiles: false });
    expect(session.view().kind).toBe('idle');
    expect(await store.read()).toBeNull();
  });

  it('does not reopen a replayed share after remount or stale discard', async () => {
    const storage = createMemoryStorage();
    const clock = clockAt('2026-09-08T12:00:00.000Z');
    const first = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await first.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = first.view();
    if (form.kind === 'form') {
      form.cancel();
    }
    await first.flush();

    const remount = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await remount.capture({ webUrl: httpsUrl, hasFiles: false });
    expect(remount.view().kind).toBe('idle');

    const stale = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await stale.capture({ webUrl: 'https://www.rollingstone.com/music/queen', hasFiles: false });
    await stale.flush();
    clock.advance(newsShareStaleWindowMs + 1);
    const afterStale = createNewsShareController(createNewsShareStore(storage), async () => created, {
      now: clock.now,
    });
    await afterStale.capture({ webUrl: 'https://www.rollingstone.com/music/queen', hasFiles: false });
    expect(afterStale.view().kind).toBe('idle');
    expect(await createNewsShareStore(storage).read()).toBeNull();
  });

  it('does not reopen a replayed choose fingerprint after cancel', async () => {
    const { session, store } = controller();
    const text = 'https://example.com/a https://example.com/b';
    await session.capture({ text, hasFiles: false });
    expect(session.view().kind).toBe('choose');
    await session.capture({ text, hasFiles: false });
    expect(session.view().kind).toBe('choose');

    const choose = session.view();
    if (choose.kind === 'choose') {
      choose.cancel();
    }
    await session.flush();
    expect(session.view().kind).toBe('idle');

    await session.capture({ text, hasFiles: false });
    expect(session.view().kind).toBe('idle');
    expect(await store.read()).toBeNull();
  });

  it('still opens a new URL after the previous share was cleared', async () => {
    const { session } = controller();
    await session.capture({ webUrl: httpsUrl, hasFiles: false });
    const form = session.view();
    if (form.kind === 'form') {
      form.cancel();
    }
    await session.flush();

    const nextUrl = 'https://www.rollingstone.com/music/queen';
    await session.capture({ webUrl: nextUrl, hasFiles: false });
    const next = session.view();
    expect(next.kind).toBe('form');
    if (next.kind !== 'form') {
      return;
    }
    expect(next.draft.url).toBe(nextUrl);
  });

  it('hydrate after a rejected share drops the ephemeral reject', async () => {
    const { session } = controller();
    await session.capture({ hasFiles: true });
    expect(session.view().kind).toBe('rejected');
    await session.hydrate();
    expect(session.view().kind).toBe('idle');
  });

  it('does not write a draft when the member picks http from choose', async () => {
    const { session, store } = controller();
    await session.capture({
      text: 'http://example.com/old https://example.com/new',
      hasFiles: false,
    });
    const choose = session.view();
    expect(choose.kind).toBe('choose');
    if (choose.kind !== 'choose') {
      return;
    }
    choose.choose('http://example.com/old');
    const rejected = session.view();
    expect(rejected.kind).toBe('rejected');
    if (rejected.kind !== 'rejected') {
      return;
    }
    expect(rejected.reason).toBe('notHttps');
    const persisted = await store.read();
    expect(persisted?.kind).toBe('choose');
  });
});
