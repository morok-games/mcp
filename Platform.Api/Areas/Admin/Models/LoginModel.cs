using System.ComponentModel.DataAnnotations;

namespace Platform.Api.Areas.Admin.Models;

public class LoginModel
{
    [Required, StringLength(64)]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
