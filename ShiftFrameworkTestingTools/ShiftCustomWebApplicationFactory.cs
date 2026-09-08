using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.TypeAuth.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShiftSoftware.ShiftFrameworkTestingTools;

public class ShiftCustomWebApplicationBearerAuthSettings
{
    public bool Enabled { get; set; }
    public string TokenKeySettingKey { get; set; } = default!;
    public string TokenIssuerSettingKey { get; set; } = default!;
    public List<Type> TypeAuthActions { get; set; } = new();
}

public class ShiftCustomWebApplicationFactory<TStartup, DB> : WebApplicationFactory<TStartup>
    where TStartup : class
    where DB : DbContext
{
    IConfiguration? config;
    static string? token;

    private string dbConnectionSettingKey;
    private ShiftCustomWebApplicationBearerAuthSettings shiftCustomWebApplicationBearerAuthSettings;

    public ShiftCustomWebApplicationFactory(string dbConnectionSettingKey, ShiftCustomWebApplicationBearerAuthSettings shiftCustomWebApplicationBearerAuthSettings)
    {
        this.dbConnectionSettingKey = dbConnectionSettingKey;
        this.shiftCustomWebApplicationBearerAuthSettings = shiftCustomWebApplicationBearerAuthSettings;
    }

    /// <summary>
    /// Builds the test configuration from appsettings.json. Override to add explicit test-only sources.
    /// Environment variables are not read by default because this factory deletes and recreates the selected
    /// database. A derived factory can opt into environment overrides with a dedicated test prefix.
    /// </summary>
    protected virtual IConfigurationBuilder CreateTestConfigurationBuilder()
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false, true);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        config = CreateTestConfigurationBuilder().Build();

        var host = builder.Build();

        var serviceProvider = host.Services;

        using (var scope = serviceProvider.CreateScope())
        {
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<DB>();

            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }

        host.Start();

        return host;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        if (shiftCustomWebApplicationBearerAuthSettings.Enabled)
        {
            if (token == null)
            {
                var secrete = config!.GetValue<string>(shiftCustomWebApplicationBearerAuthSettings.TokenKeySettingKey)!;
                var issuer = config!.GetValue<string>(shiftCustomWebApplicationBearerAuthSettings.TokenIssuerSettingKey)!;

                token = ShiftCustomWebApplicationFactory<TStartup, DB>.GenerateToken(secrete, issuer, shiftCustomWebApplicationBearerAuthSettings.TypeAuthActions);
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        client.DefaultRequestHeaders.Add("timezone-offset", TimeZoneInfo.Local.BaseUtcOffset.ToString("c"));

        base.ConfigureClient(client);
    }

    /// <summary>
    /// Creates a client authenticated with a caller-supplied TypeAuth access tree and additional claims.
    /// Unlike the default client token, this token is generated per call and is never read from or written to
    /// the factory's static wildcard-token cache.
    /// </summary>
    /// <param name="accessTreeJson">The complete TypeAuth access tree JSON stored in the token.</param>
    /// <param name="claims">
    /// Additional claims for the authenticated user, such as a current-user or tenant identifier. The TypeAuth
    /// access-tree claim itself comes from <paramref name="accessTreeJson"/> and cannot be supplied again here.
    /// </param>
    /// <returns>An <see cref="HttpClient"/> whose bearer token contains the supplied access tree and claims.</returns>
    public HttpClient CreateAuthenticatedClient(string accessTreeJson, IEnumerable<Claim>? claims = null)
    {
        if (string.IsNullOrWhiteSpace(accessTreeJson))
            throw new ArgumentException("An access-tree JSON value is required.", nameof(accessTreeJson));

        // Fail fast on malformed/non-object trees instead of producing a token that can only fail later in the API pipeline.
        using var accessTree = JsonDocument.Parse(accessTreeJson);
        if (accessTree.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The access-tree JSON root must be an object.", nameof(accessTreeJson));

        var additionalClaims = claims?.ToList() ?? new List<Claim>();
        if (additionalClaims.Any(claim => claim.Type == TypeAuthClaimTypes.AccessTree))
            throw new ArgumentException(
                $"The '{TypeAuthClaimTypes.AccessTree}' claim is supplied by {nameof(accessTreeJson)} and cannot be repeated.",
                nameof(claims));

        // CreateClient starts/configures the host and preserves the factory's normal client setup (for example,
        // timezone-offset). ConfigureClient may attach the cached wildcard token; assigning Authorization below
        // deliberately replaces it with this per-call scoped token.
        var client = CreateClient();
        var secrete = config!.GetValue<string>(shiftCustomWebApplicationBearerAuthSettings.TokenKeySettingKey)!;
        var issuer = config!.GetValue<string>(shiftCustomWebApplicationBearerAuthSettings.TokenIssuerSettingKey)!;
        var scopedToken = GenerateToken(secrete, issuer, accessTreeJson, additionalClaims);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);
        return client;
    }

    static string GenerateToken(string secrete, string issuer, List<Type> actionTrees)
    {
        var tree = new Dictionary<string, object>();

        foreach (var item in actionTrees)
        {
            tree[item.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };
        }

        var jsonTree = JsonSerializer.Serialize(tree);
        return GenerateToken(secrete, issuer, jsonTree, Array.Empty<Claim>());
    }

    static string GenerateToken(string secrete, string issuer, string accessTreeJson, IEnumerable<Claim> additionalClaims)
    {
        // APIs wired with AddShiftIdentity validate RSA-signed (asymmetric) tokens; point
        // TokenKeySettingKey at the RSA private key for those. A key that isn't an RSA
        // private key keeps the original symmetric HMAC signing.
        SigningCredentials creds;

        try
        {
            var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(secrete), out _);
            creds = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secrete)), SecurityAlgorithms.HmacSha256);
        }

        var claims = new List<Claim>
        {
            new(TypeAuthClaimTypes.AccessTree, accessTreeJson),
        };

        claims.AddRange(additionalClaims);

        var token = new JwtSecurityToken(
          issuer: issuer,
          claims: claims,
          expires: DateTime.UtcNow.AddDays(1),
          signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (config is null)
        {
            config = CreateTestConfigurationBuilder().Build();
        }

        builder
            .UseConfiguration(config)
            .ConfigureServices(services =>
            {

                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<DB>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<DB>(options =>
                {
                    options
                    .UseSqlServer(config!.GetConnectionString(dbConnectionSettingKey)!)
                    .UseTemporal(true);
                });
            });
    }
}
