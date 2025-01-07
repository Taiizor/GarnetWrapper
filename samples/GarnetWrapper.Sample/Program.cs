using GarnetWrapper.Extensions;
using GarnetWrapper.Sample.Middleware;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options => { options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Garnet Cache Sample API",
        Description = "Sample API demonstrating Garnet cache wrapper usage",
        Contact = new OpenApiContact
        {
            Name = "Taiizor",
            Email = "taiizor@vegalya.com"
        }
    });

    try
    {
        string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: XML documentation file could not be loaded: {ex.Message}");
    }
});

builder.Services.AddGarnet(options =>
{
    options.ConnectionString = builder.Configuration.GetValue<string>("Garnet:ConnectionString") ?? "localhost:6379";
    options.DefaultExpiry = TimeSpan.FromHours(1);
    options.EnableCompression = true;
    options.RetryTimeout = 5000;
    options.DatabaseId = 0;
    options.MaxRetries = 3;
});

WebApplication app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Garnet Cache Sample API V1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();