// Copyright GeoTime Contributors. All Rights Reserved.

using UnrealBuildTool;

public class GeoTimeUE : ModuleRules
{
    public GeoTimeUE(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "InputCore",
            "HTTP",
            "Json",
            "JsonUtilities",
            "Landscape",
            "ProceduralMeshComponent",
        });

        PrivateDependencyModuleNames.AddRange(new string[]
        {
            "Slate",
            "SlateCore",
        });
    }
}
