using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class DevFlowSkillManagerTests
{
    [Fact]
    public async Task InstallRecommended_ProjectScope_WritesBundledSkillsAndLock()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.Equal("install", result["action"]?.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-connect", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".maui", "devflow-skills.lock.json")));

        var statuses = await DevFlowSkillManager.CheckAsync("project", "claude", online: false);
        var skills = Assert.IsType<JsonArray>(statuses["skills"]);
        Assert.All(skills.OfType<JsonObject>(), skill => Assert.Equal("up-to-date", skill["status"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Check_ProjectScope_DetectsDirtySkillFile()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        await File.AppendAllTextAsync(skillPath, "\nmanual edit\n");

        var result = await DevFlowSkillManager.CheckAsync("project", "claude", online: false);
        var skills = Assert.IsType<JsonArray>(result["skills"]);
        var onboard = skills.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("dirty", onboard["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesObsoleteManagedSkillFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var obsoletePath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "references", "old-file.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoletePath)!);
        await File.WriteAllTextAsync(obsoletePath, "old content");

        var lockPath = Path.Combine(workspace.Path, ".maui", "devflow-skills.lock.json");
        var lockFile = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(lockPath)));
        var entries = Assert.IsType<JsonArray>(lockFile["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        var files = Assert.IsType<JsonArray>(onboardEntry["files"]);
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = "references/old-file.md",
            ["hash"] = HashContent("old content")
        });
        await File.WriteAllTextAsync(lockPath, CliJson.SerializeUntyped(lockFile));

        await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(File.Exists(obsoletePath));
    }

    [Fact]
    public async Task Update_ProjectScope_SkipsObsoleteLockFilePathsOutsideSkillDirectory()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var traversalPath = Path.Combine(workspace.Path, "traversal-sentinel.txt");
        var absolutePath = Path.Combine(workspace.Path, "absolute-sentinel.txt");
        await File.WriteAllTextAsync(traversalPath, "traversal content");
        await File.WriteAllTextAsync(absolutePath, "absolute content");

        var lockPath = Path.Combine(workspace.Path, ".maui", "devflow-skills.lock.json");
        var lockFile = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(lockPath)));
        var entries = Assert.IsType<JsonArray>(lockFile["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        var files = Assert.IsType<JsonArray>(onboardEntry["files"]);
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = Path.Combine("..", "..", "..", "traversal-sentinel.txt"),
            ["hash"] = HashContent("traversal content")
        });
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = absolutePath,
            ["hash"] = HashContent("absolute content")
        });
        await File.WriteAllTextAsync(lockPath, CliJson.SerializeUntyped(lockFile));

        await DevFlowSkillManager.UpdateAsync("project", "claude", force: true, allowDowngrade: false, CancellationToken.None);

        Assert.True(File.Exists(traversalPath));
        Assert.True(File.Exists(absolutePath));
    }

    [Fact]
    public async Task Doctor_WithDifferentLocalToolManifestVersion_ReportsWarning()
    {
        using var workspace = TemporaryWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".config"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Path, ".config", "dotnet-tools.json"),
            """
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "Microsoft.Maui.Cli": {
                  "version": "0.0.1",
                  "commands": [ "maui" ]
                }
              }
            }
            """);

        var result = await DevFlowSkillManager.DoctorAsync("project", "claude", online: false);

        var warnings = Assert.IsType<JsonArray>(result["warnings"]);
        Assert.Contains(warnings.Select(w => w?.GetValue<string>()), warning => warning?.Contains("local Microsoft.Maui.Cli version 0.0.1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void VersionsEquivalent_WithDifferentPatchPrefix_ReturnsFalse()
    {
        Assert.False(InvokeVersionsEquivalent("0.0.1", "0.0.10"));
    }

    [Fact]
    public async Task Remove_WithPathTraversalSkillName_ThrowsAndDoesNotDeleteOutsideSkillRoot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outsideDirectory = Path.Combine(workspace.Path, "outside");
        Directory.CreateDirectory(outsideDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevFlowSkillManager.RemoveAsync(Path.Combine("..", "..", "outside"), "project", "claude", force: true));

        Assert.True(Directory.Exists(outsideDirectory));
    }

    [Fact]
    public async Task Remove_UnmanagedSkillDirectoryWithoutForce_SkipsAndPreservesFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var skillDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard");
        Directory.CreateDirectory(skillDirectory);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        await File.WriteAllTextAsync(skillFile, "unmanaged content");

        var result = await DevFlowSkillManager.RemoveAsync("maui-devflow-onboard", "project", "claude", force: false);

        var results = Assert.IsType<JsonArray>(result["results"]);
        var status = Assert.IsType<JsonObject>(Assert.Single(results));
        Assert.Equal("unknown-or-unmanaged", status["status"]?.GetValue<string>());
        Assert.Equal("skipped", status["action"]?.GetValue<string>());
        Assert.Contains("not managed by this CLI", status["message"]?.GetValue<string>());
        Assert.True(File.Exists(skillFile));
    }

    sealed class TemporaryWorkspace : IDisposable
    {
        readonly string _originalDirectory;

        TemporaryWorkspace(string path)
        {
            Path = path;
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(path);
        }

        public string Path { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui-devflow-skills-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(System.IO.Path.Combine(path, ".git"), string.Empty);
            return new TemporaryWorkspace(path);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for test temp folders.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for test temp folders.
            }
        }
    }

    static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n")));
        return "sha256-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static bool InvokeVersionsEquivalent(string left, string right)
    {
        var method = typeof(DevFlowSkillManager).GetMethod("VersionsEquivalent", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, [left, right]));
    }
}
