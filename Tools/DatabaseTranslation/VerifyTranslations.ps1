param(
    [string]$ServerDirectory = 'Build\Server\Debug',
    [string]$TranslationDocument = 'Tools\DatabaseTranslation\DatabaseTranslation.json'
)
$ErrorActionPreference='Stop'
$server=[IO.Path]::GetFullPath($ServerDirectory);$document=Get-Content -Raw ([IO.Path]::GetFullPath($TranslationDocument))|ConvertFrom-Json

function Get-FunctionalTokens([string]$text){@([regex]::Matches($text,'<\$[^<>]+>|\$\([^)]+\)|\$[A-Za-z][A-Za-z0-9_]*|/[^>}]*(?=[>}])')|ForEach-Object{$_.Value.ToLowerInvariant()}|Sort-Object)}
function Get-NpcStructure([string]$path){
  $result=[Collections.Generic.List[string]]::new();$inSpeech=$false;$speechTokens=[Collections.Generic.List[string]]::new()
  $flush={if($inSpeech){$result.Add('VISIBLE:'+(@($speechTokens|Sort-Object)-join'|'));$speechTokens.Clear();$inSpeech=$false}}
  foreach($line in [IO.File]::ReadAllLines($path)){
    if($line-in @('#SAY','#ELSESAY')){. $flush;$result.Add($line.ToUpperInvariant());$inSpeech=$true;continue}
    if($inSpeech-and($line.StartsWith('#')-or$line.StartsWith('['))){. $flush}
    if($inSpeech){foreach($token in Get-FunctionalTokens $line){$speechTokens.Add($token)};continue}
    if($line-match '^(\s*(?:LOCALMESSAGE|GLOBALMESSAGE)\s+")(.*)("\s+\w+\s*)$'){$result.Add($Matches[1]+((Get-FunctionalTokens $Matches[2])-join'|')+$Matches[3]);continue}
    $result.Add($line)
  }
  . $flush;return($result-join"`n")
}
function Get-Section([string[]]$lines,[string]$header){
  $start=[Array]::FindIndex($lines,[Predicate[string]]{param($x)$x-ieq$header});if($start-lt0){throw"Missing $header"};$end=$lines.Count
  for($i=$start+1;$i-lt$lines.Count;$i++){if($lines[$i].StartsWith('[@')){$end=$i;break}}
  if($end-eq$start+1){return,[string[]]@()};return,[string[]]$lines[($start+1)..($end-1)]
}

$npcRows=Import-Csv (Join-Path $server 'Exports\NpcInfoExport.csv');$files=@{}
foreach($row in $npcRows){$path=[IO.Path]::GetFullPath((Join-Path $server ('Envir\NPCs\'+($row.FileName-replace'/','\')+'.txt')));if(Test-Path $path){$files[$path]=1}}
$backupRoots=@(Get-ChildItem (Join-Path $server 'Back Up') -Directory|Where-Object Name -Like 'Translation NPC Scripts*'|Sort-Object Name)
$structureErrors=@();$englishErrors=@();$verified=0
foreach($path in $files.Keys){
  $relative=[IO.Path]::GetRelativePath($server,$path);$backup=$null
  foreach($root in $backupRoots){$candidate=Join-Path $root.FullName $relative;if(Test-Path $candidate){$backup=$candidate;break}}
  if($backup){$verified++;if((Get-NpcStructure $path)-cne(Get-NpcStructure $backup)){$structureErrors+=$relative}}
  $inSpeech=$false
  foreach($line in [IO.File]::ReadAllLines($path)){
    if($line-in @('#SAY','#ELSESAY')){$inSpeech=$true;continue};if($inSpeech-and($line.StartsWith('#')-or$line.StartsWith('['))){$inSpeech=$false}
    $visible=$null;if($inSpeech){$visible=$line}elseif($line-match '^\s*(?:LOCALMESSAGE|GLOBALMESSAGE)\s+"(.*)"\s+\w+\s*$'){$visible=$Matches[1]}
    if($null-eq$visible){continue};$clean=$visible-replace'<\$[^>]+>',''-replace'/[^>}]*(?=[>}])',''-replace'@[A-Za-z0-9_-]+',''-replace'\$\([^)]+\)',''-replace'\$[A-Za-z][A-Za-z0-9_]*',''
    if($clean-match'\b(the|you|your|this|that|with|from|have|will|can|please|welcome|hello|sorry|need|want|would|where|what|when|required|close|back|next)\b'){$englishErrors+="${relative}: $line"}
  }
}

$questErrors=@();$colorErrors=@()
foreach($property in $document.questScripts.PSObject.Properties){
  $entry=$property.Value;$path=Join-Path $server ('Envir\Quests\'+($entry.fileName-replace'/','\')+'.txt');$lines=[IO.File]::ReadAllLines($path)
  $description=Get-Section $lines '[@Description]';$task=Get-Section $lines '[@TaskDescription]'
  if(($description-join"`n")-cne($entry.description-join"`n")-or($task-join"`n")-cne($entry.taskDescription-join"`n")){$questErrors+=$entry.fileName}
  foreach($match in [regex]::Matches((($description+$task)-join"`n"),'/([^{}]+)(?=\})')){if($match.Groups[1].Value-notmatch'^[A-Za-z]+$'){$colorErrors+="$($property.Name):$($match.Value)"}}
}
Write-Host "NPC structures: $verified verified, $($structureErrors.Count) errors; visible English: $($englishErrors.Count)"
Write-Host "Quest scripts: $(@($document.questScripts.PSObject.Properties).Count) verified, $($questErrors.Count) text errors, $($colorErrors.Count) color errors"
if($structureErrors.Count-or$englishErrors.Count-or$questErrors.Count-or$colorErrors.Count){$structureErrors+$englishErrors+$questErrors+$colorErrors|Select-Object -First 50|Write-Error;exit 1}
