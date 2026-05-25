using System.Text.Json.Nodes;

namespace AbpStudioLinux.Installer.App;

public static class RuntimeConfigPatcher
{
    public static void PatchRuntimeConfigs(string appDirectory)
    {
        foreach (var runtimeConfig in Directory.EnumerateFiles(appDirectory, "*.runtimeconfig.json", SearchOption.AllDirectories))
        {
            var root = JsonNode.Parse(File.ReadAllText(runtimeConfig))?.AsObject();

            if (root is null)
            {
                continue;
            }

            var runtimeOptions = root["runtimeOptions"] as JsonObject;

            if (runtimeOptions is null)
            {
                continue;
            }

            if (UsesAppLocalHostPolicy(runtimeConfig))
            {
                continue;
            }

            if (runtimeOptions["framework"] is null
                && runtimeOptions["frameworks"] is null
                && runtimeOptions["includedFrameworks"] is JsonArray includedFrameworks)
            {
                runtimeOptions["frameworks"] = includedFrameworks.DeepClone();
                runtimeOptions.Remove("includedFrameworks");
            }

            runtimeOptions["rollForward"] ??= "LatestPatch";
            var configProperties = runtimeOptions["configProperties"] as JsonObject ?? new JsonObject();
            configProperties["System.Reflection.Metadata.MetadataUpdater.IsSupported"] = false;
            runtimeOptions["configProperties"] = configProperties;

            File.WriteAllText(runtimeConfig, root.ToJsonString(JsonDefaults.Options));
        }
    }

    private static bool UsesAppLocalHostPolicy(string runtimeConfig)
    {
        var directory = Path.GetDirectoryName(runtimeConfig);
        return directory is not null && File.Exists(Path.Combine(directory, "libhostpolicy.so"));
    }
}
