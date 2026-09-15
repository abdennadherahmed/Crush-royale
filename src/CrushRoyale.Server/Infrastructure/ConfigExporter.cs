using System.Text.Json;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Server.Infrastructure;

/// <summary>
/// `dotnet run --project src/CrushRoyale.Server -- export-stages &lt;dir&gt;` writes the data files shipped with the client
/// (Unity Resources/Data): balance, 1000 stages, story, achievements, cosmetics.
/// </summary>
public static class ConfigExporter
{
    public static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions(Json.Options) { WriteIndented = true };
        GameBalance balance = GameBalance.CreateDefault();
        balance.Validate();
        var catalog = new StageCatalog(balance);

        Write(directory, "balance.json", balance, options);
        Write(directory, "stages.json", catalog.GetRange(1, catalog.TotalCampaignStages).ToList(), options);
        Write(directory, "story.json", new
        {
            characters = StoryDatabase.Characters,
            choices = StoryDatabase.Choices,
            events = StoryDatabase.GetEvents(balance.Story)
        }, options);
        Write(directory, "achievements.json", AchievementCatalog.All, options);
        Write(directory, "cosmetics.json", CosmeticCatalog.All, options);
        File.WriteAllText(Path.Combine(directory, "balance.hash"), balance.ComputeHash().ToString("x16"));
    }

    private static void Write<T>(string directory, string name, T value, JsonSerializerOptions options) =>
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, options));
}
