(() => {
  const share = document.querySelector('[data-sprint-share]');
  if (share) {
    share.addEventListener('click', async () => {
      const text = share.dataset.shareText;
      const url = `${location.origin}/quizzes/sprint`;
      try {
        if (navigator.share) {
          await navigator.share({ title: 'Queenzone Quiz Sprint', text, url });
        } else {
          await navigator.clipboard.writeText(`${text} ${url}`);
          share.textContent = 'Copied to clipboard';
        }
      } catch {
        // Sharing was cancelled or unavailable; nothing to do.
      }
    });
  }

  const game = document.querySelector('[data-sprint-game]');
  if (!game) return;

  const form = game.querySelector('[data-sprint-form]');
  const ticket = game.querySelector('[data-sprint-ticket]').value;
  const answerUrl = game.dataset.answerUrl;
  const questions = [...form.querySelectorAll('[data-sprint-question]')];
  const seconds = game.querySelector('[data-sprint-seconds]');
  const progress = game.querySelector('[data-sprint-progress]');
  const fill = game.querySelector('[data-sprint-fill]');
  const streakLabel = game.querySelector('[data-sprint-streak]');
  const scoreOutput = game.querySelector('[data-sprint-score]');
  const announcement = game.querySelector('[data-sprint-announcement]');
  const duration = 60000;
  const feedbackMs = 620;
  const streakBonusAt = 3;
  const endsAt = performance.now() + Math.max(0, Number(game.dataset.expiresAt) - Number(game.dataset.serverNow));
  let current = 0;
  let submitted = false;
  let locked = false;
  let score = 0;
  let streak = 0;
  let warnedThirty = false;
  let warnedTen = false;

  function submit() {
    if (submitted) return;
    submitted = true;
    form.requestSubmit();
  }

  function renderTally() {
    scoreOutput.textContent = String(score);
    streakLabel.textContent = streak >= streakBonusAt
      ? `Streak ×${streak} · Double points`
      : streak >= 2 ? `Streak ×${streak}` : 'No streak';
    streakLabel.classList.toggle('qz-sprint__streak--hot', streak >= streakBonusAt);
  }

  function showNext() {
    questions[current].hidden = true;
    current += 1;
    if (current >= questions.length) {
      submit();
      return;
    }
    questions[current].hidden = false;
    locked = false;
    questions[current].querySelector('legend').focus();
  }

  async function check(questionId, optionId) {
    try {
      const response = await fetch(answerUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ticket, questionId, optionId }),
      });
      return response.ok ? await response.json() : null;
    } catch {
      return null;
    }
  }

  async function answer(input) {
    if (locked || submitted) return;
    locked = true;
    const question = questions[current];
    const labels = [...question.querySelectorAll('.qz-sprint__answer')];
    const picked = input.closest('.qz-sprint__answer');
    // Lock the other options so the posted answer cannot change while feedback shows.
    for (const other of question.querySelectorAll('input')) {
      if (other !== input) other.disabled = true;
    }
    picked.classList.add('qz-sprint__answer--picked');

    const result = await check(question.dataset.questionId, input.dataset.optionGuid);
    if (submitted) return;
    if (result) {
      const bonus = streak >= streakBonusAt;
      if (result.isCorrect) {
        score += bonus ? 2 : 1;
        streak += 1;
        picked.classList.add('qz-sprint__answer--correct');
      } else {
        streak = 0;
        picked.classList.add('qz-sprint__answer--wrong');
        const right = labels.find(label => label.dataset.optionId === result.correctOptionId);
        if (right) right.classList.add('qz-sprint__answer--correct');
      }
      renderTally();
    }
    setTimeout(showNext, feedbackMs);
  }

  function updateTimer() {
    const remaining = Math.max(0, endsAt - performance.now());
    const wholeSeconds = Math.ceil(remaining / 1000);
    seconds.textContent = String(wholeSeconds);
    seconds.classList.toggle('qz-sprint__seconds--urgent', wholeSeconds <= 10);
    progress.setAttribute('aria-valuenow', String(wholeSeconds));
    fill.style.width = `${Math.min(100, remaining / duration * 100)}%`;
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
    if (event.target.matches('input[type="radio"]')) answer(event.target);
  });
  const interval = setInterval(updateTimer, 100);
  updateTimer();
})();
