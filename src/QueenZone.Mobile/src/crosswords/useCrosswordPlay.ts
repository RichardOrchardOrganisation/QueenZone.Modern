import AsyncStorage from '@react-native-async-storage/async-storage';
import { useEffect, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { useNetworkState } from 'expo-network';
import type { CrosswordDetail, CrosswordExplanation, CrosswordSelection, CrosswordCompletionResult } from '../api/types';
import { checkCrossword, completeCrossword, fetchCrosswordProgress, revealCrossword } from '../api/crosswords';
import { enqueueCrosswordProgress, useOfflineQueue } from '../offlineQueue';
import { flushOfflineQueue, isOfflineQueueOwnerCurrent } from '../offlineQueue/flusher';
import * as core from './core';

export function useCrosswordPlay(puzzle: CrosswordDetail, memberId: string | null, accessToken: string | null) {
  const [model] = useState(() => core.createModel(puzzle));
  const [state, setState] = useState(() => core.createPlayState(model));
  const [timer, setTimer] = useState(() => core.createTimer());
  const [ready, setReady] = useState(false);
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [guest, setGuest] = useState<core.CrosswordSavedProgress | null>(null);
  const [completion, setCompletion] = useState<{ elapsedSeconds: number; clean: boolean; rankingEligible: boolean } | null>(null);
  const [review, setReview] = useState<(Omit<CrosswordExplanation, 'explanation'> & { answer?: string; explanation: string | null })[]>([]);
  const network = useNetworkState();
  const online = network.isConnected !== false && network.isInternetReachable !== false;
  const queue = useOfflineQueue(memberId);
  const pending = queue.filter(item => item.kind === 'crossword.progress' && 'crosswordId' in item.target && item.target.crosswordId === puzzle.id);
  const latest = useRef({ state, timer, updatedAt: 0, accessToken, online, completion });
  latest.current = { ...latest.current, state, timer, accessToken, online, completion };
  const mounted = useRef(true);
  const readyRef = useRef(false);
  const dirtyRef = useRef(false);
  const controller = useRef(new AbortController());
  const saveWork = useRef(Promise.resolve());
  const attempted = useRef<string | null>(null);
  const syncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const key = core.progressStorageKey(puzzle.id, memberId);
  const version = puzzle.playVersion;

  function update(next: core.CrosswordPlayState, nextTimer = latest.current.timer) {
    latest.current = { ...latest.current, state: next, timer: nextTimer, updatedAt: Date.now() };
    setState(next); setTimer(nextTimer);
  }
  function snapshot() {
    if (!version) return null;
    return { ...core.progressSnapshot(latest.current.state, latest.current.timer, version, Date.now()),
      updatedAt: new Date(latest.current.updatedAt).toISOString() };
  }
  function persist(sync: boolean) {
    if (!readyRef.current || !dirtyRef.current) return;
    const write = snapshot();
    if (!write) return;
    saveWork.current = saveWork.current.then(async () => {
      if (!await core.saveLocalProgress(AsyncStorage, key, write) && mounted.current) setStatus('Device storage is unavailable. Keep this screen open to keep your letters.');
      if (sync && memberId && isOfflineQueueOwnerCurrent(memberId) && !latest.current.completion) {
        await enqueueCrosswordProgress({ memberId, crosswordId: puzzle.id, progress: write });
        if (latest.current.online) await flushOfflineQueue();
      }
    }).catch(() => { if (mounted.current) setStatus('Could not save on this device. Keep this screen open.'); });
  }
  function change(next: core.CrosswordPlayState) {
    if (!ready || busy || latest.current.timer.paused || latest.current.completion || next === latest.current.state) return;
    const nextTimer = next.letters === latest.current.state.letters ? latest.current.timer : core.startTimer(latest.current.timer, Date.now());
    dirtyRef.current = true; update(next, nextTimer); persist(false);
    if (syncTimer.current) clearTimeout(syncTimer.current);
    syncTimer.current = setTimeout(() => persist(true), 2000);
  }
  function select(next: core.CrosswordPlayState) { if (ready && !latest.current.timer.paused) { latest.current.state = next; setState(next); } }
  function pause() {
    dirtyRef.current = true;
    update(latest.current.state, core.setPaused(latest.current.timer, !latest.current.timer.paused, Date.now()));
    persist(true);
  }
  function selection(scope: CrosswordSelection['scope']): CrosswordSelection {
    if (scope === 'cell') return { scope, cell: latest.current.state.cell };
    if (scope === 'grid') return { scope };
    const entry = model.entries[latest.current.state.entry];
    return { scope, number: entry.number, direction: entry.direction };
  }
  function options() { return { signal: controller.current.signal, accessToken: memberId ? latest.current.accessToken : null }; }
  function accepts(result: { playVersion: string }) {
    if (!mounted.current) return false;
    if (result.playVersion !== version) { setStatus('This crossword has changed. Reload it before continuing.'); return false; }
    return true;
  }
  async function check(scope: CrosswordSelection['scope'], autoCheck = false) {
    if (!version || !latest.current.online) { setStatus('Needs a connection'); return; }
    const letters = latest.current.state.letters;
    try {
      const result = await checkCrossword(puzzle.id, version, letters, selection(scope), autoCheck, options());
      if (!accepts(result) || latest.current.state.letters !== letters) return;
      select(core.applyCheck(latest.current.state, result.cells));
      setReview(result.explanations);
      if (!autoCheck) setStatus(result.cells.some(cell => cell.status === 'incorrect') ? 'Some letters are incorrect, marked with a cross.' : 'No incorrect letters in this selection.');
    } catch (error) { if (mounted.current) setStatus(error instanceof Error ? error.message : 'Could not check this crossword.'); }
  }
  async function reveal(scope: CrosswordSelection['scope']) {
    if (!version || !latest.current.online) { setStatus('Needs a connection'); return; }
    setBusy(true);
    try {
      const result = await revealCrossword(puzzle.id, version, selection(scope), options());
      if (!accepts(result)) return;
      dirtyRef.current = true;
      update(core.applyReveal(model, latest.current.state, result.cells), core.startTimer(latest.current.timer, Date.now()));
      setReview(result.explanations); setStatus('Revealed letters have triangles. This is an assisted solve.'); persist(true);
    } catch (error) { if (mounted.current) setStatus(error instanceof Error ? error.message : 'Could not reveal this crossword.'); }
    finally { if (mounted.current) setBusy(false); }
  }
  async function finish() {
    const write = snapshot();
    if (!write || !latest.current.online || latest.current.completion || attempted.current === write.letters) return;
    attempted.current = write.letters; setBusy(true);
    try {
      const token = latest.current.accessToken;
      const result = memberId && token ? await completeCrossword(puzzle.id, write, token, controller.current.signal)
        : await checkCrossword(puzzle.id, write.playVersion, write.letters, { scope: 'grid' }, false, options());
      if (!accepts(result) || latest.current.state.letters !== write.letters) return;
      const correct = 'correct' in result ? result.correct : result.complete;
      if (!correct) { setStatus('Not quite — some letters are wrong. Use Check grid.'); return; }
      const memberResult = 'completion' in result ? result as CrosswordCompletionResult : null;
      const solved = memberResult?.completion ?? { elapsedSeconds: write.elapsedSeconds, clean: core.isClean(latest.current.state), rankingEligible: false };
      latest.current.completion = solved; setCompletion(solved);
      update(latest.current.state, core.setPaused(latest.current.timer, true, Date.now()));
      setReview(memberResult?.review ?? ('explanations' in result ? result.explanations : []));
      setStatus('Crossword complete.'); persist(false);
    } catch (error) { if (mounted.current) setStatus(error instanceof Error ? error.message : 'Could not finish this crossword.'); }
    finally { if (mounted.current) setBusy(false); }
  }
  async function restore(abort: AbortController) {
      if (!version) { setStatus('This server does not support crossword saves yet. You can explore the blank grid.'); setReady(true); return; }
      const local = await core.loadLocalProgress(AsyncStorage, key, model, version);
      let remote = null;
      if (memberId && accessToken) {
        try { remote = await fetchCrosswordProgress(puzzle.id, accessToken, abort.signal); }
        catch { /* Local progress remains usable offline or while the API is older. */ }
      }
      if (!mounted.current) return;
      const chosen = core.chooseProgress(model, version, local, remote);
      dirtyRef.current = chosen.progress !== null;
      update(core.createPlayState(model, chosen.progress ?? {}), core.createTimer(chosen.progress?.elapsedSeconds ?? 0));
      latest.current.updatedAt = chosen.progress ? Date.parse(chosen.progress.updatedAt) : Date.now();
      if (chosen.restoredFromServer) setStatus('Progress restored from another device.');
      if (memberId && !chosen.progress) {
        const savedGuest = await core.loadLocalProgress(AsyncStorage, core.progressStorageKey(puzzle.id), model, version);
        if (mounted.current) setGuest(savedGuest);
      }
      if (mounted.current) { readyRef.current = true; setReady(true); }
    }
  async function reconcile() {
    const token = latest.current.accessToken;
    if (!version || !memberId || !token) { persist(true); return; }
    try {
      const remote = await fetchCrosswordProgress(puzzle.id, token, controller.current.signal);
      if (!mounted.current || !readyRef.current) return;
      const chosen = core.chooseProgress(model, version, dirtyRef.current ? snapshot() : null, remote);
      if (chosen.progress && !latest.current.completion) {
        dirtyRef.current = true;
        const current = latest.current;
        const restored = core.createPlayState(model, chosen.progress);
        let restoredTimer = current.timer;
        if (chosen.restoredFromServer) {
          restoredTimer = core.createTimer(chosen.progress.elapsedSeconds);
          if (current.timer.started) restoredTimer = core.startTimer(restoredTimer, Date.now());
        }
        restoredTimer = core.setVisible(core.setPaused(restoredTimer, current.timer.paused, Date.now()), current.timer.visible, Date.now());
        update({ ...restored, autoCheck: current.state.autoCheck, cell: current.state.cell, entry: current.state.entry }, restoredTimer);
        latest.current.updatedAt = Date.parse(chosen.progress.updatedAt);
        if (chosen.restoredFromServer) setStatus('Progress restored from another device.');
      }
      persist(true);
    } catch { /* The queue retains writes until a connection returns. */ persist(true); }
  }
  const actions = useRef({ persist, restore, finish, check, reconcile });
  actions.current = { persist, restore, finish, check, reconcile };
  // The child solver is keyed by puzzle/version/member; stale requests are
  // aborted before another account's partition can render or issue writes.
  useEffect(() => {
    mounted.current = true;
    const abort = new AbortController();
    controller.current = abort;
    void actions.current.restore(abort).catch(() => { if (mounted.current) { setStatus('Could not restore device storage.'); readyRef.current = true; setReady(true); } });
    return () => { mounted.current = false; abort.abort(); if (syncTimer.current) clearTimeout(syncTimer.current); actions.current.persist(true); };
    // Identity and version changes remount this hook's owning solver.
  }, []);
  useEffect(() => {
    const tick = setInterval(() => setSeconds(core.elapsedSeconds(latest.current.timer, Date.now())), 1000);
    const lifecycle = AppState.addEventListener('change', value => {
      const next = core.setVisible(latest.current.timer, value === 'active', Date.now());
      latest.current.timer = next; setTimer(next); if (value === 'active') void actions.current.reconcile(); else actions.current.persist(true);
    });
    return () => { clearInterval(tick); lifecycle.remove(); };
  }, []);
  useEffect(() => {
    if (!ready || busy || completion || !online || !core.isFilled(model, state)) return;
    void actions.current.finish();
  }, [model, state, ready, online, completion, busy]);
  useEffect(() => {
    if (!ready || !state.autoCheck || !online || completion) return;
    const timeout = setTimeout(() => { void actions.current.check('grid', true); }, 500);
    return () => clearTimeout(timeout);
  }, [state.letters, state.autoCheck, online, ready, completion]);
  useEffect(() => { if (ready && online) void actions.current.reconcile(); /* session flusher owns retry/auth */
  }, [ready, online]);
  function keepGuest() {
    if (!guest) return;
    dirtyRef.current = true;
    update(core.createPlayState(model, guest), core.createTimer(guest.elapsedSeconds));
    latest.current.updatedAt = Date.parse(guest.updatedAt); setGuest(null); persist(true);
  }
  return { model, state, timer, seconds, ready, status, busy, online, completion, review, guest, keepGuest,
    pending: pending.length, needsAttention: pending.some(item => item.state === 'needs_attention'),
    change, select, pause, check, reveal,
    retryFinish() { attempted.current = null; void finish(); },
    setAutoCheck(enabled: boolean) { dirtyRef.current = true; update(core.setAutoCheck(latest.current.state, enabled)); persist(true); } };
}
