using System.ComponentModel.DataAnnotations;

namespace Authentication.API.Models;

public class RegisterModel
{
    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password")]
    public string? ConfirmPassword { get; set; }

    [Required]
    public string? DisplayName { get; set; }
}
