param(
    [string]$ServerDirectory = 'Build\Server\Debug',
    [string]$TranslationDocument = 'Tools\DatabaseTranslation\DatabaseTranslation.json',
    [string]$ApiKey = $env:DEEPSEEK_API_KEY,
    [int]$BatchSize = 10,
    [switch]$SkipApi
)
$ErrorActionPreference = 'Stop'
if (-not $SkipApi -and [string]::IsNullOrWhiteSpace($ApiKey)) { throw 'Set DEEPSEEK_API_KEY before running this script.' }
$server = [IO.Path]::GetFullPath($ServerDirectory)
$translationPath = [IO.Path]::GetFullPath($TranslationDocument)
$npcRows = Import-Csv (Join-Path $server 'Exports\NpcInfoExport.csv')
$document = Get-Content -Raw -LiteralPath $translationPath | ConvertFrom-Json
$cachePath = Join-Path ([IO.Path]::GetDirectoryName($translationPath)) 'NpcScriptTranslations.DeepSeek.json'
$cache = if (Test-Path $cachePath) { Get-Content -Raw $cachePath | ConvertFrom-Json -AsHashtable } else { @{} }

$glossary = @{}
foreach ($p in $document.maps.PSObject.Properties) { if ($p.Value.sourceTitle.Length -ge 3) { $glossary[$p.Value.sourceTitle] = $p.Value.title } }
foreach ($p in $document.npcs.PSObject.Properties) { if ($p.Value.sourceName.Length -ge 3) { $glossary[$p.Value.sourceName] = $p.Value.name } }
$visibleNames = @{}
foreach ($p in $document.npcs.PSObject.Properties) {
    if ($p.Value.sourceName -notmatch '_' -or $p.Value.name -notmatch '_') { continue }
    $sourcePerson = ($p.Value.sourceName -split '_', 2)[1]; $targetPerson = ($p.Value.name -split '_', 2)[1]
    if ($sourcePerson -and $targetPerson) { $visibleNames[$sourcePerson] = $targetPerson }
}
$visibleNames['Miss Do']='多小姐'; $visibleNames['Miss Re']='瑞小姐'; $visibleNames['Abel']='周杰'; $visibleNames['Mirian']='林雪'
$visibleNames['GT Steward']='行会领地管理员'; $visibleNames['TurnUndead']='圣言术'; $visibleNames['DoubleSlash']='双龙斩'
$visibleNames['WoomaHeart']='沃玛之心'; $visibleNames['PrajnaHeart']='潘夜之心'

function Replace-VisibleNames([string]$text) {
    foreach ($name in @($visibleNames.Keys | Sort-Object Length -Descending)) {
        $text = [regex]::Replace($text, "(?<![A-Za-z])$([regex]::Escape($name))(?=ZXQK|[^A-Za-z]|$)", [string]$visibleNames[$name])
    }
    return $text
}

function Protect-Text([string]$text) {
    $protected = $text; $values = [ordered]@{}; $number = 0
    foreach ($pattern in @('<\$[^<>]+>', '\{[A-Za-z0-9_$(),]+\}', '/[^>}]*(?:>>|>|})', '<<|>>|[<>{}]', '(?<![A-Za-z])@[A-Za-z0-9_-]+', '\$\([^)]+\)', '\$[A-Za-z][A-Za-z0-9_]*')) {
        while (($match = [regex]::Match($protected, $pattern)).Success) {
            $token = "ZXQK$($number.ToString('D4'))QXZ"; $number++; $values[$token] = $match.Value
            $protected = $protected.Substring(0, $match.Index) + $token + $protected.Substring($match.Index + $match.Length)
        }
    }
    [pscustomobject]@{ Text = $protected; Values = $values }
}

function Restore-Text([string]$text, [hashtable]$values) {
    foreach ($key in @($values.Keys | Sort-Object -Descending)) {
        if (([regex]::Matches($text, [regex]::Escape($key))).Count -ne 1) { throw "Protected token $key was changed by the API in: $text" }
        $text = $text.Replace($key, [string]$values[$key])
    }
    return $text
}

$files = [ordered]@{}
foreach ($row in $npcRows) {
    $path = Join-Path $server ('Envir\NPCs\' + ($row.FileName -replace '/', '\') + '.txt')
    if (Test-Path -LiteralPath $path) { $files[$path] = $true }
}
$work = [Collections.Generic.List[object]]::new()
foreach ($path in $files.Keys) {
    $lines = [IO.File]::ReadAllLines($path); $inSpeech = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -in @('#SAY', '#ELSESAY')) { $inSpeech = $true; continue }
        if ($inSpeech -and ($lines[$i].StartsWith('#') -or $lines[$i].StartsWith('['))) { $inSpeech = $false }
        $prefix=''; $suffix=''; $visibleText=$null
        if ($inSpeech) { $visibleText=$lines[$i] }
        elseif ($lines[$i] -match '^(\s*(?:LOCALMESSAGE|GLOBALMESSAGE)\s+")(.*)("\s+\w+\s*)$') { $prefix=$Matches[1];$visibleText=$Matches[2];$suffix=$Matches[3] }
        if ($null -eq $visibleText -or $visibleText -notmatch '[A-Za-z]{2,}') { continue }
        $protected = Protect-Text $visibleText
        $segments = @([regex]::Split($protected.Text, '(ZXQK\d{4}QXZ)') | ForEach-Object {
            $segment = $_; $hash = $null
            if ($segment -notmatch '^ZXQK\d{4}QXZ$' -and $segment -match '[A-Za-z]{2,}') {
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($segment)))
            }
            [pscustomobject]@{ Text=$segment; Hash=$hash }
        })
        $work.Add([pscustomobject]@{ Path=$path; Line=$i; Original=$lines[$i]; Prefix=$prefix; Suffix=$suffix; Protected=$protected.Text; Values=$protected.Values; Segments=$segments })
    }
}

