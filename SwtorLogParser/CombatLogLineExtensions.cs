namespace SwtorLogParser;

public static class CombatLogLineExtensions
{
    public static bool IsPlayerHeal(this CombatLogLine combatLogLine)
    {
        return combatLogLine.Source?.IsPlayer == true && 
               Action.ApplyEffectHeal.Equals(combatLogLine.Action) && 
               combatLogLine.Value is not null;
    }
    
    public static bool IsPlayerDamage(this CombatLogLine combatLogLine)
    {
        return combatLogLine.Source?.IsPlayer == true && 
               Action.ApplyEffectDamage.Equals(combatLogLine.Action) && 
               combatLogLine.Value is not null;
    }
    
    public static bool IsPlayerAbility(this CombatLogLine combatLogLine)
    {
        return combatLogLine.Source?.IsPlayer == true &&
               Action.EventAbilityActivate.Equals(combatLogLine.Action);
    }
}