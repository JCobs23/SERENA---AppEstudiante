$ErrorActionPreference = 'Stop'
$sourcePath = 'Assets/Scenes/SampleScene.unity'
$targetPath = 'Assets/Scenes/IngresoCodigo.unity'
$source = Get-Content -LiteralPath $sourcePath -Raw
$parts = [regex]::Split($source, '(?m)(?=^--- !u!)')
$blocks = @{}
foreach ($part in $parts) {
    if ($part -match '(?m)^--- !u!\d+ &(\d+)') { $blocks[$Matches[1]] = $part }
}
$nextId = 310000000
function New-Clone([string[]]$oldIds, [string]$name, [string]$parentId, [string]$position, [string]$size, [string]$text, [string]$imageColor, [bool]$isButton, [bool]$bold = $false, [string]$fontColor = '{r: 0.2, g: 0.2, b: 0.2, a: 1}') {
    $script:nextId += 100
    $base = $script:nextId
    $mapping = @{}
    for ($i = 0; $i -lt $oldIds.Length; $i++) { $mapping[$oldIds[$i]] = [string]($base + $i) }
    $out = ''
    foreach ($oldId in $oldIds) {
        $chunk = $script:blocks[$oldId]
        foreach ($key in $mapping.Keys) { $chunk = [regex]::Replace($chunk, "(?<=fileID: )$key(?=,|})", $mapping[$key]) }
        $chunk = [regex]::Replace($chunk, '(?m)^--- !u!(\d+) &\d+', ('--- !u!$1 &' + $mapping[$oldId]))
        if ($oldId -eq $oldIds[0]) { $chunk = $chunk -replace 'm_Name: [^\r\n]*', ('m_Name: ' + $name) }
        if ($chunk -match 'RectTransform:') {
            if ($oldId -eq $oldIds[1]) {
                $chunk = $chunk -replace 'm_Father: \{fileID: \d+\}', ('m_Father: {fileID: ' + $parentId + '}')
                if ($position) { $chunk = [regex]::Replace($chunk, 'm_AnchoredPosition: \{[^}]+\}', ('m_AnchoredPosition: ' + $position), 1) }
                if ($size) { $chunk = [regex]::Replace($chunk, 'm_SizeDelta: \{[^}]+\}', ('m_SizeDelta: ' + $size), 1) }
                if (-not $isButton) { $chunk = [regex]::Replace($chunk, '(?s)m_Children:\r?\n.*?(\r?\n  m_Father:)', 'm_Children: []$1', 1) }
                $chunk = [regex]::Replace($chunk, 'm_LocalScale: \{[^}]+\}', 'm_LocalScale: {x: 1, y: 1, z: 1}', 1)
                $chunk = [regex]::Replace($chunk, 'm_AnchorMin: \{[^}]+\}', 'm_AnchorMin: {x: 0.5, y: 0.5}', 1)
                $chunk = [regex]::Replace($chunk, 'm_AnchorMax: \{[^}]+\}', 'm_AnchorMax: {x: 0.5, y: 0.5}', 1)
            } elseif ($isButton) {
                $chunk = $chunk -replace 'm_Father: \{fileID: \d+\}', ('m_Father: {fileID: ' + $mapping[$oldIds[1]] + '}')
            }
        }
        if ($chunk -match 'UnityEngine.UI::UnityEngine.UI.Button') {
            $chunk = [regex]::Replace($chunk, '(?m)^    m_(Normal|Highlighted|Pressed|Selected|Disabled)Color: \{[^}]+\}', '    m_$1Color: {r: 1, g: 1, b: 1, a: 1}')
        }
        if ($chunk -match 'UnityEngine.UI::UnityEngine.UI.Image') { $chunk = [regex]::Replace($chunk, 'm_Color: \{[^}]+\}', ('m_Color: ' + $imageColor), 1) }
        if ($chunk -match 'Unity.TextMeshPro::TMPro.TextMeshProUGUI') {
            $escapedText = $text.Replace('\', '\\').Replace('"', '\"')
            $chunk = [regex]::Replace($chunk, 'm_text: .*', ('m_text: "' + $escapedText + '"'), 1)
            $chunk = [regex]::Replace($chunk, 'm_fontColor: \{[^}]+\}', ('m_fontColor: ' + $fontColor), 1)
            $fontHex = if ($fontColor -match 'r: 0.81') { 'rgba: 4291764011' } elseif ($fontColor -match 'r: 0.34') { 'rgba: 4283914071' } elseif ($fontColor -match 'r: 0.19') { 'rgba: 4281348144' } else { 'rgba: 4294967295' }
            $chunk = [regex]::Replace($chunk, 'rgba: \d+', $fontHex, 1)
            $chunk = $chunk -replace 'm_fontSize: \d+', 'm_fontSize: 22'
            $chunk = $chunk -replace 'm_fontSizeBase: \d+', 'm_fontSizeBase: 22'
            if ($bold) { $chunk = $chunk -replace 'm_fontStyle: 0', 'm_fontStyle: 1' }
        }
        $out += $chunk
    }
    return @{ Text = $out; Map = $mapping; Root = $mapping[$oldIds[0]]; Rect = $mapping[$oldIds[1]]; Button = $(if ($isButton) { $mapping[$oldIds[2]] } else { '' }); TMP = $(if ($oldIds.Length -ge 4 -and $script:blocks[$oldIds[-1]] -match 'TextMeshProUGUI') { $mapping[$oldIds[-1]] } else { $mapping[$oldIds[-2]] }) }
}

$panelIds = @('1182450820','1182450821','1182450822','1182450823')
$textIds = @('85882068','85882069','85882070','85882071')
$buttonIds = @('1544150761','1544150762','1544150763','1544150764','1544150765','1959909875','1959909876','1959909877','1959909878')
$newObjects = [System.Collections.Generic.List[string]]::new()
$rootChildren = [System.Collections.Generic.List[string]]::new()
$cardChildren = [System.Collections.Generic.List[string]]::new()

$bg = New-Clone $panelIds 'Background' '2048997612' '{x: 0, y: 0}' '{x: 0, y: 0}' '' '{r: 0.973, g: 0.973, b: 0.973, a: 1}' $false
$bg.Text = [regex]::Replace($bg.Text, 'm_AnchorMin: \{[^}]+\}', 'm_AnchorMin: {x: 0, y: 0}', 1)
$bg.Text = [regex]::Replace($bg.Text, 'm_AnchorMax: \{[^}]+\}', 'm_AnchorMax: {x: 1, y: 1}', 1)
$newObjects.Add($bg.Text); $rootChildren.Add($bg.Rect)

$brand = New-Clone $textIds 'SERENA_TITLE' '2048997612' '{x: 0, y: 268}' '{x: 500, y: 76}' 'Serena' '{r: 1, g: 1, b: 1, a: 1}' $false $true '{r: 0.81, g: 0.12, b: 0.17, a: 1}'
$brand.Text = $brand.Text -replace 'm_fontSize: 22', 'm_fontSize: 44' -replace 'm_fontSizeBase: 22', 'm_fontSizeBase: 44'
$newObjects.Add($brand.Text); $rootChildren.Add($brand.Rect)
$subtitle = New-Clone $textIds 'SESSION_CODE_INSTRUCTIONS' '2048997612' '{x: 0, y: 226}' '{x: 500, y: 38}' 'Enter the session code' '{r: 1, g: 1, b: 1, a: 1}' $false $false '{r: 0.34, g: 0.34, b: 0.34, a: 1}'
$newObjects.Add($subtitle.Text); $rootChildren.Add($subtitle.Rect)

$card = New-Clone $panelIds 'Keypad_Card' '2048997612' '{x: 0, y: -12}' '{x: 352, y: 414}' '' '{r: 1, g: 1, b: 1, a: 1}' $false
$newObjects.Add($card.Text); $rootChildren.Add($card.Rect)

$slotTmps = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt 6; $i++) {
    $x = -129 + ($i * 51.5)
    $slot = New-Clone $panelIds ('Code_Digit_Slot_' + ($i+1)) $card.Rect ("{x: $x, y: 157}") '{x: 43, y: 51}' '' '{r: 0.98, g: 0.98, b: 0.98, a: 1}' $false
    $digit = New-Clone @('1959909875','1959909876','1959909877','1959909878') ('Code_Digit_' + ($i+1)) $slot.Rect '{x: 0, y: 0}' '{x: 41, y: 49}' '' '{r: 1, g: 1, b: 1, a: 1}' $false $true
    $digit.Text = $digit.Text -replace 'm_fontSize: 22', 'm_fontSize: 24' -replace 'm_fontSizeBase: 22', 'm_fontSizeBase: 24'
    $slot.Text = $slot.Text -replace 'm_Children: \[\]', ('m_Children:' + "`r`n    - {fileID: " + $digit.Rect + '}')
    $newObjects.Add($slot.Text); $cardChildren.Add($slot.Rect)
    $newObjects.Add($digit.Text); $slotTmps.Add($digit.TMP)
}

