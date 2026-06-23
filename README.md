# Devlead.SourcePack

Opinionated MSBuild package for authoring **source NuGet packages** using standard SDK `dotnet pack`.

## Features

- Ships `.cs` files under `contentFiles/cs/{tfm}/...` with `BuildAction=Compile`
- Optional `build/{tfm}/*.props` and `*.targets` via `SourcePackFile`
- Per-target-framework dependencies from plain `PackageReference` items
- No `lib/` output (`IncludeBuildOutput=false` by default)
- Works with class libraries, console apps, and Azure Functions source projects

## Installation

Add a development dependency to the project you want to pack:

```xml
<PackageReference Include="Devlead.SourcePack" PrivateAssets="all" />
```

## Quick start

```xml
<PropertyGroup>
  <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
  <SourcePackRoot>Devlead/MyPackage</SourcePackRoot>
  <IsPackable>true</IsPackable>
</PropertyGroup>

<PackageReference Include="Devlead.SourcePack" PrivateAssets="all" />

<SourcePackFile Include="MyPackage.props" Kind="Build" />
<SourcePackFile Include="MyPackage.targets" Kind="Build" />
<SourcePackFile Include="../../README.md" Kind="Metadata" />
```

Pack in Release:

```bash
dotnet pack -c Release
```

For multi-target projects (`TargetFrameworks`), pack single-threaded to avoid duplicate `contentFiles` entries:

```bash
dotnet pack -c Release -- /m:1
```

`Devlead.SourcePack` sets `BuildInParallel=false` as an additional safeguard.

## Properties

| Property                           | Default                         | Description                                        |
|------------------------------------|---------------------------------|----------------------------------------------------|
| `EnableSourcePack`                 | `true`                          | Opt out by setting `false`                         |
| `SourcePackRoot`                   | *(required for auto sources)*   | Root folder under `contentFiles/cs/{tfm}/`         |
| `SourcePackPackConfigurations`     | `Release`                       | Configurations that pack sources                   |
| `SourcePackIncludeGeneratedUsings` | `true`                          | Pack `*.GlobalUsings.g.cs` from `obj/`             |

## SourcePackFile kinds

| Kind       | Package path                                   |
|------------|------------------------------------------------|
| `Source`   | `contentFiles/cs/{tfm}/{SourcePackRoot}/...`   |
| `Build`    | `build/{tfm}/{filename}`                       |
| `Metadata` | package root (README, icon, etc.)              |

## Build

```bash
dotnet run --file cake.cs
```

## Example projects

- [Devlead.Console](https://github.com/devlead/Devlead.Console)
- [Devlead.Testing.MockHttp](https://github.com/devlead/Devlead.Testing.MockHttp)

## License

MIT
