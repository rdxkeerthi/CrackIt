using System;
using System.Globalization;
using System.Speech.Recognition;
using System.Threading.Tasks;

namespace StealthScreenAI
{
    public class VoiceRecognitionService : IDisposable
    {
        private SpeechRecognitionEngine? _recognizer;
        public event Action<string>? VoiceCommandRecognized;
        public event Action<string>? VoiceStatusChanged;
        public bool IsListening { get; private set; } = false;

        public void Initialize()
        {
            try
            {
                // Initialize default system culture recognizer
                _recognizer = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);

                var choices = new Choices();
                // Common stealth assistant commands
                choices.Add(new string[]
                {
                    "solve screen",
                    "solve",
                    "scan screen",
                    "viva",
                    "listen viva",
                    "listen",
                    "audio solve",
                    "hide",
                    "show",
                    "ghost mode",
                    "turn off wifi",
                    "disable wifi",
                    "close edge",
                    "close chrome",
                    "mute",
                    "unmute",
                    "volume up",
                    "volume down",
                    "paste code"
                });

                var gb = new GrammarBuilder(choices);
                var g = new Grammar(gb);
                _recognizer.LoadGrammar(g);

                _recognizer.SpeechRecognized += OnSpeechRecognized;
                _recognizer.SetInputToDefaultAudioDevice();

                // Off by default so ambient speech/noise never triggers unintended refreshes or responses.
                // Users can explicitly toggle voice commands from the tray menu.
                IsListening = false;
                VoiceStatusChanged?.Invoke("🎤 VOICE: STANDBY");
            }
            catch (Exception ex)
            {
                IsListening = false;
                VoiceStatusChanged?.Invoke($"🎤 VOICE: OFF ({ex.Message})");
            }
        }

        public void ToggleListening()
        {
            if (_recognizer == null) return;

            if (IsListening)
            {
                _recognizer.RecognizeAsyncCancel();
                IsListening = false;
                VoiceStatusChanged?.Invoke("🎤 VOICE: MUTED");
            }
            else
            {
                try
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    IsListening = true;
                    VoiceStatusChanged?.Invoke("🎤 VOICE: ACTIVE");
                }
                catch
                {
                    IsListening = false;
                }
            }
        }

        private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (IsListening && e.Result != null && e.Result.Confidence > 0.72f)
            {
                string spoken = e.Result.Text.Trim();
                if (!string.IsNullOrWhiteSpace(spoken))
                {
                    VoiceCommandRecognized?.Invoke(spoken);
                }
            }
        }

        public void Dispose()
        {
            if (_recognizer != null)
            {
                try
                {
                    _recognizer.RecognizeAsyncCancel();
                    _recognizer.Dispose();
                }
                catch { }
                _recognizer = null;
            }
        }
    }
}
