# Dice Throne Roll Tree

A comprehensive .NET 8 ASP.NET Core Web API application for calculating Dice Throne roll probabilities with advanced analytics and Monte Carlo simulation.

## Features

- **Dice Notation Parser**: Parse complex dice roll objectives like `[6666]`, `[(123)(123)(123)]`, `SmallStraight`, and `LargeStraight`
- **Probability Calculator**: Analytic probability calculation using dynamic programming and optimal play strategies
- **Monte Carlo Simulator**: Statistical simulation with configurable iteration counts
- **Hero Management**: JSON-based hero data storage with multiple pre-configured heroes
- **Roll Advisor**: Intelligent dice-keeping suggestions for optimal play
- **Web Frontend**: Clean, responsive HTML/CSS/JavaScript interface
- **REST API**: Full-featured API with Swagger documentation

## Project Structure

```
/src/DiceThroneApi/          - ASP.NET Core Web API project
  /Controllers/              - API controllers
  /Models/                   - Data models
  /Services/                 - Business logic services
  /Data/heroes/              - JSON hero data files
  /wwwroot/                  - Static web frontend
/tests/DiceThroneApi.Tests/  - xUnit test project
```

## Dice Notation

- `[6666]` - Need exactly four 6s (5th die is irrelevant)
- `[66666]` - Need exactly five 6s
- `[(123)(123)(123)]` - Need 3 dice each showing a value from {1,2,3}
- `[(123)(456)(456)]` - 1 die from {1,2,3}, 2 dice from {4,5,6}
- `SmallStraight` - 4 consecutive different numbers (1-2-3-4, 2-3-4-5, or 3-4-5-6)
- `LargeStraight` - 5 consecutive different numbers (1-2-3-4-5 or 2-3-4-5-6)

## Game Mechanics

- **5 dice per turn** (configurable 1-7)
- **1 initial roll + 2 re-rolls** per turn
- Players choose which dice to keep between rolls
- Goal: Achieve specific roll objectives

## Getting Started

### Prerequisites

- .NET 8 SDK or later

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test tests/DiceThroneApi.Tests/DiceThroneApi.Tests.csproj
```

### Run the Application

```bash
cd src/DiceThroneApi
dotnet run
```

The application will start and display the URL (typically http://localhost:5022).

### Access the Web Interface

Open your browser to the displayed URL to access the interactive web interface.

### API Endpoints

- `GET /api/heroes` - List all heroes
- `GET /api/heroes/{id}` - Get specific hero with objectives
- `POST /api/roll/simulate` - Simulate dice roll with suggestions
- `POST /api/roll/probability` - Calculate probability for objective
- `POST /api/roll/advice` - Get advice for current dice state

## Heroes

The application includes three pre-configured heroes:

1. **Barbarian** - Specializes in {1,2,3} and {4,5,6} combinations
2. **Moon Elf** - Focuses on {1,2} and {3,4,5} patterns
3. **Pyromancer** - Masters of {1,2} and {4,5,6} combinations

## Technology Stack

- **.NET 8** - Framework
- **ASP.NET Core** - Web framework
- **System.Text.Json** - JSON serialization
- **Swashbuckle** - API documentation
- **xUnit** - Testing framework
- **Vanilla JavaScript** - Frontend (no frameworks)

## Implementation Details

### Probability Calculator

Uses dynamic programming with memoization to calculate optimal play probabilities:
- State: (sorted dice values, rolls remaining)
- For each state, evaluates all possible "keep" decisions
- Returns probability of success with optimal strategy

### Monte Carlo Simulator

Runs configurable iterations (default 10,000) to estimate probabilities:
- Simulates full game with random rolls
- Uses greedy strategy for dice selection
- Returns success rate as probability estimate

### Objective Matcher

Uses backtracking algorithm to match dice to objectives:
- Assigns dice to groups for Standard objectives
- Checks for consecutive sequences for Straight objectives
- Handles flexible group requirements

## License

MIT License - see LICENSE file for details