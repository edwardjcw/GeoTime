// Copyright GeoTime Contributors. All Rights Reserved.

using UnrealBuildTool;

public class GeoTimeUETarget : TargetRules
{
    public GeoTimeUETarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Game;
        DefaultBuildSettings = BuildSettingsVersion.V5;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_4;
        ExtraModuleNames.Add("GeoTimeUE");
    }
}
