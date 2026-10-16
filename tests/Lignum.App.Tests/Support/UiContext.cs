namespace Lignum.App.Tests.Support;

/// <summary>
/// View models capture <see cref="SynchronizationContext.Current"/> to marshal background events to the UI
/// thread. Inside an xUnit test that is xUnit's AsyncTestSyncContext, which posts to the thread pool, so
/// updates land "some time later" and assertions race them. Build view models through
/// <see cref="None{T}"/> so they see no context and apply updates inline, deterministically.
/// </summary>
internal static class UiContext
{
    public static T None<T>(Func<T> create)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return create();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
