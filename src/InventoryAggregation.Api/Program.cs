using InventoryAggregation.Api.Endpoints;
using InventoryAggregation.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInventoryAggregation(builder.Configuration);

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

#pragma warning disable S6966
app.Run();
#pragma warning restore S6966
