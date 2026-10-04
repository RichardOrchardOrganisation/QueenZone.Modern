document.getElementById("crossword-bulk")?.addEventListener("submit", event => {
  const form = event.currentTarget;
  form.querySelectorAll("input[name=rowVersions]").forEach(input => input.remove());
  document.querySelectorAll("input[name=ids]:checked").forEach(selected => {
    const input = document.createElement("input"); input.type = "hidden"; input.name = "rowVersions";
    input.value = selected.dataset.token; form.append(input);
  });
});
