/** Shared, solution-free crossword navigation and timer. No DOM, storage, network or platform dependencies. */
export function createModel(puzzle) {
    const size = puzzle.width * puzzle.height;
    if (!Number.isInteger(puzzle.width) || !Number.isInteger(puzzle.height) || puzzle.width < 5 || puzzle.width > 15 ||
        puzzle.height < 5 || puzzle.height > 15 || !Array.isArray(puzzle.blocks) || puzzle.blocks.length !== size ||
        puzzle.blocks.some(block => typeof block !== 'boolean') || !Array.isArray(puzzle.clues)) {
        throw new Error('The crossword grid is incompatible.');
    }
    const entries = [...puzzle.clues].sort((a, b) => a.direction.localeCompare(b.direction) || a.number - b.number)
        .map(clue => createEntry(puzzle, clue));
    if (entries.length === 0) throw new Error('The crossword has no playable clues.');
    const memberships = Array.from({ length: size }, () => []);
    entries.forEach((entry, entryIndex) => entry.cells.forEach(cell => memberships[cell].push(entryIndex)));
    if (puzzle.blocks.some((block, cell) => !block && memberships[cell].length === 0)) throw new Error('The crossword has an orphan cell.');
    return { width: puzzle.width, height: puzzle.height, blocks: [...puzzle.blocks], entries, memberships };
}

function createEntry(puzzle, clue) {
    if (!Number.isInteger(clue.number) || clue.number < 1 || !['across', 'down'].includes(clue.direction) || !Number.isInteger(clue.length) || clue.length < 3 || clue.length > 15 ||
        !Number.isInteger(clue.row) || !Number.isInteger(clue.column) || clue.row < 0 || clue.column < 0) {
        throw new Error('The crossword clue is incompatible.');
    }
    const cells = Array.from({ length: clue.length }, (_, offset) =>
        (clue.row + (clue.direction === 'down' ? offset : 0)) * puzzle.width +
        clue.column + (clue.direction === 'across' ? offset : 0));
    const endRow = clue.row + (clue.direction === 'down' ? clue.length - 1 : 0);
    const endColumn = clue.column + (clue.direction === 'across' ? clue.length - 1 : 0);
    if (endRow >= puzzle.height || endColumn >= puzzle.width || cells.some(cell => puzzle.blocks[cell])) {
        throw new Error('The crossword clue runs outside the white cells.');
    }
    return { ...clue, key: `${clue.number}-${clue.direction}`, cells };
}

export function emptyLetters(model) {
    return model.blocks.map(block => block ? '#' : '.').join('');
}

export function restoreLetters(model, letters) {
    if (typeof letters !== 'string' || letters.length !== model.blocks.length) return emptyLetters(model);
    return [...letters].map((letter, index) => model.blocks[index] ? '#' : /^[a-z]$/i.test(letter) ? letter.toUpperCase() : '.').join('');
}

export function createPlayState(model, progress = {}) {
    const letters = restoreLetters(model, progress.letters);
    const revealedCells = (progress.revealedCells ?? []).filter(cell => Number.isInteger(cell) && model.memberships[cell]?.length > 0);
    return {
        letters, cell: firstEmpty(model.entries[0], letters), entry: 0,
        incorrectCells: [], correctCells: [], revealedCells: [...new Set(revealedCells)],
        autoCheck: false, autoCheckUsed: progress.autoCheckUsed === true,
    };
}

function firstEmpty(entry, letters) {
    return entry.cells.find(cell => letters[cell] === '.') ?? entry.cells[0];
}

export function selectCell(model, state, cell) {
    const available = model.memberships[cell];
    if (!available?.length) return state;
    if (cell === state.cell && available.length > 1) return toggleDirection(model, state);
    const direction = model.entries[state.entry].direction;
    const entry = available.find(index => model.entries[index].direction === direction) ?? available[0];
    return { ...state, cell, entry };
}

export function toggleDirection(model, state) {
    const available = model.memberships[state.cell];
    const entry = available.find(index => index !== state.entry) ?? state.entry;
    return { ...state, entry };
}

export function jumpToEntry(model, state, entry) {
    if (!model.entries[entry]) return state;
    return { ...state, entry, cell: firstEmpty(model.entries[entry], state.letters) };
}

export function nextEntry(model, state, offset = 1) {
    const entry = (state.entry + offset % model.entries.length + model.entries.length) % model.entries.length;
    return jumpToEntry(model, state, entry);
}

export function typeLetter(model, state, letter) {
    if (typeof letter !== 'string' || !/^[a-z]$/i.test(letter)) return state;
    const changed = changeLetter(state, state.cell, letter.toUpperCase());
    return nextEmpty(model, changed);
}

