# 게임 창에 스캔 코드 키 · 마우스 상대 이동 보내기 (agent-desktop press는 가상 키만 보내 Unity가 못 읽음)
# 예: -Process AnotherEarthPlayCheck -Tap f2      (한 번 누르기)
#     -Process AnotherEarthPlayCheck -Hold w -Ms 1500   (누르고 있기)
#     -Process AnotherEarthPlayCheck -Move "200,0"      (마우스 상대 이동 — 1인칭 시점)
#     -Process AnotherEarthPlayCheck -Text "답 적기"     (글자 입력 — 한글 포함, 입력 칸을 먼저 클릭)
param(
    [string]$Process = "AnotherEarthPlayCheck",
    [string]$Tap,
    [string]$Hold,
    [int]$Ms = 500,
    [string]$Move,
    [string]$Text,
    [int]$Steps = 10
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class GameInput {
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
    static bool Extended(uint vk) { return vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || vk == 0x2D || vk == 0x2E || vk == 0x24 || vk == 0x23 || vk == 0x21 || vk == 0x22; }
    public static void Key(uint vk, bool up) {
        var i = new INPUT[1];
        i[0].type = 1;
        i[0].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
        i[0].u.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (Extended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Char(char c) {
        var i = new INPUT[2];
        i[0].type = 1; i[0].u.ki.wScan = c; i[0].u.ki.dwFlags = 0x4; // KEYEVENTF_UNICODE
        i[1].type = 1; i[1].u.ki.wScan = c; i[1].u.ki.dwFlags = 0x4 | KEYEVENTF_KEYUP;
        SendInput(2, i, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void MoveRel(int dx, int dy) {
        var i = new INPUT[1];
        i[0].type = 0;
        i[0].u.mi.dx = dx; i[0].u.mi.dy = dy; i[0].u.mi.dwFlags = 0x1; // MOUSEEVENTF_MOVE (상대)
        SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

function Get-Vk([string]$name) {
    $n = $name.ToLower()
    $table = @{ space = 0x20; enter = 0x0D; return = 0x0D; escape = 0x1B; esc = 0x1B; tab = 0x09; shift = 0x10; ctrl = 0x11; alt = 0x12;
                left = 0x25; up = 0x26; right = 0x27; down = 0x28; backspace = 0x08; delete = 0x2E }
    if ($table.ContainsKey($n)) { return [uint32]$table[$n] }
    if ($n -match '^f([0-9]{1,2})$') { return [uint32](0x6F + [int]$Matches[1]) }
    if ($n.Length -eq 1) { return [uint32][int][char]$n.ToUpper() }
    throw "모르는 키: $name"
}

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if ($proc -eq $null) { Write-Output "창을 못 찾음: $Process"; exit 2 }
$h = $proc.MainWindowHandle
[void][GameInput]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 150
if ([GameInput]::GetForegroundWindow() -ne $h) { Write-Output "창이 앞으로 오지 않음 (다른 창이 포커스를 쥠)"; exit 3 }

if ($Tap) {
    $vk = Get-Vk $Tap
    [GameInput]::Key($vk, $false); Start-Sleep -Milliseconds 60; [GameInput]::Key($vk, $true)
    Write-Output "tap $Tap"
}
if ($Hold) {
    $vk = Get-Vk $Hold
    [GameInput]::Key($vk, $false); Start-Sleep -Milliseconds $Ms; [GameInput]::Key($vk, $true)
    Write-Output "hold $Hold $Ms ms"
}
if ($Move) {
    $p = $Move.Split(','); $dx = [int]$p[0]; $dy = [int]$p[1]
    for ($s = 0; $s -lt $Steps; $s++) { [GameInput]::MoveRel([int]($dx / $Steps), [int]($dy / $Steps)); Start-Sleep -Milliseconds 16 }
    Write-Output "move $dx,$dy"
}
if ($Text) {
    foreach ($c in $Text.ToCharArray()) { [GameInput]::Char($c); Start-Sleep -Milliseconds 15 }
    Write-Output "text $($Text.Length)자"
}
