using System;
using System.Threading.Tasks;
using NAudio.Wave;
using System.Media;

class Program
{
    static async Task Main()
    {
        await CaptureSystemSound("Error", SystemSounds.Hand);
        await CaptureSystemSound("Warning", SystemSounds.Exclamation);
        await CaptureSystemSound("Information", SystemSounds.Asterisk);
        Console.WriteLine("Done.");
    }

    static async Task CaptureSystemSound(string name, SystemSound sound)
    {
        string fileName = $"{name}.wav";
        using var capture = new WasapiLoopbackCapture();
        using var writer = new WaveFileWriter(fileName, capture.WaveFormat);

        capture.DataAvailable += (s, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);

        // Start recording just before we play the sound
        capture.StartRecording();

        // tiny delay to ensure the capture graph is primed
        await Task.Delay(50);

        // Play the system sound
        sound.Play();

        // allow time for playback tail to capture
        await Task.Delay(1200);

        capture.StopRecording();
        Console.WriteLine($"Saved {fileName}");
    }
}
