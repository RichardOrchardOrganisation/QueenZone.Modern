using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class InMemoryQuizRepository(
    SharedQuizStore store,
    TimeProvider? timeProvider = null) : IQuizRepository
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<QuizAdminItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((quizzes, attempts) =>
        {
            IReadOnlyList<QuizAdminItem> items = quizzes
                .OrderByDescending(quiz => quiz.CreatedAt)
                .Select(quiz => new QuizAdminItem(
                    quiz.Id,
                    quiz.Title,
                    quiz.IsPublished,
                    quiz.PublishedAt,
                    quiz.CreatedAt,
                    quiz.Questions.Count,
                    attempts.Count(attempt => attempt.QuizId == quiz.Id)))
                .ToList();
            return items;
        }));

    public Task<QuizAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((quizzes, attempts) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id);
            if (quiz is null)
            {
                return null;
            }

            var attemptCount = attempts.Count(attempt => attempt.QuizId == id);
            return ToDetail(quiz, attemptCount);
        }));

    public Task<Guid> CreateAsync(
        AdminQuizDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default)
    {
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        return Task.FromResult(store.Write((quizzes, _) =>
        {
            var entity = EfQuizRepository.BuildEntity(draft, createdByMemberId, timeProvider.GetUtcNow());
            quizzes.Add(entity);
            return entity.Id;
        }));
    }

    public Task UpdateAsync(Guid id, AdminQuizDraft draft, CancellationToken cancellationToken = default)
    {
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        store.Write((quizzes, attempts) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id)
                ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");
            EnsureNoResults(id, attempts, "Questions and options cannot be changed after a quiz has results.");

            quiz.Title = draft.Title.Trim();
            quiz.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();
            quiz.Questions = BuildQuestions(quiz.Id, draft.Questions);
        });
        return Task.CompletedTask;
    }

    public Task PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((quizzes, _) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id)
                ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");
            quiz.IsPublished = true;
            quiz.PublishedAt ??= timeProvider.GetUtcNow();
        });
        return Task.CompletedTask;
    }

    public Task UnpublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((quizzes, _) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id)
                ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");
            quiz.IsPublished = false;
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        store.Write((quizzes, attempts) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id)
                ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");
            EnsureNoResults(id, attempts, "This quiz has results and cannot be deleted.");
            quizzes.Remove(quiz);
        });
        return Task.CompletedTask;
    }

    public Task RecordAttemptAsync(
        Guid quizId,
        Guid memberAccountId,
        int score,
        int correctCount,
        int questionCount,
        CancellationToken cancellationToken = default)
    {
        store.Write((_, attempts) =>
        {
            attempts.Add(new QuizAttemptEntity
            {
                Id = Guid.NewGuid(),
                QuizId = quizId,
                MemberAccountId = memberAccountId,
                Score = score,
                CorrectCount = correctCount,
                QuestionCount = questionCount,
                CompletedAt = timeProvider.GetUtcNow(),
            });
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QuizListItem>> GetPublishedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((quizzes, _) =>
        {
            IReadOnlyList<QuizListItem> items = quizzes
                .Where(quiz => quiz.IsPublished)
                .OrderByDescending(quiz => quiz.PublishedAt)
                .Select(quiz => new QuizListItem(quiz.Id, quiz.Title, quiz.Description, quiz.Questions.Count))
                .ToList();
            return items;
        }));

    public Task<QuizPlayView?> GetPublishedForPlayAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((quizzes, _) =>
        {
            var quiz = quizzes.SingleOrDefault(item => item.Id == id && item.IsPublished);
            return quiz is null ? null : ToPlayView(quiz);
        }));

    public Task<QuizSubmissionResult?> SubmitAsync(
        Guid quizId,
        Guid? memberAccountId,
        IReadOnlyList<QuizAnswerSubmission> answers,
        CancellationToken cancellationToken = default)
    {
        var quiz = store.Read((quizzes, _) => quizzes.SingleOrDefault(item => item.Id == quizId && item.IsPublished));
        if (quiz is null)
        {
            return Task.FromResult<QuizSubmissionResult?>(null);
        }

        var recorded = memberAccountId is Guid;
        var result = QuizScoring.Score(quiz, answers, recorded);
        if (memberAccountId is Guid memberId)
        {
            store.Write((_, attempts) =>
            {
                attempts.Add(new QuizAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    QuizId = quizId,
                    MemberAccountId = memberId,
                    Score = result.Score,
                    CorrectCount = result.CorrectCount,
                    QuestionCount = result.QuestionCount,
                    CompletedAt = timeProvider.GetUtcNow(),
                });
            });
        }

        return Task.FromResult<QuizSubmissionResult?>(result);
    }

    public Task<QuizLeaderboardResult> GetLeaderboardAsync(
        QuizLeaderboardScope scope,
        Guid? viewerMemberId,
        int top = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Read((_, attempts) =>
        {
            IEnumerable<QuizAttemptEntity> scoped = attempts;
            if (scope == QuizLeaderboardScope.Week)
            {
                var weekStart = QuizScoring.GetCurrentWeekStartUtc(timeProvider.GetUtcNow());
                scoped = scoped.Where(attempt => attempt.CompletedAt >= weekStart);
            }

            return QuizScoring.BuildLeaderboard(scoped, viewerMemberId, top);
        }));

    private static QuizPlayView ToPlayView(QuizEntity quiz) =>
        new(
            quiz.Id,
            quiz.Title,
            quiz.Description,
            quiz.Questions
                .OrderBy(question => question.DisplayOrder)
                .Select(question => new QuizPlayQuestion(
                    question.Id,
                    question.QuestionText,
                    question.DisplayOrder,
                    question.Points,
                    question.Options
                        .OrderBy(option => option.DisplayOrder)
                        .Select(option => new QuizPlayOption(option.Id, option.OptionText))
                        .ToList()))
                .ToList());

    private static List<QuizQuestionEntity> BuildQuestions(Guid quizId, IReadOnlyList<QuizQuestionDraft> questions) =>
        questions
            .Select((question, questionIndex) =>
            {
                var questionId = Guid.NewGuid();
                var options = QuizValidation.NormalizeOptions(question.Options);
                return new QuizQuestionEntity
                {
                    Id = questionId,
                    QuizId = quizId,
                    QuestionText = question.Text.Trim(),
                    DisplayOrder = questionIndex,
                    Points = question.Points,
                    Category = string.IsNullOrWhiteSpace(question.Category) ? null : question.Category.Trim(),
                    Difficulty = question.Difficulty,
                    Options = options
                        .Select((option, optionIndex) => new QuizOptionEntity
                        {
                            Id = Guid.NewGuid(),
                            QuestionId = questionId,
                            OptionText = option.Text,
                            DisplayOrder = optionIndex,
                            IsCorrect = option.IsCorrect,
                        })
                        .ToList(),
                };
            })
            .ToList();

    private static QuizAdminDetail ToDetail(QuizEntity quiz, int attemptCount) =>
        new(
            quiz.Id,
            quiz.Title,
            quiz.Description,
            quiz.IsPublished,
            quiz.PublishedAt,
            quiz.CreatedAt,
            attemptCount,
            quiz.Questions
                .OrderBy(question => question.DisplayOrder)
                .Select(question => new QuizQuestionView(
                    question.Id,
                    question.QuestionText,
                    question.DisplayOrder,
                    question.Points,
                    question.Options
                        .OrderBy(option => option.DisplayOrder)
                        .Select(option => new QuizOptionView(option.Id, option.OptionText, option.DisplayOrder, option.IsCorrect))
                        .ToList(),
                    question.Category,
                    question.Difficulty))
                .ToList());

    private static void EnsureNoResults(Guid quizId, IReadOnlyList<QuizAttemptEntity> attempts, string message)
    {
        if (attempts.Any(attempt => attempt.QuizId == quizId))
        {
            throw new QuizException(QuizException.HasResults, message);
        }
    }
}
