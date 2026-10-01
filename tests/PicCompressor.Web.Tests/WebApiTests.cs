using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PicCompressor.Web;

namespace PicCompressor.Web.Tests;

public sealed class WebApiTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"piccompressor-api-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-password")]
    public void Startup_rejects_missing_or_weak_web_password(string? password)
    {
        using var factory = CreateFactory(webPassword: password);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "WebPassword",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Web_interface_requires_basic_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_web_interface_has_browser_security_headers()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_requires_basic_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_configuration_can_be_loaded()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetAsync("/api/config", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("revision", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Browser_save_preserves_hidden_folder_state_and_settings()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var script = await client.GetStringAsync("/app.js", TestContext.Current.CancellationToken);

        Assert.Contains("node._configuration = value", script, StringComparison.Ordinal);
        Assert.Contains("...card._configuration", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_chroma_value_can_be_saved()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);
        client.DefaultRequestHeaders.Add("X-PicCompressor-Request", "1");
        var loaded = await client.GetFromJsonAsync<JsonObject>(
            "/api/config", TestContext.Current.CancellationToken);
        loaded!["configuration"]!["defaults"]!["chromaSubsampling"] = "420";

        var response = await client.PutAsJsonAsync(
            "/api/config",
            new
            {
                configuration = loaded["configuration"],
                revision = loaded["revision"]!.GetValue<string>()
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("mode", "99")]
    [InlineData("mode", "\"Undefined\"")]
    [InlineData("exif", "99")]
    [InlineData("exif", "\"Undefined\"")]
    [InlineData("chromaSubsampling", "999")]
    public async Task Save_rejects_numeric_or_undefined_enums_without_server_error(
        string field,
        string invalidJson)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);
        client.DefaultRequestHeaders.Add("X-PicCompressor-Request", "1");
        var loaded = await client.GetFromJsonAsync<JsonObject>(
            "/api/config", TestContext.Current.CancellationToken);
        var configuration = loaded!["configuration"]!;
        if (field == "mode")
        {
            configuration[field] = JsonNode.Parse(invalidJson);
        }
        else
        {
            configuration["defaults"]![field] = JsonNode.Parse(invalidJson);
        }

        var response = await client.PutAsJsonAsync(
            "/api/config",
            new
            {
                configuration,
                revision = loaded["revision"]!.GetValue<string>()
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mutations_require_the_same_origin_request_header()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.PostAsync("/api/scans", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manual_scan_is_queued_once()
    {
        var control = new RecordingScanControl();
        await using var factory = CreateFactory(control);
        using var client = factory.CreateClient();
        Authorize(client);
        client.DefaultRequestHeaders.Add("X-PicCompressor-Request", "1");

        var response = await client.PostAsync("/api/scans", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, control.TriggerCount);
    }

    private WebApplicationFactory<Program> CreateFactory(
        IScanControl? control = null,
        string? webPassword = "secret-test-password")
    {
        var data = Path.Combine(root, "data");
        var state = Path.Combine(root, "state");
        var config = Path.Combine(root, "config");
        Directory.CreateDirectory(Path.Combine(data, "photos"));
        Directory.CreateDirectory(Path.Combine(data, "compressed"));
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(config);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("PicCompressor:ConfigurationPath", Path.Combine(config, "piccompressor.json"));
                builder.UseSetting("PicCompressor:DataRoot", data);
                builder.UseSetting("PicCompressor:StateRoot", state);
                if (webPassword is not null)
                {
                    builder.UseSetting("PicCompressor:WebPassword", webPassword);
                }
                builder.UseSetting("PicCompressor:DisableScanner", "true");
                if (control is not null)
                {
                    builder.ConfigureServices(
                        services => services.Replace(ServiceDescriptor.Singleton(control)));
                }
            });
    }

    private static void Authorize(HttpClient client)
    {
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret-test-password"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingScanControl : IScanControl
    {
        public int TriggerCount { get; private set; }

        public ScanStatus Status => ScanStatus.Idle;

        public bool Trigger()
        {
            TriggerCount++;
            return true;
        }

        public void ConfigurationChanged()
        {
        }
    }
}