$numericButtons = [System.Collections.Generic.List[string]]::new()
$digits = @('1','2','3','4','5','6','7','8','9','0')
$keyPositions = @(@(-104,69),@(0,69),@(104,69),@(-104,-6),@(0,-6),@(104,-6),@(-104,-81),@(0,-81),@(104,-81),@(-104,-156))
for ($i = 0; $i -lt 10; $i++) {
    $p = $keyPositions[$i]
    $key = New-Clone $buttonIds ('Key_' + $digits[$i]) $card.Rect ("{x: $($p[0]), y: $($p[1])}") '{x: 92, y: 63}' $digits[$i] '{r: 1, g: 0.89, b: 0.9, a: 1}' $true $true '{r: 0.19, g: 0.19, b: 0.19, a: 1}'
    $key.Text = $key.Text -replace 'm_fontSize: 22', 'm_fontSize: 20' -replace 'm_fontSizeBase: 22', 'm_fontSizeBase: 20'
    $newObjects.Add($key.Text); $cardChildren.Add($key.Rect); $numericButtons.Add($key.Button)
}
$delete = New-Clone $buttonIds 'Delete_BUTTON' $card.Rect '{x: 52, y: -156}' '{x: 196, y: 63}' '⌫  Delete' '{r: 1, g: 0.89, b: 0.9, a: 1}' $true $true '{r: 0.19, g: 0.19, b: 0.19, a: 1}'
$newObjects.Add($delete.Text); $cardChildren.Add($delete.Rect)
$cardBlock = $newObjects[3]
$cardBlock = $cardBlock -replace 'm_Children: \[\]', ('m_Children:' + "`r`n    - {fileID: " + ($cardChildren -join "}`r`n    - {fileID: ") + '}')
$newObjects[3] = $cardBlock