function changeLetter(state, cell, letter) {
    if (state.letters[cell] === letter) return state;
    const letters = [...state.letters];
    letters[cell] = letter;
    return { ...state, letters: letters.join(''),
        incorrectCells: state.incorrectCells.filter(index => index !== cell),
        correctCells: state.correctCells.filter(index => index !== cell) };
}

function nextEmpty(model, state) {
    const entry = model.entries[state.entry];
    const remaining = entry.cells.slice(entry.cells.indexOf(state.cell) + 1);
    const cell = remaining.find(index => state.letters[index] === '.');
    if (cell !== undefined) return { ...state, cell };
    for (let offset = 1; offset <= model.entries.length; offset++) {
        const entryIndex = (state.entry + offset) % model.entries.length;
        const next = model.entries[entryIndex].cells.find(index => state.letters[index] === '.');
        if (next !== undefined) return { ...state, cell: next, entry: entryIndex };
    }
    return state;
}

export function deleteLetter(model, state) {
    if (state.letters[state.cell] !== '.') return changeLetter(state, state.cell, '.');
    const entry = model.entries[state.entry];
    const position = entry.cells.indexOf(state.cell);
    if (position > 0) return changeLetter({ ...state, cell: entry.cells[position - 1] }, entry.cells[position - 1], '.');
    const previous = (state.entry + model.entries.length - 1) % model.entries.length;
    const cell = model.entries[previous].cells.at(-1);
    return changeLetter({ ...state, entry: previous, cell }, cell, '.');
}

/** An arrow perpendicular to the entry switches direction first, then subsequent presses move. */
export function arrow(model, state, key) {
    const horizontal = key === 'ArrowLeft' || key === 'ArrowRight';
    if (!horizontal && key !== 'ArrowUp' && key !== 'ArrowDown') return state;
    const direction = horizontal ? 'across' : 'down';
    const perpendicular = model.memberships[state.cell].find(index => model.entries[index].direction === direction);
    if (model.entries[state.entry].direction !== direction && perpendicular !== undefined) return { ...state, entry: perpendicular };
    const step = key === 'ArrowLeft' || key === 'ArrowUp' ? -1 : 1;
    return moveOnAxis(model, state, horizontal, step, direction);
}

function moveOnAxis(model, state, horizontal, step, direction) {
    const row = Math.floor(state.cell / model.width);
    const column = state.cell % model.width;
    const limit = horizontal ? model.width : model.height;
    for (let position = (horizontal ? column : row) + step; position >= 0 && position < limit; position += step) {
        const cell = horizontal ? row * model.width + position : position * model.width + column;
        const available = model.memberships[cell];
        if (!available.length) continue;
        const entry = available.find(index => model.entries[index].direction === direction) ?? available[0];
        return { ...state, cell, entry };
    }
    return state;
}

export function applyCheck(state, checks) {
    const checked = new Set(checks.map(cell => cell.index));
    return { ...state,
        incorrectCells: [...state.incorrectCells.filter(cell => !checked.has(cell)), ...checks.filter(cell => cell.status === 'incorrect').map(cell => cell.index)],
        correctCells: [...state.correctCells.filter(cell => !checked.has(cell)), ...checks.filter(cell => cell.status === 'correct').map(cell => cell.index)] };
}

export function applyReveal(model, state, cells) {
    let changed = state;
    const valid = cells.filter(cell => model.memberships[cell.index]?.length > 0 && /^[A-Z]$/.test(cell.letter));
    for (const cell of valid) changed = changeLetter(changed, cell.index, cell.letter);
    return { ...changed, revealedCells: [...new Set([...state.revealedCells, ...valid.map(cell => cell.index)])],
        correctCells: [...new Set([...changed.correctCells, ...valid.map(cell => cell.index)])] };
}

export function setAutoCheck(state, enabled) {
    return { ...state, autoCheck: enabled, autoCheckUsed: state.autoCheckUsed || enabled };
}

export function isClean(state) { return state.revealedCells.length === 0 && !state.autoCheckUsed; }
export function isFilled(model, state) { return model.blocks.every((block, cell) => block || state.letters[cell] !== '.'); }
export function entryFilled(model, state, entry) { return model.entries[entry].cells.every(cell => state.letters[cell] !== '.'); }

export function cellLabel(model, state, cell) {
    if (model.blocks[cell]) return 'Block';
    const available = model.memberships[cell];
    const entryIndex = available.includes(state.entry) ? state.entry : available[0];
    const entry = model.entries[entryIndex];
    const position = entry.cells.indexOf(cell) + 1;
    const letter = state.letters[cell] === '.' ? 'blank' : state.letters[cell];
    const other = available.find(index => index !== entryIndex);
    const crossing = other === undefined ? '' : ` Also ${model.entries[other].number} ${model.entries[other].direction}.`;
    const marker = state.revealedCells.includes(cell) ? ' Revealed.' : state.incorrectCells.includes(cell) ? ' Incorrect.' : '';
    return `${entry.number} ${entry.direction}, ${entry.cells.length} letters, letter ${position}, ${letter}.${crossing}${marker}`;
}

