param([int]$Seconds = 150, [int]$Volume = 100)
$v = New-Object -ComObject SAPI.SpVoice
foreach ($o in $v.GetAudioOutputs()) { if ($o.GetDescription() -like '*Steam Streaming Microphone*') { $v.AudioOutputStream = $null; $v.AudioOutput = $o } }
foreach ($t in $v.GetVoices()) { if ($t.GetDescription() -like '*Hortense*') { $v.Voice = $t } }
$v.Volume = $Volume
"output=" + $v.AudioOutput.GetDescription()
$end = (Get-Date).AddSeconds($Seconds)
$lines = @("Bonjour, je suis le chat du bac à sable, est-ce que tu m'entends ?", "Je pense que le Mage est une anomalie, votez contre lui.", "Un, deux, trois, quatre, cinq, six, sept, huit, neuf, dix.")
$i = 0
while ((Get-Date) -lt $end) { $v.Speak($lines[$i % 3]) | Out-Null; $i++ }
"spoken=$i"
