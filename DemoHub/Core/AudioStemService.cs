using System.IO;

namespace CopilotPlusDemoHub.Core;

/// <summary>
/// Generates and manages 4 synchronized musical stems (Drums, Bass, Piano, Lead Guitar)
/// for a 3-minute demonstration track (120 BPM, 4/4 meter, C - G - Am - F).
/// </summary>
public sealed class AudioStemService
{
    public const int SampleRate = 44100;
    public const int TotalDurationSeconds = 180; // 3 minutes
    public const double TempoBpm = 120.0;
    public const double BeatDuration = 60.0 / TempoBpm; // 0.5 sec per beat

    public record ChordDefinition(
        string Name,
        string LocalizedName,
        string[] Notes,
        double[] Frequencies,
        int RootMidi);

    public static readonly ChordDefinition[] Progression =
    [
        new("C", "Do Majeur [C]", ["Do (C4)", "Mi (E4)", "Sol (G4)"], [261.63, 329.63, 392.00], 60),
        new("G", "Sol Majeur [G]", ["Sol (G3)", "Si (B3)", "Ré (D4)"], [196.00, 246.94, 293.66], 55),
        new("Am", "La mineur [Am]", ["La (A3)", "Do (C4)", "Mi (E4)"], [220.00, 261.63, 329.63], 57),
        new("F", "Fa Majeur [F]", ["Fa (F3)", "La (A3)", "Do (C4)"], [174.61, 220.00, 261.63], 53)
    ];

    private readonly string _stemsDirectory;

    public string DrumsPath => Path.Combine(_stemsDirectory, "stem_drums.wav");
    public string BassPath => Path.Combine(_stemsDirectory, "stem_bass.wav");
    public string PianoPath => Path.Combine(_stemsDirectory, "stem_piano.wav");
    public string LeadPath => Path.Combine(_stemsDirectory, "stem_lead.wav");

    public AudioStemService()
    {
        _stemsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioStems");
        EnsureStemsGenerated();
    }

    public static ChordDefinition GetChordAtSecond(double seconds)
    {
        // 2 bars per chord = 4 seconds per chord (120 BPM -> 2 sec per bar)
        int chordIndex = (int)(seconds / 4.0) % Progression.Length;
        return Progression[chordIndex];
    }

    public static (bool drums, bool bass, bool piano, bool lead) GetStemArrangementAtSecond(double seconds)
    {
        // Song structure over 180s:
        // 0-8s: Intro (Piano only)
        // 8-16s: Intro full (Piano + Bass)
        // 16-64s: Verse 1 + Chorus 1 (All 4 instruments)
        // 64-80s: Verse 2 breakdown (Bass + Piano)
        // 80-140s: Chorus 2 + Bridge (All 4 instruments)
        // 140-164s: Guitar Solo climax (Drums + Bass + Lead Guitar)
        // 164-180s: Outro (All 4 instruments fading to Piano)
        if (seconds < 8.0) return (drums: false, bass: false, piano: true, lead: false);
        if (seconds < 16.0) return (drums: false, bass: true, piano: true, lead: false);
        if (seconds < 64.0) return (drums: true, bass: true, piano: true, lead: true);
        if (seconds < 80.0) return (drums: false, bass: true, piano: true, lead: false);
        if (seconds < 140.0) return (drums: true, bass: true, piano: true, lead: true);
        if (seconds < 164.0) return (drums: true, bass: true, piano: false, lead: true);
        return (drums: true, bass: true, piano: true, lead: seconds < 174.0);
    }

    private void EnsureStemsGenerated()
    {
        Directory.CreateDirectory(_stemsDirectory);

        if (File.Exists(DrumsPath) && File.Exists(BassPath) &&
            File.Exists(PianoPath) && File.Exists(LeadPath))
        {
            // Already generated
            return;
        }

        GenerateDrumsStem(DrumsPath);
        GenerateBassStem(BassPath);
        GeneratePianoStem(PianoPath);
        GenerateLeadStem(LeadPath);
    }

