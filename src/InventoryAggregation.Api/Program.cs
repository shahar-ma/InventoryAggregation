using InventoryAggregation.Api.Endpoints;
using InventoryAggregation.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInventoryAggregation(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Inventory Aggregation API",
        Version = "v1",
        Description = "Aggregates inventory records by category. The API key for the upstream Inventory API is configured server-side and is not required by callers."
    });

    var xmlFile = $"{typeof(InventoryAggregation.Api.ApiMarker).Assembly.GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});

var app = builder.Build();

// Last-resort net: anything not mapped by the endpoint becomes a ProblemDetails 500, never a raw stack trace.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});

app.MapInventoryEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

#pragma warning disable S6966
app.Run();
#pragma warning restore S6966
