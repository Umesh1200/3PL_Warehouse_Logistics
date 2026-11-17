using System.Security.Claims;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.BackgroundJobs;
namespace Warehouse3PL.UnitTests.Analytics;

public class Phase68DashboardSecurityTests
{
    [Theory]
    [InlineData("localhost",false,false,false)]
    [InlineData("127.0.0.1",false,false,false)]
    [InlineData("localhost",true,false,false)]
    [InlineData("warehouse.example",true,true,true)]
    [InlineData("localhost",true,true,true)]
    [InlineData("warehouse.example",false,true,false)]
    public void CallerControlledHostNeverGrantsDashboardAccess(string host,bool authenticated,bool admin,bool expected)
    {
        var http=new DefaultHttpContext();http.Request.Host=new HostString(host);
        var claims=admin?new[]{new Claim(ClaimTypes.Role,SystemRoles.SuperAdmin)}:Array.Empty<Claim>();
        http.User=new ClaimsPrincipal(new ClaimsIdentity(claims,authenticated?"test":null));
        // Signature follows Hangfire.Dashboard.AspNetCoreDashboardContext(storage, options, httpContext).
        var context=new AspNetCoreDashboardContext(new UnusedStorage(),new DashboardOptions{IgnoreAntiforgeryToken=true},http);
        Assert.Equal(expected,new HangfireDashboardAuthorizationFilter().Authorize(context));
    }
    private sealed class UnusedStorage : JobStorage
    {
        public override IStorageConnection GetConnection()=>throw new NotSupportedException();
        public override IMonitoringApi GetMonitoringApi()=>throw new NotSupportedException();
    }
}
