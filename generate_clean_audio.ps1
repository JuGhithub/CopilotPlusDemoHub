Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice = $synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq 'fr' } | Select-Object -First 1
if ($voice) { 
    $synth.SelectVoice($voice.VoiceInfo.Name)
    Write-Host ("Selected voice: " + $voice.VoiceInfo.Name) 
}
$synth.SetOutputToWaveFile('Data/Audio/reunion_codir.wav')
$synth.Rate = 0
$synth.Speak("Bonjour à tous et bienvenue à notre réunion de direction. À l'ordre du jour : le déploiement de la flotte de PC Copilot Plus. Grâce au NPU dédié, nos collaborateurs bénéficient d'une transcription en direct ultra-rapide, d'une autonomie de batterie record et d'une sécurité totale sans envoyer aucune donnée dans le cloud.")
$synth.Dispose()
Write-Host 'GENERATED_OK'
