using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using PicCompressor.Application;
using PicCompressor.Infrastructure;
using PicCompressor.Web;

// Die Oberfläche liegt neben der Programmdatei; das Arbeitsverzeichnis des Containers ist `/`.
var builder = WebApplication.CreateBuilder(
    new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.ConfigureHttpJsonOptions(
    options =>
    {
        options.SerializerOptions.Converters.Add(
            new JsonScanConfigurationStore.ChromaSubsamplingConverter());
        options.SerializerOptions.Converters.Add(
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });

var configurationPath = builder.Configuration["PicCompressor:ConfigurationPath"]
    ?? "/config/piccompressor.json";
var dataRoot = builder.Configuration["PicCompressor:DataRoot"] ?? "/data";
var stateRoot = builder.Configuration["PicCompressor:StateRoot"] ?? "/state";
var webPassword = builder.Configuration["PicCompressor:WebPassword"];
if (string.IsNullOrWhiteSpace(webPassword) || webPassword.Length < 16)
{
    throw new InvalidOperationException(
        "PicCompressor:WebPassword or PICCOMPRESSOR__WEBPASSWORD must contain at least 16 characters.");
}

builder.Services.AddSingleton(new WebConfigurationService(configurationPath, dataRoot, stateRoot));
builder.Services.AddSingleton<ScanControl>();
builder.Services.AddSingleton<IScanControl>(services => services.GetRequiredService<ScanControl>());
if (!builder.Configuration.GetValue("PicCompressor:DisableScanner", false))
{
    builder.Services.AddHostedService<ScanWorker>();
}

var app = builder.Build();
app.UseDefaultFiles();

app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        if (context.Request.Path.StartsWithSegments("/health/live"))
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (!TryAuthenticate(context.Request.Headers.Authorization, webPassword))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"PicCompressor\", charset=\"UTF-8\"";
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
        {
            if (!string.Equals(
                    context.Request.Headers["X-PicCompressor-Request"],
                    "1",
                    StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next().ConfigureAwait(false);
    });

app.UseStaticFiles();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet(
    "/health/ready",
    (WebConfigurationService configurations) =>
    {
        try
        {
            var document = configurations.Load();
            configurations.Validate(document.Configuration);
            return Results.Ok(new { status = "ready" });
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ConfigurationValidationException)
        {
            return Results.Json(
                new { status = "not-ready" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });
app.MapGet("/api/config", (WebConfigurationService configurations) => configurations.Load());
app.MapPost(
    "/api/config/validate",
    (ScanConfiguration configuration, WebConfigurationService configurations) =>
    {
        try
        {
            configurations.Validate(configuration);
            return Results.Ok(new { valid = true, errors = Array.Empty<string>() });
        }
        catch (ConfigurationValidationException exception)
        {
            return Results.BadRequest(new { valid = false, errors = exception.Errors });
        }
    });
app.MapPut(
    "/api/config",
    (SaveConfigurationRequest request, WebConfigurationService configurations, ScanControl scans) =>
    {
        try
        {
            var saved = scans.PublishConfiguration(
                () => configurations.Save(request.Configuration, request.Revision));
            return Results.Ok(saved);
        }
        catch (ConfigurationConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ConfigurationValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors });
        }
    });
app.MapGet("/api/status", (IScanControl scans) => scans.Status);
app.MapPost(
    "/api/scans",
    (IScanControl scans) =>
    {
        scans.Trigger();
        return Results.Accepted("/api/status", scans.Status);
    });

app.Run();

static bool TryAuthenticate(string? authorization, string expectedPassword)
{
    const string Prefix = "Basic ";
    if (string.IsNullOrWhiteSpace(authorization)
        || !authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    try
    {
        var credential = Encoding.UTF8.GetString(
            Convert.FromBase64String(authorization[Prefix.Length..].Trim()));
        var separator = credential.IndexOf(':');
        if (separator < 0 || !string.Equals(credential[..separator], "admin", StringComparison.Ordinal))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(credential[(separator + 1)..]));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedPassword));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
    catch (FormatException)
    {
        return false;
    }
}

public sealed record SaveConfigurationRequest(ScanConfiguration Configuration, string Revision);

public partial class Program;
