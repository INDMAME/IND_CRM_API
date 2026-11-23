namespace IND_CRM_API.Services.Interfaces
{
    public interface IJwtService
    {
        JwtService.JwtTokenInfo GenerateToken(string username, int? overrideMinutes = null);
    }
}
