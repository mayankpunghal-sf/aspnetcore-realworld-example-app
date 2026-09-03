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

var builder = WebApplication.CreateBuilder(args);

// resolve the database engine once at startup from configuration; a missing or unknown
// value fails fast instead of falling back to a default engine (Data:Provider)
var databaseProvider = DatabaseProviderParser.Parse(
    builder.Configuration[DatabaseProviderParser.ConfigKey]
);

// each engine has its own named connection string; the selected one must be configured
var connectionStringName = databaseProvider switch
{
    DatabaseProvider.SqlServer => "AppDb_SqlServer",
    DatabaseProvider.PostgreSql => "AppDb_PostgreSql",
    DatabaseProvider.Sqlite => "AppDb_Sqlite",
    _ => throw new InvalidOperationException(
        $"Database provider '{databaseProvider}' has no connection string mapping."
    ),
};
var connectionString = builder.Configuration.GetConnectionString(connectionStringName);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"Connection string '{connectionStringName}' for database provider "
            + $"'{databaseProvider}' is missing or empty."
    );
}

builder.Services.AddSingleton<IDatabaseProviderAccessor>(
    new DatabaseProviderAccessor(databaseProvider)
);

builder.Services.AddDbContext<ConduitContext>(options =>
{
    switch (databaseProvider)
    {
        case DatabaseProvider.SqlServer:
            options.UseSqlServer(connectionString);
            break;
        case DatabaseProvider.PostgreSql:
            options.UseNpgsql(connectionString);
            break;
        case DatabaseProvider.Sqlite:
            options.UseSqlite(connectionString);
            break;
        default:
            throw new InvalidOperationException(
                "Database provider unknown. Please check configuration"
            );
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
