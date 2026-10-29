BeforeAll {
    . (Join-Path $PSScriptRoot 'publish-release.ps1')
    $script:OriginalSigningOrganization = $env:SIGNPATH_ORGANIZATION_ID
}

AfterAll {
    $env:SIGNPATH_ORGANIZATION_ID = $script:OriginalSigningOrganization
}

Describe 'Publishing a complete draft release' {
    BeforeEach {
        $env:SIGNPATH_ORGANIZATION_ID = $null
        $script:Directory = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Directory | Out-Null
        $script:Names = @('winshell-0.6.1-win-x64.zip', 'winshell-0.6.1-win-arm64.zip', 'SHA256SUMS.txt', 'winget-manifests.zip', 'install.ps1', 'uninstall.ps1')
        foreach ($name in $script:Names) { Set-Content -LiteralPath (Join-Path $script:Directory $name) -Value $name }
        $script:Assets = [Collections.Generic.List[object]]::new()
        $script:Calls = [Collections.Generic.List[string]]::new()
        $script:Draft = $true
        $script:FailUpload = $false
        $script:BadUpload = $false
        $global:LASTEXITCODE = 0
        Mock gh {
            $script:Calls.Add(($args -join ' '))
            if ($args[0] -eq 'api') {
                return (@{ draft = $script:Draft; body = ''; assets = $script:Assets.ToArray() } | ConvertTo-Json -Depth 5)
            }
            if ($args[0] -eq 'release' -and $args[1] -eq 'upload') {
                if ($script:FailUpload) { $global:LASTEXITCODE = 1; return }
                $file = Get-Item -LiteralPath $args[3]
                $digest = if ($script:BadUpload) { 'sha256:bad' } else { 'sha256:' + (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() }
                $script:Assets.Add(@{ name = $file.Name; digest = $digest })
                return
            }
            if ($args -contains '--draft=false') { $script:Draft = $false; return }
            if ($args -contains '--notes') { return }
            throw "Unexpected GitHub command: $args"
        }
    }

    It 'uploads and verifies all six assets before publishing' {
        Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo
        $script:Draft | Should -BeFalse
        $script:Assets.Count | Should -Be 6
        $script:Calls[-2] | Should -Be 'api repos/owner/repo/releases/tags/v0.6.1'
        $script:Calls[-1] | Should -Be 'release edit v0.6.1 --repo owner/repo --draft=false'
    }

    It 'resumes a draft without replacing an existing matching asset' {
        $first = Get-Item -LiteralPath (Join-Path $script:Directory $script:Names[0])
        $script:Assets.Add(@{ name = $first.Name; digest = 'sha256:' + (Get-FileHash -LiteralPath $first.FullName).Hash.ToLowerInvariant() })
        Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo
        @($script:Calls | Where-Object { $_ -like 'release upload *' }).Count | Should -Be 5
        $script:Draft | Should -BeFalse
    }

    It 'refuses an incomplete published release without changing it' {
        $script:Draft = $false
        { Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo } | Should -Throw '*cannot be repaired*'
        $script:Calls.Count | Should -Be 1
    }

    It 'leaves a complete published release unchanged' {
        foreach ($name in $script:Names) {
            $file = Join-Path $script:Directory $name
            $script:Assets.Add(@{ name = $name; digest = 'sha256:' + (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() })
        }
        $script:Draft = $false
        Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo
        $script:Calls.Count | Should -Be 1
    }

    It 'adds the signing policy while the release is still a draft' {
        $env:SIGNPATH_ORGANIZATION_ID = 'test-organization'
        Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo
        $script:Calls[-2] | Should -Match '--notes'
        $script:Calls[-2] | Should -Match 'Code signing policy'
        $script:Calls[-1] | Should -Match '--draft=false'
    }

    It 'leaves a draft alone if a local asset is missing' {
        Remove-Item -LiteralPath (Join-Path $script:Directory 'uninstall.ps1')
        { Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo } | Should -Throw '*Missing release asset*'
        $script:Calls.Count | Should -Be 0
    }

    It 'does not replace a different existing asset or publish the draft' {
        $script:Assets.Add(@{ name = $script:Names[0]; digest = 'sha256:other' })
        { Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo } | Should -Throw '*different digest*'
        $script:Calls.Count | Should -Be 1
        $script:Draft | Should -BeTrue
    }

    It 'keeps the draft when upload fails' {
        $script:FailUpload = $true
        { Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo } | Should -Throw '*Could not upload*'
        $script:Draft | Should -BeTrue
        @($script:Calls | Where-Object { $_ -like '*--draft=false*' }).Count | Should -Be 0
    }

    It 'keeps the draft when GitHub reports a different uploaded hash' {
        $script:BadUpload = $true
        { Publish-WinshellRelease -Tag v0.6.1 -PackageDirectory $script:Directory -Repository owner/repo } | Should -Throw '*remains a draft*'
        $script:Draft | Should -BeTrue
        @($script:Calls | Where-Object { $_ -like '*--draft=false*' }).Count | Should -Be 0
    }
}
