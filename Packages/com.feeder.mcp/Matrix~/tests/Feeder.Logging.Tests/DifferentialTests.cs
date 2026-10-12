namespace Feeder.Logging.Tests;

public class DifferentialTests
{
    public static IEnumerable<object[]> ScenarioNames()
        => Differential.Real.Scenarios.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Behaves_like_the_original_library(string scenario)
    {
        var original = Differential.Real.Scenarios.Run(scenario);
        var mine = Differential.Mine.Scenarios.Run(scenario);

        Assert.Equal(original, mine);
    }

    [Fact]
    public void Both_compilations_define_the_same_scenarios()
    {
        Assert.Equal(Differential.Real.Scenarios.Names.ToArray(), Differential.Mine.Scenarios.Names.ToArray());
    }

    [Fact]
    public void The_two_compilations_bind_to_different_libraries()
    {
        Assert.Equal("Microsoft.Extensions.Logging", Differential.Real.Scenarios.LibraryNamespace);
        Assert.Equal("Feeder.Logging", Differential.Mine.Scenarios.LibraryNamespace);
    }
}
