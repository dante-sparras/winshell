Describe 'The workflows' {
    BeforeAll {
        $script:Workflows = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../.github/workflows') -File
    }

    It 'run each job on a pinned runner image, never a moving -latest one' {
        foreach ($workflow in $script:Workflows) {
            Get-Content -Raw -LiteralPath $workflow.FullName | Should -Not -Match '-latest' -Because $workflow.Name
        }
    }

    It 'give wingetcreate its token through the environment, never on its command line' {
        $package = Get-Content -Raw -LiteralPath ($script:Workflows | Where-Object Name -EQ 'package.yml').FullName
        $package | Should -Match ([regex]::Escape('WINGET_CREATE_GITHUB_TOKEN: ${{ secrets.WINGET_TOKEN }}'))
        $package | Should -Not -Match '--token'
    }
}
