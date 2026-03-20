using DiceThroneApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddSingleton<DiceNotationParser>();
builder.Services.AddSingleton<ObjectiveMatcher>();
builder.Services.AddSingleton<ProbabilityCalculator>();
builder.Services.AddSingleton<MonteCarloSimulator>();
builder.Services.AddSingleton<DiceRollAdvisor>();
builder.Services.AddSingleton<HeroService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
