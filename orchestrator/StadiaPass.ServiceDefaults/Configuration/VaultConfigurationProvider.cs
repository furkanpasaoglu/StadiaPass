using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.Core;
using VaultSharp.V1.Commons;

namespace StadiaPass.ServiceDefaults.Configuration;

/// <summary>
/// Reads one KV v2 path and hands its contents to <see cref="IConfiguration"/>. Because the source is added
/// last, a value in Vault wins over the same key in appsettings or the environment - Vault is the authority,
/// and anything left in a file is only a fallback for what Vault does not carry.
/// </summary>
internal sealed class VaultConfigurationProvider(VaultOptions options) : ConfigurationProvider
{
    public override void Load() => LoadAsync().GetAwaiter().GetResult();

    private static VaultClient CreateClient(VaultOptions options) =>
        new VaultClient(new VaultClientSettings(options.Address, new TokenAuthMethodInfo(options.Token))
        {
            VaultServiceTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        });

    /// <summary>
    /// Keys are stored the way .NET reads them - <c>ConnectionStrings:stadiapassdb</c> - so most of this is a
    /// straight copy. A nested object is still flattened, so a secret written as JSON from the Vault UI binds
    /// exactly like one written as flat keys.
    /// </summary>
    private static void Flatten(string prefix, JsonElement element, IDictionary<string, string?> data)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(Combine(prefix, property.Name), property.Value, data);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    Flatten(Combine(prefix, index.ToString(CultureInfo.InvariantCulture)), item, data);
                    index++;
                }

                break;

            case JsonValueKind.Null or JsonValueKind.Undefined:
                data[prefix] = null;

                break;

            default:
                data[prefix] = element.ToString();

                break;
        }
    }

    private static string Combine(string prefix, string key) =>
        prefix.Length is 0 ? key : $"{prefix}{ConfigurationPath.KeyDelimiter}{key}";

    private async Task LoadAsync()
    {
        var client = CreateClient(options);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.StartupTimeoutSeconds);

        while (true)
        {
            try
            {
                var secret = await client.V1.Secrets.KeyValue.V2
                    .ReadSecretAsync(path: options.Path, mountPoint: options.MountPoint);

                Data = Read(secret);

                return;
            }
            catch (Exception exception) when (IsTransient(exception) && DateTimeOffset.UtcNow < deadline)
            {
                // Vault is up but the path is not populated yet, or the server is still unsealing. Both
                // resolve on their own within seconds; a hard failure here would only mean a manual restart.
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static Dictionary<string, string?> Read(Secret<SecretData> secret)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in secret.Data.Data)
        {
            switch (value)
            {
                case JsonElement element:
                    Flatten(key, element, data);

                    break;

                case null:
                    data[key] = null;

                    break;

                default:
                    data[key] = Convert.ToString(value, CultureInfo.InvariantCulture);

                    break;
            }
        }

        return data;
    }

    /// <summary>A missing path or a sealed server is worth waiting out; a bad token is not.</summary>
    private static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException or TaskCanceledException => true,
        VaultApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.ServiceUnavailable } => true,
        _ => false
    };
}
