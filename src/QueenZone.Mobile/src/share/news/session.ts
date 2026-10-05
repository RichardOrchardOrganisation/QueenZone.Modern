import type { NewsSuggestionCreated, NewsSuggestionWrite } from '../../api/newsSuggestions';
import type { NewsShareStore, NewsSuggestDraft, PersistedNewsShare } from './draftStore';
import { normalizeShareUrl, parseShare, type ShareIntake, type ShareRaw, type ShareRejectReason } from './parseShare';
import { isPersistedShareFresh, shareIntakeFingerprint, shareSlotFingerprint } from './sharePolicy';

type StatusError = {
  status: number;
};

export type SuggestSubmitError = {
  code: 'invalid' | 'duplicate' | 'quota' | 'unauthorized' | 'network' | 'server';
  message: string;
  retryable: boolean;
};

export type NewsShareView =
  | { kind: 'idle' }
  | { kind: 'rejected'; reason: ShareRejectReason; detail: string; cancel: () => void }
  | {
      kind: 'choose';
      candidates: [string, string, ...string[]];
      choose: (url: string) => void;
      cancel: () => void;
    }
  | {
      kind: 'form';
      draft: NewsSuggestDraft;
      patch: (p: Partial<NewsSuggestDraft>) => void;
      cancel: () => void;
      submit: (token: string) => Promise<void>;
    }
  | { kind: 'submitting'; draft: NewsSuggestDraft }
  | {
      kind: 'failed';
      draft: NewsSuggestDraft;
      error: SuggestSubmitError;
      patch: (p: Partial<NewsSuggestDraft>) => void;
      cancel: () => void;
      submit: (token: string) => Promise<void>;
    }
  | { kind: 'submitted'; created: NewsSuggestionCreated; acknowledge: () => void };

export type NewsShareSubmit = (
  input: NewsSuggestionWrite,
  accessToken: string,
) => Promise<NewsSuggestionCreated>;

export type NewsShareControllerOptions = {
  now?: () => Date;
};

export type NewsShareController = {
  hydrate(): Promise<void>;
  capture(raw: ShareRaw): Promise<void>;
  openBlank(): Promise<void>;
  flush(): Promise<void>;
  view(): NewsShareView;
  subscribe(listener: () => void): () => void;
};

type SessionState = {
  persisted: PersistedNewsShare | null;
  ephemeralReject: Extract<ShareIntake, { kind: 'rejected' }> | null;
  inFlight: boolean;
  lastCreated: NewsSuggestionCreated | null;
  lastError: SuggestSubmitError | null;
  lastConsumed: string | null;
};

const notHttpsReject: Extract<ShareIntake, { kind: 'rejected' }> = {
  kind: 'rejected',
  reason: 'notHttps',
  detail: 'News suggestions need an https:// link.',
};

