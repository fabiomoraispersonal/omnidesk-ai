using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiSuggestions;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class SuggestionActionTests
{
    [Theory]
    [InlineData("approved", null)]
    [InlineData("edited", "versão editada do texto")]
    [InlineData("discarded", null)]
    [InlineData("sent_unchanged", null)]
    public async Task RecordHumanAction_PersistsActionAndOptionalText(string actionStr, string? finalText)
    {
        var fixture = new AuthorizationFixture();
        await fixture.InitializeAsync();
        try
        {
            var mongo = new MongoClient("mongodb://localhost:27017");
            var logger = new AiSuggestionLogger(mongo);
            var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);

            var ownerId = Guid.NewGuid();
            var entry = new SuggestionLogEntry(
                Guid.NewGuid(), Guid.NewGuid(), ownerId, Guid.NewGuid(), null, 5,
                "olá maria", "gpt-4o", 50, 25, 200, DateTimeOffset.UtcNow);
            var id = await logger.LogGenerationAsync(slug, entry);

            var action = Enum.Parse<HumanAction>(actionStr, ignoreCase: true);
            var ok = await logger.RecordHumanActionAsync(slug, id, ownerId, action, finalText);
            Assert.True(ok);

            var coll = mongo.GetDatabase(slug.Replace('-', '_'))
                .GetCollection<BsonDocument>("ai_suggestion_logs");
            var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id))).FirstAsync();

            Assert.Equal(actionStr.ToLowerInvariant(), doc["human_action"].AsString);
            if (finalText is null)
                Assert.True(doc["final_message_text"].IsBsonNull);
            else
                Assert.Equal(finalText, doc["final_message_text"].AsString);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecordHumanAction_OnUnknownSuggestion_ReturnsFalse()
    {
        var fixture = new AuthorizationFixture();
        await fixture.InitializeAsync();
        try
        {
            var mongo = new MongoClient("mongodb://localhost:27017");
            var logger = new AiSuggestionLogger(mongo);
            var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);

            var ok = await logger.RecordHumanActionAsync(
                slug, ObjectId.GenerateNewId().ToString(), Guid.NewGuid(), HumanAction.Approved, null);
            Assert.False(ok);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecordHumanAction_InvalidObjectId_ReturnsFalse()
    {
        var fixture = new AuthorizationFixture();
        await fixture.InitializeAsync();
        try
        {
            var mongo = new MongoClient("mongodb://localhost:27017");
            var logger = new AiSuggestionLogger(mongo);
            var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);

            var ok = await logger.RecordHumanActionAsync(slug, "not-an-objectid", Guid.NewGuid(), HumanAction.Approved, null);
            Assert.False(ok);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
