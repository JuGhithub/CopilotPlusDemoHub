Add-Type -AssemblyName System.Speech
 = New-Object System.Speech.Synthesis.SpeechSynthesizer
 = .GetInstalledVoices() | Where-Object { .VoiceInfo.Culture.TwoLetterISOLanguageName -eq 'fr' } | Select-Object -First 1
if () { .SelectVoice(.VoiceInfo.Name); Write-Host ('Selected voice: ' + .VoiceInfo.Name) }
.SetOutputToWaveFile('Data/Audio/reunion_codir.wav')
.Rate = 0
.Speak('Bonjour à tous et bienvenue à notre réunion de direction. À l\'ordre du jour : le déploiement de la flotte de PC Copilot Plus. Grâce au NPU dédié, nos collaborateurs bénéficient d\'une transcription en direct ultra-rapide, d\'une autonomie de batterie record et d\'une sécurité totale sans envoyer aucune donnée dans le cloud.')
.Dispose()
Write-Host 'GENERATED_OK'
