import * as core from './crossword-core.js';
import { accountHint, rememberAccount } from './crossword-account.js';

const root = document.querySelector('[data-crossword]');
if (root) await initialise(root);

async function initialise(root) {
    const config = JSON.parse(root.querySelector('[data-puzzle]').textContent);
    const puzzle = config.puzzle;
    let memberId = config.preview ? null : config.memberId ?? (config.offlineShell ? accountHint() : null);
    if (!config.offlineShell && !config.preview) rememberAccount(memberId);
    const model = core.createModel(puzzle);
    const find = selector => root.querySelector(selector);
    const cells = [...root.querySelectorAll('[data-cell]')];
    const clues = [...root.querySelectorAll('[data-clue]')];
    const input = find('[data-input]');
    const previewStorage = new Map();
    const storage = config.preview ? { getItem: key => previewStorage.get(key) ?? null, setItem: (key, value) => previewStorage.set(key, value) } :
        { getItem: key => window.localStorage.getItem(key), setItem: (key, value) => window.localStorage.setItem(key, value) };
    let baseKey = core.progressStorageKey(puzzle.id, memberId);
    let practice = practiceEnabled();
    let key = practice ? `${baseKey}:practice` : baseKey;
    let attemptGeneration = 0;
    function practiceEnabled() {
        try { return storage.getItem(`${baseKey}:practice-mode`) === puzzle.playVersion; } catch { return false; }
    }
    function setPractice(enabled) {
        practice = enabled;
        key = enabled ? `${baseKey}:practice` : baseKey;
        try { storage.setItem(`${baseKey}:practice-mode`, enabled ? puzzle.playVersion : ''); }
        catch { announce('Device storage is unavailable. Keep this page open to retain practice.'); }
    }
    let requestToken = find('[data-antiforgery] input')?.value;
    let updatedAt = Date.now();
    let state = core.createPlayState(model);
    let timer = core.createTimer();
    let saveTimeout;
    let autoCheckTimeout;
    let completed = false;
    let checkingCompletion = false;
    let attemptedLetters = null;
    let previousEntry = -1;
    const explanations = new Map();
    function review(items) {
        for (const item of items ?? []) {
            if (!item.explanation && !item.answer) continue;
            explanations.set(`${item.number} ${item.direction}`, item);
        }
        const list = find('[data-review-list]');
        list.replaceChildren();
        for (const [label, item] of explanations) {
            const title = document.createElement('dt');
            title.textContent = `${label}${item.answer ? ': ' + item.answer : ''}`;
            const text = document.createElement('dd');
            text.textContent = item.explanation ?? '';
            list.append(title, text);
        }
        find('[data-review]').hidden = explanations.size === 0;
    }
    const announce = message => { find('[data-status]').textContent = message; };
    const snapshot = () => ({ ...core.progressSnapshot(state, timer, puzzle.playVersion, Date.now()), updatedAt: new Date(updatedAt).toISOString() });

    async function request(handler, body) {
        if (!navigator.onLine) throw new Error('Needs a connection');
        const generation = attemptGeneration;
        const response = await fetch(`${location.pathname}?handler=${handler}`, {
            method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
            headers: { ...(memberId && (!practice || handler === 'Session') ? { 'X-Crossword-Member': memberId } : {}), ...(body ? { 'Content-Type': 'application/json', RequestVerificationToken: requestToken ?? '' } : {}) },
            body: body ? JSON.stringify(body) : undefined,
        });
        if (generation !== attemptGeneration) throw new Error('The previous attempt has ended.');
        if (response.status === 204) return null;
        if (response.status === 409) throw new Error('This crossword has changed. Reload the page before continuing.');
        if (response.status === 401) throw new Error('Sign in to sync progress. Your letters are saved on this device.');
        if (!response.ok) throw new Error(response.status === 429 ? 'Please wait a moment before trying again.' : 'Could not connect. Your letters stay on this device.');
        const result = await response.json();
        if (result.playVersion !== puzzle.playVersion) throw new Error('This crossword has changed. Reload the page before continuing.');
        return result;
    }

    function render() {
        find('[data-practice]').hidden = !practice;
        find('[data-auto-check]').checked = state.autoCheck;
        const entry = model.entries[state.entry];
        for (const cell of cells) {
            const index = Number(cell.dataset.cell);
            cell.disabled = timer.paused || completed;
            cell.tabIndex = index === state.cell ? 0 : -1;
            cell.classList.toggle('is-selected', index === state.cell);
            cell.classList.toggle('is-entry', entry.cells.includes(index));
            cell.classList.toggle('is-incorrect', state.incorrectCells.includes(index));
            cell.setAttribute('aria-selected', String(index === state.cell));
            cell.setAttribute('aria-label', core.cellLabel(model, state, index));
            cell.querySelector('[data-letter]').textContent = state.letters[index] === '.' ? '' : state.letters[index];
            cell.querySelector('[data-marker]').textContent = state.revealedCells.includes(index) ? '▲' : state.incorrectCells.includes(index) ? '×' : '';
        }
        clues.forEach((clue, index) => {
            const active = index === state.entry;
            clue.classList.toggle('is-active', active);
            clue.classList.toggle('is-filled', core.entryFilled(model, state, index));
            clue.querySelector('[data-clue-tick]').textContent = core.entryFilled(model, state, index) ? ' ✓' : '';
            if (active && previousEntry !== state.entry && window.matchMedia('(min-width: 1024px)').matches) clue.scrollIntoView({ block: 'nearest' });
        });
        find('[data-active-clue]').textContent = `${entry.number} ${entry.direction}: ${entry.clue} ${entry.enumeration}`;
        find('[data-grid-scroll]').hidden = timer.paused && !completed;
        find('[data-paused]').hidden = !timer.paused || completed;
        find('[data-pause]').textContent = timer.paused ? 'Resume' : 'Pause';
        previousEntry = state.entry;
        updateTime();
        positionInput();
        if (document.activeElement === input) requestAnimationFrame(keepSelectionVisible);
    }

    function updateTime() {
        const seconds = core.elapsedSeconds(timer, Date.now());
        find('[data-timer]').textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
    }

    async function save(sync = false) {
        const write = snapshot();
        const saved = await core.saveLocalProgress(storage, key, write);
        if (!saved) announce('Device storage is unavailable. Keep this page open to keep your letters.');
        if (memberId && !practice && sync && navigator.onLine && !completed) {
            try { await request('Save', write); }
            catch (error) { announce(error.message); }
        }
    }

    function changed(next) {
        if (timer.paused || completed || next === state) return;
        const before = state.letters;
        state = next;
        updatedAt = Date.now();
        if (before !== state.letters) timer = core.startTimer(timer, Date.now());
        render();
        void save();
        clearTimeout(saveTimeout);
        saveTimeout = setTimeout(() => { void save(true); }, 2000);
        clearTimeout(autoCheckTimeout);
        if (state.autoCheck && before !== state.letters) autoCheckTimeout = setTimeout(() => { void check('grid', true); }, 500);
        void maybeComplete();
    }

    function selection(scope) {
        const entry = model.entries[state.entry];
        return scope === 'entry' ? { scope, number: entry.number, direction: entry.direction } :
            scope === 'cell' ? { scope, cell: state.cell } : { scope };
    }

    async function check(scope, autoCheck = false) {
        const letters = state.letters;
        try {
            const result = await request('Check', { letters, selection: selection(scope), autoCheck, playVersion: puzzle.playVersion });
            if (state.letters !== letters) return;
            state = core.applyCheck(state, result.cells);
            review(result.explanations);
            render();
            if (!autoCheck) announce(result.cells.some(cell => cell.status === 'incorrect') ? 'Some letters are incorrect, marked with a cross and strike-through.' : 'No incorrect letters in this selection.');
        } catch (error) { announce(error.message); }
    }

    async function reveal(scope) {
        if (scope !== 'cell' && !window.confirm("Reveal this selection? This solve won't count for the leaderboard.")) return;
        try {
            const result = await request('Reveal', { selection: selection(scope), playVersion: puzzle.playVersion });
            state = core.applyReveal(model, state, result.cells);
            review(result.explanations);
            updatedAt = Date.now();
            timer = core.startTimer(timer, Date.now());
            render();
            announce('Revealed letters are marked with triangles. This is an assisted solve.');
            await save(true);
            void maybeComplete();
        } catch (error) { announce(error.message); }
    }

    async function maybeComplete() {
        if (completed || checkingCompletion || !core.isFilled(model, state) || attemptedLetters === state.letters || !navigator.onLine) return;
        checkingCompletion = true;
        attemptedLetters = state.letters;
        try {
            const result = memberId && !practice ? await request('Complete', snapshot()) :
                await request('Check', { letters: state.letters, selection: { scope: 'grid' }, playVersion: puzzle.playVersion });
            if (state.letters !== attemptedLetters) return;
            if (!(result.correct ?? result.complete)) {
                find('[data-check-shortcut]').hidden = false;
                announce('Not quite — some letters are wrong. Use Check grid to see which ones.');
                return;
            }
            completed = true;
            find('[data-check-shortcut]').hidden = true;
            review(result.review ?? result.explanations);
            timer = core.setPaused(timer, true, Date.now());
            render();
            const seconds = result.completion?.elapsedSeconds ?? core.elapsedSeconds(timer, Date.now());
            const clean = result.completion?.clean ?? core.isClean(state);
            find('[data-completion-message]').textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')} · ${clean ? 'Clean solve ✓' : 'Assisted solve'}`;
            find('[data-completion]').hidden = false;
            find('[data-toolbar]').hidden = true;
            announce('Crossword complete.');
            await save();
        } catch (error) { attemptedLetters = null; announce(error.message); }
        finally { checkingCompletion = false; }
    }

    function selectedCell() { return cells.find(cell => Number(cell.dataset.cell) === state.cell); }
    function positionInput() {
        const cell = selectedCell();
        if (!cell) return;
        const bounds = cell.getBoundingClientRect();
        const board = find('.qz-crossword-board').getBoundingClientRect();
        input.style.top = `${bounds.top - board.top}px`;
        input.style.left = `${bounds.left - board.left}px`;
    }
    function keepSelectionVisible() {
        if (document.activeElement !== input || timer.paused || completed) return;
        const cell = selectedCell();
        if (!cell) return;
        const scroller = find('[data-grid-scroll]');
        let bounds = cell.getBoundingClientRect();
        const gridBounds = scroller.getBoundingClientRect();
        if (bounds.left < gridBounds.left + 4) scroller.scrollLeft += bounds.left - gridBounds.left - 4;
        else if (bounds.right > gridBounds.right - 4) scroller.scrollLeft += bounds.right - gridBounds.right + 4;
        bounds = cell.getBoundingClientRect();
        const top = Math.max(viewport?.offsetTop ?? 0, document.querySelector('[data-masthead]')?.getBoundingClientRect().bottom ?? 0) + 12;
        const bottom = root.classList.contains('keyboard-open') ? find('[data-active-clue]').getBoundingClientRect().top - 12 :
            (viewport?.offsetTop ?? 0) + (viewport?.height ?? window.innerHeight) - 12;
        if (bottom - top >= bounds.height) {
            if (bounds.bottom > bottom) window.scrollBy({ top: bounds.bottom - bottom, behavior: 'instant' });
            else if (bounds.top < top) window.scrollBy({ top: bounds.top - top, behavior: 'instant' });
        }
        positionInput();
    }
    function focusCell() {
        positionInput();
        input.focus({ preventScroll: true });
        requestAnimationFrame(keepSelectionVisible);
    }

    cells.forEach(cell => cell.addEventListener('click', () => {
        state = core.selectCell(model, state, Number(cell.dataset.cell)); render(); focusCell();
    }));
    clues.forEach((clue, index) => clue.addEventListener('click', () => {
        state = core.jumpToEntry(model, state, index); render(); focusCell();
    }));
    input.addEventListener('input', () => {
        for (const letter of input.value) changed(core.typeLetter(model, state, letter));
        input.value = '';
    });
    input.addEventListener('beforeinput', event => {
        if (event.inputType !== 'deleteContentBackward' && event.inputType !== 'deleteContentForward') return;
        event.preventDefault(); changed(core.deleteLetter(model, state)); input.value = '';
    });
    root.addEventListener('keydown', event => {
        if (event.key === 'Escape') { find('[data-menu]').open = false; return; }
        if (event.target !== input && !event.target.matches('[data-cell]')) return;
        if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); void check('entry'); }
        else if (event.key === 'Tab') { event.preventDefault(); state = core.nextEntry(model, state, event.shiftKey ? -1 : 1); render(); focusCell(); }
        else if (event.key === ' ' && event.code === 'Space' && !event.isComposing && !root.classList.contains('keyboard-open')) { event.preventDefault(); state = core.toggleDirection(model, state); render(); }
        else if (event.key.startsWith('Arrow')) { event.preventDefault(); state = core.arrow(model, state, event.key); render(); focusCell(); }
        else if (event.key === 'Backspace' || event.key === 'Delete') { event.preventDefault(); changed(core.deleteLetter(model, state)); }
        else if (event.target !== input && /^[a-z]$/i.test(event.key)) { event.preventDefault(); changed(core.typeLetter(model, state, event.key)); }
    });
    function pause() { updatedAt = Date.now(); timer = core.setPaused(timer, !timer.paused, Date.now()); render(); void save(true); }
    find('[data-pause]').addEventListener('click', pause);
    find('[data-resume]').addEventListener('click', () => { pause(); focusCell(); });
    find('[data-auto-check]').addEventListener('change', event => { state = core.setAutoCheck(state, event.target.checked); updatedAt = Date.now(); void save(true); if (state.autoCheck) void check('grid', true); });
    root.querySelectorAll('[data-check]').forEach(button => button.addEventListener('click', () => { void check(button.dataset.check); }));
    root.querySelectorAll('[data-reveal]').forEach(button => button.addEventListener('click', () => { void reveal(button.dataset.reveal); }));
    find('[data-print]').addEventListener('click', () => window.print());
    function clearAttempt() {
        attemptGeneration++;
        clearTimeout(saveTimeout);
        clearTimeout(autoCheckTimeout);
        completed = false;
        checkingCompletion = false;
        attemptedLetters = null;
        explanations.clear();
        review([]);
        find('[data-check-shortcut]').hidden = true;
        find('[data-completion]').hidden = true;
        find('[data-toolbar]').hidden = false;
    }
    root.querySelectorAll('[data-play-again]').forEach(button => button.addEventListener('click', async () => {
        if (!window.confirm('Start again with a blank grid and timer on this device? This practice attempt will not replace your saved progress or first leaderboard result.')) return;
        clearAttempt();
        setPractice(true);
        state = core.createPlayState(model);
        timer = core.createTimer();
        updatedAt = Date.now();
        render();
        await save();
        announce('Practice started. Your saved attempt and first result are preserved.');
        cells.find(cell => Number(cell.dataset.cell) === state.cell)?.focus();
    }));
    find('[data-resume-saved]').addEventListener('click', async () => {
        if (!window.confirm('Return to your saved attempt? Your practice letters stay on this device.')) return;
        await save();
        clearAttempt();
        setPractice(false);
        await restoreProgress(false);
        void maybeComplete();
        announce('Saved attempt restored.');
    });
    find('[data-share]').addEventListener('click', async () => {
        const text = `I finished ${puzzle.title} on QueenZone: ${find('[data-completion-message]').textContent}.`;
        try { if (navigator.share) await navigator.share({ title: puzzle.title, text, url: location.href });
            else { await navigator.clipboard.writeText(`${text} ${location.href}`); announce('Result copied.'); } }
        catch { announce('Sharing was cancelled or unavailable.'); }
    });
    document.addEventListener('visibilitychange', () => { timer = core.setVisible(timer, !document.hidden, Date.now()); void save(true); });
    window.addEventListener('pagehide', () => { timer = core.setVisible(timer, false, Date.now()); void save(); });
    window.addEventListener('online', async () => {
        await save();
        try { await refreshSession(); } catch (error) { announce(error.message); return; }
        const paused = timer.paused;
        await restoreProgress(false);
        timer = core.setPaused(timer, paused, Date.now());
        render();
        await save(true);
        void maybeComplete();
    });
    const viewport = window.visualViewport;
    let unfocusedViewportHeight = viewport?.height ?? window.innerHeight;
    function keyboard() {
        const focused = document.activeElement === input;
        if (!focused) unfocusedViewportHeight = Math.max(unfocusedViewportHeight, viewport?.height ?? window.innerHeight);
        const inset = viewport ? Math.max(0, document.documentElement.clientHeight - viewport.height - viewport.offsetTop) : 0;
        root.style.setProperty('--keyboard-inset', `${inset}px`);
        root.classList.toggle('keyboard-open', focused && (inset > 100 || unfocusedViewportHeight - (viewport?.height ?? window.innerHeight) > 100));
        requestAnimationFrame(keepSelectionVisible);
    }
    viewport?.addEventListener('resize', keyboard);
    viewport?.addEventListener('scroll', keyboard);
    input.addEventListener('focus', keyboard);
    input.addEventListener('blur', keyboard);
    setInterval(updateTime, 1000);

    async function refreshSession() {
        if (config.preview) return;
        const session = await request('Session');
        if (memberId !== session.memberId) {
            attemptGeneration++;
            clearTimeout(saveTimeout);
            clearTimeout(autoCheckTimeout);
            checkingCompletion = false;
            completed = false;
            attemptedLetters = null;
            explanations.clear();
            review([]);
            find('[data-check-shortcut]').hidden = true;
            find('[data-completion]').hidden = true;
            find('[data-toolbar]').hidden = false;
        }
        memberId = session.memberId;
        rememberAccount(memberId);
        requestToken = session.tokens;
        baseKey = core.progressStorageKey(puzzle.id, memberId);
        practice = practiceEnabled();
        key = practice ? `${baseKey}:practice` : baseKey;
    }

    async function restoreProgress(offerGuest) {
        const local = await core.loadLocalProgress(storage, key, model, puzzle.playVersion);
        let remote = null;
        if (memberId && !practice && navigator.onLine) { try { remote = await request('Progress'); } catch (error) { announce(error.message); } }
        const chosen = core.chooseProgress(model, puzzle.playVersion, local, remote);
        let restored = chosen.progress;
        if (offerGuest && memberId && !practice && !local && !remote) {
            const guest = await core.loadLocalProgress(storage, core.progressStorageKey(puzzle.id), model, puzzle.playVersion);
            if (guest && window.confirm('Keep progress from this device?')) restored = guest;
        }
        state = core.createPlayState(model, restored ?? {});
        timer = core.createTimer(restored?.elapsedSeconds ?? 0);
        updatedAt = restored ? Date.parse(restored.updatedAt) : Date.now();
        render();
        if (chosen.restoredFromServer) announce('Progress restored from another device.');
        return restored;
    }
    if (config.offlineShell && navigator.onLine) {
        try { await refreshSession(); } catch (error) { announce(error.message); }
    }
    const restored = await restoreProgress(true);
    find('[data-toolbar]').hidden = false;
    if (restored && navigator.onLine) await save(true);
    void maybeComplete();
}
