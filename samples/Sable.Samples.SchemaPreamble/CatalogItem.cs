// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

namespace Sable.Samples.SchemaPreamble;

public sealed class CatalogItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
}

public sealed class PendingCatalogItem
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
}