$status = New-Clone $textIds 'Code_Status' '2048997612' '{x: 0, y: -244}' '{x: 360, y: 28}' '' '{r: 1, g: 1, b: 1, a: 1}' $false $false '{r: 0.81, g: 0.12, b: 0.17, a: 1}'
$newObjects.Add($status.Text); $rootChildren.Add($status.Rect)
$join = New-Clone $buttonIds 'Join_Session_BUTTON' '2048997612' '{x: 0, y: -280}' '{x: 352, y: 51}' 'Join Session' '{r: 0.83, g: 0.83, b: 0.83, a: 1}' $true $true '{r: 1, g: 1, b: 1, a: 1}'
$newObjects.Add($join.Text); $rootChildren.Add($join.Rect)

$allButtonIds = @($numericButtons.ToArray())
$serializedButtons = ($allButtonIds | ForEach-Object { '{fileID: ' + $_ + '}' }) -join "`r`n  - "
$serializedSlots = ($slotTmps | ForEach-Object { '{fileID: ' + $_ + '}' }) -join "`r`n  - "
$controller = @"
--- !u!114 &799350049
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 799350047}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 7de31aaf6f1b47c38f30ac2a8bf31b6a, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  numberButtons:
  - $serializedButtons
  deleteButton: {fileID: $($delete.Button)}
  joinButton: {fileID: $($join.Button)}
  codeSlots:
  - $serializedSlots
  statusText: {fileID: $($status.TMP)}
"@

$headerEnd = $source.IndexOf('--- !u!1 &')
$sceneHeader = $source.Substring(0, $headerEnd)
$canvasChildren = ($rootChildren | ForEach-Object { '  - {fileID: ' + $_ + '}' }) -join "`r`n"
$canvasRoot = [regex]::Replace($blocks['2048997612'], '(?ms)(m_Children:\r?\n)(.*?)(\r?\n  m_Father:)', ('$1' + $canvasChildren + '$3'), 1)
$canvasRoot = [regex]::Replace($canvasRoot, 'm_LocalScale: \{[^}]+\}', 'm_LocalScale: {x: 1, y: 1, z: 1}', 1)
$canvasScaler = $blocks['2048997610'] -replace 'm_ReferenceResolution: \{x: 800, y: 600\}', 'm_ReferenceResolution: {x: 1500, y: 844}'
$managerObject = [regex]::Replace($blocks['799350047'], 'm_Name: [^\r\n]*', 'm_Name: SessionCodeController', 1)
$managerTransform = $blocks['799350048']
$managerTransform = [regex]::Replace($managerTransform, 'm_LocalPosition: \{[^}]+\}', 'm_LocalPosition: {x: 0, y: 0, z: 0}', 1)
$managerTransform = [regex]::Replace($managerTransform, 'm_LocalScale: \{[^}]+\}', 'm_LocalScale: {x: 1, y: 1, z: 1}', 1)
$managerTransform = [regex]::Replace($managerTransform, 'm_Father: \{fileID: \d+\}', 'm_Father: {fileID: 0}', 1)
$canvasObject = $blocks['2048997608'] + $blocks['2048997609'] + $canvasScaler + $blocks['2048997611'] + $canvasRoot
$eventSystem = $blocks['2005129845'] + $blocks['2005129846'] + $blocks['2005129847'] + $blocks['2005129848']
$sceneRoots = @'
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {fileID: 799350048}
  - {fileID: 2048997612}
  - {fileID: 2005129848}
'@
$result = $sceneHeader + $managerObject + $managerTransform + $controller + "`r`n" + $canvasObject + $eventSystem + ($newObjects -join '') + $sceneRoots
Set-Content -LiteralPath $targetPath -Value $result -Encoding utf8
