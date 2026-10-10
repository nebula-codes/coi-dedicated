namespace CoiDedicated.GameAdapter
{
    /// <summary>Explicit command wire identifiers, independent of CLR type names.</summary>
    public enum GameCommandKind : ushort
    {
        SetSimPauseState = 1,
        GameSpeedChange = 2,
        SetConstructionPriority = 3,
        MachineSetRecipeActive = 4,
        StartDeconstructionOfStaticEntity = 5,
        ToggleStaticEntityConstruction = 6,
        CreateBuilding = 7
    }
}