$pending = @($work.Segments | Where-Object { $_.Hash -and -not $cache.ContainsKey($_.Hash) } | Group-Object Hash | ForEach-Object { $_.Group[0] })
if ($SkipApi) { $pending = @() }
for ($offset = 0; $offset -lt $pending.Count; $offset += $BatchSize) {
    $batch = @($pending[$offset..([Math]::Min($offset + $BatchSize - 1, $pending.Count - 1))])
    $items = for ($i=0; $i -lt $batch.Count; $i++) { "$i`t$($batch[$i].Text)" }
    $batchText = ($batch.Text -join "`n")
    $terms = @($glossary.Keys | Where-Object { $batchText.Contains($_) } | ForEach-Object { "$_=$($glossary[$_])" })
    $system = @'
你是《热血传奇》游戏脚本本地化译者。将每行制表符后的英文可见对白翻译成简体中文，语言自然简练，沿用经典《热血传奇》术语。输出时每行严格使用“原数字、一个制表符、译文”，保持原顺序和行数。不要输出标题、代码块或解释，译文内部不要换行。
'@
    $user = "术语对应：`n$($terms -join "`n")`n待翻译内容：`n" + ($items -join "`n")
    $body = @{ model='deepseek-chat'; messages=@(@{role='system';content=$system},@{role='user';content=$user}); temperature=0.1; stream=$false } | ConvertTo-Json -Depth 8
    $completed = $false
    for ($attempt=1; $attempt -le 6 -and -not $completed; $attempt++) {
        try {
            $response = Invoke-RestMethod -Uri 'https://api.deepseek.com/chat/completions' -Method Post -Headers @{Authorization="Bearer $ApiKey"} -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
            $translatedBatch = @{}
            foreach ($line in ([string]$response.choices[0].message.content -split "`r?`n")) {
                if ($line -notmatch '^(\d+)\t(.*)$') { throw "API returned an invalid line: $line" }
                $id = [int]$Matches[1]
                if ($id -lt 0 -or $id -ge $batch.Count -or $translatedBatch.ContainsKey($id)) { throw "API returned an invalid or duplicate id $id." }
                $translatedBatch[$id] = $Matches[2]
            }
            if ($translatedBatch.Count -ne $batch.Count) { throw "API returned $($translatedBatch.Count) of $($batch.Count) translations." }
            foreach ($id in $translatedBatch.Keys) { $cache[$batch[$id].Hash] = $translatedBatch[$id] }
            $completed = $true
        } catch {
            if ($attempt -eq 6) { throw }
            Write-Warning "Batch failed validation; retrying ($attempt/6)."
        }
    }
    [IO.File]::WriteAllText($cachePath, (($cache | ConvertTo-Json -Depth 3) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Write-Host "Translated $([Math]::Min($offset + $batch.Count, $pending.Count)) / $($pending.Count) unique lines."
}

$stamp = Get-Date -Format 'yyyy-MM-dd HH-mm-ss'
$backupRoot = Join-Path $server "Back Up\Translation NPC Scripts DeepSeek $stamp"
$changed = 0; $workByPath = $work | Group-Object -Property Path -AsHashTable -AsString
foreach ($path in $files.Keys) {
    $group = $workByPath[$path]; if ($null -eq $group) { continue }
    $items = @($group | ForEach-Object { $_ }); if ($items.Count -eq 0) { continue }
    $lines = [IO.File]::ReadAllLines($path); $fileChanged = $false
    foreach ($item in $items) {
        if ($null -eq $item.Line) { throw "Invalid grouped work item type $($item.GetType().FullName): $($item | Out-String)" }
        $rebuilt = $item.Protected
        foreach ($segment in @($item.Segments | Where-Object Hash)) {
            if ($cache.ContainsKey($segment.Hash)) { $rebuilt = $rebuilt.Replace($segment.Text, [string]$cache[$segment.Hash]) }
        }
        $rebuilt = Replace-VisibleNames $rebuilt
        $translated = $item.Prefix + (Restore-Text $rebuilt $item.Values) + $item.Suffix
        if ($translated -ne $lines[$item.Line]) { $lines[$item.Line]=$translated; $fileChanged=$true }
    }
    if (-not $fileChanged) { continue }
    $relative = [IO.Path]::GetRelativePath($server, $path); $backup = Join-Path $backupRoot $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null; [IO.File]::Copy($path, $backup, $true)
    [IO.File]::WriteAllLines($path, $lines, [Text.UTF8Encoding]::new($false)); $changed++
}
Write-Host "Updated $changed NPC script files. Backup: $backupRoot"
