namespace AccessWifi.Api.Features.Admin
{
    public record CreateUserRequest(string Username, string Password, Guid? IDCompany);
    public record UserDto(Guid Id, string Username, string Role, Guid? IDCompany, string? CompanyName, DateTime CreatedAt);
}
