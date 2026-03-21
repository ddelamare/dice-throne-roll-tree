# Quick Start Guide

## Running the Application

1. **Navigate to the API directory:**
   ```bash
   cd src/DiceThroneApi
   ```

2. **Run the application:**
   ```bash
   dotnet run
   ```

3. **Access the application:**
   - Web UI: Open browser to `http://localhost:5022` (or the URL shown in console)
   - Swagger API docs: `http://localhost:5022/swagger`

## Using the Web Interface

1. **Select a Hero** from the dropdown (Barbarian, Moon Elf, or Pyromancer)
2. **Choose dice count** (1-7, default is 5)
3. **Select calculation method:**
   - **Analytic**: Exact probabilities using dynamic programming (faster for ≤5 dice)
   - **Monte Carlo**: Statistical estimation with 10,000 simulations
4. **Click "Roll Dice"** to get initial roll and probability suggestions
5. **Toggle dice** by clicking them to mark which to keep (blue) or reroll (gray)
6. **Click "Re-roll Selected"** to reroll unmarked dice
7. View probability percentages and optimal keep suggestions for each objective

## API Examples

### Get all heroes
```bash
curl http://localhost:5022/api/heroes
```

### Simulate a roll
```bash
curl -X POST http://localhost:5022/api/roll/simulate \
  -H "Content-Type: application/json" \
  -d '{"heroId":"barbarian","diceCount":5,"method":"analytic"}'
```

### Calculate probability for a custom objective
```bash
curl -X POST http://localhost:5022/api/roll/probability \
  -H "Content-Type: application/json" \
  -d '{"notation":"[6666]","diceCount":5,"method":"analytic"}'
```

### Get advice for current dice state
```bash
curl -X POST http://localhost:5022/api/roll/advice \
  -H "Content-Type: application/json" \
  -d '{
    "heroId":"barbarian",
    "currentDice":[6,6,1,2,3],
    "rollsRemaining":1,
    "method":"analytic"
  }'
```

## Understanding Probabilities

The calculator shows the probability of achieving each objective with **optimal play** from the current state.

- **100%** - Objective already achieved or guaranteed achievable
- **90%+** - Very likely with good reroll strategy
- **50-90%** - Good chance with optimal play
- **<50%** - Difficult but possible

The suggestions show which dice to keep (highlighted) to maximize your probability of success.

## Performance Notes

- **Analytic method**: Fast for 1-5 dice, slower for 6-7 dice (uses more memory)
- **Monte Carlo method**: Consistent speed regardless of dice count, slight randomness in results
- First calculation may be slower due to .NET JIT compilation
- Subsequent calculations are much faster due to memoization

## Adding New Heroes

Create a new JSON file in `src/DiceThroneApi/Data/heroes/`:

```json
{
  "id": "new-hero",
  "name": "New Hero",
  "objectives": [
    { "name": "Attack 1", "notation": "[6666]" },
    { "name": "Attack 2", "notation": "[(123)(123)(123)]" },
    { "name": "Defense", "notation": "SmallStraight" }
  ]
}
```

The application will automatically load it on startup.
