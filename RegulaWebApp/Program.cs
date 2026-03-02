using RegulaWebApp.Models;
using RegulaWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.Configure<RegulaOptions>(builder.Configuration.GetSection("Regula"));
builder.Services.Configure<DocROptions>(builder.Configuration.GetSection("DocR"));
builder.Services.AddScoped<IRegulaService, RegulaService>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<IDocumentFraudService, DocumentFraudService>();

builder.Services.AddHttpClient("Regula", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RegulaOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }

    if (!string.IsNullOrWhiteSpace(options.ApiKeyHeader) && !string.IsNullOrWhiteSpace(options.ApiKey))
    {
        client.DefaultRequestHeaders.Remove(options.ApiKeyHeader);
        client.DefaultRequestHeaders.Add(options.ApiKeyHeader, options.ApiKey);
    }

    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds <= 0 ? 30 : options.RequestTimeoutSeconds);
});

builder.Services.AddHttpClient("DocR", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DocROptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }

    if (!string.IsNullOrWhiteSpace(options.ApiKeyHeader) && !string.IsNullOrWhiteSpace(options.ApiKey))
    {
        client.DefaultRequestHeaders.Remove(options.ApiKeyHeader);
        client.DefaultRequestHeaders.Add(options.ApiKeyHeader, options.ApiKey);
    }

    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds <= 0 ? 60 : options.RequestTimeoutSeconds);
});

var app = builder.Build();

//app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
