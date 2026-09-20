using QueenZone.Data.Entities;

namespace QueenZone.Data;

public sealed class SharedQuizStore
{
    private readonly object sync = new();
    private readonly List<QuizEntity> quizzes = [];
    private readonly List<QuizAttemptEntity> attempts = [];
    private readonly List<QuizSprintRunEntity> sprintRuns = [];

    public T ReadSprintRuns<T>(Func<IReadOnlyList<QuizSprintRunEntity>, T> reader)
    {
        lock (sync)
        {
            return reader(sprintRuns);
        }
    }

    public void WriteSprintRuns(Action<List<QuizSprintRunEntity>> writer)
    {
        lock (sync)
        {
            writer(sprintRuns);
        }
    }

    public T Read<T>(Func<IReadOnlyList<QuizEntity>, IReadOnlyList<QuizAttemptEntity>, T> reader)
    {
        lock (sync)
        {
            return reader(quizzes, attempts);
        }
    }

    public T Write<T>(Func<List<QuizEntity>, List<QuizAttemptEntity>, T> writer)
    {
        lock (sync)
        {
            return writer(quizzes, attempts);
        }
    }

    public void Write(Action<List<QuizEntity>, List<QuizAttemptEntity>> writer)
    {
        lock (sync)
        {
            writer(quizzes, attempts);
        }
    }
}
