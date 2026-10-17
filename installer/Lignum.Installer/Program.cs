// The Lignum installer (ticket 07, docs/adr/0001-ship-an-installer.md), built with FalkForge:
//   dotnet publish src/Lignum.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Version=X.Y.Z
//   dotnet run -c Release --project installer/Lignum.Installer -- -o artifacts/Lignum-X.Y.Z.msi
// LIGNUM_VERSION sets the version (default 0.1.0); LIGNUM_PUBLISH_DIR the folder with Lignum.exe
// (default: the publish folder above).
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

var root = FindRepoRoot();
var publishDir = Environment.GetEnvironmentVariable("LIGNUM_PUBLISH_DIR")
                 ?? Path.Combine(root, "src", "Lignum.App", "bin", "Release", "net10.0-windows", "win-x64", "publish");
var exe = Path.Combine(publishDir, "Lignum.exe");
var icon = Path.Combine(root, "src", "Lignum.App", "Assets", "Lignum.ico");
var version = Version.Parse(Environment.GetEnvironmentVariable("LIGNUM_VERSION") ?? "0.1.0");
if (!File.Exists(exe))
{
    Console.Error.WriteLine($"Lignum.exe not found in {publishDir}. Run the publish command first.");
    return 1;
}

return Installer.Build(args, package =>
{
    package.Name = "Lignum";
    package.Manufacturer = "Lignum";
    package.Version = version;
    // Never change: Windows uses it to find older versions to upgrade.
    package.UpgradeCode = new Guid("6f0b6f53-2c47-4b8e-9d0c-6b2f3f1f7a51");
    // The default is the current user only (no admin rights); the Advanced dialogs let the user pick all users.
    package.Scope = InstallScope.PerUser;
    package.UseDialogSet(MsiDialogSet.Advanced);
    // A dual-purpose package: per user (under %LocalAppData%\Programs, no admin) unless the scope dialog says
    // all users. Without these two, a per-user install would still target Program Files.
    package.Property("ALLUSERS", "2");
    package.Property("MSIINSTALLPERUSER", "1");
    package.LicenseFile = Path.Combine(AppContext.BaseDirectory, "License.rtf");
    package.ProductIcon(icon); // the icon in Apps & features
    // Upgrades replace the old version; settings, bookmarks and the session live in %APPDATA%\Lignum, which the
    // installer never touches, so they survive upgrades and uninstalls.
    package.MajorUpgrade(upgrade => upgrade.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate));

    package.Feature("Lignum", feature =>
    {
        feature.Title = "Lignum";
        feature.Description = "The log viewer, a Start-menu shortcut and the .lm package file type.";
        feature.IsRequired = true;
        feature.Files(files => files.Add(exe).To(KnownFolder.ProgramFiles / "Lignum"));
        feature.Shortcut("Lignum", exe).WithIcon(icon).WithDescription("Log viewer for very large files").OnStartMenu();
        feature.FileAssociation(".lm", association =>
        {
            association.ProgId("Lignum.Package");
            association.Description = "Lignum package with notes";
            association.IconFile = icon;
            association.Verb("open", "\"%1\"", verb => verb.Command = "&Open with Lignum");
        });
    });

    package.Feature("LogAssociation", feature =>
    {
        feature.Title = "Open .log files with Lignum";
        feature.Description = "Double-clicking a .log file opens it in Lignum.";
        feature.IsDefault = false; // off unless the user ticks it
        feature.FileAssociation(".log", association =>
        {
            association.ProgId("Lignum.LogFile");
            association.Description = "Log file";
            association.IconFile = icon;
            association.Verb("open", "\"%1\"", verb => verb.Command = "&Open with Lignum");
        });
    });
}, new MsiCompiler());

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lignum.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
