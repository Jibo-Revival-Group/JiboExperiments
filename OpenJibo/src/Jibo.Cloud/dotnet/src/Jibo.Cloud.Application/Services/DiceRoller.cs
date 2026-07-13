namespace Jibo.Cloud.Application.Services;

public static class DiceRoller
{
    public static int Roll(IJiboRandomizer randomizer, int sides) =>
        randomizer.Choose(Enumerable.Range(1, sides).ToArray());
}
