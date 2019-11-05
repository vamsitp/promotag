# promotagz
AzDevOps Tags Promoter - Specified Tags at `PBI`/`UserStory` level will be promoted to the parent `Feature` and then to the `Epic`

**Usage:**

1. `promotagz Tag1 Tag2`
2. `AzDO-Account` `AzDO-Project` `AzDO-PAT`

```batch
# Install from nuget.org
dotnet tool install -g promotagz

# Upgrade to latest version from nuget.org
dotnet tool update -g promotagz --no-cache

# Install a specific version from nuget.org
dotnet tool install -g promotagz --version 1.0.x

# Uninstall
dotnet tool uninstall -g promotagz

# Update/Reset config-settings
%userprofile%\.dotnet\tools\.store\promotagz\**\PromoTagz.dll.config
```
> **NOTE**: If the Tool is not accessible post installation, add `%USERPROFILE%\.dotnet\tools` to the PATH env-var.

##### CONTRIBUTION
```batch
# Install from local project path
dotnet tool install -g --add-source ./bin promotagz

# Publish package to nuget.org
nuget push ./bin/PromoTagz.1.0.0.nupkg -ApiKey <key> -Source https://api.nuget.org/v3/index.json
