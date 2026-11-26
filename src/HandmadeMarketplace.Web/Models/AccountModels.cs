using System.ComponentModel.DataAnnotations;
namespace HandmadeMarketplace.Web.Models;
public class LoginInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
public class RegisterInput : LoginInput
{
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string City { get; set; } = "";
}
public class ProfileInput
{
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string City { get; set; } = "";
    [StringLength(1000)] public string Bio { get; set; } = "";
}
