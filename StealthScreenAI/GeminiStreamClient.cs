using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StealthScreenAI
{
    public enum SolveMode
    {
        General,
        Coding,
        MCQ,
        RepoFix
    }

    public class GeminiStreamClient
    {
        private sealed record ConversationTurn(string Role, string Text);

        private const int MaxHistoryTurns = 20;
        private const int MaxHistoryChars = 40000;

        private readonly List<ConversationTurn> _history = new();
        private readonly object _historyLock = new();

        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            DefaultRequestVersion = new Version(2, 0),
            // Per-phase timeouts are managed via CancellationTokenSource so long streams are never cut off mid-response
            Timeout = Timeout.InfiniteTimeSpan
        };

        // Fallback default list of models prioritized by speed, quota reliability & multimodal support
        private static readonly string[] DefaultFallbackModels = new[]
        {
            "gemini-flash-lite-latest",
            "gemini-3.1-flash-lite-preview",
            "gemini-3.5-flash-lite",
            "gemini-3-flash-preview",
            "gemini-3.1-flash-lite",
            "gemini-3.8-flash",
            "gemini-3.6-flash",
            "gemini-3.7-flash",
            "gemini-3.5-flash",
            "gemini-flash-latest",
            "gemini-pro-latest",
            "gemini-3.1-pro-preview"
        };

        private static List<string>? _cachedAvailableModels;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly SemaphoreSlim _modelFetchLock = new(1, 1);

        private const string GeneralSystemPrompt =
            "You are an elite, discreet technical assistant and exam screen solver.\n" +
            "Analyze the provided screenshot of the user's screen.\n" +
            "Extract any problem, challenge, bug, or question accurately—only describe what is actually visible.\n\n" +
            "Respond in this concise, high-density format:\n\n" +
            "### 🎯 Summary\n" +
            "- **Question**: <1-sentence summary>\n" +
            "- **Answer / Strategy**: <Direct answer or algorithmic approach>\n\n" +
            "### 💻 Solution / Code (if applicable)\n" +
            "```\n" +
            "<Clean, optimal production code or answer>\n" +
            "```\n\n" +
            "### ⚡ Key Proof\n" +
            "- <1-line justification or edge case>";

        private const string CodingSystemPrompt =
            "You are an elite competitive programmer and senior software engineer copilot.\n" +
            "Analyze the provided screenshot carefully:\n" +
            "1. Detect the coding challenge, function signature, problem constraints, and test cases.\n" +
            "2. AUTOMATICALLY DETECT THE TARGET PROGRAMMING LANGUAGE:\n" +
            "   - Check for programming language specified in the problem text (e.g., Python, C++, Java, C#, JavaScript, TypeScript, SQL, Go, Rust).\n" +
            "   - If not in text, inspect the active IDE / code editor / sandbox interface visible in the screenshot to match the exact language and syntax.\n" +
            "   - If undetermined, default to Python 3.\n\n" +
            "Respond in this ultra-concise, copy-ready format:\n\n" +
            "### 🌐 Detected Language: [Language Name]\n" +
            "### 🎯 Core Strategy: [1-sentence logic + Time: O(...) | Space: O(...)]\n\n" +
            "### 💻 Ready-to-Paste Code\n" +
            "```\n" +
            "<Optimal, full, production-ready implementation handling all edge cases>\n" +
            "```\n\n" +
            "### ⚡ Edge Cases Handled\n" +
            "- <1 to 2 bullet points on boundary conditions>";

        private const string MCQSystemPrompt =
            "You are an expert multiple choice question (MCQ) solver.\n" +
            "Analyze the provided screenshot to extract the question and all multiple-choice options (A, B, C, D, etc.).\n\n" +
            "Respond IMMEDIATELY in this exact format:\n\n" +
            "# ✅ CORRECT ANSWER: [OPTION LETTER] - [Option Text]\n\n" +
            "### 💡 Proof / Justification:\n" +
            "<1 to 2 sentences proving why this option is correct and why other options are invalid>";

        private const string VivaSystemPrompt =
            "You are an elite viva exam, technical interview, and oral defense copilot.\n" +
            "The user is in a live oral viva or technical interview. An oral question was asked in the provided system audio.\n\n" +
            "Analyze the audio and respond in this high-impact, immediate viva format:\n\n" +
            "### ❓ Detected Question\n" +
            "<Exact transcription of the oral question>\n\n" +
            "### 🗣️ WHAT TO SAY (Viva Spoken Answer)\n" +
            "> \"<Natural, confident, concise 2-4 sentences the candidate should speak out loud to the examiner right now>\"\n\n" +
            "### 🧠 Key Concepts & Follow-Ups\n" +
            "- **Core Principle**: <1-2 bullet points explaining how it works>\n" +
            "- **Anticipated Follow-Up**: <What the examiner might ask next and quick 1-line answer>\n\n" +
            "### 💻 Code / Technical Syntax (if applicable)\n" +
            "```\n" +
            "<Minimal code or command snippet>\n" +
            "```\n\n" +
            "Prioritize giving the spoken answer first so the user can speak to the examiner immediately without delay.";

        private const string ChatSystemPrompt =
            "You are an elite technical assistant, coding copilot, and debugging partner.\n" +
            "Continue the conversation using previous context whenever the new query relates to earlier questions, code, or screen captures.\n" +
            "If the user switches to a completely new topic, address the new query directly.\n" +
            "Keep answers accurate, concise, well-structured, and strictly grounded in provided screenshots, code, or repository files.";

        private const string RepoFixSystemPrompt =
            "You are a senior software engineer specializing in root-cause debugging and repository bug fixing (both local repos and browser-based IDEs like GitHub Codespaces, VS Code Web, or Replit).\n" +
            "You will be given failing test output, error messages, screenshots (inspect open editor tabs, line numbers in gutter, explorer file tree, and terminal/Problems output), and/or numbered source files.\n" +
            "Only cite file paths, line numbers, and symbols present in the provided screenshot, output, or files.\n\n" +
            "Respond in this clear, actionable format:\n\n" +
            "### 🐞 What Failed (Root Cause)\n" +
            "- <Concise explanation of why the error/test failure occurred>\n\n" +
            "### 📍 Where to Fix\n" +
            "- **File**: `<file path or tab name>`\n" +
            "- **Line(s)**: `<line number(s) and symbol/method name>`\n\n" +
            "### 🔧 Exact Fix\n" +
            "```\n" +
            "<Corrected code snippet showing the exact fix>\n" +
            "```\n\n" +
            "### ✅ Next / Verify\n" +
            "- <Command to re-run, OR if the failing file wasn't visible on screen yet, tell the user which file to click open in their browser IDE and to press [🐞 Fix] or send with [📷 Screen] checked>";

        public int HistoryTurnCount
        {
            get { lock (_historyLock) return _history.Count; }
        }

        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _history.Clear();
            }
        }

        private void AppendHistory(string userText, string modelText)
        {
            if (string.IsNullOrWhiteSpace(modelText)) return;

            lock (_historyLock)
            {
                _history.Add(new ConversationTurn("user", userText));
                _history.Add(new ConversationTurn("model", modelText));

                while (_history.Count > MaxHistoryTurns)
                {
                    _history.RemoveAt(0);
                }

                int totalChars = _history.Sum(t => t.Text.Length);
                while (_history.Count > 2 && totalChars > MaxHistoryChars)
                {
                    totalChars -= _history[0].Text.Length;
                    _history.RemoveAt(0);
                }
            }
        }

        private object BuildConversationPayload(string systemPrompt, string currentUserText, object[]? extraMediaParts = null)
        {
            var contents = new List<object>();

            lock (_historyLock)
            {
                foreach (var turn in _history)
                {
                    contents.Add(new
                    {
                        role = turn.Role,
                        parts = new object[] { new { text = turn.Text } }
                    });
                }
            }

            var currentParts = new List<object> { new { text = currentUserText } };
            if (extraMediaParts != null)
            {
                currentParts.AddRange(extraMediaParts);
            }

            contents.Add(new
            {
                role = "user",
                parts = currentParts.ToArray()
            });

            return new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = contents.ToArray(),
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 8192
                }
            };
        }

        /// <summary>
        /// Dynamically retrieves all available models for the given API key from Google's API,
        /// filtering for models that support generateContent and multimodal inputs.
        /// </summary>
        public static async Task<List<string>> FetchAvailableModelsAsync(string apiKey, bool forceRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return DefaultFallbackModels.ToList();
            }

            if (!forceRefresh && _cachedAvailableModels != null && DateTime.UtcNow < _cacheExpiry)
            {
                return new List<string>(_cachedAvailableModels);
            }

            await _modelFetchLock.WaitAsync();
            try
            {
                if (!forceRefresh && _cachedAvailableModels != null && DateTime.UtcNow < _cacheExpiry)
                {
                    return new List<string>(_cachedAvailableModels);
                }

                string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await HttpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("models", out var modelsElem) &&
                        modelsElem.ValueKind == JsonValueKind.Array)
                    {
                        var fetchedModels = new List<string>();

                        foreach (var m in modelsElem.EnumerateArray())
                        {
                            if (!m.TryGetProperty("name", out var nameElem)) continue;
                            string fullName = nameElem.GetString() ?? "";
                            string modelName = fullName.StartsWith("models/") ? fullName.Substring(7) : fullName;

                            bool supportsGenerate = false;
                            if (m.TryGetProperty("supportedGenerationMethods", out var methodsElem) &&
                                methodsElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var method in methodsElem.EnumerateArray())
                                {
                                    if (method.GetString() == "generateContent")
                                    {
                                        supportsGenerate = true;
                                        break;
                                    }
                                }
                            }

                            if (!supportsGenerate) continue;

                            string lower = modelName.ToLowerInvariant();
                            if (lower.EndsWith("-tts") || lower.EndsWith("-tts-preview") ||
                                lower.Contains("embedding") || lower.Contains("aqa") ||
                                lower.Contains("lyria") || lower.Contains("robotics") ||
                                lower.Contains("transcribe") || lower.Contains("clip"))
                            {
                                continue;
                            }

                            fetchedModels.Add(modelName);
                        }

                        if (fetchedModels.Count > 0)
                        {
                            var sorted = PrioritizeModels(fetchedModels);
                            _cachedAvailableModels = sorted;
                            _cacheExpiry = DateTime.UtcNow.AddMinutes(20);
                            return new List<string>(_cachedAvailableModels);
                        }
                    }
                }
            }
            catch
            {
                // Fall back to default list on network errors
            }
            finally
            {
                _modelFetchLock.Release();
            }

            return DefaultFallbackModels.ToList();
        }

        private static List<string> PrioritizeModels(IEnumerable<string> models)
        {
            var result = new List<string>();
            var set = new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);

            string[] preferredOrder = new[]
            {
                "gemini-flash-lite-latest",
                "gemini-3.1-flash-lite-preview",
                "gemini-3.5-flash-lite",
                "gemini-3-flash-preview",
                "gemini-3.1-flash-lite",
                "gemini-3.8-flash",
                "gemini-3.6-flash",
                "gemini-3.7-flash",
                "gemini-3.5-flash",
                "gemini-flash-latest",
                "gemini-pro-latest",
                "gemini-3.1-pro-preview",
                "gemini-2.5-flash",
                "gemma-4-26b-a4b-it",
                "gemma-4-31b-it"
            };

            foreach (var pref in preferredOrder)
            {
                if (set.Contains(pref))
                {
                    result.Add(pref);
                    set.Remove(pref);
                }
            }

            foreach (var remaining in set)
            {
                result.Add(remaining);
            }

            return result;
        }

        public async Task StreamSolveScreenAsync(
            string imageBase64,
            SolveMode mode,
            Action<string> onChunkReceived,
            Action<string>? onStatusUpdate = null,
            string? repoContext = null,
            CancellationToken ct = default)
        {
            string apiKey = ConfigManager.Current.GeminiApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onChunkReceived("⚠️ **Gemini API Key Missing**\n\nPlease click **[🔑 Key]** on the bar to save your Gemini API Key.");
                return;
            }

            string systemPrompt = mode switch
            {
                SolveMode.Coding => CodingSystemPrompt,
                SolveMode.MCQ => MCQSystemPrompt,
                SolveMode.RepoFix => RepoFixSystemPrompt,
                _ => GeneralSystemPrompt
            };

            string userPrompt = mode switch
            {
                SolveMode.Coding => "Analyze this screenshot and solve the coding problem in the exact target language.",
                SolveMode.MCQ => "Analyze this screenshot and identify the correct multiple-choice answer with concise justification.",
                SolveMode.RepoFix => "Analyze the error/bug visible in this screenshot" +
                    (string.IsNullOrWhiteSpace(repoContext)
                        ? " and explain what and where to fix."
                        : $" alongside this repository context:\n\n{repoContext}"),
                _ => "Analyze this screenshot and solve the problem or answer the question shown."
            };

            var mediaParts = new object[]
            {
                new
                {
                    inline_data = new
                    {
                        mime_type = "image/jpeg",
                        data = imageBase64
                    }
                }
            };

            var requestBody = BuildConversationPayload(systemPrompt, userPrompt, mediaParts);
            string fullResponse = await ExecuteWithModelFallbackAsync(requestBody, onChunkReceived, onStatusUpdate, ct);

            if (!string.IsNullOrWhiteSpace(fullResponse))
            {
                string summaryPrompt = mode == SolveMode.RepoFix
                    ? "[Screen Capture — Repo Bug Diagnosis]"
                    : $"[Screen Capture — {mode} Solve]";
                AppendHistory(summaryPrompt, fullResponse);
            }
        }

        public async Task StreamSolveAudioAsync(
            string audioBase64,
            Action<string> onChunkReceived,
            Action<string>? onStatusUpdate = null,
            CancellationToken ct = default)
        {
            string apiKey = ConfigManager.Current.GeminiApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onChunkReceived("⚠️ **Gemini API Key Missing**\n\nPlease click **[🔑 Key]** on the bar to save your Gemini API Key.");
                return;
            }

            var mediaParts = new object[]
            {
                new
                {
                    inline_data = new
                    {
                        mime_type = "audio/wav",
                        data = audioBase64
                    }
                }
            };

            var requestBody = BuildConversationPayload(VivaSystemPrompt, "Transcribe the oral question in this audio and give the spoken viva answer.", mediaParts);
            string fullResponse = await ExecuteWithModelFallbackAsync(requestBody, onChunkReceived, onStatusUpdate, ct);

            if (!string.IsNullOrWhiteSpace(fullResponse))
            {
                AppendHistory("[Audio Viva Question]", fullResponse);
            }
        }

        public async Task StreamChatAsync(
            string userMessage,
            string? imageBase64,
            string? repoContext,
            bool isBugFixMode,
            Action<string> onChunkReceived,
            Action<string>? onStatusUpdate = null,
            CancellationToken ct = default)
        {
            if (TryHandleLocalAutomation(userMessage, out string localResult))
            {
                onChunkReceived(localResult);
                return;
            }

            string apiKey = ConfigManager.Current.GeminiApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onChunkReceived("⚠️ **Gemini API Key Missing**\n\nPlease click **[🔑 Key]** on the bar to save your Gemini API Key.");
                return;
            }

            string systemPrompt = isBugFixMode ? RepoFixSystemPrompt : ChatSystemPrompt;
            string promptText = string.IsNullOrWhiteSpace(repoContext)
                ? userMessage
                : $"{userMessage}\n\n---\n{repoContext}";

            object[]? mediaParts = null;
            if (!string.IsNullOrWhiteSpace(imageBase64))
            {
                mediaParts = new object[]
                {
                    new
                    {
                        inline_data = new
                        {
                            mime_type = "image/jpeg",
                            data = imageBase64
                        }
                    }
                };
            }

            var requestBody = BuildConversationPayload(systemPrompt, promptText, mediaParts);
            string fullResponse = await ExecuteWithModelFallbackAsync(requestBody, onChunkReceived, onStatusUpdate, ct);

            if (!string.IsNullOrWhiteSpace(fullResponse))
            {
                // Keep history compact by storing userMessage (plus repo hint) rather than full raw dump if huge
                string historyUserEntry = userMessage.Length > 4000
                    ? userMessage.Substring(0, 4000) + "\n...[truncated]..."
                    : userMessage;
                AppendHistory(historyUserEntry, fullResponse);
            }
        }

        private static bool TryHandleLocalAutomation(string commandText, out string result)
        {
            string lower = commandText.Trim().ToLowerInvariant();

            if (lower.Contains("turn off wifi") || lower.Contains("disable wifi") || lower == "wifi off")
            {
                result = NativeAutomation.DisconnectWiFi();
                return true;
            }
            if (lower.StartsWith("close ") || lower.StartsWith("kill ") || lower.StartsWith("terminate "))
            {
                string target = lower.Replace("close ", "").Replace("kill ", "").Replace("terminate ", "").Trim();
                result = NativeAutomation.TerminateProcessByName(target);
                return true;
            }
            if (lower == "mute" || lower == "unmute" || lower.Contains("toggle mute"))
            {
                result = NativeAutomation.ToggleMute();
                return true;
            }
            if (lower.Contains("volume up") || lower.Contains("increase volume"))
            {
                result = NativeAutomation.AdjustVolume(true);
                return true;
            }
            if (lower.Contains("volume down") || lower.Contains("decrease volume"))
            {
                result = NativeAutomation.AdjustVolume(false);
                return true;
            }

            result = "";
            return false;
        }

        private async Task<string> ExecuteWithModelFallbackAsync(
            object requestBody,
            Action<string> onChunkReceived,
            Action<string>? onStatusUpdate,
            CancellationToken ct)
        {
            string apiKey = ConfigManager.Current.GeminiApiKey;
            string userModel = ConfigManager.Current.GeminiModel?.Trim() ?? "";
            string jsonPayload = JsonSerializer.Serialize(requestBody);

            List<string> availableModels = await FetchAvailableModelsAsync(apiKey);

            var modelsToTry = new List<string>();
            if (!string.IsNullOrWhiteSpace(userModel) &&
                !userModel.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                !userModel.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                modelsToTry.Add(userModel);
            }

            foreach (var m in availableModels)
            {
                if (!modelsToTry.Contains(m, StringComparer.OrdinalIgnoreCase))
                {
                    modelsToTry.Add(m);
                }
            }

            string lastError = "";

            for (int i = 0; i < modelsToTry.Count; i++)
            {
                if (ct.IsCancellationRequested) return "";

                string rawModel = modelsToTry[i];
                string cleanModel = rawModel.StartsWith("models/") ? rawModel.Substring(7) : rawModel;
                string? nextModel = (i + 1 < modelsToTry.Count) ? modelsToTry[i + 1] : null;

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:streamGenerateContent?alt=sse&key={apiKey}";

                var accumulated = new StringBuilder();
                bool receivedAnyToken = false;

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // Stage 1: 20s timeout only to establish connection & read response headers
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(20));

                    using HttpResponseMessage response = await HttpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        connectCts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                        using var reader = new StreamReader(stream);

                        string? finishReason = null;

                        while (!reader.EndOfStream && !ct.IsCancellationRequested)
                        {
                            // Stage 2: Resettable 35s inactivity timeout per SSE line (never cuts off active long streams)
                            using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            chunkCts.CancelAfter(TimeSpan.FromSeconds(35));

                            string? line = await reader.ReadLineAsync(chunkCts.Token);
                            if (line == null) break;
                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;

                            string dataJson = line.Substring(5).Trim();
                            if (string.IsNullOrEmpty(dataJson) || dataJson == "[DONE]") continue;

                            var (token, reason) = ExtractTokenAndFinishReason(dataJson);
                            if (!string.IsNullOrEmpty(reason))
                            {
                                finishReason = reason;
                            }
                            if (!string.IsNullOrEmpty(token))
                            {
                                receivedAnyToken = true;
                                accumulated.Append(token);
                                onChunkReceived(token);
                            }
                        }

                        if (receivedAnyToken)
                        {
                            if (!string.IsNullOrEmpty(finishReason) &&
                                !finishReason.Equals("STOP", StringComparison.OrdinalIgnoreCase))
                            {
                                string notice = $"\n\nℹ️ [Response ended: {finishReason}]";
                                accumulated.Append(notice);
                                onChunkReceived(notice);
                            }

                            if (!string.Equals(ConfigManager.Current.GeminiModel, cleanModel, StringComparison.OrdinalIgnoreCase))
                            {
                                ConfigManager.Current.GeminiModel = cleanModel;
                                ConfigManager.Save();
                            }
                            return accumulated.ToString();
                        }
                        else
                        {
                            lastError = $"{cleanModel} finished stream with 0 tokens.";
                        }
                    }
                    else
                    {
                        string errBody = await response.Content.ReadAsStringAsync(connectCts.Token);
                        int statusCode = (int)response.StatusCode;
                        lastError = $"HTTP {statusCode} ({cleanModel}): {errBody}";

                        if (nextModel != null)
                        {
                            string reason = statusCode == 429 ? "Quota reached (429)" :
                                           statusCode == 404 ? "Model deprecated (404)" :
                                           $"Error ({statusCode})";
                            onStatusUpdate?.Invoke($"⚡ {cleanModel} {reason} → Auto-switching to {nextModel}...");
                        }

                        await Task.Delay(150, ct);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return accumulated.ToString();
                }
                catch (Exception ex)
                {
                    // Never erase or switch away if tokens were already streamed to the user
                    if (receivedAnyToken)
                    {
                        string warn = $"\n\n⚠️ [Stream interrupted: {ex.Message}]";
                        accumulated.Append(warn);
                        onChunkReceived(warn);
                        return accumulated.ToString();
                    }

                    lastError = $"{cleanModel} error: {ex.Message}";
                    if (nextModel != null)
                    {
                        onStatusUpdate?.Invoke($"⚡ {cleanModel} timeout/network issue → Auto-switching to {nextModel}...");
                    }
                    try { await Task.Delay(150, ct); } catch { return ""; }
                }
            }

            if (!ct.IsCancellationRequested)
            {
                onChunkReceived($"\n❌ **All Gemini Models Failed**\n\nDetails: {lastError}\n\n💡 Tip: Your API key may have reached overall quota limits across models. Click **[🔑 Key]** on the bar to test or update your key.");
            }
            return "";
        }

        public async Task<string> ProcessUserCommandAsync(string commandText)
        {
            var sb = new StringBuilder();
            bool isBug = RepoContextService.LooksLikeErrorOrBugQuery(commandText);
            string repoPath = RepoContextService.ResolveActiveRepoPath();
            string repoCtx = isBug && !string.IsNullOrEmpty(repoPath)
                ? await RepoContextService.BuildRepoDebugContextAsync(repoPath, commandText)
                : "";

            await StreamChatAsync(
                commandText,
                imageBase64: null,
                repoContext: repoCtx,
                isBugFixMode: isBug,
                onChunkReceived: chunk => sb.Append(chunk));

            return sb.ToString();
        }

        private static (string? Token, string? FinishReason) ExtractTokenAndFinishReason(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCand = candidates[0];
                    string? finishReason = null;
                    if (firstCand.TryGetProperty("finishReason", out var finishElem) &&
                        finishElem.ValueKind == JsonValueKind.String)
                    {
                        finishReason = finishElem.GetString();
                    }

                    if (firstCand.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("thought", out var thoughtElem) && thoughtElem.GetBoolean())
                            {
                                continue;
                            }
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                string? text = textElem.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    sb.Append(text);
                                }
                            }
                        }

                        return (sb.Length > 0 ? sb.ToString() : null, finishReason);
                    }

                    return (null, finishReason);
                }
            }
            catch { }
            return (null, null);
        }
    }
}
