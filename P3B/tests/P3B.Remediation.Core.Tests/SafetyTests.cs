using System.Reflection;
using P3B.Remediation.Core.Configuration;
using P3B.Remediation.Core.Execution;
using P3B.Remediation.Core.Installation;
using P3B.Remediation.Core.Verification;

namespace P3B.Remediation.Core.Tests;

/// <summary>
/// Guards that the core contains only decision logic, and that this test suite can only ever
/// drive in-memory fakes, so running the tests never installs, upgrades, uninstalls or
/// configures real software.
/// </summary>
public class SafetyTests
{
    private static readonly Assembly CoreAssembly = typeof(RemediationService).Assembly;
    private static readonly Assembly TestAssembly = typeof(SafetyTests).Assembly;

    private static readonly string[] SystemChangingAssemblies =
    [
        "System.Diagnostics.Process",
        "Microsoft.Win32.Registry",
        "System.Net.Http",
    ];

    [Fact]
    public void Production_core_contains_no_installer_environment_file_or_command_implementations()
    {
        var concreteTypes = CoreAssembly.GetTypes().Where(t => t is { IsInterface: false, IsAbstract: false }).ToArray();

        Assert.DoesNotContain(concreteTypes, t => typeof(IInstaller).IsAssignableFrom(t));
        Assert.DoesNotContain(concreteTypes, t => typeof(IUserEnvironment).IsAssignableFrom(t));
        Assert.DoesNotContain(concreteTypes, t => typeof(IFileProbe).IsAssignableFrom(t));
        Assert.DoesNotContain(concreteTypes, t => typeof(ICommandLocator).IsAssignableFrom(t));
    }

    [Fact]
    public void The_only_core_smoke_test_runner_delegates_to_check_handlers()
    {
        var runners = CoreAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(ISmokeTestRunner).IsAssignableFrom(t));

        Assert.Equal(typeof(SmokeTestService), Assert.Single(runners));
    }

    [Fact]
    public void The_only_core_executor_delegates_to_installers()
    {
        var executors = CoreAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(IRemediationExecutor).IsAssignableFrom(t));

        Assert.Equal(typeof(InstallationService), Assert.Single(executors));
    }

    [Fact]
    public void Production_core_does_not_reference_process_registry_or_network_apis()
    {
        AssertReferencesNone(CoreAssembly);
    }

    [Fact]
    public void Test_suite_does_not_reference_process_registry_or_network_apis()
    {
        AssertReferencesNone(TestAssembly);
    }

    [Fact]
    public void Every_executor_installer_handler_and_environment_in_the_tests_is_a_fake()
    {
        Type[] seams =
        [
            typeof(IRemediationExecutor), typeof(ISmokeTestRunner), typeof(IInstaller), typeof(IUserEnvironment),
            typeof(ISmokeCheckHandler), typeof(IFileProbe), typeof(ICommandLocator),
        ];
        var doubles = TestAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && seams.Any(s => s.IsAssignableFrom(t)))
            .ToArray();

        Assert.NotEmpty(doubles);
        Assert.All(doubles, t => Assert.StartsWith("Fake", t.Name, StringComparison.Ordinal));
    }

    private static void AssertReferencesNone(Assembly assembly)
    {
        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        foreach (var name in SystemChangingAssemblies)
        {
            Assert.DoesNotContain(name, referenced);
        }
    }
}
