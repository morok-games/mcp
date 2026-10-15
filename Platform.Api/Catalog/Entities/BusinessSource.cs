namespace Platform.Api.Catalog.Entities;

/// <summary>Where a business's catalog is imported from, and how the last import went.</summary>
public class BusinessSource
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    /// <summary>Selects the adapter.</summary>
    public SourcePlatform Platform { get; set; }

    /// <summary>Entry point for website-based platforms, e.g. the product page.</summary>
    public string? Url { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastImportedAt { get; set; }

    public bool? LastImportSucceeded { get; set; }

    /// <summary>Warnings of the last successful import (one per line, null if none), or the error message if it failed.</summary>
    public string? LastImportSummary { get; set; }
}
