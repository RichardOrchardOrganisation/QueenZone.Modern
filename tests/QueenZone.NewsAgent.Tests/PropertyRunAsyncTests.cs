using System.Security.Cryptography;
using System.Text;
using CsCheck;
using QueenZone.TestUtilities;

namespace QueenZone.NewsAgent.Tests;

public sealed class PropertyRunAsyncTests
{
    [Fact]
    public async Task Failing_async_property_includes_seed_in_exception()
    {
        var failure = new Exception("Async property assertion failed.");
        var asserted = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(nameof(Failing_async_property_includes_seed_in_exception), Gen.Int, async _ =>
            {
                await Task.Yield();
                asserted++;
                throw failure;
            }, iter: 100));

        Assert.Contains(nameof(Failing_async_property_includes_seed_in_exception), exception.Message);
        Assert.Contains("initial seed:", exception.Message);
        Assert.Contains("\nseed: ", exception.Message);
        Assert.Contains($"\nShrinks: {asserted - 1}\n", exception.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Same(failure, exception.InnerException);
        Assert.InRange(asserted, 2, 99);
    }

    [Fact]
    public async Task Generator_throw_is_reported()
    {
        var failure = new Exception("Generation failed.");
        var generated = 0;
        var asserted = 0;
        var gen = Gen.Int.Select<int, int>(_ =>
        {
            generated++;
            throw failure;
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(nameof(Generator_throw_is_reported), gen, _ =>
            {
                asserted++;
                return Task.CompletedTask;
            }, iter: 2));

        Assert.Contains("The generator threw.", exception.Message);
        Assert.Contains("\nShrinks: 0\n", exception.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(2, generated);
        Assert.Equal(0, asserted);
    }

    [Fact]
    public async Task Later_generator_throw_preserves_assertion_failure()
    {
        var failure = new Exception("First assertion failed.");
        var generated = 0;
        var gen = Gen.Const(() =>
        {
            if (++generated > 1)
            {
                throw new Exception("Later generation failed.");
            }

            return 42;
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(nameof(Later_generator_throw_preserves_assertion_failure), gen, async _ =>
            {
                await Task.Yield();
                throw failure;
            }, iter: 2));

        Assert.Same(failure, exception.InnerException);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains("\n42\nShrinks: 0\n", exception.Message);
        Assert.DoesNotContain("The generator threw.", exception.Message);
        Assert.DoesNotContain("Later generation failed.", exception.Message);
        Assert.Equal(2, generated);
    }

    [Fact]
    public async Task Successful_async_property_runs_all_iterations()
    {
        var asserted = 0;

        await PropertyRun.RunAsync(nameof(Successful_async_property_runs_all_iterations), Gen.Int, async _ =>
        {
            await Task.Yield();
            asserted++;
        }, iter: 17);

        Assert.Equal(17, asserted);
    }

    [Fact]
    public async Task Faulted_task_is_reported_as_assertion_failure()
    {
        var failure = new Exception("Task assertion failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(nameof(Faulted_task_is_reported_as_assertion_failure), Gen.Const(42),
                _ => Task.FromException(failure), iter: 1));

        Assert.Same(failure, exception.InnerException);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains("\n42\nShrinks: 0\n", exception.Message);
        Assert.DoesNotContain("The generator threw.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Argument_null_in_assertion_is_reported_with_seed(bool faultedTask)
    {
        const string propertyName = nameof(Argument_null_in_assertion_is_reported_with_seed);
        var failure = new ArgumentNullException("value");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(propertyName, Gen.Const(42),
                _ => faultedTask ? Task.FromException(failure) : throw failure, iter: 1));

        var expectedSeed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(propertyName)))[..16];
        Assert.Same(failure, exception.InnerException);
        Assert.Contains(propertyName, exception.Message);
        Assert.Contains($"initial seed: {expectedSeed}", exception.Message);
        Assert.Contains("\nseed: ", exception.Message);
        Assert.Contains("\n42\nShrinks: 0\n", exception.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.DoesNotContain("The generator threw.", exception.Message);
    }

    [Fact]
    public async Task Run_and_RunAsync_share_default_seed()
    {
        const string propertyName = "Shared async property default seed";
        var failure = new Exception("Seed assertion failed.");

        var syncException = Assert.Throws<InvalidOperationException>(() =>
            PropertyRun.Run(propertyName, Gen.Int, _ => throw failure, iter: 1));
        var asyncException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(propertyName, Gen.Int, _ => Task.FromException(failure), iter: 1));
        var syncSeed = syncException.Message.Split("initial seed: ")[1].Split(')')[0];
        var asyncSeed = asyncException.Message.Split("initial seed: ")[1].Split(')')[0];
        var expectedSeed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(propertyName)))[..16];

        Assert.Equal(expectedSeed, syncSeed);
        Assert.Equal(syncSeed, asyncSeed);
    }

    [Fact]
    public async Task Hung_assertion_fails_on_first_timeout_with_seed()
    {
        var calls = 0;
        var pending = new TaskCompletionSource();
        var run = PropertyRun.RunAsync(nameof(Hung_assertion_fails_on_first_timeout_with_seed), Gen.Int, _ =>
        {
            calls++;
            return pending.Task;
        }, iter: 20, iterationTimeout: TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Contains(nameof(Hung_assertion_fails_on_first_timeout_with_seed), exception.Message);
        Assert.Contains("initial seed:", exception.Message);
        Assert.Contains("\nseed: ", exception.Message);
        Assert.Contains("Shrinks: 0", exception.Message);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Null_task_is_a_usage_error()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            PropertyRun.RunAsync(nameof(Null_task_is_a_usage_error), Gen.Int, _ => null!));

        Assert.Equal("task", exception.ParamName);
        Assert.DoesNotContain("initial seed:", exception.Message);
    }

    [Fact]
    public async Task Caller_cancellation_aborts_pending_assertion()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var calls = 0;
        var run = PropertyRun.RunAsync(nameof(Caller_cancellation_aborts_pending_assertion), Gen.Int, _ =>
        {
            calls++;
            entered.SetResult();
            return pending.Task;
        }, cancellationToken: cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Already_cancelled_run_does_not_assert()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PropertyRun.RunAsync(nameof(Already_cancelled_run_does_not_assert), Gen.Int, _ =>
            {
                calls++;
                return Task.CompletedTask;
            }, cancellationToken: cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Unrelated_cancellation_is_a_counterexample()
    {
        var failure = new OperationCanceledException();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PropertyRun.RunAsync(nameof(Unrelated_cancellation_is_a_counterexample), Gen.Int,
                _ => Task.FromException(failure), iter: 1));

        Assert.Same(failure, exception.InnerException);
        Assert.Contains("initial seed:", exception.Message);
    }
}
