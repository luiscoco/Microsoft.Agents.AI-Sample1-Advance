//using System;
//using System.Threading.Tasks;
//using Azure.Identity;
//using Azure.Security.KeyVault.Secrets;

//class Program
//{
//    static async Task Main()
//    {
//        // ✅ Replace with your real values
//        const string secretName = "AzureOpenAI--ApiKey";
//        const string keyVaultName = "myKeyVaultLuisCoco";
//        var kvUri = new Uri($"https://{keyVaultName}.vault.azure.net/");

//        var credential = new DefaultAzureCredential(); // or ClientSecretCredential if you set env vars
//        var client = new SecretClient(kvUri, credential);

//        // Read (no listing)
//        var secret = await client.GetSecretAsync(secretName);
//        Console.WriteLine($"Secret value: {secret.Value.Value}");
//    }
//}

using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using System;
using System.IO;

static TokenCredential CreateCredential(out string used)
{
    var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
    var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

    if (!string.IsNullOrWhiteSpace(tenantId) &&
        !string.IsNullOrWhiteSpace(clientId) &&
        !string.IsNullOrWhiteSpace(clientSecret))
    {
        used = "ClientSecretCredential (env)";
        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }

    used = "DefaultAzureCredential";
    return new DefaultAzureCredential();
}

try
{
    // 1) Load non-secret config
    var cfg = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();

    var endpoint = cfg["AzureOpenAI:Endpoint"]
        ?? throw new InvalidOperationException("Missing AzureOpenAI:Endpoint.");
    var deploymentName = cfg["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

    var vaultUri = cfg["AzureOpenAI:KeyVault:VaultUri"]
        ?? throw new InvalidOperationException("Missing AzureOpenAI:KeyVault:VaultUri.");
    var secretName = cfg["AzureOpenAI:KeyVault:SecretName"] ?? "AzureOpenAI--ApiKey";

    // 2) Credential
    var credential = CreateCredential(out var usedCred);
    Console.WriteLine($"[Auth] Using: {usedCred}");

    // 3) Fetch the secret directly (no listing, no readMetadata)
    var secretClient = new SecretClient(new Uri(vaultUri), credential);
    KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
    var apiKey = secret.Value
        ?? throw new InvalidOperationException($"Secret '{secretName}' has no value.");

    // 4) Build the agent
    const string JokerName = "Joker";
    const string JokerInstructions = "You are good at telling jokes.";

    AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
        .GetChatClient(deploymentName)
        .CreateAIAgent(JokerInstructions, JokerName);

    Console.WriteLine(await agent.RunAsync("Tell me a joke about a pirate."));
    await foreach (var chunk in agent.RunStreamingAsync("Another pirate joke, please."))
    {
        Console.WriteLine(chunk);
    }
}
catch (AuthenticationFailedException ex)
{
    Console.Error.WriteLine("Authentication failed: " + ex.Message);
    Console.Error.WriteLine("If using ClientSecretCredential, ensure the client secret is valid and not expired.");
    throw;
}
catch (RequestFailedException ex)
{
    Console.Error.WriteLine($"Key Vault error ({ex.Status}): {ex.Message}");
    Console.Error.WriteLine("Ensure this identity has at least Secret GET permission (RBAC role or access policy).");
    throw;
}
