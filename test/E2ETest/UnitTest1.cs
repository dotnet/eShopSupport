using System.Net;
using E2ETest.Infrastructure;

namespace E2ETest;

[Collection(AppTestCollection.Name)]
public class UnitTest1(AppHostFixture app)
{
    [Fact]
    public async Task Test1()
    {
        var httpClient = await app.StaffWebUI.CreateHttpClientAsync();
        var response = await httpClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Test2()
    {
        var httpClient = await app.StaffWebUI.CreateHttpClientAsync();
        var response = await httpClient.GetAsync("/qwewqe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
