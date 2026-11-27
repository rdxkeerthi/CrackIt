using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StealthScreenAI
{
    public record TestRunResult(bool Success, int ExitCode, string Command, string Output, bool TimedOut);

    public record GitHubRepoSpec(string Owner, string Repo, string? Branch);

    /// <summary>
    /// Provides repository file indexing, stack-trace / error location extraction,
    /// remote GitHub/Codespace file fetching in RAM, and local test execution
    /// to ground Gemini bug-fix responses in exact file paths and line numbers.
    /// </summary>
    public static class RepoContextService
    {
        private static readonly HttpClient GitHubHttp = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private sealed record CachedTree(string Branch, List<string> Files, DateTime ExpiresAt);
        private static readonly ConcurrentDictionary<string, CachedTree> TreeCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", ".idea", "bin", "obj", "node_modules",
            "publish_single", "__pycache__", ".pytest_cache", "dist", "build",
            "target", ".venv", "venv", "packages", "coverage", ".next"
        };

        private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".sln", ".py", ".ts", ".tsx", ".js", ".jsx",
            ".go", ".rs", ".java", ".kt", ".cpp", ".cc", ".c", ".h", ".hpp",
            ".json", ".yaml", ".yml", ".toml", ".xml", ".sql", ".ps1", ".sh"
        };

        private sealed record FileHint(string PathOrName, int? LineNumber);

        static RepoContextService()
        {
            GitHubHttp.DefaultRequestHeaders.UserAgent.ParseAdd("StealthScreenAI/1.0");
            GitHubHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public static bool TryParseGitHubRepo(string? input, out GitHubRepoSpec? spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string trimmed = input.Trim();
            if (Directory.Exists(trimmed)) return false;

            // https://github.com/owner/repo(/tree/branch)
            var urlMatch = Regex.Match(trimmed,
                @"^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?(?:/tree/([^/?#]+))?(?:/.*)?$",
                RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                spec = new GitHubRepoSpec(
                    urlMatch.Groups[1].Value,
                    urlMatch.Groups[2].Value,
                    urlMatch.Groups[3].Success ? urlMatch.Groups[3].Value : null);
                return true;
            }

            // Shorthand: owner/repo (no spaces, not a Windows drive path)
            var shortMatch = Regex.Match(trimmed, @"^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$");
            if (shortMatch.Success && !trimmed.Contains(':'))
            {
                spec = new GitHubRepoSpec(shortMatch.Groups[1].Value, shortMatch.Groups[2].Value, null);
                return true;
            }

            return false;
        }

        public static bool IsBrowserWorkspaceUrl(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string trimmed = input.Trim();
            if (TryParseGitHubRepo(trimmed, out _)) return false;

            if (Uri.TryCreate(trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : "https://" + trimmed, UriKind.Absolute, out var uri))
            {
                string host = uri.Host.ToLowerInvariant();
                return host.EndsWith(".github.dev") ||
                       host == "github.dev" ||
                       host == "vscode.dev" ||
                       host.EndsWith(".vscode.dev") ||
                       host.EndsWith(".gitpod.io") ||
                       host.EndsWith("replit.com") ||
                       host.EndsWith("stackblitz.com") ||
                       host.EndsWith("codesandbox.io") ||
                       trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static bool IsRemoteRepoConfigured()
        {
            return TryParseGitHubRepo(ConfigManager.Current.RepoPath, out _) ||
                   IsBrowserWorkspaceUrl(ConfigManager.Current.RepoPath);
        }

        public static string ResolveActiveRepoPath()
        {
            string configured = ConfigManager.Current.RepoPath?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Directory.Exists(configured) ||
                    TryParseGitHubRepo(configured, out _) ||
                    IsBrowserWorkspaceUrl(configured))
                {
                    return configured;
                }
            }

            return "";
        }

        public static string DetectTestCommand(string repoPath)
        {
            if (!string.IsNullOrWhiteSpace(ConfigManager.Current.TestCommand))
            {
                return ConfigManager.Current.TestCommand.Trim();
            }

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                return "";
            }

            try
            {
                if (Directory.GetFiles(repoPath, "*.sln", SearchOption.TopDirectoryOnly).Length > 0 ||
                    Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
                        .Any(p => !IsInsideIgnoredDir(repoPath, p)))
                {
                    return "dotnet test";
                }
                if (File.Exists(Path.Combine(repoPath, "package.json")))
                {
                    return "npm test";
                }
                if (File.Exists(Path.Combine(repoPath, "pyproject.toml")) ||
                    File.Exists(Path.Combine(repoPath, "pytest.ini")) ||
                    File.Exists(Path.Combine(repoPath, "requirements.txt")))
                {
                    return "pytest";
                }
                if (File.Exists(Path.Combine(repoPath, "Cargo.toml")))
                {
                    return "cargo test";
                }
                if (File.Exists(Path.Combine(repoPath, "go.mod")))
                {
                    return "go test ./...";
                }
            }
            catch { }

            return "";
        }

        public static async Task<TestRunResult> RunTestsAsync(string repoPath, string testCommand, CancellationToken ct = default)
        {
            if (TryParseGitHubRepo(repoPath, out _) || IsBrowserWorkspaceUrl(repoPath))
            {
                return new TestRunResult(
                    false,
                    -1,
                    testCommand,
                    "Configured repository is a remote browser/Codespaces workspace. Run your tests inside the browser/Codespace terminal, then click [🐞 Fix] or paste the failing output into the chatbox.",
                    false);
            }

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                return new TestRunResult(false, -1, testCommand, "Local repository folder is not set or does not exist.", false);
            }

            if (string.IsNullOrWhiteSpace(testCommand))
            {
                return new TestRunResult(false, -1, "", "Could not auto-detect a test command for this repository. Type a test command (e.g. 'dotnet test', 'pytest', 'npm test') in the chatbox or configure it in Settings.", false);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {testCommand}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return new TestRunResult(false, -1, testCommand, "Failed to start test process.", false);
                }

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

                bool timedOut = false;
                try
                {
                    await proc.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    timedOut = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }

                string stdout = await stdoutTask;
                string stderr = await stderrTask;
                var combined = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(stdout)) combined.AppendLine(stdout.TrimEnd());
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    if (combined.Length > 0) combined.AppendLine();
                    combined.AppendLine("[STDERR]");
                    combined.AppendLine(stderr.TrimEnd());
                }

                string output = combined.ToString().Trim();
                if (output.Length > 14000)
                {
                    output = "... [earlier test output truncated] ...\n" + output.Substring(output.Length - 14000);
                }

                if (timedOut)
                {
                    return new TestRunResult(false, -1, testCommand, output + "\n\n[Timed out after 90s]", true);
                }

                return new TestRunResult(proc.ExitCode == 0, proc.ExitCode, testCommand, output, false);
            }
            catch (Exception ex)
            {
                return new TestRunResult(false, -1, testCommand, $"Failed to run tests: {ex.Message}", false);
            }
        }

        public static bool LooksLikeErrorOrBugQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            return Regex.IsMatch(text,
                @"(\b(error|exception|traceback|failed|failure|stack\s*trace|assert(ion)?|nullreference|typeerror|syntaxerror|undefined|cannot find|cs\d{4}|ts\d{4}|bug|where to fix|what to fix|failing test)\b|" +
                @"\.[a-zA-Z0-9]{1,6}(:\d+|\(\d+(,\d+)?\))|" +
                @"File\s+""[^""]+"",\s+line\s+\d+)",
                RegexOptions.IgnoreCase);
        }

        public static async Task<string> BuildRepoDebugContextAsync(string repoSpecOrPath, string errorOrQueryText, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(repoSpecOrPath))
            {
                return "";
            }

            if (TryParseGitHubRepo(repoSpecOrPath, out GitHubRepoSpec? ghSpec) && ghSpec != null)
            {
                return await BuildGitHubDebugContextAsync(ghSpec, errorOrQueryText, ct);
            }

            if (IsBrowserWorkspaceUrl(repoSpecOrPath))
            {
                var resolved = await TryResolveCodespaceSpecAsync(repoSpecOrPath, ct);
                if (resolved != null)
                {
                    return await BuildGitHubDebugContextAsync(resolved, errorOrQueryText, ct);
                }

                return $"### Browser IDE / Codespace Workspace: {repoSpecOrPath}\n(Remote cloud workspace — inspect visible editor tabs, Explorer tree, and terminal output on screen or in the pasted error.)";
            }

            return BuildRepoDebugContext(repoSpecOrPath, errorOrQueryText);
        }

        private static async Task<GitHubRepoSpec?> TryResolveCodespaceSpecAsync(string workspaceUrl, CancellationToken ct)
        {
            string token = ConfigManager.Current.GitHubToken?.Trim() ?? "";
            if (string.IsNullOrEmpty(token)) return null;

            string normalizedUrl = workspaceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? workspaceUrl
                : "https://" + workspaceUrl;

            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)) return null;
            if (!uri.Host.EndsWith(".github.dev", StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                using var req = CreateGitHubRequest("https://api.github.com/user/codespaces");
                using var resp = await GitHubHttp.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("codespaces", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var cs in arr.EnumerateArray())
                {
                    string webUrl = cs.TryGetProperty("web_url", out var w) ? (w.GetString() ?? "") : "";
                    string name = cs.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";

                    bool matches = webUrl.Contains(uri.Host, StringComparison.OrdinalIgnoreCase) ||
                                   (!string.IsNullOrEmpty(name) && uri.Host.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                    if (!matches) continue;

                    if (cs.TryGetProperty("repository", out var repoObj) &&
                        repoObj.TryGetProperty("full_name", out var fullNameProp))
                    {
                        string fullName = fullNameProp.GetString() ?? "";
                        string[] parts = fullName.Split('/');
                        if (parts.Length == 2)
                        {
                            string? branch = null;
                            if (cs.TryGetProperty("git_status", out var gs) &&
                                gs.TryGetProperty("ref", out var refProp))
                            {
                                branch = refProp.GetString();
                            }
                            return new GitHubRepoSpec(parts[0], parts[1], branch);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static string BuildRepoDebugContext(string repoPath, string errorOrQueryText)
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                return "";
            }

            var allFiles = EnumerateSourceFiles(repoPath, maxFiles: 400);
            var sb = new StringBuilder();
            sb.AppendLine($"### Local Repository Root: {repoPath}");

            AppendFileTree(sb, allFiles);

            var selected = MatchRelevantFiles(
                allFiles,
                errorOrQueryText,
                readFile: rel =>
                {
                    string full = Path.Combine(repoPath, rel);
                    var info = new FileInfo(full);
                    return info.Length <= 150_000 ? File.ReadAllText(full) : null;
                },
                isLocal: true,
                repoPath);

            if (selected.Count > 0)
            {
                sb.AppendLine("### Relevant Source Files (with 1-based line numbers):");
                int budget = 18000;
                foreach (var (rel, focusLine) in selected)
                {
                    if (budget <= 500) break;
                    try
                    {
                        string[] lines = File.ReadAllLines(Path.Combine(repoPath, rel));
                        string snippet = FormatNumberedLines(lines, focusLine, maxChars: Math.Min(5000, budget));
                        if (!string.IsNullOrEmpty(snippet))
                        {
                            sb.AppendLine($"--- FILE: {rel} ---");
                            sb.AppendLine(snippet);
                            budget -= snippet.Length;
                        }
                    }
                    catch { }
                }
            }

            return sb.ToString();
        }

        private static async Task<string> BuildGitHubDebugContextAsync(GitHubRepoSpec spec, string errorOrQueryText, CancellationToken ct)
        {
            try
            {
                var (branch, allFiles) = await FetchGitHubTreeAsync(spec, ct);
                if (allFiles.Count == 0)
                {
                    return $"### GitHub Repository: {spec.Owner}/{spec.Repo} (could not list files — check URL or GitHub Token in Settings for private repos)";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"### GitHub / Codespace Repository: {spec.Owner}/{spec.Repo} (branch: {branch})");
                AppendFileTree(sb, allFiles);

                var selected = MatchRelevantFiles(
                    allFiles,
                    errorOrQueryText,
                    readFile: null,
                    isLocal: false,
                    localRoot: "");

                if (selected.Count > 0)
                {
                    sb.AppendLine("### Relevant Source Files from GitHub (with 1-based line numbers):");
                    int budget = 18000;
                    foreach (var (rel, focusLine) in selected)
                    {
                        if (budget <= 500) break;
                        string? content = await FetchGitHubFileContentAsync(spec.Owner, spec.Repo, branch, rel, ct);
                        if (string.IsNullOrEmpty(content)) continue;

                        string[] lines = content.Replace("\r\n", "\n").Split('\n');
                        string snippet = FormatNumberedLines(lines, focusLine, maxChars: Math.Min(5000, budget));
                        if (!string.IsNullOrEmpty(snippet))
                        {
                            sb.AppendLine($"--- FILE: {rel} ---");
                            sb.AppendLine(snippet);
                            budget -= snippet.Length;
                        }
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"### GitHub Repository: {spec.Owner}/{spec.Repo} (fetch warning: {ex.Message})";
            }
        }

        private static async Task<(string Branch, List<string> Files)> FetchGitHubTreeAsync(GitHubRepoSpec spec, CancellationToken ct)
        {
            string cacheKey = $"{spec.Owner}/{spec.Repo}@{spec.Branch ?? "default"}";
            if (TreeCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
            {
                return (cached.Branch, cached.Files);
            }

            string branch = spec.Branch ?? "";
            if (string.IsNullOrWhiteSpace(branch))
            {
                using var metaReq = CreateGitHubRequest($"https://api.github.com/repos/{spec.Owner}/{spec.Repo}");
                using var metaResp = await GitHubHttp.SendAsync(metaReq, ct);
                if (!metaResp.IsSuccessStatusCode)
                {
                    return ("main", new List<string>());
                }
                string metaJson = await metaResp.Content.ReadAsStringAsync(ct);
                using var metaDoc = JsonDocument.Parse(metaJson);
                branch = metaDoc.RootElement.TryGetProperty("default_branch", out var bProp)
                    ? (bProp.GetString() ?? "main")
                    : "main";
            }

            string treeUrl = $"https://api.github.com/repos/{spec.Owner}/{spec.Repo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
            using var treeReq = CreateGitHubRequest(treeUrl);
            using var treeResp = await GitHubHttp.SendAsync(treeReq, ct);
            if (!treeResp.IsSuccessStatusCode)
            {
                return (branch, new List<string>());
            }

            string treeJson = await treeResp.Content.ReadAsStringAsync(ct);
            using var treeDoc = JsonDocument.Parse(treeJson);
            var files = new List<string>();

            if (treeDoc.RootElement.TryGetProperty("tree", out var treeArr) && treeArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in treeArr.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "blob") continue;
                    if (!item.TryGetProperty("path", out var pathProp)) continue;
                    string path = pathProp.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    string[] segments = path.Split('/');
                    if (segments.Take(Math.Max(0, segments.Length - 1)).Any(s => IgnoredDirs.Contains(s))) continue;

                    string ext = Path.GetExtension(path);
                    if (SourceExtensions.Contains(ext))
                    {
                        files.Add(path);
                        if (files.Count >= 400) break;
                    }
                }
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            TreeCache[cacheKey] = new CachedTree(branch, files, DateTime.UtcNow.AddMinutes(5));
            return (branch, files);
        }

        private static async Task<string?> FetchGitHubFileContentAsync(string owner, string repo, string branch, string relPath, CancellationToken ct)
        {
            string token = ConfigManager.Current.GitHubToken?.Trim() ?? "";
            string escapedPath = string.Join("/", relPath.Split('/').Select(Uri.EscapeDataString));

            if (!string.IsNullOrEmpty(token))
            {
                string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{escapedPath}?ref={Uri.EscapeDataString(branch)}";
                using var req = CreateGitHubRequest(apiUrl);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
                using var resp = await GitHubHttp.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    return await resp.Content.ReadAsStringAsync(ct);
                }
            }

            string rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{Uri.EscapeDataString(branch)}/{escapedPath}";
            using var rawReq = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            rawReq.Headers.UserAgent.ParseAdd("StealthScreenAI/1.0");
            using var rawResp = await GitHubHttp.SendAsync(rawReq, ct);
            if (!rawResp.IsSuccessStatusCode) return null;
            return await rawResp.Content.ReadAsStringAsync(ct);
        }

        private static HttpRequestMessage CreateGitHubRequest(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            string token = ConfigManager.Current.GitHubToken?.Trim() ?? "";
            if (!string.IsNullOrEmpty(token))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            return req;
        }

        private static void AppendFileTree(StringBuilder sb, List<string> allFiles)
        {
            sb.AppendLine("### Repository Files (partial):");
            foreach (var rel in allFiles.Take(120))
            {
                sb.AppendLine($"- {rel}");
            }
            if (allFiles.Count > 120)
            {
                sb.AppendLine($"- ... ({allFiles.Count - 120} more files)");
            }
            sb.AppendLine();
        }

        private static List<(string RelPath, int? FocusLine)> MatchRelevantFiles(
            List<string> allFiles,
            string errorOrQueryText,
            Func<string, string?>? readFile,
            bool isLocal,
            string localRoot)
        {
            var hints = ExtractFileHints(errorOrQueryText);
            var matched = new List<(string RelPath, int? FocusLine, int Score)>();
            var seenRel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hint in hints)
            {
                string normalizedHint = hint.PathOrName.Replace('\\', '/').Trim();
                foreach (var rel in allFiles)
                {
                    if (seenRel.Contains(rel)) continue;

                    string relSlash = rel.Replace('\\', '/');
                    string fullSlash = isLocal ? Path.Combine(localRoot, rel).Replace('\\', '/') : relSlash;
                    string fileName = Path.GetFileName(relSlash);
                    string hintName = Path.GetFileName(normalizedHint);

                    bool exactOrSuffix =
                        normalizedHint.Equals(relSlash, StringComparison.OrdinalIgnoreCase) ||
                        normalizedHint.Equals(fullSlash, StringComparison.OrdinalIgnoreCase) ||
                        normalizedHint.EndsWith("/" + relSlash, StringComparison.OrdinalIgnoreCase) ||
                        relSlash.EndsWith("/" + normalizedHint, StringComparison.OrdinalIgnoreCase);

                    bool nameMatch = !string.IsNullOrEmpty(hintName) &&
                        fileName.Equals(hintName, StringComparison.OrdinalIgnoreCase);

                    if (exactOrSuffix || nameMatch)
                    {
                        seenRel.Add(rel);
                        int score = (exactOrSuffix ? 10 : 5) + (hint.LineNumber.HasValue ? 5 : 0);
                        matched.Add((rel, hint.LineNumber, score));
                    }
                }
            }

            if (readFile != null && matched.Count < 2 && !string.IsNullOrWhiteSpace(errorOrQueryText))
            {
                var tokens = ExtractCandidateIdentifiers(errorOrQueryText);
                if (tokens.Count > 0)
                {
                    foreach (var rel in allFiles.Take(120))
                    {
                        if (seenRel.Contains(rel)) continue;
                        try
                        {
                            string? content = readFile(rel);
                            if (string.IsNullOrEmpty(content)) continue;
                            int hits = tokens.Count(tok => content.Contains(tok, StringComparison.Ordinal));
                            if (hits > 0)
                            {
                                seenRel.Add(rel);
                                matched.Add((rel, null, hits));
                            }
                        }
                        catch { }
                    }
                }
            }

            foreach (var rel in allFiles)
            {
                if (matched.Count >= 6) break;
                if (seenRel.Contains(rel)) continue;
                string name = Path.GetFileName(rel);
                if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
                {
                    seenRel.Add(rel);
                    matched.Add((rel, null, 1));
                }
            }

            return matched
                .OrderByDescending(x => x.Score)
                .Take(6)
                .Select(x => (x.RelPath, x.FocusLine))
                .ToList();
        }

        private static List<FileHint> ExtractFileHints(string text)
        {
            var hints = new List<FileHint>();
            if (string.IsNullOrWhiteSpace(text)) return hints;

            foreach (Match m in Regex.Matches(text, @"in\s+([^\r\n:]+(?:\.[a-zA-Z0-9]+)):line\s+(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[2].Value, out int line))
                    hints.Add(new FileHint(m.Groups[1].Value.Trim(), line));
            }

            foreach (Match m in Regex.Matches(text, @"File\s+""([^""]+)"",\s+line\s+(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[2].Value, out int line))
                    hints.Add(new FileHint(m.Groups[1].Value.Trim(), line));
            }

            foreach (Match m in Regex.Matches(text, @"([A-Za-z0-9_./\\:-]+\.[a-zA-Z0-9]{1,8})\((\d+)(?:,\d+)?\)"))
            {
                if (int.TryParse(m.Groups[2].Value, out int line))
                    hints.Add(new FileHint(m.Groups[1].Value.Trim(), line));
            }

            foreach (Match m in Regex.Matches(text, @"([A-Za-z0-9_./\\:-]+\.[a-zA-Z0-9]{1,8}):(\d+)(?::\d+)?"))
            {
                if (int.TryParse(m.Groups[2].Value, out int line))
                    hints.Add(new FileHint(m.Groups[1].Value.Trim(), line));
            }

            foreach (Match m in Regex.Matches(text, @"\b([A-Za-z0-9_.-]+\.(?:cs|py|ts|tsx|js|jsx|go|rs|java|cpp|c|h|json|csproj|sql))\b", RegexOptions.IgnoreCase))
            {
                hints.Add(new FileHint(m.Groups[1].Value.Trim(), null));
            }

            return hints;
        }

        private static List<string> ExtractCandidateIdentifiers(string text)
        {
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Error", "Exception", "Failed", "Failure", "System", "String", "Object",
                "Expected", "Actual", "Assert", "Test", "Tests", "Line", "File", "True", "False"
            };

            return Regex.Matches(text, @"\b[A-Za-z_][A-Za-z0-9_]{4,}\b")
                .Select(m => m.Value)
                .Where(v => !ignored.Contains(v))
                .Distinct()
                .Take(8)
                .ToList();
        }

        private static string FormatNumberedLines(string[] lines, int? focusLine, int maxChars)
        {
            if (lines.Length == 0) return "";

            int start = 0;
            int end = lines.Length - 1;

            if (focusLine.HasValue && focusLine.Value > 0)
            {
                int idx = Math.Clamp(focusLine.Value - 1, 0, lines.Length - 1);
                start = Math.Max(0, idx - 60);
                end = Math.Min(lines.Length - 1, idx + 60);
            }
            else if (lines.Length > 200)
            {
                end = 199;
            }

            var sb = new StringBuilder();
            if (start > 0) sb.AppendLine($"... ({start} earlier lines omitted) ...");

            for (int i = start; i <= end; i++)
            {
                string marker = (focusLine.HasValue && i + 1 == focusLine.Value) ? ">>" : "  ";
                string lineStr = $"{marker} {i + 1,4} | {lines[i]}";
                if (sb.Length + lineStr.Length + 2 > maxChars)
                {
                    sb.AppendLine("... (truncated) ...");
                    break;
                }
                sb.AppendLine(lineStr);
            }

            if (end < lines.Length - 1)
            {
                sb.AppendLine($"... ({lines.Length - 1 - end} remaining lines omitted) ...");
            }
            return sb.ToString();
        }

        private static List<string> EnumerateSourceFiles(string rootPath, int maxFiles)
        {
            var results = new List<string>();
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0 && results.Count < maxFiles)
            {
                string current = stack.Pop();
                try
                {
                    foreach (string dir in Directory.GetDirectories(current))
                    {
                        string name = Path.GetFileName(dir);
                        if (!IgnoredDirs.Contains(name))
                        {
                            stack.Push(dir);
                        }
                    }

                    foreach (string file in Directory.GetFiles(current))
                    {
                        string ext = Path.GetExtension(file);
                        if (SourceExtensions.Contains(ext))
                        {
                            results.Add(Path.GetRelativePath(rootPath, file));
                            if (results.Count >= maxFiles) break;
                        }
                    }
                }
                catch { }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        private static bool IsInsideIgnoredDir(string rootPath, string filePath)
        {
            string rel = Path.GetRelativePath(rootPath, filePath);
            string[] parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Take(Math.Max(0, parts.Length - 1)).Any(p => IgnoredDirs.Contains(p));
        }
    }
}
