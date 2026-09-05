using System;
using System.Collections.Generic;
using Conduit;
using Conduit.Infrastructure;
using Conduit.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

// read database configuration (database provider + database connection) from configuration
// (appsettings.json and/or environment variables)
var defaultDatabaseConnectionString = "Filename=realworld.db";
var defaultDatabaseProvider = "sqlite";

var builder = WebApplication.CreateBuilder(args);

// resolve the database provider once at startup into a typed value; unknown values fail fast
var databaseProvider = DatabaseProviderResolver.Resolve(
    builder.Configuration["Conduit:DatabaseProvider"]
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_Conduit_DatabaseProvider")
        ?? defaultDatabaseProvider
);

// connection string for the selected engine: the per-engine named connection string first, then
// the shared Conduit:ConnectionString setting, then the hard-coded SQLite default
var connectionString =
    builder.Configuration.GetConnectionString(
        databaseProvider switch
        {
            DatabaseProvider.PostgreSql => "AppDb_PostgreSql",
            DatabaseProvider.SqlServer => "AppDb_SqlServer",
            _ => "AppDb_Sqlite",
        }
    )
    ?? builder.Configuration["Conduit:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_Conduit_ConnectionString")
    ?? (databaseProvider == DatabaseProvider.Sqlite ? defaultDatabaseConnectionString : null);

// fail fast at startup when the selected engine has no usable connection string
// (placeholder-only values from appsettings.json are rejected too)
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains('<'))
{
    throw new InvalidOperationException(
        $"No connection string configured for database provider '{databaseProvider}'. "
            + "Set 'ConnectionStrings:AppDb_"
            + databaseProvider
            + "' or 'Conduit:ConnectionString'."
    );
}

builder.Services.AddDbContext<ConduitContext>(options =>
{
    switch (databaseProvider)
    {
        case DatabaseProvider.Sqlite:
            options.UseSqlite(connectionString);
            break;
        case DatabaseProvider.SqlServer:
            options.UseSqlServer(connectionString);
            break;
        case DatabaseProvider.PostgreSql:
            options.UseNpgsql(connectionString);
            break;
    }
});

builder.Services.AddLocalization(x => x.ResourcesPath = "Resources");

// Inject an implementation of ISwaggerProvider with defaulted settings applied
builder.Services.AddSwaggerGen(x =>
{
    x.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Please insert JWT with Bearer into field",
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            BearerFormat = "JWT",
        }
    );

    x.SupportNonNullableReferenceTypes();

    x.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
    });
    x.SwaggerDoc("v1", new OpenApiInfo { Title = "RealWorld API", Version = "v1" });
    x.CustomSchemaIds(y => y.FullName);
    x.DocInclusionPredicate((_, _) => true);
    x.TagActionsBy(y => new List<string> { y.GroupName ?? throw new InvalidOperationException() });
    x.CustomSchemaIds(s => s.FullName?.Replace("+", "."));
});

builder.Services.AddCors();
builder
    .Services.AddMvc(opt =>
    {
        opt.Conventions.Add(new GroupByApiRootConvention());
        // the RealWorld API spec mounts all endpoints under /api
        opt.Conventions.Add(
            new ApiRoutePrefixConvention(builder.Configuration["ApiPrefix"] ?? "api")
        );
        opt.Filters.Add<ValidatorActionFilter>();
        opt.EnableEndpointRouting = false;
    })
    // the RealWorld spec expects nullable fields (bio, image, ...) to be serialized as explicit nulls
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.DefaultIgnoreCondition = System
            .Text
            .Json
            .Serialization
            .JsonIgnoreCondition
            .Never
    );

builder.Services.AddConduit();

builder.Services.AddJwt();

var app = builder.Build();

app.Services.GetRequiredService<ILoggerFactory>().AddSerilogLogging();

app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseCors(x => x.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

app.UseAuthentication();
app.UseMvc();

// Enable middleware to serve generated Swagger as a JSON endpoint
app.UseSwagger(c => c.RouteTemplate = "swagger/{documentName}/swagger.json");

// Enable middleware to serve swagger-ui assets(HTML, JS, CSS etc.)
app.UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "RealWorld API V1"));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope
        .ServiceProvider.GetRequiredService<ConduitContext>()
        .Database.EnsureCreated();
    // use context
}
app.Run();
