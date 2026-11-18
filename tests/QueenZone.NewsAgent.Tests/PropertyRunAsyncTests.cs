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
}
