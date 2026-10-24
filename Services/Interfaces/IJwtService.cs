namespace IND_CRM_API.Services.Interfaces
{
    public interface IJwtService
    {
        JwtService.JwtTokenInfo GenerateToken(string username, int? overrideMinutes = null);

        /// <summary>
        /// Generates a token with explicit API roles (ind:role claims) for the user.
        /// </summary>
        JwtService.JwtTokenInfo GenerateToken(string username, int? overrideMinutes, System.Collections.Generic.IEnumerable<string> roles);
    }
}
