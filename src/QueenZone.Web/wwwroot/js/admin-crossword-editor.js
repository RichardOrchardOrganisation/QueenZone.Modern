const root = document.querySelector('[data-crossword-editor]');
if (root) {
  const form = root.querySelector('[data-editor-form]');
  const source = root.querySelector('[data-seed]');
  const grid = root.querySelector('[data-grid]');
  const cluePanel = root.querySelector('[data-clues]');
  const validationPanel = root.querySelector('[data-validation]');
  let seed;
  try { seed = JSON.parse(source.value); } catch { seed = null; }
  if (seed) {
    let selected = 0, runs = [], validationRequest = 0, timer, controller;
    root.querySelector('[data-builder]').hidden = false;
    root.querySelector('[data-json-label]').hidden = true;
    const fields = [...root.querySelectorAll('[data-field]')];
    fields.forEach(field => {
      field.value = seed[field.dataset.field];
      field.addEventListener('input', () => {
        const key = field.dataset.field;
        if (key === 'width' || key === 'height') {
          const value = Number(field.value);
          if (!Number.isInteger(value) || value < 5 || value > 15) return;
          seed[key] = value;
          seed.grid = Array.from({ length: seed.height }, (_, row) =>
            Array.from({ length: seed.width }, (_, column) => seed.grid[row]?.[column] ?? '.').join(''));
          selected = Math.min(selected, seed.width * seed.height - 1);
          drawGrid();
        } else seed[key] = field.value;
        changed();
      });
    });
    function sync() { source.value = JSON.stringify(seed); }
    function changed() {
      // An edit invalidates the in-flight snapshot immediately, including while
      // the next validation is still waiting for the debounce timer.
      validationRequest++; controller?.abort();
      sync(); clearTimeout(timer); timer = setTimeout(() => { void validate(); }, 180);
    }
    function cellValue(index) { return seed.grid[Math.floor(index / seed.width)][index % seed.width]; }
    function setCell(index, value) {
      const row = Math.floor(index / seed.width), column = index % seed.width;
      seed.grid[row] = seed.grid[row].slice(0, column) + value + seed.grid[row].slice(column + 1);
    }
    function focusCell(index) {
      selected = Math.max(0, Math.min(seed.width * seed.height - 1, index));
      grid.children[selected]?.focus();
    }
    function drawGrid() {
      grid.style.gridTemplateColumns = `repeat(${seed.width},44px)`;
      grid.replaceChildren();
      for (let index = 0; index < seed.width * seed.height; index++) {
        const button = document.createElement('button'); button.type = 'button';
        button.className = `admin-crossword-cell${cellValue(index) === '#' ? ' is-block' : ''}`;
        const number = runs.find(run => run.row * seed.width + run.column === index)?.number;
        if (number) { const badge = document.createElement('small'); badge.textContent = number; button.append(badge); }
        button.append(document.createTextNode(cellValue(index) === '.' ? '' : cellValue(index)));
        button.setAttribute('aria-label', `Row ${Math.floor(index / seed.width) + 1}, column ${index % seed.width + 1}, ${cellValue(index) === '#' ? 'block' : cellValue(index) === '.' ? 'empty' : cellValue(index)}${number ? `, number ${number}` : ''}`);
        button.addEventListener('click', () => {
          selected = index; button.focus();
          if (form.querySelector('input[name=mode]:checked').value !== 'blocks') return;
          const value = cellValue(index) === '#' ? '.' : '#'; setCell(index, value);
          if (root.querySelector('[data-symmetry]').checked) setCell(seed.width * seed.height - index - 1, value);
          drawGrid(); focusCell(index); changed();
        });
        button.addEventListener('keydown', event => {
          const offsets = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -seed.width, ArrowDown: seed.width };
          if (event.key in offsets) { event.preventDefault(); focusCell(index + offsets[event.key]); return; }
          if (form.querySelector('input[name=mode]:checked').value !== 'letters' || cellValue(index) === '#') return;
          if (!/^[a-z]$/i.test(event.key) && event.key !== 'Backspace' && event.key !== 'Delete') return;
          event.preventDefault(); setCell(index, /^[a-z]$/i.test(event.key) ? event.key.toUpperCase() : '.');
          drawGrid(); focusCell(event.key === 'Backspace' ? index - 1 : index + 1); changed();
        });
        grid.append(button);
      }
    }
    function drawClues(previousRuns) {
      const previous = seed.entries ?? [];
      seed.entries = runs.map(run => {
        const oldRun = previousRuns.find(previous => previous.row === run.row && previous.column === run.column && previous.direction === run.direction);
        const oldNumber = oldRun?.number ?? (previousRuns.length === 0 ? run.number : null);
        const old = previous.find(entry => entry.number === oldNumber && entry.direction === run.direction);
        return { number: run.number, direction: run.direction, answer: run.answer,
          clue: old?.clue ?? '', enumeration: old?.enumeration ?? `(${run.answer.length})`, explanation: old?.explanation ?? '' };
      });
      cluePanel.replaceChildren();
      seed.entries.forEach(entry => {
        const group = document.createElement('fieldset'), legend = document.createElement('legend');
        legend.textContent = `${entry.number} ${entry.direction}: ${entry.answer}`; group.append(legend);
        for (const [key, title, maximum] of [['clue', 'Clue', 500], ['enumeration', 'Enumeration', 50], ['explanation', 'Explanation (optional)', 300]]) {
          const label = document.createElement('label'); label.textContent = title;
          const input = document.createElement(key === 'explanation' ? 'textarea' : 'input');
          input.value = entry[key]; input.maxLength = maximum;
          input.addEventListener('input', () => { entry[key] = input.value; changed(); });
          label.append(input); group.append(label);
        }
        cluePanel.append(group);
      });
      sync();
    }
    function showIssues(errors, warnings) {
      validationPanel.replaceChildren();
      if (!errors.length && !warnings.length) validationPanel.textContent = 'No validation errors or warnings.';
      [...errors.map(issue => ({ ...issue, level: 'Error' })), ...warnings.map(issue => ({ ...issue, level: 'Warning' }))].forEach(issue => {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = `${issue.level}: ${issue.code}: ${issue.message}`;
        button.addEventListener('click', () => {
          const run = runs.find(run => run.number === issue.number && run.direction === (issue.direction === 0 ? 'across' : 'down'));
          if (issue.row != null && issue.column != null) focusCell(issue.row * seed.width + issue.column);
          else if (run) focusCell(run.row * seed.width + run.column);
          else grid.children[selected]?.focus();
        });
        const paragraph = document.createElement('p'); paragraph.append(button); validationPanel.append(paragraph);
      });
    }
    async function validate() {
      const request = ++validationRequest; controller?.abort(); controller = new AbortController();
      try {
        const response = await fetch(`${location.pathname}?handler=Validate`, { method: 'POST', credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('input[name=__RequestVerificationToken]').value },
          body: JSON.stringify(seed), signal: controller.signal });
        if (!response.ok) throw new Error('Validation is unavailable. Reload the page to retry.');
        const result = await response.json(); if (request !== validationRequest) return;
        const shape = JSON.stringify(runs.map(run => [run.number, run.direction, run.row, run.column, run.answer]));
        const previousRuns = runs;
        runs = result.runs ?? [];
        if (shape !== JSON.stringify(runs.map(run => [run.number, run.direction, run.row, run.column, run.answer]))) {
          const gridHadFocus = grid.contains(document.activeElement); drawGrid(); drawClues(previousRuns);
          if (gridHadFocus) focusCell(selected);
          // Revalidate after generated entries have been included in the draft.
          changed();
        } else showIssues(result.errors ?? [], result.warnings ?? []);
      } catch (error) { if (request === validationRequest && error.name !== 'AbortError') validationPanel.textContent = error.message; }
    }
    form.addEventListener('submit', sync); drawGrid(); await validate();
  }
}
