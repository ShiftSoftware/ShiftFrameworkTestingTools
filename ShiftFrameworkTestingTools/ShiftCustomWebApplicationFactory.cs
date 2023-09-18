using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.TypeAuth.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ShiftSoftware.ShiftEntity.EFCore.Extensions;

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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        config = new ConfigurationBuilder()
               .SetBasePath(AppContext.BaseDirectory)
               .AddJsonFile("appsettings.json", false, true)
               .Build();

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

            client.DefaultRequestHeaders.Add("Authorization", $"bearer {token}");
        }

        client.DefaultRequestHeaders.Add("timezone-offset", TimeZoneInfo.Local.BaseUtcOffset.ToString("c"));

        base.ConfigureClient(client);
    }

    static string GenerateToken(string secrete, string issuer, List<Type> actionTrees)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secrete));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tree = new Dictionary<string, object>();

        var claims = new List<Claim>()
        {
        };

        foreach (var item in actionTrees)
        {
            tree[item.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };
        }

        var jsonTree = JsonSerializer.Serialize(tree);

        claims.Add(new Claim(TypeAuthClaimTypes.AccessTree, jsonTree));

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
        builder
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
