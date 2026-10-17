using System.Runtime.CompilerServices;
using Aetherphone.Core.Apps;
using Xunit;

namespace Aetherphone.Tests;

public sealed class ResumableAppsTests
{
    private static readonly string[] RestartFreshAppIds = { "camera", "casino", "games" };

    [Fact]
    public void EveryApp_ResumesWhereItWasLeft_ExceptTheOnesThatRestartFresh()
    {
        var apps = RegisteredApps();
        Assert.True(apps.Count > RestartFreshAppIds.Length, "No IPhoneApp types were discovered.");
        for (var index = 0; index < apps.Count; index++)
        {
            var (appId, type) = apps[index];
            var restartsFresh = Array.IndexOf(RestartFreshAppIds, appId) >= 0;
            Assert.True(typeof(IResumableApp).IsAssignableFrom(type) != restartsFresh,
                restartsFresh ? $"'{appId}' must restart fresh" : $"'{appId}' must resume where it was left");
        }
    }

    [Theory]
    [InlineData("camera")]
    [InlineData("casino")]
    [InlineData("games")]
    public void RestartFreshApp_IsRegisteredAndNotResumable(string appId)
    {
        var apps = RegisteredApps();
        for (var index = 0; index < apps.Count; index++)
        {
            if (apps[index].Id == appId)
            {
                Assert.False(typeof(IResumableApp).IsAssignableFrom(apps[index].Type));
                return;
            }
        }

        Assert.Fail($"'{appId}' is not registered");
    }

    private static List<(string Id, Type Type)> RegisteredApps()
    {
        var apps = new List<(string Id, Type Type)>();
        var types = typeof(IPhoneApp).Assembly.GetTypes();
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            if (type.IsAbstract || type.IsInterface || !typeof(IPhoneApp).IsAssignableFrom(type) ||
                type.Namespace is null || !type.Namespace.StartsWith("Aetherphone.Apps", StringComparison.Ordinal))
            {
                continue;
            }

            apps.Add((((IPhoneApp)RuntimeHelpers.GetUninitializedObject(type)).Id, type));
        }

        return apps;
    }
}
