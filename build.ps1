#! /usr/bin/env pwsh

$ErrorActionPreference = "Stop" 
$PSNativeCommandUseErrorActionPreference = $true 

dotnet tool restore


dotnet test

dotnet pack -c release ./src/fasmi/ -o bin/nuget