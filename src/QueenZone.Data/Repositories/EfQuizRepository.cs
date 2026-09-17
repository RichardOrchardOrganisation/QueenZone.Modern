using Microsoft.EntityFrameworkCore;
using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class EfQuizRepository(QueenZoneDbContext dbContext, TimeProvider timeProvider) : IQuizRepository
{
    public async Task<IReadOnlyList<QuizAdminItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var quizzes = await dbContext.Quizzes
            .AsNoTracking()
            .Include(quiz => quiz.Questions)
            .OrderByDescending(quiz => quiz.CreatedAt)
            .ToListAsync(cancellationToken);

        var attemptCounts = await dbContext.QuizAttempts
            .AsNoTracking()
            .GroupBy(attempt => attempt.QuizId)
            .Select(group => new { QuizId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.QuizId, row => row.Count, cancellationToken);

        return quizzes
            .Select(quiz => new QuizAdminItem(
                quiz.Id,
                quiz.Title,
                quiz.IsPublished,
                quiz.PublishedAt,
                quiz.CreatedAt,
                quiz.Questions.Count,
                attemptCounts.GetValueOrDefault(quiz.Id)))
            .ToList();
    }

    public async Task<QuizAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .AsNoTracking()
            .Include(item => item.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (quiz is null)
        {
            return null;
        }

        var attemptCount = await dbContext.QuizAttempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.QuizId == id, cancellationToken);

        return ToDetail(quiz, attemptCount);
    }

    public async Task<Guid> CreateAsync(
        AdminQuizDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default)
    {
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        var now = timeProvider.GetUtcNow();
        var entity = BuildEntity(draft, createdByMemberId, now);
        dbContext.Quizzes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(Guid id, AdminQuizDraft draft, CancellationToken cancellationToken = default)
    {
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(draft));
        }

        var quiz = await dbContext.Quizzes
            .Include(item => item.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");

        await EnsureNoResultsAsync(id, "Questions and options cannot be changed after a quiz has results.", cancellationToken);

        quiz.Title = draft.Title.Trim();
        quiz.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim();

        foreach (var question in quiz.Questions)
        {
            dbContext.QuizOptions.RemoveRange(question.Options);
        }

        dbContext.QuizQuestions.RemoveRange(quiz.Questions);

        quiz.Questions = BuildQuestions(quiz.Id, draft.Questions);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");

        quiz.IsPublished = true;
        quiz.PublishedAt ??= timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnpublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");

        quiz.IsPublished = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new QuizException(QuizException.NotFound, "Quiz was not found.");

        await EnsureNoResultsAsync(id, "This quiz has results and cannot be deleted.", cancellationToken);
        dbContext.Quizzes.Remove(quiz);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAttemptAsync(
        Guid quizId,
        Guid memberAccountId,
        int score,
        int correctCount,
        int questionCount,
        CancellationToken cancellationToken = default)
    {
        dbContext.QuizAttempts.Add(new QuizAttemptEntity
        {
            Id = Guid.NewGuid(),
            QuizId = quizId,
            MemberAccountId = memberAccountId,
            Score = score,
            CorrectCount = correctCount,
            QuestionCount = questionCount,
            CompletedAt = timeProvider.GetUtcNow(),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuizListItem>> GetPublishedAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Quizzes
            .AsNoTracking()
            .Where(quiz => quiz.IsPublished)
            .OrderByDescending(quiz => quiz.PublishedAt)
            .Select(quiz => new QuizListItem(quiz.Id, quiz.Title, quiz.Description, quiz.Questions.Count))
            .ToListAsync(cancellationToken);

    public async Task<QuizPlayView?> GetPublishedForPlayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .AsNoTracking()
            .Include(item => item.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == id && item.IsPublished, cancellationToken);

        return quiz is null ? null : ToPlayView(quiz);
    }

    public async Task<QuizSubmissionResult?> SubmitAsync(
        Guid quizId,
        Guid? memberAccountId,
        IReadOnlyList<QuizAnswerSubmission> answers,
        CancellationToken cancellationToken = default)
    {
        var quiz = await dbContext.Quizzes
            .AsNoTracking()
            .Include(item => item.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.Id == quizId && item.IsPublished, cancellationToken);
        if (quiz is null)
        {
            return null;
        }

        var recorded = memberAccountId is Guid memberId;
        var result = QuizScoring.Score(quiz, answers, recorded);
        if (recorded)
        {
            await RecordAttemptAsync(
                quizId,
                memberAccountId!.Value,
                result.Score,
                result.CorrectCount,
                result.QuestionCount,
                cancellationToken);
        }

        return result;
    }

    public async Task<QuizLeaderboardResult> GetLeaderboardAsync(
        QuizLeaderboardScope scope,
        Guid? viewerMemberId,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.QuizAttempts.AsNoTracking();
        if (scope == QuizLeaderboardScope.Week)
        {
            var weekStart = QuizScoring.GetCurrentWeekStartUtc(timeProvider.GetUtcNow());
            query = query.Where(attempt => attempt.CompletedAt >= weekStart);
        }

        var attempts = await query.ToListAsync(cancellationToken);
        return QuizScoring.BuildLeaderboard(attempts, viewerMemberId, top);
    }

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

    internal static QuizEntity BuildEntity(AdminQuizDraft draft, Guid createdByMemberId, DateTimeOffset createdAt)
    {
        var quizId = Guid.NewGuid();
        return new QuizEntity
        {
            Id = quizId,
            Title = draft.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim(),
            IsPublished = false,
            CreatedAt = createdAt,
            PublishedAt = null,
            CreatedByMemberId = createdByMemberId,
            Questions = BuildQuestions(quizId, draft.Questions),
        };
    }

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
                        .ToList()))
                .ToList());

    private async Task EnsureNoResultsAsync(Guid quizId, string message, CancellationToken cancellationToken)
    {
        var hasResults = await dbContext.QuizAttempts
            .AsNoTracking()
            .AnyAsync(attempt => attempt.QuizId == quizId, cancellationToken);
        if (hasResults)
        {
            throw new QuizException(QuizException.HasResults, message);
        }
    }
}
