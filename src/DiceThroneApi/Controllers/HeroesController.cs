using Microsoft.AspNetCore.Mvc;
using DiceThroneApi.Services;

namespace DiceThroneApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HeroesController : ControllerBase
{
    private readonly HeroService _heroService;

    public HeroesController(HeroService heroService)
    {
        _heroService = heroService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllHeroes()
    {
        var heroes = await _heroService.GetAllHeroesAsync();
        return Ok(heroes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetHero(string id)
    {
        var hero = await _heroService.GetHeroByIdAsync(id);
        if (hero == null)
        {
            return NotFound();
        }
        return Ok(hero);
    }
}
