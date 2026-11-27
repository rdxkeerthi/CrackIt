using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace StealthScreenAI
{
    /// <summary>
    /// Captures system audio (WASAPI Loopback - what the interviewer/computer is saying)
    /// and microphone audio in RAM with zero disk I/O.
    /// Used to solve oral viva questions and voice-based tests.
    /// </summary>
    public static class AudioCaptureService
    {
        private static bool _isRecording = false;
        private static CancellationTokenSource? _recordCts;

        public static bool IsRecording => _isRecording;

        /// <summary>
        /// Captures system audio loopback (interviewer speaking through speakers/headphones).
        /// If loopback is silent or unavailable, captures from default microphone.
        /// </summary>
        public static async Task<string> CaptureSystemAudioWavBase64Async(
            int durationSeconds = 8,
            Action<int>? onCountdownTick = null,
            CancellationToken externalCt = default)
        {
            _recordCts?.Cancel();
            _recordCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            CancellationToken ct = _recordCts.Token;

            _isRecording = true;
            using var memStream = new MemoryStream();

            try
            {
#pragma warning disable CS0618
                // 1. Attempt WASAPI Loopback Capture (System Audio output)
                IWaveIn? captureDevice = null;
                try
                {
                    captureDevice = new WasapiLoopbackCapture();
                }
                catch
                {
                    // Fallback to default Microphone if loopback is restricted
                    try
                    {
                        captureDevice = new WaveIn
                        {
                            WaveFormat = new WaveFormat(16000, 16, 1)
                        };
                    }
                    catch
                    {
                        return "";
                    }
                }
#pragma warning restore CS0618

                using (captureDevice)
                {
                    WaveFileWriter? writer = null;
                    try
                    {
                        writer = new WaveFileWriter(memStream, captureDevice.WaveFormat);

                        captureDevice.DataAvailable += (s, e) =>
                        {
                            if (writer != null && e.BytesRecorded > 0)
                            {
                                lock (writer)
                                {
                                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                                }
                            }
                        };

                        captureDevice.StartRecording();

                        for (int remaining = durationSeconds; remaining > 0; remaining--)
                        {
                            if (ct.IsCancellationRequested) break;
                            onCountdownTick?.Invoke(remaining);
                            await Task.Delay(1000, ct);
                        }

                        captureDevice.StopRecording();
                        await Task.Delay(150); // Flush buffer
                    }
                    finally
                    {
                        if (writer != null)
                        {
                            writer.Flush();
                            writer.Dispose();
                        }
                    }
                }

                byte[] wavBytes = memStream.ToArray();
                if (wavBytes.Length < 100) return "";
                return Convert.ToBase64String(wavBytes);
            }
            catch (OperationCanceledException)
            {
                byte[] wavBytes = memStream.ToArray();
                return wavBytes.Length >= 100 ? Convert.ToBase64String(wavBytes) : "";
            }
            catch
            {
                return "";
            }
            finally
            {
                _isRecording = false;
            }
        }

        public static void StopRecordingEarly()
        {
            _recordCts?.Cancel();
        }
    }
}
