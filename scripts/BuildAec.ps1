param([string]$Python='python')
$ErrorActionPreference='Stop'
$aecRepo=Split-Path -Parent $PSScriptRoot
$aecArtifacts=Join-Path $aecRepo 'artifacts\aec'
& $Python -m pip install -r (Join-Path $aecRepo 'aec\build-requirements.txt')
if($LASTEXITCODE -ne 0){throw 'AEC dependencies failed'}
& $Python (Join-Path $aecRepo 'tests\Aec\test_engine.py')
if($LASTEXITCODE -ne 0){throw 'AEC regression checks failed'}
& $Python -m PyInstaller --noconfirm --clean --onedir --console --name VoiceBridge.Aec --collect-all pywebrtc_audio --collect-all sounddevice --distpath (Join-Path $aecArtifacts 'dist') --workpath (Join-Path $aecArtifacts 'build') --specpath $aecArtifacts (Join-Path $aecRepo 'aec\engine.py')
if($LASTEXITCODE -ne 0){throw 'AEC executable build failed'}
$portAudio=Join-Path $aecArtifacts 'dist\VoiceBridge.Aec\_internal\_sounddevice_data\portaudio-binaries'
Get-ChildItem -LiteralPath $portAudio -File | Where-Object { $_.Name -like 'libportaudio*' -and $_.Name -ne 'libportaudio64bit.dll' } | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
& $Python (Join-Path $aecRepo 'scripts\CollectAecLicenses.py') (Join-Path $aecArtifacts 'dist\VoiceBridge.Aec\licenses')
if($LASTEXITCODE -ne 0){throw 'AEC license collection failed'}
Copy-Item -Path (Join-Path $aecRepo 'licenses\AEC-*') -Destination (Join-Path $aecArtifacts 'dist\VoiceBridge.Aec\licenses')
Write-Output (Join-Path $aecArtifacts 'dist\VoiceBridge.Aec')
