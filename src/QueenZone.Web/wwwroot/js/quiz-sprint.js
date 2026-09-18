(() => {
  const game = document.querySelector('[data-sprint-game]');
  if (!game) return;

  const form = game.querySelector('[data-sprint-form]');
  const questions = [...form.querySelectorAll('[data-sprint-question]')];
  const seconds = game.querySelector('[data-sprint-seconds]');
  const progress = game.querySelector('[data-sprint-progress]');
  const fill = game.querySelector('[data-sprint-fill]');
  const counter = game.querySelector('[data-sprint-counter]');
  const announcement = game.querySelector('[data-sprint-announcement]');
  const skip = game.querySelector('[data-sprint-skip]');
  const finish = game.querySelector('[data-sprint-finish]');
  const duration = 60000;
  const initialRemaining = Math.max(0, Number(game.dataset.expiresAt) - Number(game.dataset.serverNow));
  const startedAt = performance.now();
  let current = 0;
  let submitted = false;
  let warnedThirty = false;
  let warnedTen = false;

  function submit() {
    if (submitted) return;
    submitted = true;
    skip.disabled = true;
    finish.disabled = true;
    form.requestSubmit();
  }

  function showNext() {
    questions[current].hidden = true;
    current += 1;
    if (current >= questions.length) {
      submit();
      return;
    }
    questions[current].hidden = false;
    counter.textContent = `Question ${current + 1} of ${questions.length}`;
    questions[current].querySelector('legend').focus();
  }

  function updateTimer() {
    const remaining = Math.max(0, initialRemaining - (performance.now() - startedAt));
    const wholeSeconds = Math.ceil(remaining / 1000);
    seconds.textContent = `${wholeSeconds}s`;
    progress.setAttribute('aria-valuenow', String(wholeSeconds));
    fill.style.width = `${Math.min(100, remaining / duration * 100)}%`;
    game.classList.toggle('qz-sprint--warning', remaining <= 30000 && remaining > 10000);
    game.classList.toggle('qz-sprint--urgent', remaining <= 10000);
    if (remaining <= 30000 && !warnedThirty) {
      warnedThirty = true;
      announcement.textContent = '30 seconds remaining';
    }
    if (remaining <= 10000 && !warnedTen) {
      warnedTen = true;
      announcement.textContent = '10 seconds remaining';
    }
    if (remaining <= 0) {
      clearInterval(interval);
      submit();
    }
  }

  form.addEventListener('change', event => {
    if (event.target.matches('input[type="radio"]') && !submitted) showNext();
  });
  skip.addEventListener('click', () => {
    if (!submitted) showNext();
  });
  form.addEventListener('submit', () => {
    submitted = true;
    skip.disabled = true;
    finish.disabled = true;
  });
  const interval = setInterval(updateTimer, 100);
  updateTimer();
})();
