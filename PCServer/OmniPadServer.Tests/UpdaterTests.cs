using System;
using System.IO;
using System.Threading.Tasks;
using OmniPadServer.Core;
using Xunit;

namespace OmniPadServer.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("1.3.0", "1.2.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("v1.3.0", "v1.2.0", true)]
    [InlineData("v1.2.0-beta", "v1.2.0", false)]
    public void TestVersionComparison(string latest, string current, bool expectedNewer)
    {
        bool newer = UpdateChecker.IsNewer(latest, current);
        Assert.Equal(expectedNewer, newer);
    }

    [Fact]
    public async Task TestCheckForUpdatesWithEmptyDir()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var info = await UpdateChecker.CheckForUpdatesAsync(tempDir);
            Assert.NotNull(info);
            Assert.Equal(UpdateChecker.DefaultCurrentVersion, info.CurrentVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TestCheckForUpdatesWithLocalManifest()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string manifestFile = Path.Combine(tempDir, "update_manifest.json");
            File.WriteAllText(manifestFile, "{\"version\":\"1.5.0\",\"title\":\"Test Release\",\"notes\":\"New features\"}");

            var info = await UpdateChecker.CheckForUpdatesAsync(tempDir);
            Assert.NotNull(info);
            Assert.Equal("1.5.0", info.LatestVersion);
            Assert.True(info.IsUpdateAvailable);
            Assert.Equal("Test Release", info.ReleaseTitle);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TestGitHubRepoEndpointTargeting()
    {
        Assert.Equal("WizardOfXerox", UpdateChecker.GitHubRepoOwner);
        Assert.Equal("OmniPad", UpdateChecker.GitHubRepoName);
        Assert.Contains("WizardOfXerox/OmniPad/releases/latest", UpdateChecker.GitHubReleasesApiUrl);
        Assert.Contains("WizardOfXerox/OmniPad/main/version.json", UpdateChecker.GitHubRawVersionUrl);
    }

    [Fact]
    public void TestGetInstalledVersionFromDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "version.json"), "{\"version\":\"2.4.6\"}");
            string ver = UpdateChecker.GetInstalledVersion(tempDir);
            Assert.Equal("2.4.6", ver);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
