using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace EvaluationTests;

public class Settings
{

    public readonly string DeploymentName;
    public readonly string ModelName;
    public readonly string Endpoint;
    public readonly string StorageRootPath;

    public Settings(IConfiguration config)
    {
        DeploymentName = config.GetValue<string>("DeploymentName") ?? throw new ArgumentNullException(nameof(DeploymentName));
        ModelName = config.GetValue<string>("ModelName") ?? throw new ArgumentNullException(nameof(ModelName));
        Endpoint = config.GetValue<string>("Endpoint") ?? throw new ArgumentNullException(nameof(Endpoint));
        StorageRootPath = config.GetValue<string>("StorageRootPath") ?? throw new ArgumentNullException(nameof(StorageRootPath));
    }

    private static Settings? currentSettings = null;

    public static Settings Current
    {
        get {
            currentSettings ??= GetCurrentSettings();
            return currentSettings;
        }
    }

    private static Settings GetCurrentSettings()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        IConfigurationRoot config = builder.Build();

        return new Settings(config);
    }
}
