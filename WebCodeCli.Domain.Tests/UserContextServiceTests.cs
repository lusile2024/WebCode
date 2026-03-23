using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WebCodeCli.Domain.Domain.Service;

namespace WebCodeCli.Domain.Tests;

public class UserContextServiceTests
{
    [Fact]
    public void GetCurrentUsername_WhenAuthenticatedClaimExists_PrefersClaimOverOverride()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:DefaultUsername"] = "default-user"
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "test-user")
        ], "Cookies"));

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        var service = new UserContextService(configuration, accessor);
        service.SetCurrentUsername("stale-user");

        var username = service.GetCurrentUsername();

        Assert.Equal("test-user", username);
    }
}
