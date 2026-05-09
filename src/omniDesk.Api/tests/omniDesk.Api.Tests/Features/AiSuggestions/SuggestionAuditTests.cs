using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiSuggestions;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class SuggestionAuditTests
{
    /// <summary>
    /// SC-007 audit guarantee: the suggestion path NEVER bypasses the human approval step.
    /// We assert this by inspecting the audit collection: no human action means no message
    /// was emitted (the parent component is the only one allowed to dispatch the message,
    /// and it always goes through `recordAction` first).
    /// </summary>
    [Fact]
    public async Task SuggestionWithoutHumanAction_HasNoFinalMessageText()
    {
        var fixture = new AuthorizationFixture();
        await fixture.InitializeAsync();
        try
        {
            var mongo = new MongoClient("mongodb://localhost:27017");
            var logger = new AiSuggestionLogger(mongo);
            var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);

            var entry = new SuggestionLogEntry(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 5,
                "olá", "gpt-4o", 100, 50, 200, DateTimeOffset.UtcNow);
            var suggestionId = await logger.LogGenerationAsync(slug, entry);

            var coll = mongo.GetDatabase(slug.Replace('-', '_'))
                .GetCollection<BsonDocument>("ai_suggestion_logs");
            var doc = await coll.Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(suggestionId)))
                .FirstAsync();
            Assert.False(doc.Contains("human_action") && !doc["human_action"].IsBsonNull,
                "Generation log must NOT auto-fill human_action.");
            Assert.False(doc.Contains("final_message_text") && !doc["final_message_text"].IsBsonNull,
                "Generation log must NOT auto-fill final_message_text — only the human action endpoint sets it.");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecordHumanAction_RejectsCallerOtherThanOwner()
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
                Guid.NewGuid(), Guid.NewGuid(), ownerId, Guid.NewGuid(), null, 1,
                "x", "gpt-4o", 1, 1, 1, DateTimeOffset.UtcNow);
            var suggestionId = await logger.LogGenerationAsync(slug, entry);

            // Different attendant tries to record action — must fail.
            var ok = await logger.RecordHumanActionAsync(slug, suggestionId, Guid.NewGuid(), HumanAction.Approved, null);
            Assert.False(ok);

            // Owner succeeds.
            var okOwner = await logger.RecordHumanActionAsync(slug, suggestionId, ownerId, HumanAction.Approved, null);
            Assert.True(okOwner);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
