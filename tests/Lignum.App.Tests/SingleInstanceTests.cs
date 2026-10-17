namespace Lignum.App.Tests;

/// <summary>Single instance (ticket 07): a second start hands its file to the running Lignum.</summary>
public sealed class SingleInstanceTests
{
    private static string UniqueName() => SingleInstance.NameFor(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    [Fact]
    public void Only_the_first_start_becomes_the_running_instance()
    {
        var name = UniqueName();
        using (var first = SingleInstance.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstance.TryAcquire(name));
        }

        using var again = SingleInstance.TryAcquire(name); // the first one exited
        Assert.NotNull(again);
    }

    [Fact]
    public void A_second_start_hands_its_file_to_the_running_instance()
    {
        var name = UniqueName();
        using var running = SingleInstance.TryAcquire(name)!;
        var received = new List<string>();
        using var got = new CountdownEvent(2);
        running.Listen(message =>
        {
            lock (received) received.Add(message);
            got.Signal();
        });

        Assert.True(SingleInstance.TrySend(name, @"C:\logs\app.log"));
        Assert.True(SingleInstance.TrySend(name, null)); // no file: just come to the front

        Assert.True(got.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal([@"C:\logs\app.log", ""], received);
    }

    [Fact]
    public void Sending_with_nobody_running_fails_quickly() =>
        Assert.False(SingleInstance.TrySend(UniqueName(), "x.log", timeoutMilliseconds: 200));

    [Fact]
    public void Settings_folders_get_separate_instances() =>
        Assert.NotEqual(SingleInstance.NameFor(@"C:\a"), SingleInstance.NameFor(@"C:\b"));
}
