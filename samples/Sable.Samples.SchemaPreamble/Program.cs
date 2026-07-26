// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using JasperFx;
using Marten;
using Sable.Extensions;
using Sable.Samples.SchemaPreamble;
using Weasel.Core;

const string pendingChangeVariable = "SABLE_SAMPLE_INCLUDE_PENDING_CHANGE";
const string schemaName = "schema_preamble_sample";

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();
builder.Services.AddMartenWithSableSupport(_ =>
{
    var options = new StoreOptions();
    options.Connection(
        builder.Configuration.GetConnectionString("Marten")
            ?? throw new InvalidOperationException("ConnectionStrings:Marten is required.")
    );
    options.DatabaseSchemaName = schemaName;
    options.AutoCreateSchemaObjects = AutoCreate.None;
    options.Schema.For<CatalogItem>().Index(item => item.Sku);

    if (
        string.Equals(
            Environment.GetEnvironmentVariable(pendingChangeVariable),
            "1",
            StringComparison.Ordinal
        )
    )
    {
        options.Schema.For<PendingCatalogItem>();
    }

    return options;
});

var app = builder.Build();
app.MapGet("/", () => "Sable schema preamble compatibility sample");

return await app.RunJasperFxCommands(args);
