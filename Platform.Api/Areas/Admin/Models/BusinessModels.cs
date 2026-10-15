using System.ComponentModel.DataAnnotations;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Areas.Admin.Models;

public record BusinessRow(Guid Id, string Name, BusinessSourceRow? Source);

/// <param name="LastImportSummary">Warnings after a successful import, or the error message after a failed one.</param>
public record BusinessSourceRow(
    SourcePlatform Platform,
    string? Url,
    DateTimeOffset? LastImportedAt,
    bool? LastImportSucceeded,
    string? LastImportSummary)
{
    public bool HasWarnings => LastImportSucceeded == true && !string.IsNullOrWhiteSpace(LastImportSummary);
}

/// <summary>Minimal form: the profile (aliases, time zone, currency, description) is filled by the import.</summary>
public class BusinessCreateModel
{
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required, Url, StringLength(2048)]
    [Display(Name = "Link (product page)")]
    public string SourceUrl { get; set; } = "";

    [Display(Name = "Adapter")]
    public SourcePlatform SourcePlatform { get; set; } = SourcePlatform.Tilda;
}