export function createTimer(elapsedSeconds = 0) {
    return { elapsedMs: Math.max(0, Number.isFinite(elapsedSeconds) ? elapsedSeconds : 0) * 1000,
        runningSince: null, started: elapsedSeconds > 0, paused: false, visible: true };
}

export function elapsedSeconds(timer, now) {
    return Math.floor((timer.elapsedMs + (timer.runningSince === null ? 0 : Math.max(0, now - timer.runningSince))) / 1000);
}

export function startTimer(timer, now) {
    if (timer.paused || !timer.visible || timer.runningSince !== null) return timer;
    return { ...timer, started: true, runningSince: now };
}

function stopTimer(timer, now) {
    return { ...timer, elapsedMs: timer.elapsedMs + (timer.runningSince === null ? 0 : Math.max(0, now - timer.runningSince)), runningSince: null };
}

export function setPaused(timer, paused, now) {
    const next = { ...stopTimer(timer, now), paused };
    return !paused && next.visible && next.started ? startTimer(next, now) : next;
}

export function setVisible(timer, visible, now) {
    const next = { ...stopTimer(timer, now), visible };
    return visible && !next.paused && next.started ? startTimer(next, now) : next;
}

/** A grid version is mandatory: an old device must never recreate a reset attempt. */
export function progressSnapshot(state, timer, playVersion, now) {
    if (!validVersion(playVersion)) throw new Error('Reload the crossword before saving.');
    return { playVersion, letters: state.letters, elapsedSeconds: elapsedSeconds(timer, now),
        revealedCells: [...state.revealedCells], autoCheckUsed: state.autoCheckUsed, updatedAt: new Date(now).toISOString() };
}

function validVersion(version) {
    return typeof version === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(version) &&
        version !== '00000000-0000-0000-0000-000000000000';
}

/** Fail closed on corrupt or obsolete storage, while callers keep playing with an empty state. */
export function parseProgress(model, playVersion, value) {
    if (!validVersion(playVersion) || !value || value.playVersion !== playVersion || typeof value.letters !== 'string' ||
        value.letters.length !== model.blocks.length || !Number.isInteger(value.elapsedSeconds) || value.elapsedSeconds < 0 ||
        typeof value.autoCheckUsed !== 'boolean' || !Array.isArray(value.revealedCells) ||
        typeof value.updatedAt !== 'string' || !Number.isFinite(Date.parse(value.updatedAt))) return null;
    if ([...value.letters].some((letter, cell) => model.blocks[cell] ? letter !== '#' : !/^[A-Z.]$/.test(letter)) ||
        value.revealedCells.some(cell => !Number.isInteger(cell) || !model.memberships[cell]?.length)) return null;
    return { playVersion, letters: value.letters, elapsedSeconds: value.elapsedSeconds,
        revealedCells: [...new Set(value.revealedCells)], autoCheckUsed: value.autoCheckUsed, updatedAt: value.updatedAt };
}

export function chooseProgress(model, playVersion, local, server) {
    const device = parseProgress(model, playVersion, local);
    const remote = parseProgress(model, playVersion, server);
    const restoredFromServer = remote !== null && (device === null || Date.parse(remote.updatedAt) > Date.parse(device.updatedAt));
    const chosen = restoredFromServer ? remote : device ?? remote;
    if (chosen === null) return { progress: null, restoredFromServer };
    const revealedCells = [...new Set([...(device?.revealedCells ?? []), ...(remote?.revealedCells ?? [])])].sort((a, b) => a - b);
    return { progress: { ...chosen, revealedCells, autoCheckUsed: Boolean(device?.autoCheckUsed || remote?.autoCheckUsed) }, restoredFromServer };
}

export function progressStorageKey(puzzleId, memberId = null) {
    return `qz:crossword:v1:${encodeURIComponent(puzzleId)}:${memberId ? `member:${encodeURIComponent(memberId)}` : 'guest'}`;
}

/** Works with browser localStorage and asynchronous mobile storage; failures never block play. */
export async function loadLocalProgress(storage, key, model, playVersion) {
    try {
        const raw = await storage.getItem(key);
        return parseProgress(model, playVersion, raw === null ? null : JSON.parse(raw));
    } catch { return null; }
}

export async function saveLocalProgress(storage, key, progress) {
    try {
        await storage.setItem(key, JSON.stringify(progress));
        return true;
    } catch { return false; }
}
