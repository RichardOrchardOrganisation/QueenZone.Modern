const player = document.querySelector('[data-preview-player]');
if (player) {
  const answers = JSON.parse(document.querySelector('[data-preview-answers]').textContent).join('');
  player.querySelectorAll('[data-cell]').forEach(cell => {
    const answer = document.createElement('span'); answer.className = 'admin-crossword-answer';
    answer.textContent = answers[Number(cell.dataset.cell)]; answer.hidden = true; cell.append(answer);
  });
  document.querySelector('[data-show-answers]').addEventListener('change', event => {
    player.querySelectorAll('.admin-crossword-answer').forEach(answer => { answer.hidden = !event.target.checked; });
  });
  document.querySelector('[data-preview-width]').addEventListener('change', event => { player.style.maxWidth = event.target.value; });
}
