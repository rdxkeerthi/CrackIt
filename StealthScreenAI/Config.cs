using System;
using System.IO;
using System.Text.Json;

namespace StealthScreenAI
{
    public class AppConfigData
    {
        public string GeminiApiKey { get; set; } = "";
        public string GeminiModel { get; set; } = "gemini-flash-lite-latest";
        public double Opacity { get; set; } = 0.88;
        public bool ExcludeFromCapture { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = true;
        public bool ClickThrough { get; set; } = false;
        public string RepoPath { get; set; } = "";
        public string TestCommand { get; set; } = "";
        public string GitHubToken { get; set; } = "";
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        public static AppConfigData Current { get; private set; } = new();

        static ConfigManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    Current = JsonSerializer.Deserialize<AppConfigData>(json) ?? new AppConfigData();

                    // Migrate old deprecated or known-exhausted models
                    if (Current.GeminiModel == "gemini-3.6-flash" || string.IsNullOrWhiteSpace(Current.GeminiModel))
                    {
                        Current.GeminiModel = "gemini-flash-lite-latest";
                    }
                }
                // Fallback to environment variable if empty
                if (string.IsNullOrWhiteSpace(Current.GeminiApiKey))
                {
                    Current.GeminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Error loading config: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Error saving config: {ex.Message}");
            }
        }
    }
}
