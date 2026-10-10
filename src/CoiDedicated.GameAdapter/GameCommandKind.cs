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
        CreateBuilding = 7,
        BatchCreateBuildings = 8,
        RuinsScrap = 20,
        FleetLoadCrew = 21,
        DesignateHarvestedTrees = 22,
        NavigateVehicleToPosition = 23,
        ToggleEnabled = 24,
        BuildTransport = 25,
        ResearchStart = 40,
        ResearchQueueDequeue = 41,
        ResearchStop = 42,
        ToggleEdictEnabled = 43,
        ToggleContract = 44,
        QuickTrade = 45,
        ShipyardSetRepairing = 46,
        AddTerrainDesignations = 60,
        RemoveDesignations = 61,
        MineTowerAreaChange = 62,
        ForestryTowerAreaChange = 63,
        AddProductToDump = 64,
        RemoveProductToDump = 65,
        AssignVehicleTypeToEntity = 79,
        UnassignVehicleFromEntity = 80
    }
}
