using System.Collections.Generic;

public interface IElementReceiver
{
    EffectFaction Faction { get; }
    void ReceiveElements(IReadOnlyList<ElementDefinitionSO> elements, EffectFaction sourceFaction);
}
