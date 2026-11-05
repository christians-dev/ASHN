using System.ComponentModel.DataAnnotations;

namespace Ashen.BuildingBlocks;

/// <summary>Store-wide identity, kept in configuration so a second store would need no code change (constitution X).</summary>
public sealed class StoreOptions
{
    public const string SectionName = "Ashen:Store";

    [Required]
    public string StoreName { get; set; } = "";

    /// <summary>Public address of the storefront, used for links in emails.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    [Required, EmailAddress]
    public string SupportEmail { get; set; } = "";

    [Required]
    public string EmailSenderName { get; set; } = "";

    public string Link(string relativePath) => $"{BaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
}
