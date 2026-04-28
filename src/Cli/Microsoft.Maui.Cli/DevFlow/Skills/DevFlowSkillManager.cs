using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow.Skills;

internal static class DevFlowSkillManager
{
    const string ResourceRoot = "devflow.skills";
    const string PackageId = "Microsoft.Maui.Cli";
    const string LockFileRelativePath = ".maui/devflow-skills.lock.json";
    static readonly TimeSpan LockFileRetryTimeout = TimeSpan.FromSeconds(10);

    static readonly DevFlowSkillDefinition[] s_skills =
    [
        new("maui-devflow-onboard", "MAUI DevFlow Onboard", "Guides first-time MAUI DevFlow project integration.", Recommended: true),
        new("maui-devflow-connect", "MAUI DevFlow Connect", "Diagnoses DevFlow broker, agent, and device connectivity.", Recommended: true),
        new("maui-devflow-debug", "MAUI DevFlow Debug", "Guides build, deploy, inspect, and debug loops with MAUI DevFlow.", Recommended: true)
    ];

    static readonly Dictionary<string, string> s_targetDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = Path.Combine(".claude", "skills"),
        ["github"] = Path.Combine(".github", "skills"),
        ["agents"] = Path.Combine(".agents", "skills")
    };

    public static async Task<JsonObject> InstallRecommendedAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Where(s => s.Recommended).Select(s => s.Id), scope, target, force, allowDowngrade, "install", cancellationToken);

    public static async Task<JsonObject> InstallAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Select(s => s.Id), scope, target, force, allowDowngrade, "install", cancellationToken);

    public static async Task<JsonObject> UpdateAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Select(s => s.Id), scope, target, force, allowDowngrade, "update", cancellationToken);

    public static async Task<JsonObject> ListAsync(string scope, string target, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("list", scope, target);
        var items = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, allowAll: true);
        var skillBundles = await LoadSkillBundlesAsync(s_skills, cancellationToken);
        foreach (var installTarget in installTargets)
        {
            foreach (var (skill, bundle) in skillBundles)
                AddJsonObject(items, CreateStatusObject(installTarget, skill.Id, bundle));
        }

        result["skills"] = items;
        return result;
    }

    public static async Task<JsonObject> CheckAsync(string scope, string target, bool online, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("check", scope, target);
        result["online"] = online;
        if (online)
        {
            result["onlineMessage"] = "Online skill file checks are intentionally not implemented. Update Microsoft.Maui.Cli to get newer bundled skills.";
        }

        var items = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, allowAll: true);
        var skillBundles = await LoadSkillBundlesAsync(s_skills, cancellationToken);
        foreach (var installTarget in installTargets)
        {
            foreach (var (skill, bundle) in skillBundles)
                AddJsonObject(items, CreateStatusObject(installTarget, skill.Id, bundle));
        }

        result["skills"] = items;
        return result;
    }

    public static async Task<JsonObject> DoctorAsync(string scope, string target, bool online, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("doctor", scope, target);
        result["online"] = online;

        var items = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, allowAll: true);
        var skillBundles = await LoadSkillBundlesAsync(s_skills, cancellationToken);
        foreach (var installTarget in installTargets)
        {
            foreach (var (skill, bundle) in skillBundles)
                AddJsonObject(items, CreateStatusObject(installTarget, skill.Id, bundle));
        }
        result["skills"] = items;

        var diagnostics = new JsonObject
        {
            ["cliVersion"] = GetCurrentCliVersion(),
            ["baseDirectory"] = AppContext.BaseDirectory,
            ["processPath"] = Environment.ProcessPath
        };

        var localToolVersion = TryGetLocalToolVersion(FindWorkspaceRoot(Directory.GetCurrentDirectory()));
        if (localToolVersion != null)
            diagnostics["localToolManifestVersion"] = localToolVersion;

        result["diagnostics"] = diagnostics;

        var warnings = new JsonArray();
        if (localToolVersion != null && !VersionsEquivalent(localToolVersion, GetCurrentCliVersion()))
        {
            AddJsonString(warnings, $"This repo declares local {PackageId} version {localToolVersion}, but the running CLI is {GetCurrentCliVersion()}. Run `dotnet tool restore` and `dotnet tool run maui devflow init` if project-scoped skills should come from the local tool.");
        }

        AddScopeConflictWarnings(warnings, target);
        result["warnings"] = warnings;
        return result;
    }

    public static async Task<JsonObject> RemoveAsync(string skillId, string scope, string target, bool force, CancellationToken cancellationToken)
    {
        var skill = GetSkill(skillId);
        var installTargets = ResolveInstallTargets(scope, target, allowAll: true);
        var bundle = await LoadSkillBundleAsync(skill, cancellationToken);
        var result = CreateBaseResult("remove", scope, target);
        var results = new JsonArray();

        foreach (var installTarget in installTargets)
        {
            var status = CreateStatusObject(installTarget, skill.Id, bundle);
            var statusValue = status["status"]?.GetValue<string>();
            if (statusValue == "dirty" && !force)
            {
                status["action"] = "skipped";
                status["message"] = "Skill files differ from the lockfile. Re-run with --force to remove anyway.";
                AddJsonObject(results, status);
                continue;
            }

            if (statusValue == "unknown-or-unmanaged" && !force)
            {
                status["action"] = "skipped";
                status["message"] = "Skill files are not managed by this CLI. Re-run with --force to remove anyway.";
                AddJsonObject(results, status);
                continue;
            }

            var skillDirectory = GetSkillDirectory(installTarget, skill.Id);
            if (Directory.Exists(skillDirectory))
                Directory.Delete(skillDirectory, recursive: true);

            UpdateLockFile(installTarget.LockFilePath, cancellationToken, lockFile =>
            {
                RemoveLockEntry(lockFile, installTarget, skill.Id);
                return true;
            });

            status["action"] = "removed";
            status["status"] = "removed";
            AddJsonObject(results, status);
        }

        result["results"] = results;
        return result;
    }

    static async Task<JsonObject> WriteSkillsAsync(IEnumerable<string> skillIds, string scope, string target, bool force, bool allowDowngrade, string action, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult(action, scope, target);
        var results = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, allowAll: false);
        var skillBundles = new List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>();
        foreach (var skillId in skillIds)
        {
            var skill = GetSkill(skillId);
            var bundle = await LoadSkillBundleAsync(skill, cancellationToken);
            skillBundles.Add((skill, bundle));
        }

        foreach (var installTarget in installTargets)
        {
            var targetResults = new List<JsonObject>();
            UpdateLockFile(installTarget.LockFilePath, cancellationToken, lockFile =>
            {
                var lockFileChanged = false;
                foreach (var (skill, bundle) in skillBundles)
                {
                    var status = CreateStatusObject(installTarget, skill.Id, bundle, lockFile);
                    var statusValue = status["status"]?.GetValue<string>();

                    if (statusValue == "dirty" && !force)
                    {
                        status["action"] = "skipped";
                        status["message"] = "Skill files differ from the lockfile. Re-run with --force to overwrite.";
                        targetResults.Add(status);
                        continue;
                    }

                    if (statusValue == "unknown-or-unmanaged" && !force)
                    {
                        status["action"] = "skipped";
                        status["message"] = "Skill files already exist but are not managed by this CLI. Re-run with --force to overwrite.";
                        targetResults.Add(status);
                        continue;
                    }

                    if (statusValue == "installed-from-newer-cli" && !allowDowngrade && !force)
                    {
                        status["action"] = "skipped";
                        status["message"] = "Installed skill was written by a newer CLI. Re-run with --allow-downgrade or --force to replace it.";
                        targetResults.Add(status);
                        continue;
                    }

                    WriteSkillBundle(installTarget, skill, bundle, lockFile);
                    UpsertLockEntry(lockFile, installTarget, skill, bundle);
                    lockFileChanged = true;

                    status = CreateStatusObject(installTarget, skill.Id, bundle, lockFile);
                    status["action"] = statusValue == "up-to-date" ? "unchanged" : "written";
                    targetResults.Add(status);
                }

                return lockFileChanged;
            });

            foreach (var targetResult in targetResults)
                AddJsonObject(results, targetResult);
        }

        result["results"] = results;
        return result;
    }

    static JsonObject CreateBaseResult(string action, string scope, string target)
        => new()
        {
            ["action"] = action,
            ["scope"] = scope,
            ["target"] = target,
            ["cliVersion"] = GetCurrentCliVersion()
        };

    static JsonObject CreateStatusObject(InstallTarget installTarget, string skillId, SkillBundle bundle, JsonObject? knownLockFile = null)
    {
        var lockFile = knownLockFile ?? ReadLockFile(installTarget.LockFilePath);
        var entry = FindLockEntry(lockFile, installTarget, skillId);
        var skillDirectory = GetSkillDirectory(installTarget, skillId);
        var exists = Directory.Exists(skillDirectory);

        var status = "missing";
        if (entry == null && exists)
        {
            status = "unknown-or-unmanaged";
        }
        else if (entry != null && !exists)
        {
            status = "missing";
        }
        else if (entry != null)
        {
            if (IsDirty(skillDirectory, entry))
            {
                status = "dirty";
            }
            else if (string.Equals(GetString(entry, "contentHash"), bundle.ContentHash, StringComparison.Ordinal))
            {
                status = "up-to-date";
            }
            else
            {
                var installedByVersion = GetString(entry, "installedByCliVersion");
                var comparison = CompareVersionLike(installedByVersion, GetCurrentCliVersion());
                status = comparison > 0
                    ? "installed-from-newer-cli"
                    : comparison == 0
                        ? "installed-from-different-cli-same-version"
                        : "update-available-from-current-cli";
            }
        }

        return new JsonObject
        {
            ["skillId"] = skillId,
            ["scope"] = installTarget.Scope,
            ["target"] = installTarget.TargetKind,
            ["status"] = status,
            ["path"] = installTarget.GetDisplayPath(skillId),
            ["installedVersion"] = entry != null ? GetString(entry, "skillVersion") : null,
            ["bundledVersion"] = bundle.Version,
            ["installedByCliVersion"] = entry != null ? GetString(entry, "installedByCliVersion") : null,
            ["contentHash"] = entry != null ? GetString(entry, "contentHash") : null,
            ["bundledContentHash"] = bundle.ContentHash
        };
    }

    static async Task<List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>> LoadSkillBundlesAsync(IEnumerable<DevFlowSkillDefinition> skills, CancellationToken cancellationToken)
    {
        var bundles = new List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>();
        foreach (var skill in skills)
        {
            var bundle = await LoadSkillBundleAsync(skill, cancellationToken);
            bundles.Add((skill, bundle));
        }

        return bundles;
    }

    static async Task<SkillBundle> LoadSkillBundleAsync(DevFlowSkillDefinition skill, CancellationToken cancellationToken)
    {
        var assembly = typeof(DevFlowSkillManager).Assembly;
        var prefix = $"{ResourceRoot}/{skill.Id}/";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidOperationException($"No embedded DevFlow skill resources found for '{skill.Id}'.");

        var files = new List<SkillAssetFile>();
        foreach (var resourceName in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(resourceName[prefix.Length..]);
            if (ShouldExcludeSkillAsset(relativePath))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded skill resource not found: {resourceName}");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            files.Add(new SkillAssetFile(relativePath, content, HashContent(content)));
        }

        return new SkillBundle(skill.Id, GetCurrentCliVersion(), files, HashBundle(files));
    }

    static bool ShouldExcludeSkillAsset(string relativePath)
        => relativePath.StartsWith($"evals{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(relativePath, "evals", StringComparison.OrdinalIgnoreCase);

    static void WriteSkillBundle(InstallTarget installTarget, DevFlowSkillDefinition skill, SkillBundle bundle, JsonObject lockFile)
    {
        var skillDirectory = GetSkillDirectory(installTarget, skill.Id);
        Directory.CreateDirectory(skillDirectory);

        foreach (var file in bundle.Files)
        {
            var filePath = Path.Combine(skillDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, file.Content);
        }

        var existingEntry = FindLockEntry(lockFile, installTarget, skill.Id);
        if (existingEntry?["files"] is JsonArray files)
        {
            var currentFiles = bundle.Files.Select(f => NormalizeRelativePath(f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deletedObsoleteFile = false;
            foreach (var file in files.OfType<JsonObject>())
            {
                var relativePath = GetString(file, "path");
                if (relativePath == null || currentFiles.Contains(NormalizeRelativePath(relativePath)))
                    continue;

                if (!TryResolveSkillFilePath(skillDirectory, relativePath, out var obsoletePath))
                    continue;

                if (File.Exists(obsoletePath))
                {
                    File.Delete(obsoletePath);
                    deletedObsoleteFile = true;
                }
            }

            if (deletedObsoleteFile)
                PruneEmptyDirectories(skillDirectory);
        }
    }

    static bool IsDirty(string skillDirectory, JsonObject entry)
    {
        if (entry["files"] is not JsonArray files)
            return true;

        foreach (var file in files.OfType<JsonObject>())
        {
            var relativePath = GetString(file, "path");
            var expectedHash = GetString(file, "hash");
            if (relativePath == null || expectedHash == null)
                return true;

            if (!TryResolveSkillFilePath(skillDirectory, relativePath, out var filePath))
                return true;

            if (!File.Exists(filePath))
                return true;

            var actualHash = HashContent(File.ReadAllText(filePath));
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static JsonObject ReadLockFile(string lockFilePath)
    {
        if (File.Exists(lockFilePath))
        {
            try
            {
                var parsed = CliJson.ParseNode(File.ReadAllText(lockFilePath)) as JsonObject
                    ?? throw new InvalidOperationException($"DevFlow skills lockfile '{lockFilePath}' must contain a JSON object.");
                if (parsed["entries"] is not JsonArray)
                    parsed["entries"] = new JsonArray();
                return parsed;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"DevFlow skills lockfile '{lockFilePath}' is not valid JSON.", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Could not read DevFlow skills lockfile '{lockFilePath}'.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Could not access DevFlow skills lockfile '{lockFilePath}'.", ex);
            }
        }

        return CreateEmptyLockFile();
    }

    static JsonObject ReadLockFile(string lockFilePath, FileStream stream, bool allowEmpty)
    {
        try
        {
            stream.Position = 0;
            if (stream.Length == 0 && allowEmpty)
                return CreateEmptyLockFile();

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var parsed = CliJson.ParseNode(reader.ReadToEnd()) as JsonObject
                ?? throw new InvalidOperationException($"DevFlow skills lockfile '{lockFilePath}' must contain a JSON object.");
            if (parsed["entries"] is not JsonArray)
                parsed["entries"] = new JsonArray();
            return parsed;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"DevFlow skills lockfile '{lockFilePath}' is not valid JSON.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Could not read DevFlow skills lockfile '{lockFilePath}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Could not access DevFlow skills lockfile '{lockFilePath}'.", ex);
        }
    }

    static JsonObject CreateEmptyLockFile()
        => new()
        {
            ["version"] = 1,
            ["entries"] = new JsonArray()
        };

    static void WriteLockFile(string lockFilePath, JsonObject lockFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        PrepareLockFileForWrite(lockFile);
        File.WriteAllText(lockFilePath, CliJson.SerializeUntyped(lockFile));
    }

    static void WriteLockFile(string lockFilePath, JsonObject lockFile, FileStream stream)
    {
        PrepareLockFileForWrite(lockFile);
        var bytes = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(lockFile));
        stream.Position = 0;
        stream.SetLength(0);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    static void PrepareLockFileForWrite(JsonObject lockFile)
    {
        lockFile["version"] = 1;
        lockFile.Remove("updatedAtUtc");

        if (lockFile["entries"] is not JsonArray entries)
            return;

        foreach (var entry in entries.OfType<JsonObject>())
        {
            entry.Remove("installedByCommandPath");
            entry.Remove("installedAtUtc");
        }
    }

    static void UpdateLockFile(string lockFilePath, CancellationToken cancellationToken, Func<JsonObject, bool> update)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        var createdLockFile = false;
        using var stream = OpenLockFileStream(lockFilePath, cancellationToken, out createdLockFile);
        var lockFile = ReadLockFile(lockFilePath, stream, allowEmpty: createdLockFile);

        if (update(lockFile) || createdLockFile)
            WriteLockFile(lockFilePath, lockFile, stream);
    }

    static FileStream OpenLockFileStream(string lockFilePath, CancellationToken cancellationToken, out bool createdLockFile)
    {
        var startedAt = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                createdLockFile = !File.Exists(lockFilePath);
                return new FileStream(
                    lockFilePath,
                    createdLockFile ? FileMode.CreateNew : FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - startedAt < LockFileRetryTimeout)
            {
                Thread.Sleep(50);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Could not acquire exclusive access to DevFlow skills lockfile '{lockFilePath}'.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Could not access DevFlow skills lockfile '{lockFilePath}'.", ex);
            }
        }
    }

    static JsonObject? FindLockEntry(JsonObject lockFile, InstallTarget installTarget, string skillId)
    {
        if (lockFile["entries"] is not JsonArray entries)
            return null;

        return entries.OfType<JsonObject>().FirstOrDefault(entry =>
            string.Equals(GetString(entry, "skillId"), skillId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetString(entry, "scope"), installTarget.Scope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetString(entry, "target"), installTarget.TargetKind, StringComparison.OrdinalIgnoreCase));
    }

    static void UpsertLockEntry(JsonObject lockFile, InstallTarget installTarget, DevFlowSkillDefinition skill, SkillBundle bundle)
    {
        RemoveLockEntry(lockFile, installTarget, skill.Id);
        var entries = (JsonArray)lockFile["entries"]!;

        var files = new JsonArray();
        foreach (var file in bundle.Files)
        {
            AddJsonObject(files, new JsonObject
            {
                ["path"] = NormalizeRelativePath(file.RelativePath).Replace(Path.DirectorySeparatorChar, '/'),
                ["hash"] = file.Hash
            });
        }

        AddJsonObject(entries, new JsonObject
        {
            ["skillId"] = skill.Id,
            ["skillVersion"] = bundle.Version,
            ["scope"] = installTarget.Scope,
            ["target"] = installTarget.TargetKind,
            ["targetPath"] = installTarget.GetDisplayPath(skill.Id),
            ["contentHash"] = bundle.ContentHash,
            ["installedByCliVersion"] = GetCurrentCliVersion(),
            ["installedByBundleHash"] = bundle.ContentHash,
            ["files"] = files
        });
    }

    static void RemoveLockEntry(JsonObject lockFile, InstallTarget installTarget, string skillId)
    {
        if (lockFile["entries"] is not JsonArray entries)
            return;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] is JsonObject entry &&
                string.Equals(GetString(entry, "skillId"), skillId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetString(entry, "scope"), installTarget.Scope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetString(entry, "target"), installTarget.TargetKind, StringComparison.OrdinalIgnoreCase))
            {
                entries.RemoveAt(i);
            }
        }
    }

    static IReadOnlyList<InstallTarget> ResolveInstallTargets(string scope, string target, bool allowAll)
    {
        var workspaceRoot = FindWorkspaceRoot(Directory.GetCurrentDirectory());
        var scopes = ResolveScopes(scope, allowAll);
        var targetDirectory = ResolveTargetDirectory(target);
        var targets = new List<InstallTarget>();

        foreach (var resolvedScope in scopes)
        {
            var root = resolvedScope == "user" ? GetUserRoot() : workspaceRoot;
            targets.Add(new InstallTarget(
                resolvedScope,
                target,
                root,
                targetDirectory,
                Path.Combine(root, NormalizeRelativePath(LockFileRelativePath))));
        }

        return targets;
    }

    static IReadOnlyList<string> ResolveScopes(string scope, bool allowAll)
    {
        if (string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase))
            return ["project"];
        if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase))
            return ["user"];
        if (string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase) ||
            (allowAll && string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)))
            return ["project", "user"];

        throw new InvalidOperationException(allowAll
            ? "Scope must be project, user, both, or all."
            : "Scope must be project, user, or both.");
    }

    static string ResolveTargetDirectory(string target)
    {
        if (s_targetDirectories.TryGetValue(target, out var directory))
            return directory;

        throw new InvalidOperationException($"Target must be one of: {string.Join(", ", s_targetDirectories.Keys)}.");
    }

    static string FindWorkspaceRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        return startDirectory;
    }

    static string GetUserRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        throw new InvalidOperationException("Cannot determine the user profile directory for user-scope skill installation.");
    }

    static string GetSkillDirectory(InstallTarget installTarget, string skillId)
        => Path.Combine(installTarget.RootDirectory, installTarget.RelativeSkillDirectory, skillId);

    static DevFlowSkillDefinition GetSkill(string skillId)
        => s_skills.FirstOrDefault(skill => string.Equals(skill.Id, skillId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"Unknown DevFlow skill '{skillId}'.");

    static string NormalizeRelativePath(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    static bool TryResolveSkillFilePath(string skillDirectory, string relativePath, out string filePath)
    {
        filePath = string.Empty;

        try
        {
            var skillRoot = Path.GetFullPath(skillDirectory);
            if (!Path.EndsInDirectorySeparator(skillRoot))
                skillRoot += Path.DirectorySeparatorChar;

            filePath = Path.GetFullPath(Path.Combine(skillDirectory, NormalizeRelativePath(relativePath)));
            return filePath.StartsWith(skillRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    static string HashContent(string content)
        => HashString(content.ReplaceLineEndings("\n"));

    static string HashBundle(IReadOnlyList<SkillAssetFile> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(file.RelativePath.Replace(Path.DirectorySeparatorChar, '/'));
            builder.Append('\n');
            builder.Append(file.Hash);
            builder.Append('\n');
        }

        return HashString(builder.ToString());
    }

    static string HashString(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string GetCurrentCliVersion()
        => typeof(DevFlowSkillManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(DevFlowSkillManager).Assembly.GetName().Version?.ToString()
           ?? "unknown";

    static string? GetString(JsonObject node, string propertyName)
        => node.TryGetPropertyValue(propertyName, out var value) ? value?.GetValue<string>() : null;

    static int CompareVersionLike(string? left, string? right)
    {
        if (TryParseSemanticVersion(left, out var leftVersion) &&
            TryParseSemanticVersion(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    static bool VersionsEquivalent(string left, string right)
        => CompareVersionLike(left, right) == 0;

    static bool TryParseSemanticVersion(string? value, out ComparableSemanticVersion version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        var buildMetadataIndex = normalized.IndexOf('+');
        if (buildMetadataIndex >= 0)
            normalized = normalized[..buildMetadataIndex];

        string[] prereleaseIdentifiers = [];
        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            var prerelease = normalized[(prereleaseIndex + 1)..];
            if (string.IsNullOrWhiteSpace(prerelease))
                return false;

            prereleaseIdentifiers = prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries);
            normalized = normalized[..prereleaseIndex];
        }

        var releaseParts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (releaseParts.Length == 0 || releaseParts.Length > 4)
            return false;

        var releaseNumbers = new List<int>(releaseParts.Length);
        foreach (var part in releaseParts)
        {
            if (!int.TryParse(part, out var number) || number < 0)
                return false;

            releaseNumbers.Add(number);
        }

        while (releaseNumbers.Count < 3)
            releaseNumbers.Add(0);

        version = new ComparableSemanticVersion(releaseNumbers.ToArray(), prereleaseIdentifiers);
        return true;
    }

    static string? TryGetLocalToolVersion(string workspaceRoot)
    {
        var current = new DirectoryInfo(workspaceRoot);
        while (current != null)
        {
            var manifestPath = Path.Combine(current.FullName, ".config", "dotnet-tools.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = CliJson.ParseNode(File.ReadAllText(manifestPath)) as JsonObject;
                    if (manifest?["tools"] is JsonObject tools)
                    {
                        foreach (var tool in tools)
                        {
                            if (!string.Equals(tool.Key, PackageId, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (tool.Value is JsonObject toolObject)
                                return GetString(toolObject, "version");
                        }
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    static void AddScopeConflictWarnings(JsonArray warnings, string target)
    {
        if (!s_targetDirectories.ContainsKey(target))
            return;

        var projectTarget = ResolveInstallTargets("project", target, allowAll: true)[0];
        var userTarget = ResolveInstallTargets("user", target, allowAll: true)[0];
        var projectLock = ReadLockFile(projectTarget.LockFilePath);
        var userLock = ReadLockFile(userTarget.LockFilePath);

        foreach (var skill in s_skills)
        {
            var projectEntry = FindLockEntry(projectLock, projectTarget, skill.Id);
            var userEntry = FindLockEntry(userLock, userTarget, skill.Id);
            if (projectEntry == null || userEntry == null)
                continue;

            var projectHash = GetString(projectEntry, "contentHash");
            var userHash = GetString(userEntry, "contentHash");
            if (!string.Equals(projectHash, userHash, StringComparison.Ordinal))
                AddJsonString(warnings, $"{skill.Id} is installed in both project and user scope with different content. AI host precedence may choose a different version than expected.");
        }
    }

    static void AddJsonObject(JsonArray array, JsonObject item)
        => array.Add((JsonNode)item);

    static void AddJsonString(JsonArray array, string value)
        => array.Add((JsonNode?)JsonValue.Create(value));

    static void PruneEmptyDirectories(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            return;

        foreach (var directory in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    sealed record DevFlowSkillDefinition(string Id, string DisplayName, string Description, bool Recommended);
    sealed record SkillAssetFile(string RelativePath, string Content, string Hash);
    sealed record SkillBundle(string Id, string Version, IReadOnlyList<SkillAssetFile> Files, string ContentHash);

    sealed record ComparableSemanticVersion(int[] ReleaseParts, string[] PrereleaseIdentifiers)
    {
        public int CompareTo(ComparableSemanticVersion other)
        {
            var maxReleaseParts = Math.Max(ReleaseParts.Length, other.ReleaseParts.Length);
            for (var i = 0; i < maxReleaseParts; i++)
            {
                var left = i < ReleaseParts.Length ? ReleaseParts[i] : 0;
                var right = i < other.ReleaseParts.Length ? other.ReleaseParts[i] : 0;
                var releaseComparison = left.CompareTo(right);
                if (releaseComparison != 0)
                    return releaseComparison;
            }

            if (PrereleaseIdentifiers.Length == 0 && other.PrereleaseIdentifiers.Length == 0)
                return 0;

            if (PrereleaseIdentifiers.Length == 0)
                return 1;

            if (other.PrereleaseIdentifiers.Length == 0)
                return -1;

            var maxPrereleaseIdentifiers = Math.Max(PrereleaseIdentifiers.Length, other.PrereleaseIdentifiers.Length);
            for (var i = 0; i < maxPrereleaseIdentifiers; i++)
            {
                if (i >= PrereleaseIdentifiers.Length)
                    return -1;

                if (i >= other.PrereleaseIdentifiers.Length)
                    return 1;

                var left = PrereleaseIdentifiers[i];
                var right = other.PrereleaseIdentifiers[i];
                var leftIsNumeric = long.TryParse(left, out var leftNumber);
                var rightIsNumeric = long.TryParse(right, out var rightNumber);

                int prereleaseComparison;
                if (leftIsNumeric && rightIsNumeric)
                    prereleaseComparison = leftNumber.CompareTo(rightNumber);
                else if (leftIsNumeric)
                    prereleaseComparison = -1;
                else if (rightIsNumeric)
                    prereleaseComparison = 1;
                else
                    prereleaseComparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

                if (prereleaseComparison != 0)
                    return prereleaseComparison;
            }

            return 0;
        }
    }

    sealed record InstallTarget(string Scope, string TargetKind, string RootDirectory, string RelativeSkillDirectory, string LockFilePath)
    {
        public string GetDisplayPath(string skillId)
        {
            var path = Path.Combine(RelativeSkillDirectory, skillId).Replace(Path.DirectorySeparatorChar, '/');
            return Scope == "user" ? $"~/{path}" : path;
        }
    }
}