export function createNewsShareController(
  store: NewsShareStore,
  submitSuggestion: NewsShareSubmit,
  options?: NewsShareControllerOptions,
): NewsShareController {
  const now = options?.now ?? (() => new Date());
  const listeners = new Set<() => void>();
  const state: SessionState = {
    persisted: null,
    ephemeralReject: null,
    inFlight: false,
    lastCreated: null,
    lastError: null,
    lastConsumed: null,
  };
  let writeTail: Promise<void> = Promise.resolve();
  let hydrated = false;

  function emit(): void {
    for (const listener of listeners) {
      listener();
    }
  }

  function scheduleWrite(op: () => Promise<void>): Promise<void> {
    writeTail = writeTail.then(op, op);
    // Observe background writes while preserving rejection for flush/persistNow callers.
    void writeTail.catch((error: unknown) => {
      console.warn('Could not persist the news suggestion draft.', error);
    });
    return writeTail;
  }

  function nowIso(): string {
    return now().toISOString();
  }

  function setPersisted(value: PersistedNewsShare | null): void {
    state.persisted = value;
    if (value) {
      void scheduleWrite(() => store.write(value));
    } else {
      void scheduleWrite(() => store.clear());
    }
  }

  function rememberConsumed(fingerprint: string): void {
    state.lastConsumed = fingerprint;
    void scheduleWrite(() => store.writeLastConsumed(fingerprint));
  }

  async function persistNow(value: PersistedNewsShare | null): Promise<void> {
    if (value) {
      setPersisted({ ...value, savedAt: nowIso() });
    } else {
      setPersisted(null);
    }
    await writeTail;
  }

  function patchDraft(partial: Partial<NewsSuggestDraft>): void {
    if (state.persisted?.kind !== 'form') {
      return;
    }
    setPersisted({
      v: 1,
      kind: 'form',
      draft: { ...state.persisted.draft, ...partial },
      savedAt: nowIso(),
    });
    emit();
  }

  function cancel(): void {
    const fingerprint = state.persisted ? shareSlotFingerprint(state.persisted) : null;
    if (fingerprint) {
      rememberConsumed(fingerprint);
    }
    state.ephemeralReject = null;
    state.lastCreated = null;
    state.lastError = null;
    state.inFlight = false;
    void persistNow(null).then(emit, emit);
  }

  function acknowledge(): void {
    state.lastCreated = null;
    emit();
  }

  async function choose(url: string): Promise<void> {
    if (!url.startsWith('https://')) {
      state.ephemeralReject = notHttpsReject;
      emit();
      return;
    }

    state.ephemeralReject = null;
    await persistNow({
      v: 1,
      kind: 'form',
      draft: { url, title: '', notes: '', origin: 'share' },
    });
    emit();
  }

  async function submit(accessToken: string): Promise<void> {
    const current = view();
    if (current.kind !== 'form' && current.kind !== 'failed') {
      return;
    }
    if (state.inFlight) {
      return;
    }

    state.inFlight = true;
    state.lastError = null;
    emit();

    try {
      const created = await submitSuggestion(
        {
          url: current.draft.url,
          title: emptyToNull(current.draft.title),
          notes: emptyToNull(current.draft.notes),
        },
        accessToken,
      );
      const fingerprint = normalizeShareUrl(current.draft.url);
      if (fingerprint) {
        rememberConsumed(fingerprint);
      }
      await persistNow(null);
      state.lastCreated = created;
    } catch (error) {
      state.lastError = mapNewsSuggestionError(error);
    } finally {
      state.inFlight = false;
      emit();
    }
  }

  function view(): NewsShareView {
    if (state.lastCreated) {
      return { kind: 'submitted', created: state.lastCreated, acknowledge };
    }

    if (state.ephemeralReject) {
      return {
        kind: 'rejected',
        reason: state.ephemeralReject.reason,
        detail: state.ephemeralReject.detail,
        cancel,
      };
    }

    if (state.persisted?.kind === 'choose') {
      return {
        kind: 'choose',
        candidates: state.persisted.candidates,
        choose: (url) => {
          void choose(url).catch((error: unknown) => {
            state.lastError = mapNewsSuggestionError(error);
            emit();
          });
        },
        cancel,
      };
    }

    if (state.persisted?.kind === 'form') {
      const draft = state.persisted.draft;
      if (state.inFlight) {
        return { kind: 'submitting', draft };
      }
      if (state.lastError) {
        return {
          kind: 'failed',
          draft,
          error: state.lastError,
          patch: patchDraft,
          cancel,
          submit,
        };
      }
      return {
        kind: 'form',
        draft,
        patch: patchDraft,
        cancel,
        submit,
      };
    }

    return { kind: 'idle' };
  }

  async function discardStaleSlot(slot: PersistedNewsShare): Promise<void> {
    const fingerprint = shareSlotFingerprint(slot);
    if (fingerprint) {
      state.lastConsumed = fingerprint;
      await store.writeLastConsumed(fingerprint);
    }
    await store.clear();
    state.persisted = null;
  }

  async function hydrateFromStore(): Promise<void> {
    await scheduleWrite(async () => {
      state.lastConsumed = await store.readLastConsumed();
      const slot = await store.read();
      if (slot && !isPersistedShareFresh(slot.savedAt, now().getTime())) {
        await discardStaleSlot(slot);
      } else {
        state.persisted = slot;
      }
      state.ephemeralReject = null;
      state.inFlight = false;
      state.lastCreated = null;
      state.lastError = null;
      hydrated = true;
      emit();
    });
  }

  async function ensureHydrated(): Promise<void> {
    if (!hydrated) {
      await hydrateFromStore();
    }
  }

  return {
    async hydrate() {
      await hydrateFromStore();
    },
    async capture(raw) {
      const intake = parseShare(raw);
      switch (intake.kind) {
        case 'accepted': {
          await ensureHydrated();
          const fingerprint = shareIntakeFingerprint(intake);
          const current = state.persisted;
          if (
            current?.kind === 'form' &&
            current.draft.url &&
            fingerprint &&
            normalizeShareUrl(current.draft.url) === fingerprint
          ) {
            state.ephemeralReject = null;
            emit();
            break;
          }
          if (!current && fingerprint && state.lastConsumed === fingerprint) {
            state.ephemeralReject = null;
            emit();
            break;
          }
          if (fingerprint) {
            rememberConsumed(fingerprint);
          }
          await persistNow({
            v: 1,
            kind: 'form',
            draft: {
              url: intake.url,
              title: intake.leftoverText,
              notes: '',
              origin: 'share',
            },
          });
          state.ephemeralReject = null;
          state.lastCreated = null;
          state.lastError = null;
          emit();
          break;
        }
        case 'choose': {
          await ensureHydrated();
          const fingerprint = shareIntakeFingerprint(intake);
          const current = state.persisted;
          if (current?.kind === 'choose' && fingerprint && shareSlotFingerprint(current) === fingerprint) {
            state.ephemeralReject = null;
            emit();
            break;
          }
          if (!current && fingerprint && state.lastConsumed === fingerprint) {
            state.ephemeralReject = null;
            emit();
            break;
          }
          if (fingerprint) {
            rememberConsumed(fingerprint);
          }
          await persistNow({ v: 1, kind: 'choose', candidates: intake.candidates });
          state.ephemeralReject = null;
          state.lastCreated = null;
          state.lastError = null;
          emit();
          break;
        }
        case 'rejected':
          await persistNow(null);
          state.ephemeralReject = intake;
          state.lastCreated = null;
          state.lastError = null;
          emit();
          break;
      }
    },
    async openBlank() {
      if (state.persisted) {
        return;
      }
      await persistNow({
        v: 1,
        kind: 'form',
        draft: { url: '', title: '', notes: '', origin: 'inApp' },
      });
      state.ephemeralReject = null;
      state.lastCreated = null;
      state.lastError = null;
      emit();
    },
    async flush() {
      await writeTail;
    },
    view,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function isStatusError(error: unknown): error is StatusError & Error {
  return (
    error instanceof Error &&
    'status' in error &&
    typeof (error as StatusError).status === 'number'
  );
}

function mapNewsSuggestionError(error: unknown): SuggestSubmitError {
  if (isStatusError(error)) {
    if (error.status === 400) {
      return { code: 'invalid', message: error.message, retryable: false };
    }
    if (error.status === 409) {
      return { code: 'duplicate', message: error.message, retryable: false };
    }
    if (error.status === 429) {
      return { code: 'quota', message: error.message, retryable: false };
    }
    if (error.status === 401) {
      return { code: 'unauthorized', message: error.message, retryable: false };
    }
    if (error.status === 0) {
      return { code: 'network', message: error.message, retryable: true };
    }
    if (error.status >= 500) {
      return { code: 'server', message: error.message, retryable: true };
    }
  }

  if (error instanceof TypeError) {
    return {
      code: 'network',
      message: 'Unable to reach QueenZone. Check your connection and try again.',
      retryable: true,
    };
  }

  return {
    code: 'server',
    message: error instanceof Error ? error.message : 'Could not submit this story.',
    retryable: true,
  };
}
