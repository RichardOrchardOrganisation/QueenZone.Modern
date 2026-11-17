using System.Security.Cryptography;
using System.Text;
using CsCheck;
using QueenZone.TestUtilities;

namespace QueenZone.NewsAgent.Tests;

public sealed class PropertyRunTests
{
    [Fact]
    public void Failing_property_includes_seed_in_exception()
    {
        var failure = new Exception("Property assertion failed.");
        var asserted = 0;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropertyRun.Run(nameof(Failing_property_includes_seed_in_exception), Gen.Int, _ =>
            {
                asserted++;
                throw failure;
            }, iter: 100));

        Assert.Contains(nameof(Failing_property_includes_seed_in_exception), exception.Message);
        Assert.Contains("initial seed:", exception.Message);
        Assert.Contains("\nseed: ", exception.Message);
        Assert.Contains($"\nShrinks: {asserted - 1}\n", exception.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Same(failure, exception.InnerException);
        Assert.InRange(asserted, 2, 99);
    }

    [Fact]
    public void Default_seed_is_deterministic_and_depends_on_property_name()
    {
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QZ_PROPERTY_SEED")));
        const string firstName = "First property";
        const string secondName = "Second property";

        var first = InitialSeed(firstName);
        var repeated = InitialSeed(firstName);
        var second = InitialSeed(secondName);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstName)))[..16], first);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secondName)))[..16], second);
        Assert.Equal(first, repeated);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Generator_throw_is_reported()
    {
        var failure = new Exception("Generation failed.");
        var generated = 0;
        var asserted = 0;
        var gen = Gen.Int.Select<int, int>(_ =>
        {
            generated++;
            throw failure;
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropertyRun.Run(nameof(Generator_throw_is_reported), gen, _ => asserted++, iter: 2));

        Assert.Contains("The generator threw.", exception.Message);
        Assert.Contains("\nShrinks: 0\n", exception.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal(2, generated);
        Assert.Equal(0, asserted);
    }

    [Fact]
    public void Later_generator_throw_preserves_assertion_failure()
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

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropertyRun.Run(nameof(Later_generator_throw_preserves_assertion_failure), gen,
                _ => throw failure, iter: 2));

        Assert.Same(failure, exception.InnerException);
        Assert.Contains(failure.Message, exception.Message);
        Assert.DoesNotContain("The generator threw.", exception.Message);
        Assert.DoesNotContain("Later generation failed.", exception.Message);
        Assert.Equal(2, generated);
    }

    private static string InitialSeed(string propertyName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PropertyRun.Run(propertyName, Gen.Int, _ => throw new Exception("Failed."), iter: 1));

        return exception.Message.Split("initial seed: ")[1].Split(')')[0];
    }
}