    private static void GenerateDrumsStem(string filePath)
    {
        int totalSamples = SampleRate * TotalDurationSeconds;
        short[] buffer = new short[totalSamples];
        var random = new Random(42);

        int beatSamples = (int)(SampleRate * BeatDuration); // 0.5s
        int eighthSamples = beatSamples / 2;                // 0.25s

        for (int sample = 0; sample < totalSamples; sample++)
        {
            double sec = (double)sample / SampleRate;
            var (drumsActive, _, _, _) = GetStemArrangementAtSecond(sec);
            if (!drumsActive) continue;

            int beatInBar = (sample / beatSamples) % 4;
            int offsetInBeat = sample % beatSamples;
            double tBeat = (double)offsetInBeat / SampleRate;

            double val = 0.0;

            // 1. Kick on Beat 0 and 2
            if (beatInBar is 0 or 2 && tBeat < 0.2)
            {
                double kickFreq = 120.0 * Math.Exp(-tBeat * 25.0) + 40.0;
                double kickEnv = Math.Exp(-tBeat * 16.0);
                val += Math.Sin(2.0 * Math.PI * kickFreq * tBeat) * kickEnv * 0.7;
            }

            // 2. Snare on Beat 1 and 3
            if (beatInBar is 1 or 3 && tBeat < 0.25)
            {
                double snareTone = Math.Sin(2.0 * Math.PI * 180.0 * tBeat) * Math.Exp(-tBeat * 20.0) * 0.35;
                double snareNoise = (random.NextDouble() * 2.0 - 1.0) * Math.Exp(-tBeat * 15.0) * 0.45;
                val += snareTone + snareNoise;
            }

            // 3. Hi-Hat on every eighth note
            int offsetInEighth = sample % eighthSamples;
            double tEighth = (double)offsetInEighth / SampleRate;
            if (tEighth < 0.05)
            {
                double hatNoise = (random.NextDouble() * 2.0 - 1.0) * Math.Exp(-tEighth * 70.0) * 0.25;
                val += hatNoise;
            }

            buffer[sample] = (short)Math.Clamp(val * 32767.0, -32767.0, 32767.0);
        }

        WritePcmWavFile(filePath, buffer, SampleRate);
    }

    private static void GenerateBassStem(string filePath)
    {
        int totalSamples = SampleRate * TotalDurationSeconds;
        short[] buffer = new short[totalSamples];

        // Root notes for C - G - Am - F
        double[] rootFreqs = [65.41, 49.00, 55.00, 43.65]; // C2, G1, A1, F1
        int eighthSamples = (int)(SampleRate * (BeatDuration / 2.0));

        for (int sample = 0; sample < totalSamples; sample++)
        {
            double sec = (double)sample / SampleRate;
            var (_, bassActive, _, _) = GetStemArrangementAtSecond(sec);
            if (!bassActive) continue;

            int chordIdx = (int)(sec / 4.0) % rootFreqs.Length;
            double freq = rootFreqs[chordIdx];

            int offsetInEighth = sample % eighthSamples;
            double tNote = (double)offsetInEighth / SampleRate;

            // Punchy bass envelope
            double env = Math.Exp(-tNote * 8.0);
            double wave = Math.Sin(2.0 * Math.PI * freq * tNote) * 0.7
                        + Math.Sin(2.0 * Math.PI * (freq * 2.0) * tNote) * 0.3;

            buffer[sample] = (short)Math.Clamp(wave * env * 26000.0, -32767.0, 32767.0);
        }

        WritePcmWavFile(filePath, buffer, SampleRate);
    }

