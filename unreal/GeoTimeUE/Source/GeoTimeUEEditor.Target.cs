// Copyright GeoTime Contributors. All Rights Reserved.

using UnrealBuildTool;

public class GeoTimeUEEditorTarget : TargetRules
{
    public GeoTimeUEEditorTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.V5;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_4;
        ExtraModuleNames.Add("GeoTimeUE");
    }
}
