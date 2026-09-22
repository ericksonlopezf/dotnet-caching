# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Verifies all internal markdown links and media references across the repository.
#>

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
Write-Host "Verifying internal markdown links in: $repoRoot" -ForegroundColor Cyan

$mdFiles = Get-ChildItem -Path $repoRoot -Recurse -Filter *.md | Where-Object { $_.FullName -notmatch "[\\/](obj|bin|coverage-report|StrykerOutput|BenchmarkDotNet\.Artifacts|\.git)[\\/]" }
$brokenLinks = 0
$totalLinks = 0

foreach ($file in $mdFiles) {
    $content = Get-Content $file.FullName -Raw
    $fileDir = $file.DirectoryName

    # Match markdown links [text](path) excluding external http/https/mailto
    $regex = '\[([^\]]+)\]\(([^)]+)\)'
    $matches = [regex]::Matches($content, $regex)

    foreach ($m in $matches) {
        $link = $m.Groups[2].Value.Trim()

        # Skip web links, anchors, mailto
        if ($link -match '^(https?://|mailto:|#)' ) {
            continue
        }

        # Remove in-page anchors if present: path#anchor
        $pathOnly = $link.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($pathOnly)) {
            continue
        }

        $totalLinks++
        if ($pathOnly -match '^file:') {
            try {
                $targetPath = ([System.Uri]$pathOnly).LocalPath
            } catch {
                $targetPath = $pathOnly
            }
        } elseif ([System.IO.Path]::IsPathRooted($pathOnly)) {
            $targetPath = $pathOnly
        } else {
            $targetPath = Join-Path $fileDir $pathOnly
        }

        if (-not (Test-Path $targetPath)) {
            Write-Host "[BROKEN LINK] in $($file.FullName): '$link' -> '$targetPath'" -ForegroundColor Red
            $brokenLinks++
        }
    }
}

Write-Host "Link Verification Summary: Total Checked = $totalLinks, Broken = $brokenLinks" -ForegroundColor Cyan
if ($brokenLinks -gt 0) {
    Write-Error "Found $brokenLinks broken links across documentation."
    exit 1
} else {
    Write-Host "[PASS] All internal documentation links are valid." -ForegroundColor Green
    exit 0
}