    private static void GeneratePianoStem(string filePath)
    {
        int totalSamples = SampleRate * TotalDurationSeconds;
        short[] buffer = new short[totalSamples];

        int beatSamples = (int)(SampleRate * BeatDuration);

        for (int sample = 0; sample < totalSamples; sample++)
        {
            double sec = (double)sample / SampleRate;
            var (_, _, pianoActive, _) = GetStemArrangementAtSecond(sec);
            if (!pianoActive) continue;

            var chord = GetChordAtSecond(sec);
            int offsetInBeat = sample % beatSamples;
            double tBeat = (double)offsetInBeat / SampleRate;

            // Play chord hits on beats 0, 1.5, 2.5
            int beatInBar = (sample / beatSamples) % 4;
            bool isHit = beatInBar == 0 || beatInBar == 2;
            if (!isHit) continue;

            double env = Math.Exp(-tBeat * 3.5);
            double val = 0.0;

            foreach (double f in chord.Frequencies)
            {
                // Piano harmonic spectrum (fundamental + overtones)
                val += Math.Sin(2.0 * Math.PI * f * tBeat) * 0.4;
                val += Math.Sin(2.0 * Math.PI * (f * 2.0) * tBeat) * 0.2;
                val += Math.Sin(2.0 * Math.PI * (f * 3.0) * tBeat) * 0.1;
            }

            buffer[sample] = (short)Math.Clamp(val * env * 18000.0, -32767.0, 32767.0);
        }

        WritePcmWavFile(filePath, buffer, SampleRate);
    }

    private static void GenerateLeadStem(string filePath)
    {
        int totalSamples = SampleRate * TotalDurationSeconds;
        short[] buffer = new short[totalSamples];

        // Pentatonic scale notes (C5, D5, E5, G5, A5)
        double[] leadNotes = [523.25, 587.33, 659.25, 783.99, 880.00];
        int noteSamples = (int)(SampleRate * (BeatDuration / 2.0)); // 16th/8th notes

        for (int sample = 0; sample < totalSamples; sample++)
        {
            double sec = (double)sample / SampleRate;
            var (_, _, _, leadActive) = GetStemArrangementAtSecond(sec);
            if (!leadActive) continue;

            int noteIndex = (int)(sec / 0.5) % leadNotes.Length;
            double freq = leadNotes[noteIndex];

            int offsetInNote = sample % noteSamples;
            double tNote = (double)offsetInNote / SampleRate;

            // Vibrato & smooth envelope
            double vibrato = Math.Sin(2.0 * Math.PI * 6.0 * tNote) * 4.0;
            double env = Math.Min(tNote * 25.0, 1.0) * Math.Exp(-tNote * 3.0);
            double wave = Math.Sin(2.0 * Math.PI * (freq + vibrato) * tNote) * 0.7
                        + Math.Sin(2.0 * Math.PI * ((freq + vibrato) * 2.0) * tNote) * 0.25;

            buffer[sample] = (short)Math.Clamp(wave * env * 22000.0, -32767.0, 32767.0);
        }

        WritePcmWavFile(filePath, buffer, SampleRate);
    }

    private static void WritePcmWavFile(string filePath, short[] pcmData, int sampleRate)
    {
        int byteRate = sampleRate * 1 * sizeof(short);
        int dataChunkSize = pcmData.Length * sizeof(short);

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // RIFF Header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataChunkSize);
        writer.Write("WAVE"u8.ToArray());

        // fmt sub-chunk
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);          // Subchunk1Size
        writer.Write((short)1);     // AudioFormat (PCM = 1)
        writer.Write((short)1);     // NumChannels (Mono = 1)
        writer.Write(sampleRate);   // SampleRate
        writer.Write(byteRate);     // ByteRate
        writer.Write((short)2);     // BlockAlign (1 * 16 / 8)
        writer.Write((short)16);    // BitsPerSample

        // data sub-chunk
        writer.Write("data"u8.ToArray());
        writer.Write(dataChunkSize);

        byte[] byteBuffer = new byte[pcmData.Length * sizeof(short)];
        Buffer.BlockCopy(pcmData, 0, byteBuffer, 0, byteBuffer.Length);
        writer.Write(byteBuffer);
    }
}
