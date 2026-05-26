# Prompter.Setup

WiX v5-based MSI installer for the Prompter WPF app.

## Build

```bash
dotnet build Prompter.Setup/Prompter.Setup.wixproj -c Release
```

The resulting MSI is placed at:

```
Prompter.Setup/bin/x64/Release/Prompter.Setup.msi
```

The project automatically:
1. Publishes `Prompter.csproj` as a self-contained `win-x64` app
2. Harvests the entire publish directory into the MSI
3. Creates a Start Menu shortcut and ARP entry

## Versioning

Version is defined in a single place at the solution root:

```
Directory.Build.props
```

To change the version, edit that file and update the `Version` property:

```xml
<PropertyGroup>
  <Version>1.2.3</Version>
</PropertyGroup>
```

This value flows automatically to:
- The Prompter assembly version
- The MSI `ProductVersion` property

## Upgrade behavior

The MSI uses a fixed `UpgradeCode` GUID, enabling **major upgrades**. Installing a newer version removes the old one automatically. Downgrades are blocked with the message:

> A newer version of Prompter is already installed.
