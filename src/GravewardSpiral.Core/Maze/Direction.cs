namespace GravewardSpiral.Core.Maze;

/// <summary>向き。原点は北西の角で、北は y が減る向き、東は x が増える向き。</summary>
public enum Direction { North = 0, East = 1, South = 2, West = 3 }

public static class DirectionExtensions
{
    public static readonly Direction[] All = [Direction.North, Direction.East, Direction.South, Direction.West];

    public static int Dx(this Direction d) => d switch { Direction.East => 1, Direction.West => -1, _ => 0 };
    public static int Dy(this Direction d) => d switch { Direction.South => 1, Direction.North => -1, _ => 0 };

    public static Direction Right(this Direction d) => (Direction)(((int)d + 1) % 4);
    public static Direction Left(this Direction d) => (Direction)(((int)d + 3) % 4);
    public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) % 4);

    public static char Letter(this Direction d) => "NESW"[(int)d];

    public static Direction FromLetter(char c) => char.ToUpperInvariant(c) switch
    {
        'N' => Direction.North,
        'E' => Direction.East,
        'S' => Direction.South,
        'W' => Direction.West,
        _ => throw new FormatException($"向きは N/E/S/W のどれかです: '{c}'"),
    };
}
