using System.Reflection;

namespace Lignum.Core.Tests;

public class ArchitectureTests
{
    private static readonly string[] DesktopAssemblies =
    [
        "PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml",
        "System.Windows.Forms", "Microsoft.WindowsDesktop.App",
    ];

    [Fact]
    public void Core_does_not_reference_any_Windows_desktop_framework()
    {
        var core = Assembly.Load("Lignum.Core");

        var referenced = core.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(referenced, name => DesktopAssemblies.Contains(name));
    }

    [Fact]
    public void Core_targets_plain_net_not_windows()
    {
        var core = Assembly.Load("Lignum.Core");

        var framework = core.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

        Assert.NotNull(framework);
        Assert.StartsWith(".NETCoreApp", framework.FrameworkName);
        Assert.Null(core.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>());
    }
}
