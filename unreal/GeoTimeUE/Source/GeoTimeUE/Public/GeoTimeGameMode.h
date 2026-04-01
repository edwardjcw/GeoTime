// Copyright GeoTime Contributors. All Rights Reserved.
//
// AGeoTimeGameMode
// ----------------
// Sets up the GeoTime Unreal Engine viewer session:
//   • Fetches terrain metadata from the backend on BeginPlay.
//   • Creates and initialises the terrain actor.
//   • Provides the camera manager with references it needs.
//
// The GameMode is the recommended entry-point for the viewer.  Simply set it
// as the DefaultGameMode in Project Settings (or on the level) and supply
// the BackendUrl property.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "GeoTimeBackendClient.h"
#include "GeoTimeTerrainActor.h"
#include "GeoTimeCameraManager.h"
#include "GeoTimeGameMode.generated.h"

UCLASS(BlueprintType, Blueprintable)
class GEOTIMEUE_API AGeoTimeGameMode : public AGameModeBase
{
    GENERATED_BODY()

public:
    AGeoTimeGameMode();

    // ── Configuration ─────────────────────────────────────────────────────────

    /** URL of the GeoTime .NET backend (no trailing slash). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime")
    FString BackendUrl = TEXT("http://localhost:5000");

    /** Class to use for the terrain actor. Override to use a subclass. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "GeoTime")
    TSubclassOf<AGeoTimeTerrainActor> TerrainActorClass;

    // ── Runtime references ────────────────────────────────────────────────────

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime")
    UGeoTimeBackendClient* BackendClient = nullptr;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime")
    AGeoTimeTerrainActor* TerrainActor = nullptr;

    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "GeoTime")
    FTerrainMeta TerrainMeta;

protected:
    virtual void BeginPlay() override;

private:
    void OnTerrainMetaReceived(const FTerrainMeta& Meta);
    void OnBackendError(const FString& ErrorMessage);

    void FinishSetup();
};
