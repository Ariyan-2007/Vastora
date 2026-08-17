using Vastora.Application.Content;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Content;

public class ContentServiceTests
{
    [Fact]
    public async Task SetImageAsync_SetsOnlyTheImage_LeavingOtherFieldsUntouched()
    {
        var blocks = new FakeMongoRepository<ContentBlock>();
        var service = new ContentService(blocks);
        var banner = blocks.Seed(new ContentBlock
        {
            TenantId = "tenant-1", BusinessId = "biz-1", Type = ContentBlockType.Banner, Title = "Summer Sale"
        })[^1];

        var result = await service.SetImageAsync("tenant-1", "biz-1", banner.Id, "/uploads/biz-1/banner.jpg", CancellationToken.None);

        Assert.Equal("/uploads/biz-1/banner.jpg", result.ImageUrl);
        Assert.Equal("Summer Sale", result.Title);
    }
}
