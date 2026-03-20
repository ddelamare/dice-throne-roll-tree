using System.Text.Json;
using DiceThroneApi.Models;

namespace DiceThroneApi.Services;

public class HeroService
{
    private readonly string _heroDataPath;
    private readonly DiceNotationParser _parser;

    public HeroService(IWebHostEnvironment env, DiceNotationParser parser)
    {
        _heroDataPath = Path.Combine(env.ContentRootPath, "Data", "heroes");
        _parser = parser;
    }

    public async Task<List<Hero>> GetAllHeroesAsync()
    {
        var heroes = new List<Hero>();
        var heroFiles = Directory.GetFiles(_heroDataPath, "*.json");

        foreach (var file in heroFiles)
        {
            var hero = await LoadHeroAsync(file);
            if (hero != null)
            {
                heroes.Add(hero);
            }
        }

        return heroes;
    }

    public async Task<Hero?> GetHeroByIdAsync(string id)
    {
        var filePath = Path.Combine(_heroDataPath, $"{id}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await LoadHeroAsync(filePath);
    }

    private async Task<Hero?> LoadHeroAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var heroData = JsonSerializer.Deserialize<HeroData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (heroData == null)
            {
                return null;
            }

            var hero = new Hero
            {
                Id = heroData.Id,
                Name = heroData.Name,
                Objectives = new List<RollObjective>()
            };

            foreach (var objData in heroData.Objectives)
            {
                var objective = _parser.Parse(objData.Name, objData.Notation);
                hero.Objectives.Add(objective);
            }

            return hero;
        }
        catch
        {
            return null;
        }
    }

    private class HeroData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<ObjectiveData> Objectives { get; set; } = new();
    }

    private class ObjectiveData
    {
        public string Name { get; set; } = string.Empty;
        public string Notation { get; set; } = string.Empty;
    }
}
