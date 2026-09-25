(function () {
  "use strict";

  var form = document.querySelector(".admin-quiz-form");
  if (!form) {
    return;
  }

  var questionsContainer = form.querySelector("[data-quiz-questions]");
  var addQuestionButton = form.querySelector("[data-add-question]");
  var questionTemplate = document.querySelector("[data-question-template]");
  var optionTemplate = document.querySelector("[data-option-template]");
  var maxQuestions = parseInt(form.getAttribute("data-max-questions"), 10) || 50;
  var maxOptions = parseInt(form.getAttribute("data-max-options"), 10) || 4;

  function questionCount() {
    return questionsContainer.querySelectorAll("[data-quiz-question]").length;
  }

  function optionCount(questionEl) {
    return questionEl.querySelectorAll("[data-quiz-option]").length;
  }

  function renumberQuestions() {
    var questions = questionsContainer.querySelectorAll("[data-quiz-question]");
    questions.forEach(function (questionEl, questionIndex) {
      questionEl.querySelector("legend").textContent = "Question " + (questionIndex + 1);
      questionEl.querySelectorAll("input, textarea, select").forEach(function (input) {
        input.name = input.name.replace(/questions\[[^\]]*\]/, "questions[" + questionIndex + "]");
      });
      questionEl.querySelectorAll("[data-quiz-option]").forEach(function (optionEl, optionIndex) {
        var radio = optionEl.querySelector('input[type="radio"]');
        if (radio) {
          radio.value = optionIndex;
        }
        var textLabel = optionEl.querySelector("label:last-child");
        if (textLabel) {
          textLabel.firstChild.textContent = "Option " + (optionIndex + 1);
        }
      });
    });
  }

  function addOptionTo(questionEl) {
    if (!optionTemplate || optionCount(questionEl) >= maxOptions) {
      return;
    }

    var fragment = optionTemplate.content.cloneNode(true);
    var optionsContainer = questionEl.querySelector("[data-quiz-options]");
    optionsContainer.appendChild(fragment);
    renumberQuestions();
  }

  function addQuestion() {
    if (!questionTemplate || questionCount() >= maxQuestions) {
      return;
    }

    var fragment = questionTemplate.content.cloneNode(true);
    questionsContainer.appendChild(fragment);
    renumberQuestions();
    bindQuestion(questionsContainer.lastElementChild);
  }

  function bindQuestion(questionEl) {
    var addOptionButton = questionEl.querySelector("[data-add-option]");
    if (addOptionButton) {
      addOptionButton.addEventListener("click", function () {
        addOptionTo(questionEl);
      });
    }
  }

  if (addQuestionButton) {
    addQuestionButton.addEventListener("click", addQuestion);
  }

  questionsContainer.querySelectorAll("[data-quiz-question]").forEach(bindQuestion);
})();
