namespace SwtorLogParser.Model;

public class CombatLogLineComparer : IEqualityComparer<CombatLogLine>
{
    public bool Equals(CombatLogLine? x, CombatLogLine? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null)
        {
            return false;
        }

        if (y is null)
        {
            return false;
        }

        if (x.GetType() != y.GetType())
        {
            return false;
        }

        return GetHashCode(x).Equals(GetHashCode(y));
    }

    public int GetHashCode(CombatLogLine combatLogLine) => combatLogLine.GetHashCode();
}
