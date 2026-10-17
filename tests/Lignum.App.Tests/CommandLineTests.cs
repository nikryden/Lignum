namespace Lignum.App.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void A_bare_argument_is_the_file_to_open() =>
        Assert.Equal(new CommandLine(null, @"C:\logs\a.log"), CommandLine.Parse([@"C:\logs\a.log"]));

    [Fact]
    public void Settings_dir_takes_the_next_argument_in_any_order()
    {
        Assert.Equal(new CommandLine(@"D:\s", "a.log"), CommandLine.Parse(["--settings-dir", @"D:\s", "a.log"]));
        Assert.Equal(new CommandLine(@"D:\s", "a.log"), CommandLine.Parse(["a.log", "--SETTINGS-DIR", @"D:\s"]));
    }

    [Fact]
    public void Mcp_mode_is_not_a_file_to_open() =>
        Assert.Equal(new CommandLine(@"D:\s", null, IsMcp: true), CommandLine.Parse(["--settings-dir", @"D:\s", "--mcp"]));

    [Fact]
    public void No_arguments_means_defaults() =>
        Assert.Equal(new CommandLine(null, null), CommandLine.Parse([]));
}
