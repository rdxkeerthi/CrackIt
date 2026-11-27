using System;
using System.IO;
using System.Threading.Tasks;
using StealthScreenAI;
using Xunit;

namespace StealthScreenAI.Tests
{
    public class StealthUnitTests
    {
        [Theory]
        [InlineData("https://github.com/rdxkeerthi/CrackIt", "rdxkeerthi", "CrackIt", null)]
        [InlineData("https://github.com/rdxkeerthi/CrackIt.git", "rdxkeerthi", "CrackIt", null)]
        [InlineData("https://github.com/octocat/Hello-World/tree/main", "octocat", "Hello-World", "main")]
        [InlineData("rdxkeerthi/CrackIt", "rdxkeerthi", "CrackIt", null)]
        public void TryParseGitHubRepo_ValidInputs_ReturnsExpectedSpec(
            string input, string expectedOwner, string expectedRepo, string? expectedBranch)
        {
            bool ok = RepoContextService.TryParseGitHubRepo(input, out var spec);
            Assert.True(ok);
            Assert.NotNull(spec);
            Assert.Equal(expectedOwner, spec!.Owner);
            Assert.Equal(expectedRepo, spec.Repo);
            Assert.Equal(expectedBranch, spec.Branch);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("C:\\Users\\admin\\repo")]
        [InlineData("not a repo")]
        public void TryParseGitHubRepo_InvalidInputs_ReturnsFalse(string input)
        {
            bool ok = RepoContextService.TryParseGitHubRepo(input, out var spec);
            Assert.False(ok);
            Assert.Null(spec);
        }

        [Theory]
        [InlineData("https://my-space-123.github.dev", true)]
        [InlineData("https://vscode.dev/github/octocat/Hello-World", true)]
        [InlineData("https://replit.com/@user/project", true)]
        [InlineData("https://github.com/rdxkeerthi/CrackIt", false)]
        [InlineData("", false)]
        public void IsBrowserWorkspaceUrl_DetectsCloudIdes(string url, bool expected)
        {
            Assert.Equal(expected, RepoContextService.IsBrowserWorkspaceUrl(url));
        }

        [Theory]
        [InlineData("error CS1002 in Program.cs:line 42", true)]
        [InlineData("Traceback (most recent call last): File \"app.py\", line 12", true)]
        [InlineData("NullReferenceException at StealthOverlayWindow.cs:100", true)]
        [InlineData("Explain binary search time complexity", false)]
        public void LooksLikeErrorOrBugQuery_ClassifiesQueriesAccurately(string query, bool expected)
        {
            Assert.Equal(expected, RepoContextService.LooksLikeErrorOrBugQuery(query));
        }

        [Fact]
        public void BuildRepoDebugContext_FindsSourceFilesAndLineHints()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "StealthRepoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string filePath = Path.Combine(tempDir, "Sample.cs");
                File.WriteAllText(filePath, "using System;\nclass Sample {\n  static void Main() { Console.WriteLine(1); }\n}\n");

                string ctx = RepoContextService.BuildRepoDebugContext(tempDir, "error in Sample.cs:line 3");
                Assert.Contains("Sample.cs", ctx);
                Assert.Contains(">>    3 |", ctx);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task GeminiStreamClient_DefaultFallbackAndHistory_WorksWithoutApiKey()
        {
            var client = new GeminiStreamClient();
            Assert.Equal(0, client.HistoryTurnCount);
            client.ClearHistory();
            Assert.Equal(0, client.HistoryTurnCount);

            var models = await GeminiStreamClient.FetchAvailableModelsAsync("");
            Assert.NotEmpty(models);
            Assert.Contains("gemini-flash-lite-latest", models);
        }

        [Fact]
        public void NativeWin32_StealthConstants_MatchExpectedWin32Values()
        {
            Assert.Equal(0x08000000, NativeWin32.WS_EX_NOACTIVATE);
            Assert.Equal(0x00000080, NativeWin32.WS_EX_TOOLWINDOW);
            Assert.Equal(0x00000008, NativeWin32.WS_EX_TOPMOST);
            Assert.Equal(0x00040000, NativeWin32.WS_EX_APPWINDOW);
            Assert.Equal((uint)0x00000011, NativeWin32.WDA_EXCLUDEFROMCAPTURE);
            Assert.Equal(3, NativeWin32.MA_NOACTIVATE);
        }

        [Fact]
        public void AdvancedTriggerService_StartStopDispose_DoesNotThrow()
        {
            using var service = new AdvancedTriggerService();
            service.Start();
            service.Stop();
        }
    }
}
