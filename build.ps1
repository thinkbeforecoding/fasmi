#! /usr/bin/env pwsh

$ErrorActionPreference = "Stop" 
$PSNativeCommandUseErrorActionPreference = $true 

if (Test-Path "bin") {
    Remove-Item bin -Recurse
}

dotnet tool restore

dotnet test

dotnet pack -c release ./src/fasmi/ -o bin/nuget