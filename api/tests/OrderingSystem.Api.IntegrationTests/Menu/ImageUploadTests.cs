using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using OrderingSystem.Api.IntegrationTests.Auth;
using OrderingSystem.Api.IntegrationTests.Persistence;
using OrderingSystem.Application.Features.Menu;
using OrderingSystem.Infrastructure.Persistence.Seed;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.EntityFrameworkCore;

namespace OrderingSystem.Api.IntegrationTests.Menu;

/// <summary>
/// Uploads, which are the endpoint an attacker reaches for first. Each test here corresponds to
/// something the storage layer deliberately refuses.
/// </summary>
public sealed class ImageUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_photo_is_stored_and_then_served()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await ItemIdAsync("Classic Smash");

        var response = await UploadAsync(client, itemId, await PngAsync(600, 400), "burger.png");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var item = await response.Content.ReadFromJsonAsync<MenuItemResponse>(Ct);
        item!.ImageUrl.ShouldNotBeNull();
        item.ImageUrl.ShouldStartWith("/media/");

        // Re-encoded, not stored as sent: a PNG went in and a WebP came out.
        item.ImageUrl.ShouldEndWith(".webp");

        var served = await factory.CreateClient().GetAsync(item.ImageUrl, Ct);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
    }

    [Fact]
    public async Task An_oversized_image_is_shrunk_rather_than_stored_as_sent()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await ItemIdAsync("Double Smash");

        var response = await UploadAsync(client, itemId, await PngAsync(3000, 2000), "huge.png");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var item = await response.Content.ReadFromJsonAsync<MenuItemResponse>(Ct);
        var bytes = await factory.CreateClient().GetByteArrayAsync(item!.ImageUrl!, Ct);

        using var stored = Image.Load(bytes);
        Math.Max(stored.Width, stored.Height).ShouldBeLessThanOrEqualTo(1600,
            "a menu photo does not need to be 3000px wide");
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await ItemIdAsync("Bacon Lab");

        // Named .png, and not a PNG. Trusting the extension is the oldest upload bug there is.
        var notAnImage = Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?>");

        var response = await UploadAsync(client, itemId, notAnImage, "innocent.png");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("not an image");
    }

    [Fact]
    public async Task Uploading_to_another_restaurants_item_is_refused()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var foreignItemId = await ForeignItemIdAsync();

        var response = await UploadAsync(client, foreignItemId, await PngAsync(200, 200), "x.png");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Replacing_a_photo_removes_the_one_it_replaced()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await ItemIdAsync("Spicy Inferno");

        var first = await (await UploadAsync(client, itemId, await PngAsync(300, 300), "a.png"))
            .Content.ReadFromJsonAsync<MenuItemResponse>(Ct);
        var second = await (await UploadAsync(client, itemId, await PngAsync(300, 300), "b.png"))
            .Content.ReadFromJsonAsync<MenuItemResponse>(Ct);

        second!.ImageUrl.ShouldNotBe(first!.ImageUrl);

        // Otherwise every edit leaves a file behind forever.
        var orphan = await factory.CreateClient().GetAsync(first.ImageUrl!, Ct);
        orphan.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_photo_clears_the_item_and_the_file()
    {
        var client = await SignInAsync("staff@frieslab.test");
        var itemId = await ItemIdAsync("Cheese Lab Fries");

        var uploaded = await (await UploadAsync(client, itemId, await PngAsync(300, 300), "c.png"))
            .Content.ReadFromJsonAsync<MenuItemResponse>(Ct);

        var removed = await client.DeleteAsync($"/api/restaurant/menu-items/{itemId}/image", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await removed.Content.ReadFromJsonAsync<MenuItemResponse>(Ct))!.ImageUrl.ShouldBeNull();
        (await factory.CreateClient().GetAsync(uploaded!.ImageUrl!, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<byte[]> PngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var buffer = new MemoryStream();
        await image.SaveAsync(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid itemId, byte[] bytes, string fileName)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);

        return await client.PostAsync($"/api/restaurant/menu-items/{itemId}/image", form, Ct);
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new OrderingSystem.Application.Features.Auth.LoginRequest(email, DatabaseSeeder.SeedPassword), Ct);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content
            .ReadFromJsonAsync<OrderingSystem.Application.Features.Auth.AuthTokensResponse>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private async Task<Guid> ItemIdAsync(string name)
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "frieslab" && i.Name == name)
            .Select(i => i.Id).FirstAsync(Ct);
    }

    private async Task<Guid> ForeignItemIdAsync()
    {
        await using var db = factory.CreateDbContext(TestTenant.PlatformAdmin());
        return await db.MenuItems
            .Where(i => i.Restaurant.Slug == "beirut-mezze-house")
            .Select(i => i.Id).FirstAsync(Ct);
    }
}
